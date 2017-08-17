using ACESim.Util;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Threading;

namespace ACESim
{
    [Serializable]
    public partial class CounterfactualRegretMaximization : IStrategiesDeveloper
    {

        #region Options

        public const int MaxNumPlayers = 4; // this affects fixed-size stack-allocated buffers
        public const int MaxPossibleActions = 100; // same

        bool TraceVanillaCFR = false;
        bool TraceProbingCFR = false;
        bool TraceAverageStrategySampling = false;

        bool ShouldEstimateImprovementOverTime = false;
        const int NumRandomGamePlaysForEstimatingImprovement = 1000;

        public InformationSetLookupApproach LookupApproach = InformationSetLookupApproach.CachedGameHistoryOnly;
        bool AllowSkipEveryPermutationInitialization = true;
        public bool SkipEveryPermutationInitialization => (AllowSkipEveryPermutationInitialization && (Navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || Navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)) && EvolutionSettings.Algorithm != GameApproximationAlgorithm.PureStrategyFinder;

        double CurrentEpsilonValue; // set in algorithm.
        //double epsilon = 0.05, beta = 1000000, tau = 1000; // note that beta will keep sampling even at first, but becomes less important later on. Epsilon ensures some exploration, and larger tau weights things later toward low-probability strategies
        double epsilon = 0.05, beta = 100, tau = 1; // note that beta will keep sampling even at first, but becomes less important later on. Epsilon ensures some exploration, and larger tau weights things later toward low-probability strategies

        #endregion

        #region Class variables

        public List<Strategy> Strategies { get; set; }

        public EvolutionSettings EvolutionSettings { get; set; }

        public GameDefinition GameDefinition { get; set; }

        public IGameFactory GameFactory { get; set; }

        public GamePlayer GamePlayer { get; set; }

        public HistoryNavigationInfo Navigation;

        public ActionStrategies _ActionStrategy;
        public ActionStrategies ActionStrategy
        {
            get => _ActionStrategy;
            set
            {
                _ActionStrategy = value;
                Strategies.ForEach(x => x.ActionStrategy = value);
            }
        }

        /// <summary>
        /// A game history tree. On each internal node, the object contained is the information set of the player making the decision, including the information set of the chance player at that game point. 
        /// On each leaf node, the object contained is an array of the players' terminal utilities.
        /// The game history tree is not used if LookupApproach == Strategies
        /// </summary>
        public NWayTreeStorageInternal<IGameState> GameHistoryTree;

        public int NumNonChancePlayers;
        public int NumChancePlayers; // note that chance players MUST be indexed after nonchance players in the player list

        public int NumInitializedGamePaths = 0;

        #endregion

        #region Construction

        public CounterfactualRegretMaximization()
        {
            Navigation.SetGameStateFn(GetGameState);
        }

        public CounterfactualRegretMaximization(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition)
        {
            Strategies = existingStrategyState;
            EvolutionSettings = evolutionSettings;
            GameDefinition = gameDefinition;
            GameFactory = GameDefinition.GameFactory;
            NumNonChancePlayers = GameDefinition.Players.Count(x => !x.PlayerIsChance);
            NumChancePlayers = GameDefinition.Players.Count(x => x.PlayerIsChance);
        }

        public IStrategiesDeveloper DeepCopy()
        {
            return new CounterfactualRegretMaximization()
            {
                Strategies = Strategies.Select(x => x.DeepCopy()).ToList(),
                EvolutionSettings = EvolutionSettings,
                GameDefinition = GameDefinition,
                GameFactory = GameFactory,
                Navigation = Navigation,
                LookupApproach = LookupApproach
            };
        }

        #endregion

        #region Initialization

        public void DevelopStrategies()
        {
            Initialize();
            switch (EvolutionSettings.Algorithm)
            {
                case GameApproximationAlgorithm.AverageStrategySampling:
                    SolveAvgStrategySamplingCFR();
                    break;
                case GameApproximationAlgorithm.ExplorativeProbing:
                    SolveExplorativeProbingCFR();
                    break;
                case GameApproximationAlgorithm.GibsonProbing:
                    SolveGibsonProbingCFR();
                    break;
                case GameApproximationAlgorithm.AbramowiczProbing:
                    SolveAbramowiczProbingCFR();
                    break;
                case GameApproximationAlgorithm.Probing:
                    SolveProbingCFR();
                    break;
                case GameApproximationAlgorithm.Vanilla:
                    SolveVanillaCFR();
                    break;
                case GameApproximationAlgorithm.PureStrategyFinder:
                    FindPureStrategies();
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public unsafe void Initialize()
        {
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, GetGameState);
            foreach (Strategy strategy in Strategies)
                strategy.Navigation = Navigation;

            GamePlayer = new GamePlayer(Strategies, EvolutionSettings.ParallelOptimization, GameDefinition);

            foreach (Strategy s in Strategies)
            {
                s.CreateInformationSetTree(GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.PlayerNumber == s.PlayerInfo.PlayerIndex)?.NumPossibleActions ?? (byte)1);
            }

            if (SkipEveryPermutationInitialization)
                return; // no initialization needed (that's a benefit of using GameHistory -- we can initialize information sets on the fly, which may be much faster than playing every game permutation)

            // Create game trees
            GameHistoryTree = new NWayTreeStorageInternal<IGameState>(null, GameDefinition.DecisionsExecutionOrder.First().NumPossibleActions);

            NumInitializedGamePaths = GamePlayer.PlayAllPaths(ProcessInitializedGameProgress);
            Debug.WriteLine($"Initialized. Total paths: {NumInitializedGamePaths}");
            PrintSameGameResults();
        }

        unsafe void ProcessInitializedGameProgress(GameProgress gameProgress)
        {
            // First, add the utilities at the end of the tree for this path.
            byte* actionsEnumerator = stackalloc byte[GameHistory.MaxNumActions];
            gameProgress.GameHistory.GetActions(actionsEnumerator);
            //var actionsAsList = ListExtensions.GetPointerAsList_255Terminated(actionsEnumerator);

            // Go through each non-chance decision point on this path and make sure that the information set tree extends there. We then store the regrets etc. at these points. 

            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            IEnumerable<InformationSetHistory> informationSetHistories = gameProgress.GameHistory.GetInformationSetHistoryItems();
            foreach (var informationSetHistory in informationSetHistories)
            {
                //var informationSetHistoryString = informationSetHistory.ToString();
                historyPoint.SetInformationIfNotSet(Navigation, gameProgress, informationSetHistory);
                historyPoint = historyPoint.GetBranch(Navigation, informationSetHistory.ActionChosen);
                //var actionsToHere = historyPoint.GetActionsToHereString(Navigation);
            }
            historyPoint.SetFinalUtilitiesAtPoint(Navigation, gameProgress);
        }

        public IGameState GetGameState(HistoryPoint_CachedGameHistoryOnly historyPoint, HistoryNavigationInfo? navigation = null)
        {
            HistoryNavigationInfo navigationSettings = navigation ?? Navigation;
            return historyPoint.GetGameStateForCurrentPlayer(navigationSettings.GameDefinition, navigationSettings.Strategies) ?? GetGameStateByPlayingUnderlyingGame(new HistoryPoint(null, historyPoint.HistoryToPoint, null), navigationSettings);
        }

        public IGameState GetGameState(HistoryPoint historyPoint, HistoryNavigationInfo? navigation = null)
        {
            HistoryNavigationInfo navigationSettings = navigation ?? Navigation;
            return historyPoint.GetGameStateForCurrentPlayer(navigationSettings) ?? GetGameStateByPlayingUnderlyingGame(historyPoint, navigationSettings);
        }

        private IGameState GetGameStateByPlayingUnderlyingGame(HistoryPoint historyPoint, HistoryNavigationInfo navigationSettings)
        {
            IGameState gameState;
            List<byte> actionsSoFar = historyPoint.GetActionsToHere(navigationSettings);
            (GameProgress progress, _) = GamePlayer.PlayPath(actionsSoFar, false);
            ProcessInitializedGameProgress(progress);
            NumInitializedGamePaths++; // Note: This may not be exact if we initialize the same game path twice
            gameState = historyPoint.GetGameStateForCurrentPlayer(navigationSettings);
            if (gameState == null)
                throw new Exception("Internal error.");
            return gameState;
        }

        #endregion

        #region Printing

        public void PrintInformationSets()
        {
            foreach (Strategy s in Strategies)
            {
                if (!s.PlayerInfo.PlayerIsChance || !EvolutionSettings.PrintNonChanceInformationSetsOnly)
                {
                    Debug.WriteLine($"{s.PlayerInfo}");
                    string tree = s.GetInformationSetTreeString();
                    Debug.WriteLine(tree);
                }
            }
        }

        private void PrintInformationSets(NWayTreeStorageInternal<IGameState> informationSetTree)
        {
            throw new NotImplementedException();
        }

        public void PrintGameTree()
        {
            PrintGameTree_Helper(GetStartOfGameHistoryPoint());
        }

        public unsafe double[] PrintGameTree_Helper(HistoryPoint historyPoint)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            //if (TraceProbingCFR)
            //    TabbedText.WriteLine($"Probe optimizing player {playerBeingOptimized}");
            if (gameStateForCurrentPlayer is FinalUtilities finalUtilities)
            {
                TabbedText.WriteLine($"--> {String.Join(",", finalUtilities.Utilities.Select(x => $"{x:N2}"))}");
                return finalUtilities.Utilities;
            }
            else
            {
                if (gameStateForCurrentPlayer is ChanceNodeSettings chanceNodeSettings)
                {
                    byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
                    double[] cumUtilities = null;
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        double chanceProbability = chanceNodeSettings.GetActionProbability(action);
                        TabbedText.WriteLine($"{action} (C): p={chanceProbability:N2}");
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action);
                        TabbedText.Tabs++;
                        double[] utilitiesAtNextHistoryPoint = PrintGameTree_Helper(nextHistoryPoint);
                        TabbedText.Tabs--;
                        if (cumUtilities == null)
                            cumUtilities = new double[utilitiesAtNextHistoryPoint.Length];
                        for (int i = 0; i < cumUtilities.Length; i++)
                            cumUtilities[i] += utilitiesAtNextHistoryPoint[i] * chanceProbability;
                    }
                    TabbedText.WriteLine($"--> {String.Join(",", cumUtilities.Select(x => $"{x:N2}"))}");
                    return cumUtilities;
                }
                else if (gameStateForCurrentPlayer is InformationSetNodeTally informationSet)
                {
                    byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
                    double[] cumUtilities = null;
                    double* actionProbabilities = stackalloc double[numPossibleActions];
                    informationSet.GetRegretMatchingProbabilities(actionProbabilities);
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        TabbedText.WriteLine($"{action} (P{informationSet.PlayerIndex}): p={actionProbabilities[action - 1]:N2} (from regrets {informationSet.GetCumulativeRegretsString()})");
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action);
                        TabbedText.Tabs++;
                        double[] utilitiesAtNextHistoryPoint = PrintGameTree_Helper(nextHistoryPoint);
                        TabbedText.Tabs--;
                        if (cumUtilities == null)
                            cumUtilities = new double[utilitiesAtNextHistoryPoint.Length];
                        for (int i = 0; i < cumUtilities.Length; i++)
                            cumUtilities[i] += utilitiesAtNextHistoryPoint[i] * actionProbabilities[action - 1];
                    }
                    TabbedText.WriteLine($"--> {String.Join(",", cumUtilities.Select(x => $"{x:N2}"))}");
                    return cumUtilities;
                }
            }
            throw new NotImplementedException();
        }

        double printProbability = 0.0;
        bool processIfNotPrinting = false;
        private unsafe void PrintSameGameResults()
        {
            //player.PlaySinglePathAndKeepGoing("1,1,1,1,2,1,1", inputs); // use this to trace through a single path
            if (printProbability == 0 && !processIfNotPrinting)
                return;
            GamePlayer.PlayAllPaths(PrintGameProbabilistically);
        }

        private unsafe void PrintGameProbabilistically(GameProgress progress)
        {
            byte* path = stackalloc byte[GameHistory.MaxNumActions];
            bool overridePrint = false;
            string actionsList = progress.GameHistory.GetActionsAsListString();
            if (actionsList == "INSERT_PATH_HERE") // use this to print a single path
            {
                overridePrint = true;
            }
            if (overridePrint || RandomGenerator.NextDouble() < printProbability)
            {
                lock (this)
                {

                    progress.GameHistory.GetActions(path);
                    List<byte> path2 = new List<byte>();
                    int i = 0;
                    while (*(path + i) != 255)
                    {
                        path2.Add(*(path + i));
                        i++;
                    }
                    TabbedText.WriteLine($"{String.Join(",", path2)}");
                    TabbedText.Tabs++;
                    PrintGenericGameProgress(progress);
                    TabbedText.Tabs--;
                }
            }
        }

        public void PrintGenericGameProgress(GameProgress progress)
        {
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            // Go through each non-chance decision point 
            foreach (var informationSetHistory in progress.GameHistory.GetInformationSetHistoryItems())
            {
                var informationSetHistoryCopy = informationSetHistory; // must copy because informationSetHistory is foreach iteration variable.
                var decision = GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
                TabbedText.WriteLine($"Decision {decision.Name} ({decision.DecisionByteCode}) for player {GameDefinition.Players[decision.PlayerNumber].PlayerName} ({GameDefinition.Players[decision.PlayerNumber].PlayerIndex})");
                TabbedText.Tabs++;
                bool playerIsChance = GameDefinition.Players[informationSetHistory.PlayerIndex].PlayerIsChance;
                var playersStrategy = Strategies[informationSetHistory.PlayerIndex];
                unsafe
                {
                    List<byte> informationSetList = ListExtensions.GetPointerAsList_255Terminated(informationSetHistoryCopy.InformationSetForPlayer);
                    TabbedText.WriteLine($"Information set before action: {String.Join(",", informationSetList)}");
                    IGameState gameState = GetGameState(historyPoint);
                    TabbedText.WriteLine($"Game state before action: {gameState}");
                }
                TabbedText.WriteLine($"==> Action chosen: {informationSetHistory.ActionChosen}");
                TabbedText.Tabs--;
                historyPoint = historyPoint.GetBranch(Navigation, informationSetHistory.ActionChosen);
            }
            double[] finalUtilities = historyPoint.GetFinalUtilities(Navigation);
            TabbedText.WriteLine($"--> Utilities: { String.Join(",", finalUtilities)}");
        }

        #endregion

        #region Utility methods

        private unsafe HistoryPoint GetStartOfGameHistoryPoint()
        {
            switch (Navigation.LookupApproach)
            {
                case InformationSetLookupApproach.PlayUnderlyingGame:
                    GameProgress startingProgress = GameFactory.CreateNewGameProgress(new IterationID(1));
                    return new HistoryPoint(null, startingProgress.GameHistory, startingProgress);
                case InformationSetLookupApproach.CachedGameTreeOnly:
                    return new HistoryPoint(GameHistoryTree, new GameHistory() /* won't even initialize, since won't be used */, null);
                case InformationSetLookupApproach.CachedGameHistoryOnly:
                    return new HistoryPoint(null, new GameHistory(), null);
                case InformationSetLookupApproach.CachedBothMethods:
                    return new HistoryPoint(GameHistoryTree, new GameHistory(), null);
                default:
                    throw new Exception(); // unexpected lookup approach -- won't be called
            }
        }

        private unsafe HistoryPoint GetHistoryPointBasedOnProgress(GameProgress gameProgress)
        {
            if (Navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
                return new HistoryPoint(null, gameProgress.GameHistory, gameProgress);
            if (Navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly)
                return new HistoryPoint(null, gameProgress.GameHistory, null);
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            foreach (var informationSetHistory in gameProgress.GameHistory.GetInformationSetHistoryItems())
            {
                historyPoint = historyPoint.GetBranch(Navigation, informationSetHistory.ActionChosen);
            }
            return historyPoint;
        }

        public double GetUtilityFromTerminalHistory(HistoryPoint historyPoint, byte playerIndex)
        {
            double[] utilities = GetUtilities(historyPoint);
            return utilities[playerIndex];
        }

        public double[] GetUtilities(HistoryPoint completedGame)
        {
            return completedGame.GetFinalUtilities(Navigation);
        }

        public void PreSerialize()
        {
        }

        public void UndoPreSerialize()
        {
        }

        public byte NumPossibleActionsAtDecision(byte decisionIndex)
        {
            return GameDefinition.DecisionsExecutionOrder[decisionIndex].NumPossibleActions;
        }

        #endregion

        #region Game play and reporting

        private unsafe void GenerateReports(int iteration, Func<string> prefaceFn)
        {
            if (EvolutionSettings.ReportEveryNIterations != null && iteration % EvolutionSettings.ReportEveryNIterations == 0)
            {
                ActionStrategies previous = ActionStrategy;
                ActionStrategy = ActionStrategies.RegretMatching;
                bool useRandomPaths = SkipEveryPermutationInitialization || NumInitializedGamePaths > EvolutionSettings.NumRandomIterationsForReporting;
                bool doBestResponse = (EvolutionSettings.BestResponseEveryMIterations != null && iteration % EvolutionSettings.BestResponseEveryMIterations == 0 && EvolutionSettings.BestResponseEveryMIterations != EvolutionSettings.EffectivelyNever && iteration != 0);
                if (doBestResponse)
                    useRandomPaths = false;
                Debug.WriteLine("");
                Debug.WriteLine(prefaceFn());
                if (EvolutionSettings.Algorithm == GameApproximationAlgorithm.AverageStrategySampling)
                    Debug.WriteLine($"{NumberAverageStrategySamplingExplorations / (double)EvolutionSettings.ReportEveryNIterations}");
                NumberAverageStrategySamplingExplorations = 0;
                MainReport(useRandomPaths, null);
                MeasureRegretMatchingChanges();
                if (EvolutionSettings.AlternativeOverride != null)
                {
                    Debug.WriteLine("With alternative:");
                    MainReport(useRandomPaths, EvolutionSettings.AlternativeOverride);
                }
                if (ShouldEstimateImprovementOverTime)
                    ReportEstimatedImprovementsOverTime();
                if (doBestResponse)
                    CompareBestResponse(iteration, useRandomPaths);
                if (EvolutionSettings.AlwaysUseAverageStrategyInReporting)
                    ActionStrategy = previous;
                if (EvolutionSettings.PrintGameTreeAfterReport)
                    PrintGameTree();
                if (EvolutionSettings.PrintInformationSetsAfterReport)
                    PrintInformationSets();
                ActionStrategy = previous;
            }
        }

        private unsafe void MainReport(bool useRandomPaths, Func<Decision, GameProgress, byte> actionOverride)
        {
            Action<GamePlayer, Func<Decision, GameProgress, byte>> reportGenerator;
            if (useRandomPaths)
            {
                Debug.WriteLine($"Result using {EvolutionSettings.NumRandomIterationsForReporting} randomly chosen paths");
                reportGenerator = GenerateReports_RandomPaths;
            }
            else
            {
                Debug.WriteLine($"Result using all paths");
                reportGenerator = GenerateReports_AllPaths;
            }
            Debug.WriteLine($"{GenerateReports(reportGenerator, actionOverride)}");
            //Debug.WriteLine($"Number initialized game paths: {NumInitializedGamePaths}");
        }

        private unsafe void CompareBestResponse(int iteration, bool useRandomPaths)
        {
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                double bestResponseUtility = CalculateBestResponse(playerBeingOptimized, ActionStrategy);
                double bestResponseImprovement = bestResponseUtility - UtilityCalculations[playerBeingOptimized].Average();
                if (!useRandomPaths && bestResponseImprovement < -1E-15)
                    throw new Exception("Best response function worse."); // it can be slightly negative as a result of rounding error or if we are using random paths as a result of sampling error
                Debug.WriteLine($"Player {playerBeingOptimized} utility with regret matching {UtilityCalculations[playerBeingOptimized].Average()} using best response against regret matching {bestResponseUtility} best response improvement {bestResponseImprovement}");
            }
        }


        public List<GameProgress> GetRandomCompleteGames(GamePlayer player, int numIterations, Func<Decision, GameProgress, byte> actionOverride)
        {
            return player.PlayMultipleIterations(null, numIterations, null, actionOverride).ToList();
        }

        private void GenerateReports_RandomPaths(GamePlayer player, Func<Decision, GameProgress, byte> actionOverride)
        {
            var gameProgresses = GetRandomCompleteGames(player, EvolutionSettings.NumRandomIterationsForReporting, actionOverride);
            UtilityCalculations = new StatCollector[NumNonChancePlayers];
            for (int p = 0; p < NumNonChancePlayers; p++)
                UtilityCalculations[p] = new StatCollector();
            // start Task Parallel Library consumer/producer pattern
            // we'll set up step1, step2, and step3 (but not in that order, since step 1 triggers step 2)
            var step2_buffer = new BufferBlock<Tuple<GameProgress, double>>(new DataflowBlockOptions { BoundedCapacity = 10000 });
            var step3_consumer = AddGameProgressToReport(step2_buffer);
            foreach (var gameProgress in gameProgresses)
                step2_buffer.SendAsync(new Tuple<GameProgress, double>(gameProgress, 1.0));
            step2_buffer.Complete(); // tell consumer nothing more to be produced
            step3_consumer.Wait(); // wait until all have been processed
        }

        private void CountPaths(List<GameProgress> gameProgresses)
        {
            // this is just for testing
            var CountPaths = new Dictionary<string, int>();
            for (int i = 0; i < EvolutionSettings.NumRandomIterationsForReporting; i++)
            {
                GameProgress gameProgress1 = gameProgresses[i];
                string gameActions = gameProgress1.GameHistory.GetActionsAsListString();
                if (!CountPaths.ContainsKey(gameActions))
                    CountPaths[gameActions] = 1;
                else
                    CountPaths[gameActions] = CountPaths[gameActions] + 1;
                //Debug.WriteLine($"{gameActions} {gameProgress1.GetNonChancePlayerUtilities()[0]}");
            }
            foreach (var item in CountPaths.AsEnumerable().OrderBy(x => x.Key))
                Debug.WriteLine($"{item.Key} => {((double)item.Value) / (double)EvolutionSettings.NumRandomIterationsForReporting}");
        }

        public void ProcessAllPaths(HistoryPoint history, Action<HistoryPoint, double> pathPlayer)
        {
            ProcessAllPaths_Recursive(history, pathPlayer, ActionStrategy, 1.0);
        }

        private void ProcessAllPaths_Recursive(HistoryPoint history, Action<HistoryPoint, double> pathPlayer, ActionStrategies actionStrategy, double probability)
        {
            // Note that this method is different from GamePlayer.PlayAllPaths, because it relies on the cached history, rather than needing to play the game to discover what the next paths are.
            if (history.IsComplete(Navigation))
            {
                pathPlayer(history, probability);
                return;
            }
            ProcessAllPaths_Helper(history, probability, pathPlayer, actionStrategy);
        }

        private unsafe void ProcessAllPaths_Helper(HistoryPoint historyPoint, double probability, Action<HistoryPoint, double> completedGameProcessor, ActionStrategies actionStrategy)
        {
            double* probabilities = stackalloc double[GameHistory.MaxNumActions];
            byte numPossibleActions = NumPossibleActionsAtDecision(historyPoint.GetNextDecisionIndex(Navigation));
            IGameState gameState = GetGameState(historyPoint);
            ActionProbabilityUtilities.GetActionProbabilitiesAtHistoryPoint(gameState, actionStrategy, probabilities, numPossibleActions, null, Navigation);
            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, EvolutionSettings.MaxParallelDepth, 1, (byte)(numPossibleActions + 1), (action) =>
            {
                if (probabilities[action - 1] > 0)
                {
                    ProcessAllPaths_Recursive(historyPoint.GetBranch(Navigation, action), completedGameProcessor, actionStrategy, probability * probabilities[action - 1]);
                }
            });
        }

        public double[] GetAverageUtilities()
        {
            double[] cumulated = new double[NumNonChancePlayers];
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, GetGameState);
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            GetAverageUtilities_Helper(historyPoint, cumulated, 1.0);
            return cumulated;
        }

        public unsafe void GetAverageUtilities_Helper(HistoryPoint historyPoint, double[] cumulated, double prob)
        {
            IGameState gameState = historyPoint.GetGameStateForCurrentPlayer(Navigation);
            if (gameState is FinalUtilities finalUtilities)
            {
                for (byte p = 0; p < NumNonChancePlayers; p++)
                    cumulated[p] += finalUtilities.Utilities[p] * prob;
            }
            else if (gameState is ChanceNodeSettings chanceNode)
            {
                byte numPossibilities = GameDefinition.DecisionsExecutionOrder[chanceNode.DecisionIndex].NumPossibleActions;
                for (byte action = 1; action <= numPossibilities; action++)
                {
                    double actionProb = chanceNode.GetActionProbability(action);
                    if (actionProb > 0)
                    {
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action);
                        GetAverageUtilities_Helper(nextHistoryPoint, cumulated, prob * actionProb);
                    }
                }
            }
            else if (gameState is InformationSetNodeTally nodeTally)
            {
                byte numPossibilities = GameDefinition.DecisionsExecutionOrder[nodeTally.DecisionIndex].NumPossibleActions;
                double* actionProbabilities = stackalloc double[numPossibilities];
                nodeTally.GetRegretMatchingProbabilities(actionProbabilities);
                for (byte action = 1; action <= numPossibilities; action++)
                {
                    if (actionProbabilities[action - 1] > 0)
                    {
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action);
                        GetAverageUtilities_Helper(nextHistoryPoint, cumulated, prob * actionProbabilities[action - 1]);
                    }
                }
            }
        }

        SimpleReport[] ReportsBeingGenerated = null;

        public string GenerateReports(Action<GamePlayer, Func<Decision, GameProgress, byte>> generator, Func<Decision, GameProgress, byte> actionOverride)
        {
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, GetGameState);
            StringBuilder sb = new StringBuilder();
            var simpleReportDefinitions = GameDefinition.GetSimpleReportDefinitions();
            int simpleReportDefinitionsCount = simpleReportDefinitions.Count();
            ReportsBeingGenerated = new SimpleReport[simpleReportDefinitionsCount];
            for (int i = 0; i < simpleReportDefinitionsCount; i++)
                ReportsBeingGenerated[i] = new SimpleReport(simpleReportDefinitions[i], simpleReportDefinitions[i].DivideColumnFiltersByImmediatelyEarlierReport ? ReportsBeingGenerated[i - 1] : null);
            generator(GamePlayer, actionOverride);
            for (int i = 0; i < simpleReportDefinitionsCount; i++)
                ReportsBeingGenerated[i].GetReport(sb, false);
            ReportsBeingGenerated = null;
            return sb.ToString();
        }

        StatCollector[] UtilityCalculations;

        private void GenerateReports_AllPaths(GamePlayer player, Func<Decision, GameProgress, byte> actionOverride)
        {
            UtilityCalculations = new StatCollector[NumNonChancePlayers];
            for (int p = 0; p < NumNonChancePlayers; p++)
                UtilityCalculations[p] = new StatCollector();
            // start Task Parallel Library consumer/producer pattern
            // we'll set up step1, step2, and step3 (but not in that order, since step 1 triggers step 2)
            var step2_buffer = new BufferBlock<Tuple<GameProgress, double>>(new DataflowBlockOptions { BoundedCapacity = 10000 });
            var step3_consumer = AddGameProgressToReport(step2_buffer);
            void step1_playPath(HistoryPoint completedGame, double probabilityOfPath)
            {
                // play each path and then asynchronously consume the result, including the probability of the game path
                List<byte> actions = completedGame.GetActionsToHere(Navigation);
                (GameProgress progress, _) = player.PlayPath(actions, false);
                // do the simple aggregation of utilities. note that this is different from the value returned by vanilla, since that uses regret matching, instead of average strategies.
                double[] utilities = GetUtilities(completedGame);
                for (int p = 0; p < NumNonChancePlayers; p++)
                    UtilityCalculations[p].Add(utilities[p], probabilityOfPath);
                // consume the result for reports
                step2_buffer.SendAsync(new Tuple<GameProgress, double>(progress, probabilityOfPath));
            };
            // Now, we have to send the paths through all of these steps and make sure that step 3 is completely finished.
            ProcessAllPaths(GetStartOfGameHistoryPoint(), step1_playPath);
            step2_buffer.Complete(); // tell consumer nothing more to be produced
            step3_consumer.Wait(); // wait until all have been processed

            //for (int p = 0; p < NumNonChancePlayers; p++)
            //    if (Math.Abs(UtilityCalculations[p].sumOfWeights - 1.0) > 0.0001)
            //        throw new Exception("Imperfect sampling.");
        }

        async Task AddGameProgressToReport(ISourceBlock<Tuple<GameProgress, double>> source)
        {
            var simpleReportDefinitions = GameDefinition.GetSimpleReportDefinitions();
            int simpleReportDefinitionsCount = simpleReportDefinitions.Count();
            while (await source.OutputAvailableAsync())
            {
                Tuple<GameProgress, double> toProcess = source.Receive();
                if (toProcess.Item2 > 0) // probability
                    for (int i = 0; i < simpleReportDefinitionsCount; i++)
                        ReportsBeingGenerated[i].ProcessGameProgress(toProcess.Item1, toProcess.Item2);
            }
        }

        #endregion

        #region Pi values utility methods

        public double GetPiValue(double[] piValues, byte playerIndex, byte decisionNum)
        {
            return piValues[playerIndex];
        }

        public unsafe double GetInversePiValue(double* piValues, byte playerIndex)
        {
            double product = 1.0;
            for (byte p = 0; p < NumNonChancePlayers; p++)
                if (p != playerIndex)
                    product *= piValues[p];
            return product;
        }

        private unsafe void GetNextPiValues(double* currentPiValues, byte playerIndex, double probabilityToMultiplyBy, bool changeOtherPlayers, double* nextPiValues)
        {
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                double currentPiValue = currentPiValues[p];
                double nextPiValue;
                if (p == playerIndex)
                    nextPiValue = changeOtherPlayers ? currentPiValue : currentPiValue * probabilityToMultiplyBy;
                else
                    nextPiValue = changeOtherPlayers ? currentPiValue * probabilityToMultiplyBy : currentPiValue;
                nextPiValues[p] = nextPiValue;
            }
        }

        private unsafe void GetInitialPiValues(double* initialPiValues)
        {
            for (byte p = 0; p < NumNonChancePlayers; p++)
                initialPiValues[p] = 1.0;
        }

        #endregion

        // Based on Algorithm 9 in the Lanctot thesis. Since we won't be calculating best response much, adopting more efficient approaches probably isn't necessary.

        // http://papers.nips.cc/paper/4569-efficient-monte-carlo-counterfactual-regret-minimization-in-games-with-many-player-actions.pdf

        #region Estimate improvement over time

        double[] MostRecentUtilities;
        Queue<double>[] MostRecentImprovements;
        double[] AggregateRecentImprovements;
        const int SizeOfMovingAverage = 100;
        long PseudorandomSpot = 1;
        bool EstimatingImprovementOverTimeMode = false;

        public double[] GetUtilitiesFromRandomGamePlay(int repetitions)
        {
            double[] aggregatedUtilities = new double[NumNonChancePlayers];
            for (int r = 0; r < repetitions; r++)
            {
                double[] utilities = GetUtilitiesFromRandomGamePlay(new ConsistentRandomSequenceProducer((PseudorandomSpot + r) * 1000));
                for (int i = 0; i < NumNonChancePlayers; i++)
                {
                    aggregatedUtilities[i] += utilities[i];
                    if (r == repetitions - 1)
                        aggregatedUtilities[i] /= (double)NumRandomGamePlaysForEstimatingImprovement;
                }
            }
            return aggregatedUtilities;
        }

        public double[] GetUtilitiesFromRandomGamePlay(IRandomProducer randomProducer)
        {
            EstimatingImprovementOverTimeMode = true;
            double[] returnVal = Probe(GetStartOfGameHistoryPoint(), randomProducer);
            EstimatingImprovementOverTimeMode = false;
            return returnVal;
        }

        public void InitializeImprovementOverTimeEstimation()
        {
            AggregateRecentImprovements = new double[NumNonChancePlayers];
            MostRecentImprovements = new Queue<double>[NumNonChancePlayers];
            for (int p = 0; p < NumNonChancePlayers; p++)
                MostRecentImprovements[p] = new Queue<double>();
        }

        public void PrepareForImprovementOverTimeEstimation(byte playerAboutToBeOptimized)
        {
            MostRecentUtilities = GetUtilitiesFromRandomGamePlay(NumRandomGamePlaysForEstimatingImprovement);
        }

        public void UpdateImprovementOverTimeEstimation(byte playerJustOptimized, int iteration)
        {
            double[] previousMostRecentUtilities = MostRecentUtilities;
            MostRecentUtilities = GetUtilitiesFromRandomGamePlay(NumRandomGamePlaysForEstimatingImprovement);
            double improvement = MostRecentUtilities[playerJustOptimized] - previousMostRecentUtilities[playerJustOptimized];
            MostRecentImprovements[playerJustOptimized].Enqueue(improvement);
            UpdateAggregateRecentImprovements(playerJustOptimized, improvement, true);
            if (iteration >= SizeOfMovingAverage)
            {
                double oldImprovement = MostRecentImprovements[playerJustOptimized].Dequeue();
                UpdateAggregateRecentImprovements(playerJustOptimized, oldImprovement, false);
            }
            PseudorandomSpot += NumRandomGamePlaysForEstimatingImprovement * 1000;
        }

        private void UpdateAggregateRecentImprovements(byte playerJustOptimized, double improvement, bool add)
        {
            if (add)
                AggregateRecentImprovements[playerJustOptimized] += improvement;
            else
                AggregateRecentImprovements[playerJustOptimized] -= improvement;
        }

        private void ReportEstimatedImprovementsOverTime()
        {
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                double numItems = MostRecentImprovements[p].Count();
                Debug.WriteLine($"Estimated average improvement per iteration over last {SizeOfMovingAverage} iterations for player {p}: {AggregateRecentImprovements[p]/numItems}");
            }
        }

        #endregion

        #region Measure change sizes

        NWayTreeStorage<List<double>>[] PreviousRegretMatchingState;

        public void MeasureRegretMatchingChanges()
        {
            NWayTreeStorage<List<double>>[] newRegretMatchingState = new NWayTreeStorage<List<double>>[NumNonChancePlayers];
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                newRegretMatchingState[p] = Strategies[p].GetRegretMatchingTree();
                if (PreviousRegretMatchingState != null)
                {
                    (double totalChange, double proportionMixed) = MeasureRegretMatchingChange(PreviousRegretMatchingState[p], newRegretMatchingState[p]);
                    Debug.WriteLine($"Change size for player {p} change {totalChange} proportion mixed {proportionMixed}");
                }
            }
            PreviousRegretMatchingState = newRegretMatchingState;
        }

        private (double totalChange, double proportionMixed) MeasureRegretMatchingChange(NWayTreeStorage<List<double>> previous, NWayTreeStorage<List<double>> replacement)
        {
            double total = 0;
            int nodesCount = 0;
            int mixedStrategyNodesCount = 0;
            previous.WalkTree(node =>
            {
                if (node.StoredValue != null)
                {
                    var sequenceToHere = node.GetSequenceToHere();
                    NWayTreeStorage<List<double>> corresponding = replacement;
                    int i = 0;
                    while (corresponding != null && i < sequenceToHere.Count())
                        corresponding = corresponding.GetBranch(sequenceToHere[i++]);
                    if (corresponding.StoredValue != null)
                    {
                        for (i = 0; i < node.StoredValue.Count(); i++)
                        {
                            total += Math.Abs(corresponding.StoredValue[i] - node.StoredValue[i]);
                        }
                        nodesCount++;
                        if (node.StoredValue.Where(x => x > 0).Count() >= 2)
                            mixedStrategyNodesCount++;
                    }
                }
            }
            );
            return (total, (double) mixedStrategyNodesCount / (double) nodesCount);
        }

        #endregion

    }
}
