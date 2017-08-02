using ACESim.Util;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.VisualStudio.Profiler;
using System.Threading;

namespace ACESim
{
    [Serializable]
    public class CRMDevelopment : IStrategiesDeveloper
    {
        public const int MaxNumPlayers = 4; // this affects fixed-size stack-allocated buffers
        public const int MaxPossibleActions = 100; // same

        public enum CRMAlgorithm
        {
            Vanilla,
            Probing,
            AverageStrategySampling,
            PureStrategyFinder
        }

        CRMAlgorithm Algorithm = CRMAlgorithm.AverageStrategySampling;
        const int TotalAvgStrategySamplingCFRIterations = 100000000;
        const int TotalProbingCFRIterations = 100000000;
        const int TotalVanillaCFRIterations = 100000000;
        bool TraceVanillaCRM = false;
        bool TraceProbingCRM = false;
        bool TraceAverageStrategySampling = false;

        // The following apply to probing and average strategy sampling. The MCCFR algorithm is not guaranteed to visit all information sets.
        bool UseEpsilonOnPolicyForOpponent = true;
        double FirstOpponentEpsilonValue = 0.5;
        double LastOpponentEpsilonValue = 0.00001;
        int LastOpponentEpsilonIteration = 10000;
        double CurrentEpsilonValue; // set in algorithm.

        public InformationSetLookupApproach LookupApproach = InformationSetLookupApproach.CachedGameHistoryOnly;
        bool AllowSkipEveryPermutationInitialization = true;
        public bool SkipEveryPermutationInitialization => (AllowSkipEveryPermutationInitialization && (Navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || Navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)) && Algorithm != CRMAlgorithm.PureStrategyFinder;

        int? ReportEveryNIterations => Algorithm == CRMAlgorithm.Vanilla ? 10000 : 10000;
        const int EffectivelyNever = 999999999;
        int? BestResponseEveryMIterations => EffectivelyNever; // For now, don't do it. This takes most of the time when dealing with partial recall games.
        public int NumRandomIterationsForReporting = 10000;
        bool PrintGameTreeAfterReport = false;
        bool PrintInformationSetsAfterReport = false;
        bool PrintNonChanceInformationSetsOnly = true;
        bool AlwaysUseAverageStrategyInReporting = true;

        public List<Strategy> Strategies { get; set; }

        public EvolutionSettings EvolutionSettings { get; set; }

        public GameDefinition GameDefinition { get; set; }

        public IGameFactory GameFactory { get; set; }

        public GamePlayer GamePlayer { get; set; }

        public CurrentExecutionInformation CurrentExecutionInformation { get; set; }

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
        public NWayTreeStorageInternal<ICRMGameState> GameHistoryTree;

        public int NumNonChancePlayers;
        public int NumChancePlayers; // note that chance players MUST be indexed after nonchance players in the player list

        public int NumInitializedGamePaths = 0;

        #region Construction

        public CRMDevelopment()
        {
            Navigation.SetGameStateFn(GetGameState);
        }

        public CRMDevelopment(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition, IGameFactory gameFactory, CurrentExecutionInformation currentExecutionInformation)
        {
            Strategies = existingStrategyState;
            EvolutionSettings = evolutionSettings;
            GameDefinition = gameDefinition;
            GameFactory = gameFactory;
            CurrentExecutionInformation = currentExecutionInformation;
            NumNonChancePlayers = GameDefinition.Players.Count(x => !x.PlayerIsChance);
            NumChancePlayers = GameDefinition.Players.Count(x => x.PlayerIsChance);
        }

        public IStrategiesDeveloper DeepCopy()
        {
            return new CRMDevelopment()
            {
                Strategies = Strategies.Select(x => x.DeepCopy()).ToList(),
                EvolutionSettings = EvolutionSettings.DeepCopy(),
                GameDefinition = GameDefinition,
                GameFactory = GameFactory,
                CurrentExecutionInformation = CurrentExecutionInformation,
                Navigation = Navigation,
                Algorithm = Algorithm,
                LookupApproach = LookupApproach
            };
        }

        #endregion

        #region Initialization

        public void DevelopStrategies()
        {
            Initialize();
            switch (Algorithm)
            {
                case CRMAlgorithm.AverageStrategySampling:
                    SolveAvgStrategySamplingCRM();
                    break;
                case CRMAlgorithm.Probing:
                    SolveProbingCRM();
                    break;
                case CRMAlgorithm.Vanilla:
                    SolveVanillaCRM();
                    break;
                case CRMAlgorithm.PureStrategyFinder:
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

            GamePlayer = new GamePlayer(Strategies, GameFactory, EvolutionSettings.ParallelOptimization, GameDefinition);

            foreach (Strategy s in Strategies)
            {
                s.CreateInformationSetTree(GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.PlayerNumber == s.PlayerInfo.PlayerIndex)?.NumPossibleActions ?? (byte)1);
            }

            if (SkipEveryPermutationInitialization)
                return; // no initialization needed (that's a benefit of using GameHistory -- we can initialize information sets on the fly, which may be much faster than playing every game permutation)

            // Create game trees
            GameHistoryTree = new NWayTreeStorageInternal<ICRMGameState>(null, GameDefinition.DecisionsExecutionOrder.First().NumPossibleActions);

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
                var informationSetHistoryString = informationSetHistory.ToString(); // DEBUG
                historyPoint.SetInformationIfNotSet(Navigation, gameProgress, informationSetHistory);
                historyPoint = historyPoint.GetBranch(Navigation, informationSetHistory.ActionChosen);
                //var actionsToHere = historyPoint.GetActionsToHereString(Navigation);
            }
            historyPoint.SetFinalUtilitiesAtPoint(Navigation, gameProgress);
        }

        public ICRMGameState GetGameState(HistoryPoint historyPoint, HistoryNavigationInfo? navigation = null)
        {
            HistoryNavigationInfo navigationSettings = navigation ?? Navigation;
            var gameState = historyPoint.GetGameStateForCurrentPlayer(navigationSettings);
            if (gameState == null)
            {
                List<byte> actionsSoFar = historyPoint.GetActionsToHere(navigationSettings);
                var DEBUG = historyPoint.HistoryToPoint.GetInformationSetHistoryItems();
                (GameProgress progress, _) = GamePlayer.PlayPath(actionsSoFar, false);
                ProcessInitializedGameProgress(progress);
                NumInitializedGamePaths++; // Note: This may not be exact if we initialize the same game path twice
                gameState = historyPoint.GetGameStateForCurrentPlayer(navigationSettings);
                if (gameState == null)
                    throw new Exception("Internal error.");
            }
            return gameState;
        }

        #endregion

        #region Printing

        public void PrintInformationSets()
        {
            foreach (Strategy s in Strategies)
            {
                if (!s.PlayerInfo.PlayerIsChance || !PrintNonChanceInformationSetsOnly)
                {
                    Debug.WriteLine($"{s.PlayerInfo}");
                    string tree = s.GetInformationSetTreeString();
                    Debug.WriteLine(tree);
                }
            }
        }

        private void PrintInformationSets(NWayTreeStorageInternal<ICRMGameState> informationSetTree)
        {
            throw new NotImplementedException();
        }

        public void PrintGameTree()
        {
            PrintGameTree_Helper(GetStartOfGameHistoryPoint());
        }

        public unsafe double[] PrintGameTree_Helper(HistoryPoint historyPoint)
        {
            ICRMGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            //if (TraceProbingCRM)
            //    TabbedText.WriteLine($"Probe optimizing player {playerBeingOptimized}");
            if (gameStateForCurrentPlayer is CRMFinalUtilities finalUtilities)
            {
                TabbedText.WriteLine($"--> {String.Join(",", finalUtilities.Utilities.Select(x => $"{x:N2}"))}");
                return finalUtilities.Utilities;
            }
            else
            {
                if (gameStateForCurrentPlayer is CRMChanceNodeSettings chanceNodeSettings)
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
                else if (gameStateForCurrentPlayer is CRMInformationSetNodeTally informationSet)
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
            //player.PlaySinglePath("1,1,1,1,2,1,1", inputs); // use this to trace through a single path
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
                    ICRMGameState gameState = GetGameState(historyPoint);
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
            GameHistory gameHistory = new GameHistory();
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
            if (ReportEveryNIterations != null && iteration % ReportEveryNIterations == 0)
            {
                ActionStrategies previous = ActionStrategy;
                bool useRandomPaths = SkipEveryPermutationInitialization || NumInitializedGamePaths > NumRandomIterationsForReporting;
                Debug.WriteLine("");
                Debug.WriteLine(prefaceFn());
                MainReport(useRandomPaths);
                CompareBestResponse(iteration, useRandomPaths);
                if (AlwaysUseAverageStrategyInReporting)
                    ActionStrategy = previous;
                if (PrintGameTreeAfterReport)
                    PrintGameTree();
                if (PrintInformationSetsAfterReport)
                    PrintInformationSets();
            }
        }

        private unsafe void MainReport(bool useRandomPaths)
        {
            Action<GamePlayer> reportGenerator;
            if (useRandomPaths)
            {
                Debug.WriteLine($"Result using {NumRandomIterationsForReporting} randomly chosen paths");
                reportGenerator = GenerateReports_RandomPaths;
            }
            else
            {
                Debug.WriteLine($"Result using all paths");
                reportGenerator = GenerateReports_AllPaths;
            }
            Debug.WriteLine($"{GenerateReports(reportGenerator)}");
            //Debug.WriteLine($"Number initialized game paths: {NumInitializedGamePaths}");
        }

        private unsafe void CompareBestResponse(int iteration, bool useRandomPaths)
        {
            if (BestResponseEveryMIterations != null && iteration % BestResponseEveryMIterations == 0 && BestResponseEveryMIterations != EffectivelyNever)
                for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
                {
                    double bestResponseUtility = CalculateBestResponse(playerBeingOptimized, ActionStrategy);
                    double bestResponseImprovement = bestResponseUtility - UtilityCalculations[playerBeingOptimized].Average();
                    if (!useRandomPaths && bestResponseImprovement < -1E-15)
                        throw new Exception("Best response function worse."); // it can be slightly negative as a result of rounding error or if we are using random paths as a result of sampling error
                    Debug.WriteLine($"Player {playerBeingOptimized} utility with regret matching {UtilityCalculations[playerBeingOptimized].Average()} using best response against regret matching {bestResponseUtility} best response improvement {bestResponseImprovement}");
                }
        }


        public List<GameProgress> GetRandomCompleteGames(GamePlayer player, int numIterations)
        {
            return player.PlayMultipleIterations(null, numIterations, CurrentExecutionInformation.UiInteraction).ToList();
        }

        private void GenerateReports_RandomPaths(GamePlayer player)
        {
            var gameProgresses = GetRandomCompleteGames(player, NumRandomIterationsForReporting);
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
            for (int i = 0; i < NumRandomIterationsForReporting; i++)
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
                Debug.WriteLine($"{item.Key} => {((double)item.Value) / (double)NumRandomIterationsForReporting}");
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
            ICRMGameState gameState = GetGameState(historyPoint);
            CRMActionProbabilities.GetActionProbabilitiesAtHistoryPoint(gameState, actionStrategy, probabilities, numPossibleActions, null, Navigation);
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
            ICRMGameState gameState = historyPoint.GetGameStateForCurrentPlayer(Navigation);
            if (gameState is CRMFinalUtilities finalUtilities)
            {
                for (byte p = 0; p < NumNonChancePlayers; p++)
                    cumulated[p] += finalUtilities.Utilities[p] * prob;
            }
            else if (gameState is CRMChanceNodeSettings chanceNode)
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
            else if (gameState is CRMInformationSetNodeTally nodeTally)
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

        public string GenerateReports(Action<GamePlayer> generator)
        {
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, GetGameState);
            StringBuilder sb = new StringBuilder();
            var simpleReportDefinitions = GameDefinition.GetSimpleReportDefinitions();
            int simpleReportDefinitionsCount = simpleReportDefinitions.Count();
            ReportsBeingGenerated = new SimpleReport[simpleReportDefinitionsCount];
            for (int i = 0; i < simpleReportDefinitionsCount; i++)
                ReportsBeingGenerated[i] = new SimpleReport(simpleReportDefinitions[i], simpleReportDefinitions[i].DivideColumnFiltersByImmediatelyEarlierReport ? ReportsBeingGenerated[i - 1] : null);
            generator(GamePlayer);
            for (int i = 0; i < simpleReportDefinitionsCount; i++)
                ReportsBeingGenerated[i].GetReport(sb, false);
            ReportsBeingGenerated = null;
            return sb.ToString();
        }

        StatCollector[] UtilityCalculations;

        private void GenerateReports_AllPaths(GamePlayer player)
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

        #region Best response function

        // Based on Algorithm 9 in the Lanctot thesis. Since we won't be calculating best response much, adopting more efficient approaches probably isn't necessary.
        
        bool TraceGEBR = false;
        List<byte> TraceGEBR_SkipDecisions = new List<byte>() { };

        /// <summary>
        /// This calculates the best response for a player, and it also sets up the information set so that we can then play that best response strategy.
        /// </summary>
        /// <param name="playerIndex"></param>
        /// <returns></returns>
        public double CalculateBestResponse(byte playerIndex, ActionStrategies opponentsActionStrategy)
        {
            HashSet<byte> depthsOfPlayerDecisions = new HashSet<byte>();
            GEBRPass1(GetStartOfGameHistoryPoint(), playerIndex, 1, depthsOfPlayerDecisions); // setup counting first decision as depth 1
            List<byte> depthsOrdered = depthsOfPlayerDecisions.OrderByDescending(x => x).ToList();
            depthsOrdered.Add(0); // last depth to play should return outcome
            double bestResponseUtility = 0;
            foreach (byte depthToTarget in depthsOrdered)
            {
                if (TraceGEBR)
                {
                    TabbedText.WriteLine($"Optimizing {playerIndex} depthToTarget {depthToTarget}: ");
                    TabbedText.Tabs++;
                }
                bestResponseUtility = GEBRPass2(GetStartOfGameHistoryPoint(), playerIndex, depthToTarget, 1, 1.0, opponentsActionStrategy);
                if (TraceGEBR)
                    TabbedText.Tabs--;
            }
            return bestResponseUtility;
        }

        public void GEBRPass1(HistoryPoint historyPoint, byte playerIndex, byte depth, HashSet<byte> depthOfPlayerDecisions)
        {
            if (historyPoint.IsComplete(Navigation))
                return;
            else
            {
                byte numPossibleActions;
                ICRMGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
                if (gameStateForCurrentPlayer is CRMChanceNodeSettings chanceNodeSettings)
                {
                    numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
                }
                else if (gameStateForCurrentPlayer is CRMInformationSetNodeTally informationSet)
                {
                    byte decisionNum = informationSet.DecisionIndex;
                    numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
                    byte playerMakingDecision = informationSet.PlayerIndex;
                    if (playerMakingDecision == playerIndex)
                    {
                        if (!depthOfPlayerDecisions.Contains(depth))
                            depthOfPlayerDecisions.Add(depth);
                        informationSet.ResetBestResponseData();
                    }
                }
                else
                    throw new Exception("Unexpected game state type.");
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    var nextHistory = historyPoint.GetBranch(Navigation, action);
                    GEBRPass1(nextHistory, playerIndex, (byte)(depth + 1), depthOfPlayerDecisions);
                }
            }
        }

        public double GEBRPass2(HistoryPoint historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            if (historyPoint.IsComplete(Navigation))
                return GetUtilityFromTerminalHistory(historyPoint, playerIndex);
            else
            {
                ICRMGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
                if (gameStateForCurrentPlayer is CRMChanceNodeSettings)
                    return GEBRPass2_ChanceNode(historyPoint, playerIndex, depthToTarget, depthSoFar, inversePi, opponentsActionStrategy);
            }
            return GEBRPass2_DecisionNode(historyPoint, playerIndex, depthToTarget, depthSoFar, inversePi, opponentsActionStrategy);
        }

        private unsafe double GEBRPass2_DecisionNode(HistoryPoint historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            ICRMGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            var informationSet = (CRMInformationSetNodeTally) gameStateForCurrentPlayer;
            byte decisionIndex = informationSet.DecisionIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionIndex);
            byte playerMakingDecision = informationSet.PlayerIndex;
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
            {
                TabbedText.WriteLine($"Decision {decisionIndex} {GameDefinition.DecisionsExecutionOrder[decisionIndex].Name} playerMakingDecision {playerMakingDecision} information set {informationSet.InformationSetNumber} inversePi {inversePi} depthSoFar {depthSoFar} ");
            }
            if (playerMakingDecision == playerIndex && depthSoFar > depthToTarget)
            {
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                    TabbedText.Tabs++;
                byte action = GameDefinition.DecisionsExecutionOrder[decisionIndex].AlwaysDoAction ?? informationSet.GetBestResponseAction();
                double expectedValue = GEBRPass2(historyPoint.GetBranch(Navigation, action), playerIndex, depthToTarget, (byte)(depthSoFar + 1), inversePi, opponentsActionStrategy);
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"Best response action {action} producing expected value {expectedValue}");
                }
                return expectedValue;
            }
            else
            {
                double* actionProbabilities = stackalloc double[numPossibleActions];
                byte? alwaysDoAction = GameDefinition.DecisionsExecutionOrder[decisionIndex].AlwaysDoAction;
                if (alwaysDoAction != null)
                    CRMActionProbabilities.SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions, actionProbabilities, (byte)alwaysDoAction);
                else if (opponentsActionStrategy == ActionStrategies.RegretMatching)
                    informationSet.GetRegretMatchingProbabilities(actionProbabilities);
                else if (opponentsActionStrategy == ActionStrategies.RegretMatchingWithPruning)
                    informationSet.GetRegretMatchingProbabilities_WithPruning(actionProbabilities);
                else if (opponentsActionStrategy == ActionStrategies.AverageStrategy)
                    informationSet.GetAverageStrategies(actionProbabilities);
                else
                    throw new NotImplementedException();
                double expectedValueSum = 0;
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double nextInversePi = inversePi;
                    if (playerMakingDecision != playerIndex)
                        nextInversePi *= actionProbabilities[action - 1];
                    if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                    {
                        TabbedText.WriteLine($"action {action} for playerMakingDecision {playerMakingDecision}...");
                        TabbedText.Tabs++; 
                    }
                    double expectedValue = GEBRPass2(historyPoint.GetBranch(Navigation, action), playerIndex, depthToTarget, (byte)(depthSoFar + 1), nextInversePi, opponentsActionStrategy);
                    double product = actionProbabilities[action - 1] * expectedValue;
                    if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                    {
                        TabbedText.Tabs--;
                        TabbedText.WriteLine($"... action {action} producing expected value {expectedValue} * probability {actionProbabilities[action - 1]} = product {product}");
                    }
                    if (playerMakingDecision != playerIndex)
                        expectedValueSum += product;
                    else if (playerMakingDecision == playerIndex && depthToTarget == depthSoFar)
                    {
                        informationSet.IncrementBestResponse(action, inversePi, expectedValue);
                        if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                        {
                            TabbedText.WriteLine($"Incrementing best response for information set {informationSet.InformationSetNumber} for action {action} inversePi {inversePi} expectedValue {expectedValue}");
                        }
                    }
                }
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                {
                    if (playerMakingDecision != playerIndex)
                        TabbedText.WriteLine($"Returning from other player node expectedvaluesum {expectedValueSum}");
                    else if (playerMakingDecision == playerIndex && depthToTarget == depthSoFar)
                        TabbedText.WriteLine($"Returning 0 (from own decision not yet fully developed)");
                }
                return expectedValueSum;
            }
        }

        private double GEBRPass2_ChanceNode(HistoryPoint historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            ICRMGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings) gameStateForCurrentPlayer; 
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
            {
                TabbedText.WriteLine($"Num chance actions {numPossibleActions} for decision {chanceNodeSettings.DecisionByteCode} {GameDefinition.DecisionsExecutionOrder[chanceNodeSettings.DecisionByteCode].Name}");
            }
            double expectedValueSum = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
                    TabbedText.WriteLine($"chance action {action} for decision {chanceNodeSettings.DecisionByteCode} {GameDefinition.DecisionsExecutionOrder[chanceNodeSettings.DecisionIndex].Name} ... ");
                double probability = chanceNodeSettings.GetActionProbability(action);
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
                    TabbedText.Tabs++;
                var valueBelow = GEBRPass2(historyPoint.GetBranch(Navigation, action), playerIndex, depthToTarget, (byte)(depthSoFar + 1), inversePi * probability, opponentsActionStrategy);
                double expectedValue = probability * valueBelow;
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"... chance action {action} probability {probability} * valueBelow {valueBelow} = expectedValue {expectedValue}");
                }
                expectedValueSum += expectedValue;
            }
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
            {
                TabbedText.WriteLine($"Chance expected value sum {expectedValueSum}");
            }
            return expectedValueSum;
        }

        #endregion

        #region ProbingCRM

        public unsafe double Probe(HistoryPoint historyPoint, byte playerBeingOptimized)
        {
            ICRMGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            //if (TraceProbingCRM)
            //    TabbedText.WriteLine($"Probe optimizing player {playerBeingOptimized}");
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                CRMFinalUtilities finalUtilities = (CRMFinalUtilities)gameStateForCurrentPlayer;
                var utility = finalUtilities.Utilities[playerBeingOptimized];
                if (TraceProbingCRM)
                    TabbedText.WriteLine($"Utility returned {utility}");
                return utility;
            }
            else
            {
                byte sampledAction = 0;
                if (gameStateType == GameStateTypeEnum.Chance)
                {
                    CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings)gameStateForCurrentPlayer;
                    byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
                    sampledAction = chanceNodeSettings.SampleAction(numPossibleActions, RandomGenerator.NextDouble());
                    if (TraceProbingCRM)
                        TabbedText.WriteLine($"{sampledAction}: Sampled chance action {sampledAction} of {numPossibleActions} with probability {chanceNodeSettings.GetActionProbability(sampledAction)}");
                }
                else if (gameStateType == GameStateTypeEnum.Tally)
                {
                    CRMInformationSetNodeTally informationSet = (CRMInformationSetNodeTally)gameStateForCurrentPlayer;
                    byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
                    double* actionProbabilities = stackalloc double[numPossibleActions];
                    // the use of epsilon-on-policy for early iterations of opponent's strategy is a deviation from Gibson.
                    if (UseEpsilonOnPolicyForOpponent && AvgStrategySamplingCFRIterationNum <= LastOpponentEpsilonIteration)
                        informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(actionProbabilities, CurrentEpsilonValue);
                    else
                        informationSet.GetRegretMatchingProbabilities(actionProbabilities);
                    sampledAction = SampleAction(actionProbabilities, numPossibleActions, RandomGenerator.NextDouble());
                    if (TraceProbingCRM)
                        TabbedText.WriteLine($"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {informationSet.PlayerIndex}");
                }
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction);
                if (TraceProbingCRM)
                    TabbedText.Tabs++;
                double probeResult = Probe(nextHistoryPoint, playerBeingOptimized);
                if (TraceProbingCRM)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"Returning probe result {probeResult}");
                }
                return probeResult;
            }
        }

        public unsafe double Probe_WalkTree(HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ)
        {
            if (TraceProbingCRM)
                TabbedText.WriteLine($"WalkTree sampling probability {samplingProbabilityQ}");
            ICRMGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            byte sampledAction = 0;
            if (gameStateForCurrentPlayer is CRMFinalUtilities finalUtilities)
            {
                var utility = finalUtilities.Utilities[playerBeingOptimized];
                if (TraceProbingCRM)
                    TabbedText.WriteLine($"Utility returned {utility}");
                return utility;
            }
            else if (gameStateForCurrentPlayer is CRMChanceNodeSettings chanceNodeSettings)
            {
                byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
                sampledAction = chanceNodeSettings.SampleAction(numPossibleActions, RandomGenerator.NextDouble());
                if (TraceProbingCRM)
                    TabbedText.WriteLine($"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNodeSettings.DecisionIndex}");
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction);
                if (TraceProbingCRM)
                    TabbedText.Tabs++;
                double walkTreeValue = Probe_WalkTree(nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ);
                if (TraceProbingCRM)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
                }
                return walkTreeValue;
            }
            else if (gameStateForCurrentPlayer is CRMInformationSetNodeTally informationSet)
            {
                byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
                double* sigma_regretMatchedActionProbabilities = stackalloc double[numPossibleActions];
                informationSet.GetRegretMatchingProbabilities(sigma_regretMatchedActionProbabilities);
                byte playerAtPoint = informationSet.PlayerIndex;
                if (playerAtPoint != playerBeingOptimized)
                {
                    // the use of epsilon-on-policy for early iterations of opponent's strategy is a deviation from Gibson.
                    if (UseEpsilonOnPolicyForOpponent && AvgStrategySamplingCFRIterationNum <= LastOpponentEpsilonIteration)
                        informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(sigma_regretMatchedActionProbabilities, CurrentEpsilonValue);
                    else
                        informationSet.GetRegretMatchingProbabilities(sigma_regretMatchedActionProbabilities);
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        double cumulativeStrategyIncrement = sigma_regretMatchedActionProbabilities[action - 1] / samplingProbabilityQ;
                        informationSet.IncrementCumulativeStrategy(action, cumulativeStrategyIncrement);
                        if (TraceProbingCRM)
                            TabbedText.WriteLine($"Incrementing cumulative strategy for {action} by {cumulativeStrategyIncrement} to {informationSet.GetCumulativeStrategy(action)}");
                    }
                    sampledAction = SampleAction(sigma_regretMatchedActionProbabilities, numPossibleActions, RandomGenerator.NextDouble());
                    if (TraceProbingCRM)
                        TabbedText.WriteLine($"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigma_regretMatchedActionProbabilities[sampledAction - 1]}");
                    HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction);
                    if (TraceProbingCRM)
                        TabbedText.Tabs++;
                    double walkTreeValue = Probe_WalkTree(nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ);
                    if (TraceProbingCRM)
                    {
                        TabbedText.Tabs--;
                        TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
                    }
                    return walkTreeValue;
                }
                double* samplingProbabilities = stackalloc double[numPossibleActions];
                const double epsilon = 0.5;
                informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(samplingProbabilities, epsilon);
                sampledAction = SampleAction(samplingProbabilities, numPossibleActions, RandomGenerator.NextDouble());
                if (TraceProbingCRM)
                    TabbedText.WriteLine($"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigma_regretMatchedActionProbabilities[sampledAction - 1]}");
                double* counterfactualValues = stackalloc double[numPossibleActions];
                double summation = 0;
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action);
                    if (action == sampledAction)
                    {
                        if (TraceProbingCRM)
                            TabbedText.WriteLine($"{action}: Sampling selected action {action} for player {informationSet.PlayerIndex} decision {informationSet.DecisionIndex}");
                        if (TraceProbingCRM)
                            TabbedText.Tabs++;
                        double samplingProbabilityQPrime = samplingProbabilityQ * samplingProbabilities[action - 1];
                        counterfactualValues[action - 1] = Probe_WalkTree(nextHistoryPoint, playerBeingOptimized, samplingProbabilityQPrime);
                    }
                    else
                    {
                        if (TraceProbingCRM)
                            TabbedText.WriteLine($"{action}: Probing unselected action {action} for player {informationSet.PlayerIndex}decision {informationSet.DecisionIndex}");
                        if (TraceProbingCRM)
                            TabbedText.Tabs++;
                        counterfactualValues[action - 1] = Probe(nextHistoryPoint, playerBeingOptimized);
                    }
                    double summationDelta = sigma_regretMatchedActionProbabilities[action - 1] * counterfactualValues[action - 1];
                    summation += summationDelta;
                    if (TraceProbingCRM)
                    {
                        TabbedText.Tabs--;
                        TabbedText.WriteLine($"Counterfactual value for action {action} is {counterfactualValues[action - 1]} => increment summation by {sigma_regretMatchedActionProbabilities[action - 1]} * {counterfactualValues[action - 1]} = {summationDelta} to {summation}");
                    }
                }
                double inverseSamplingProbabilityQ = (1.0 / samplingProbabilityQ);
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double cumulativeRegretIncrement = inverseSamplingProbabilityQ * (counterfactualValues[action - 1] - summation);
                    informationSet.IncrementCumulativeRegret(action, cumulativeRegretIncrement);
                    if (TraceProbingCRM)
                    {
                        //TabbedText.WriteLine($"Optimizing {playerBeingOptimized} Iteration {ProbingCFRIterationNum} Actions to here {historyPoint.GetActionsToHereString(Navigation)}");
                        TabbedText.WriteLine($"Increasing cumulative regret for action {action} by {inverseSamplingProbabilityQ} * {(counterfactualValues[action - 1])} - {summation} = {cumulativeRegretIncrement} to {informationSet.GetCumulativeRegret(action)}");
                    }
                }
                return summation;
            }
            else
                throw new NotImplementedException();
        }

        private unsafe byte SampleAction(double* actionProbabilities, byte numPossibleActions, double randomNumber)
        {

            double cumulative = 0;
            byte action = 1;
            do
            {
                cumulative += actionProbabilities[action - 1];
                if (cumulative >= randomNumber || action == numPossibleActions)
                    return action;
                else
                    action++;
            }
            while (true);
        }

        public void ProbingCFRIteration(int iteration)
        {
            CurrentEpsilonValue = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(FirstOpponentEpsilonValue, LastOpponentEpsilonValue, 0.75, (double)iteration / (double)TotalProbingCFRIterations);
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                if (TraceProbingCRM)
                {
                    TabbedText.WriteLine($"Optimize player {playerBeingOptimized}");
                    TabbedText.Tabs++;
                }
                Probe_WalkTree(historyPoint, playerBeingOptimized, 1.0);
                if (TraceProbingCRM)
                    TabbedText.Tabs--;
            }
        }

        int ProbingCFRIterationNum;
        public unsafe void SolveProbingCRM()
        {
            Stopwatch s = new Stopwatch();
            if (NumNonChancePlayers > 2)
                throw new Exception("Internal error. Must implement extra code from Gibson algorithm 2 for more than 2 players.");
            ActionStrategy = ActionStrategies.RegretMatching;
            for (ProbingCFRIterationNum = 0; ProbingCFRIterationNum < TotalProbingCFRIterations; ProbingCFRIterationNum++)
            {
                s.Start();
                ProbingCFRIteration(ProbingCFRIterationNum);
                s.Stop();
                GenerateReports(ProbingCFRIterationNum, () => $"Iteration {ProbingCFRIterationNum} Overall milliseconds per iteration {((s.ElapsedMilliseconds / ((double)(ProbingCFRIterationNum + 1))))}");
            }
        }

        #endregion

        #region Average Strategy Sampling

        // http://papers.nips.cc/paper/4569-efficient-monte-carlo-counterfactual-regret-minimization-in-games-with-many-player-actions.pdf

        double epsilon = 0.05, beta = 1000000, tau = 1000; // note that beta will keep sampling even at first, but becomes less important later on. Epsilon ensures some exploration, and tau weights things later on toward the best strategies


        public unsafe double AverageStrategySampling_WalkTree(HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ)
        {
            if (TraceAverageStrategySampling)
                TabbedText.WriteLine($"WalkTree sampling probability {samplingProbabilityQ}");
            ICRMGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            byte sampledAction = 0;
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                CRMFinalUtilities finalUtilities = (CRMFinalUtilities)gameStateForCurrentPlayer;
                var utility = finalUtilities.Utilities[playerBeingOptimized];
                if (TraceAverageStrategySampling)
                    TabbedText.WriteLine($"Utility returned {utility}");
                return utility / samplingProbabilityQ;
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings)gameStateForCurrentPlayer;
                byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
                sampledAction = chanceNodeSettings.SampleAction(numPossibleActions, RandomGenerator.NextDouble());
                if (TraceAverageStrategySampling)
                    TabbedText.WriteLine($"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNodeSettings.DecisionIndex}");
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction);
                if (TraceAverageStrategySampling)
                    TabbedText.Tabs++;
                double walkTreeValue = AverageStrategySampling_WalkTree(nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ);
                if (TraceAverageStrategySampling)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
                }
                return walkTreeValue;
            }
            else if (gameStateType == GameStateTypeEnum.Tally)
            {
                CRMInformationSetNodeTally informationSet = (CRMInformationSetNodeTally)gameStateForCurrentPlayer;
                byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
                double* sigma_regretMatchedActionProbabilities = stackalloc double[numPossibleActions];
                // the use of epsilon-on-policy for early iterations of opponent's strategy is a deviation from Gibson.
                if (UseEpsilonOnPolicyForOpponent && AvgStrategySamplingCFRIterationNum <= LastOpponentEpsilonIteration)
                    informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(sigma_regretMatchedActionProbabilities, CurrentEpsilonValue);
                else
                    informationSet.GetRegretMatchingProbabilities(sigma_regretMatchedActionProbabilities);
                byte playerAtPoint = informationSet.PlayerIndex;
                if (playerAtPoint != playerBeingOptimized)
                {
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        double cumulativeStrategyIncrement = sigma_regretMatchedActionProbabilities[action - 1] / samplingProbabilityQ;
                        if (EvolutionSettings.ParallelOptimization)
                            informationSet.IncrementCumulativeStrategy_Parallel(action, cumulativeStrategyIncrement);
                        else
                            informationSet.IncrementCumulativeStrategy(action, cumulativeStrategyIncrement);
                        if (TraceProbingCRM)
                            TabbedText.WriteLine($"Incrementing cumulative strategy for {action} by {cumulativeStrategyIncrement} to {informationSet.GetCumulativeStrategy(action)}");
                    }
                    sampledAction = SampleAction(sigma_regretMatchedActionProbabilities, numPossibleActions, RandomGenerator.NextDouble());
                    if (TraceAverageStrategySampling)
                        TabbedText.WriteLine($"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigma_regretMatchedActionProbabilities[sampledAction - 1]}");
                    HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction);
                    if (TraceAverageStrategySampling)
                        TabbedText.Tabs++;
                    double walkTreeValue = AverageStrategySampling_WalkTree(nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ);
                    if (TraceAverageStrategySampling)
                    {
                        TabbedText.Tabs--;
                        TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
                    }
                    return walkTreeValue;
                }
                // player being optimized is player at this information set
                double sumCumulativeStrategies = 0;
                double* cumulativeStrategies = stackalloc double[numPossibleActions];
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double cumulativeStrategy = informationSet.GetCumulativeStrategy(action);
                    cumulativeStrategies[action - 1] = cumulativeStrategy;
                    sumCumulativeStrategies += cumulativeStrategy;
                }
                double* counterfactualValues = stackalloc double[numPossibleActions];
                double counterfactualSummation = 0;
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    // Note that we may sample multiple actions here.
                    double rho = Math.Max(epsilon, (beta + tau * cumulativeStrategies[action - 1]) / (beta + sumCumulativeStrategies));
                    double rnd = RandomGenerator.NextDouble();
                    bool explore = rnd < rho;
                    if (TraceAverageStrategySampling)
                    {
                        TabbedText.WriteLine($"action {action}: {(explore ? "Explore" : "Do not explore")} rnd: {rnd} rho: {rho}");
                    }
                    if (explore)
                    {
                        if (TraceAverageStrategySampling)
                            TabbedText.Tabs++;
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action);
                        counterfactualValues[action - 1] = AverageStrategySampling_WalkTree(nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ * Math.Min(1.0, rho));
                        counterfactualSummation += sigma_regretMatchedActionProbabilities[action - 1] * counterfactualValues[action - 1];
                        if (TraceAverageStrategySampling)
                            TabbedText.Tabs--;
                    }
                    else
                        counterfactualValues[action - 1] = 0;
                }
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double cumulativeRegretIncrement = counterfactualValues[action - 1] - counterfactualSummation;
                    if (EvolutionSettings.ParallelOptimization)
                        informationSet.IncrementCumulativeRegret_Parallel(action, cumulativeRegretIncrement);
                    else
                        informationSet.IncrementCumulativeRegret(action, cumulativeRegretIncrement);
                    if (TraceAverageStrategySampling)
                    {
                        // v(a) is set to 0 for many of the a E A(I).The key to understanding this is that we're multiplying the utilities we get back by 1/q. So if there is a 1/3 probability of sampling, then we multiply the utility by 3. So, when we don't sample, we're adding 0 to the regrets; and when we sample, we're adding 3 * counterfactual value.Thus, v(a) is an unbiased predictor of value.Meanwhile, we're always subtracting the regret-matched probability-adjusted counterfactual values. 
                        TabbedText.WriteLine($"Increasing cumulative regret for action {action} by {(counterfactualValues[action - 1])} - {counterfactualSummation} = {cumulativeRegretIncrement} to {informationSet.GetCumulativeRegret(action)}");
                    }
                }
                if (TraceAverageStrategySampling)
                {
                    TabbedText.WriteLine($"Returning {counterfactualSummation}");
                }
                return counterfactualSummation;
            }
            else
                throw new NotImplementedException();
        }
        
        public void AvgStrategySamplingCFRIteration(int iteration)
        {
            CurrentEpsilonValue = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(FirstOpponentEpsilonValue, LastOpponentEpsilonValue, 0.75, (double)iteration / (double)TotalAvgStrategySamplingCFRIterations);
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                if (TraceAverageStrategySampling)
                {
                    TabbedText.WriteLine($"Optimize player {playerBeingOptimized}");
                    TabbedText.Tabs++;
                }
                AverageStrategySampling_WalkTree(historyPoint, playerBeingOptimized, 1.0);
                if (TraceAverageStrategySampling)
                    TabbedText.Tabs--;
            }
        }

        int AvgStrategySamplingCFRIterationNum;
        public unsafe void SolveAvgStrategySamplingCRM()
        {
            if (NumNonChancePlayers > 2)
                throw new Exception("Internal error. Must implement extra code from Gibson algorithm 2 for more than 2 players.");
            ActionStrategy = ActionStrategies.RegretMatching;
            AvgStrategySamplingCFRIterationNum = -1;
            int reportingGroupSize = ReportEveryNIterations ?? TotalAvgStrategySamplingCFRIterations;
            Stopwatch s = new Stopwatch();
            for (int iterationGrouper = 0; iterationGrouper < TotalAvgStrategySamplingCFRIterations; iterationGrouper += reportingGroupSize)
            {
                s.Reset();
                s.Start();
                Parallelizer.Go(EvolutionSettings.ParallelOptimization, iterationGrouper, iterationGrouper + reportingGroupSize, i =>
                {
                    Interlocked.Increment(ref AvgStrategySamplingCFRIterationNum);
                    AvgStrategySamplingCFRIteration(AvgStrategySamplingCFRIterationNum);
                });
                s.Stop();
                GenerateReports(iterationGrouper, () => $"Iteration {iterationGrouper} Milliseconds per iteration {((s.ElapsedMilliseconds / ((double)reportingGroupSize)))}");
            }
            //for (AvgStrategySamplingCFRIterationNum = 0; AvgStrategySamplingCFRIterationNum < TotalAvgStrategySamplingCFRIterations; AvgStrategySamplingCFRIterationNum++)
            //{
            //    AvgStrategySamplingCFRIteration(AvgStrategySamplingCFRIterationNum);
            //}
        }
        #endregion

        #region Vanilla CRM

        /// <summary>
        /// Performs an iteration of vanilla counterfactual regret minimization.
        /// </summary>
        /// <param name="historyPoint">The game tree, pointing to the particular point in the game where we are located</param>
        /// <param name="playerBeingOptimized">0 for first player, etc. Note that this corresponds in Lanctot to 1, 2, etc. We are using zero-basing for player index (even though we are 1-basing actions).</param>
        /// <returns></returns>
        public unsafe double VanillaCRM(HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues, bool usePruning)
        {
            if (usePruning)
            {
                bool allZero = true;
                for (int i = 0; i < NumNonChancePlayers; i++)
                    if (*(piValues + i) != 0)
                    {
                        allZero = false;
                        break;
                    }
                if (allZero)
                    return 0; // this is zero probability, so the result doesn't matter
            }
            ICRMGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                CRMFinalUtilities finalUtilities = (CRMFinalUtilities)gameStateForCurrentPlayer;
                return finalUtilities.Utilities[playerBeingOptimized];
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings)gameStateForCurrentPlayer;
                return VanillaCRM_ChanceNode(historyPoint, playerBeingOptimized, piValues, usePruning);
            }
            else
                return VanillaCRM_DecisionNode(historyPoint, playerBeingOptimized, piValues, usePruning);
        }
        
        private unsafe double VanillaCRM_DecisionNode(HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues, bool usePruning)
        {
            double* nextPiValues = stackalloc double[MaxNumPlayers];
            //var actionsToHere = historyPoint.GetActionsToHere(Navigation);
            //var historyPointString = historyPoint.ToString();

            ICRMGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            var informationSet = (CRMInformationSetNodeTally) gameStateForCurrentPlayer;
            byte decisionNum = informationSet.DecisionIndex;
            byte playerMakingDecision = informationSet.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            // todo: stackalloc or use simple stack based array pool http://stackoverflow.com/questions/1123939/is-c-sharp-compiler-deciding-to-use-stackalloc-by-itself
            double* actionProbabilities = stackalloc double[numPossibleActions];
            byte? alwaysDoAction = GameDefinition.DecisionsExecutionOrder[decisionNum].AlwaysDoAction;
            if (alwaysDoAction == null && !usePruning)
                informationSet.GetRegretMatchingProbabilities(actionProbabilities);
            else if (alwaysDoAction == null && usePruning)
                informationSet.GetRegretMatchingProbabilities_WithPruning(actionProbabilities);
            else
                CRMActionProbabilities.SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions, actionProbabilities, (byte) alwaysDoAction);
            double* expectedValueOfAction = stackalloc double[numPossibleActions];
            double expectedValue = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                double probabilityOfAction = actionProbabilities[action - 1];
                GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false, nextPiValues); // reduce probability associated with player being optimized, without changing probabilities for other players
                if (TraceVanillaCRM)
                {
                    TabbedText.WriteLine($"decisionNum {decisionNum} optimizing player {playerBeingOptimized}  own decision {playerMakingDecision == playerBeingOptimized} action {action} probability {probabilityOfAction} ...");
                    TabbedText.Tabs++;
                }
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action);
                expectedValueOfAction[action - 1] = VanillaCRM(nextHistoryPoint, playerBeingOptimized, nextPiValues, usePruning);
                expectedValue += probabilityOfAction * expectedValueOfAction[action - 1];

                if (TraceVanillaCRM)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"... action {action} expected value {expectedValueOfAction[action - 1]} cum expected value {expectedValue}");
                }
            }
            if (playerMakingDecision == playerBeingOptimized)
            {
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double inversePi = GetInversePiValue(piValues, playerBeingOptimized);
                    double pi = piValues[playerBeingOptimized];
                    var regret = (expectedValueOfAction[action - 1] - expectedValue);
                    informationSet.IncrementCumulativeRegret(action, inversePi * regret);
                    informationSet.IncrementCumulativeStrategy(action, pi * actionProbabilities[action - 1]);
                    if (TraceVanillaCRM)
                    {
                        TabbedText.WriteLine($"PiValues {piValues[0]} {piValues[1]}");
                        TabbedText.WriteLine($"Regrets: Action {action} regret {regret} prob-adjust {inversePi * regret} new regret {informationSet.GetCumulativeRegret(action)} strategy inc {pi * actionProbabilities[action - 1]} new cum strategy {informationSet.GetCumulativeStrategy(action)}");
                    }
                }
            }
            return expectedValue;
        }

        private unsafe double VanillaCRM_ChanceNode(HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues, bool usePruning)
        {
            double* equalProbabilityNextPiValues = stackalloc double[MaxNumPlayers];
            ICRMGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings) gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            bool equalProbabilities = chanceNodeSettings.AllProbabilitiesEqual();
            if (equalProbabilities) // can set next probabilities once for all actions
                GetNextPiValues(piValues, playerBeingOptimized, chanceNodeSettings.GetActionProbability(1), true, equalProbabilityNextPiValues);
            else
                equalProbabilityNextPiValues = null;
            double expectedValue = 0;
            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, EvolutionSettings.MaxParallelDepth, 1, (byte)(numPossibleActions + 1),
                action =>
                {
                    //double* piValuesToPass = stackalloc double[MaxNumPlayers];
                    //for (int i = 0; i < MaxNumPlayers; i++)
                    //    *(piValuesToPass + i) = *(piValues + i);
                    //double* equalProbabilityPiValuesToPass = stackalloc double[MaxNumPlayers];
                    //if (equalProbabilityNextPiValues == null)
                    //    equalProbabilityPiValuesToPass = null;
                    //else
                    //    for (int i = 0; i < MaxNumPlayers; i++)
                    //        *(equalProbabilityPiValuesToPass + i) = *(equalProbabilityNextPiValues + i);
                    double probabilityAdjustedExpectedValueParticularAction = VanillaCRM_ChanceNode_NextAction(historyPoint, playerBeingOptimized, piValues, chanceNodeSettings, equalProbabilityNextPiValues, expectedValue, action, usePruning);
                    expectedValue += probabilityAdjustedExpectedValueParticularAction;
                });

            return expectedValue;
        }

        private unsafe double VanillaCRM_ChanceNode_NextAction(HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues, CRMChanceNodeSettings chanceNodeSettings, double* equalProbabilityNextPiValues, double expectedValue, byte action, bool usePruning)
        {
            double* nextPiValues = stackalloc double[MaxNumPlayers];
            if (equalProbabilityNextPiValues != null)
            {
                double* locTarget = nextPiValues;
                double* locSource = equalProbabilityNextPiValues;
                for (int i = 0; i < NumNonChancePlayers; i++)
                {
                    (*locTarget) = (*locSource);
                    locTarget++;
                    locSource++;
                }
            }
            else // must set probability separately for each action we take
                GetNextPiValues(piValues, playerBeingOptimized, chanceNodeSettings.GetActionProbability(action), true, nextPiValues);
            double actionProbability = chanceNodeSettings.GetActionProbability(action);
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action);
            if (TraceVanillaCRM)
            {
                TabbedText.WriteLine($"Chance decisionNum {chanceNodeSettings.DecisionByteCode} action {action} probability {actionProbability} ...");
                TabbedText.Tabs++;
            }
            double expectedValueParticularAction = VanillaCRM(nextHistoryPoint, playerBeingOptimized, nextPiValues, usePruning);
            var probabilityAdjustedExpectedValueParticularAction = actionProbability * expectedValueParticularAction;
            if (TraceVanillaCRM)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"... action {action} expected value {expectedValueParticularAction} cum expected value {expectedValue}");
            }

            return probabilityAdjustedExpectedValueParticularAction;
        }


        public unsafe void SolveVanillaCRM()
        {
            for (int iteration = 0; iteration < TotalVanillaCFRIterations; iteration++)
            {
                VanillaCFRIteration(iteration); 
            }
        }

        unsafe void VanillaCFRIteration(int iteration)
        {

            Stopwatch s = new Stopwatch();
            double[] lastUtilities = new double[NumNonChancePlayers];

            bool usePruning = iteration >= 100;
            ActionStrategy = usePruning ? ActionStrategies.RegretMatchingWithPruning : ActionStrategies.RegretMatching;
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                double* initialPiValues = stackalloc double[MaxNumPlayers];
                GetInitialPiValues(initialPiValues);
                if (TraceVanillaCRM)
                    TabbedText.WriteLine($"Iteration {iteration} Player {playerBeingOptimized}");
                s.Start();
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                lastUtilities[playerBeingOptimized] = VanillaCRM(historyPoint, playerBeingOptimized, initialPiValues, usePruning);
                s.Stop();
            }
            
            GenerateReports(iteration, () => $"Iteration {iteration} Overall milliseconds per iteration {((s.ElapsedMilliseconds / ((double)iteration + 1.0)))}");
        }


        #endregion

        #region Pure strategy finder

        // Find pure equilibria:
        // 1. Fully initialize game tree
        // 2. For (P, D), enumerate information sets. Define a global strategy index that specifies a pure strategy in each information set for each player. 
        // 3. For each pair of information sets, set the pure strategy for each player, playing all chance strategies. Record the average resulting utilities.
        // 4. Using the matrix, eliminated dominated strategies. That is, for each column A, look to see if there is another column B that is always at least as good for column player. If so, eliminate A. Do same for rows (for row player). Repeat until a cycle produces no changes.

        public void FindPureStrategies()
        {
            if (NumNonChancePlayers != 2)
                throw new NotImplementedException();
            List<(CRMInformationSetNodeTally, int)> player0InformationSets = Strategies[0].GetTallyNodes(GameDefinition);
            List<(CRMInformationSetNodeTally, int)> player1InformationSets = Strategies[1].GetTallyNodes(GameDefinition);
            int player0Permutations, player1Permutations;
            double[,] player0Utilities, player1Utilities;
            GetUtilitiesForStrategyCombinations(player0InformationSets, player1InformationSets, out player0Permutations, out player1Permutations, out player0Utilities, out player1Utilities);
            bool[] player0StrategyEliminated = new bool[player0Permutations];
            bool[] player1StrategyEliminated = new bool[player1Permutations];
            EliminateDominatedStrategies(player0Permutations, player1Permutations, player0Utilities, player1Utilities, player0StrategyEliminated, player1StrategyEliminated);
            List<(int player0Strategy, int player1Strategy)> nashEquilibria = NarrowToNashEquilibria(player0Permutations, player1Permutations, player0Utilities, player1Utilities, player0StrategyEliminated, player1StrategyEliminated, true);
            PrintAllEquilibriumStrategies(player0InformationSets, player1InformationSets, player0Permutations, player1Permutations, nashEquilibria);
        }

        private void PrintMatrix(double[,] arr)
        {
            int rowLength = arr.GetLength(0);
            int colLength = arr.GetLength(1);

            for (int i = 0; i < rowLength; i++)
            {
                for (int j = 0; j < colLength; j++)
                {
                    Debug.Write(string.Format("{0:N2} ", arr[i, j]));
                }
                Debug.Write(Environment.NewLine + Environment.NewLine);
            }
        }

        private void PrintAllEquilibriumStrategies(List<(CRMInformationSetNodeTally, int)> player0InformationSets, List<(CRMInformationSetNodeTally, int)> player1InformationSets, int player0Permutations, int player1Permutations, List<(int player0Strategy, int player1Strategy)> nashEquilibria)
        {
            int numPrinted = 0;
            for (int player0StrategyIndex = 0; player0StrategyIndex < player0Permutations; player0StrategyIndex++)
            {
                SetPureStrategyBasedOnIndex(player0InformationSets, player0StrategyIndex, player0Permutations);
                for (int player1StrategyIndex = 0; player1StrategyIndex < player1Permutations; player1StrategyIndex++)
                {
                    if (nashEquilibria.Any(x => x.player0Strategy == player0StrategyIndex && x.player1Strategy == player1StrategyIndex))
                    {
                        SetPureStrategyBasedOnIndex(player1InformationSets, player1StrategyIndex, player1Permutations);
                        Debug.WriteLine($"Player0StrategyIndex {player0StrategyIndex} Player1StrategyIndex {player1StrategyIndex}");
                        GenerateReports(0, () => "");
                        //PrintGameTree();
                        Debug.WriteLine("");
                        numPrinted++;
                    }
                }
            }
            Debug.WriteLine($"Total equilibria: {numPrinted}");
        }

        private void GetUtilitiesForStrategyCombinations(List<(CRMInformationSetNodeTally, int)> player0InformationSets, List<(CRMInformationSetNodeTally, int)> player1InformationSets, out int player0Permutations, out int player1Permutations, out double[,] player0Utilities, out double[,] player1Utilities)
        {
            long p0P = player0InformationSets.Aggregate(1L, (acc, val) => acc * (long)val.Item2);
            long p1P = player1InformationSets.Aggregate(1L, (acc, val) => acc * (long)val.Item2);
            if (p0P * p1P > 10000000)
                throw new Exception("Too many combinations.");
            player0Permutations = (int)p0P;
            player1Permutations = (int)p1P;
            player0Utilities = new double[player0Permutations, player1Permutations];
            player1Utilities = new double[player0Permutations, player1Permutations];
            for (int player0StrategyIndex = 0; player0StrategyIndex < player0Permutations; player0StrategyIndex++)
            {
                SetPureStrategyBasedOnIndex(player0InformationSets, player0StrategyIndex, player0Permutations);
                for (int player1StrategyIndex = 0; player1StrategyIndex < player1Permutations; player1StrategyIndex++)
                {
                    SetPureStrategyBasedOnIndex(player1InformationSets, player1StrategyIndex, player1Permutations);
                    double[] utils = GetAverageUtilities();
                    player0Utilities[player0StrategyIndex, player1StrategyIndex] = utils[0];
                    player1Utilities[player0StrategyIndex, player1StrategyIndex] = utils[1];
                }
            }
        }

        private static void EliminateDominatedStrategies(int player0Permutations, int player1Permutations, double[,] player0Utilities, double[,] player1Utilities, bool[] player0StrategyEliminated, bool[] player1StrategyEliminated)
        {
            bool atLeastOneEliminated = true;
            while (atLeastOneEliminated)
            {
                atLeastOneEliminated = EliminateDominatedStrategies(player0Permutations, player1Permutations, (player0Index, player1Index) => player0Utilities[player0Index, player1Index], player0StrategyEliminated, player1StrategyEliminated);
                atLeastOneEliminated = atLeastOneEliminated | EliminateDominatedStrategies(player1Permutations, player0Permutations, (player1Index, player0Index) => player1Utilities[player0Index, player1Index], player1StrategyEliminated, player0StrategyEliminated);
            }
        }

        private static bool EliminateDominatedStrategies(int thisPlayerPermutations, int otherPlayerPermutations, Func<int, int, double> getUtilityFn, bool[] thisPlayerStrategyEliminated, bool[] otherPlayerStrategyEliminated)
        {
            const bool requireStrictDominance = true;
            bool atLeastOneEliminated = false;
            // compare pairs of strategies by this player to see if one dominates the other
            for (int thisPlayerStrategyIndex1 = 0; thisPlayerStrategyIndex1 < thisPlayerPermutations; thisPlayerStrategyIndex1++)
                for (int thisPlayerStrategyIndex2 = 0; thisPlayerStrategyIndex2 < thisPlayerPermutations; thisPlayerStrategyIndex2++)
                {
                    if (thisPlayerStrategyIndex1 == thisPlayerStrategyIndex2 || thisPlayerStrategyEliminated[thisPlayerStrategyIndex1] || thisPlayerStrategyEliminated[thisPlayerStrategyIndex2])
                        continue; // go to next pair to compare
                    bool index1SometimesBetter = false, index2SometimesBetter = false, sometimesEqual = false;
                    for (int opponentStrategyIndex = 0; opponentStrategyIndex < otherPlayerPermutations; opponentStrategyIndex++)
                    {
                        if (otherPlayerStrategyEliminated[opponentStrategyIndex])
                            continue;
                        double thisPlayerStrategyIndex1Utility = getUtilityFn(thisPlayerStrategyIndex1, opponentStrategyIndex);
                        double thisPlayerStrategyIndex2Utility = getUtilityFn(thisPlayerStrategyIndex2, opponentStrategyIndex);
                        if (thisPlayerStrategyIndex1Utility == thisPlayerStrategyIndex2Utility)
                        {
                            sometimesEqual = true;
                            if (requireStrictDominance)
                                break;
                        }
                        if (thisPlayerStrategyIndex1Utility > thisPlayerStrategyIndex2Utility)
                            index1SometimesBetter = true;
                        else
                            index2SometimesBetter = true;
                        if (index1SometimesBetter && index2SometimesBetter)
                            break;
                    }
                    if (requireStrictDominance && sometimesEqual)
                        continue; // we are not eliminating weakly dominant strategies
                    if (index1SometimesBetter && !index2SometimesBetter)
                    {
                        thisPlayerStrategyEliminated[thisPlayerStrategyIndex2] = true;
                        atLeastOneEliminated = true;
                    }
                    else if (!index1SometimesBetter && index2SometimesBetter)
                    {
                        atLeastOneEliminated = true;
                        thisPlayerStrategyEliminated[thisPlayerStrategyIndex1] = true;
                    }
                }
            return atLeastOneEliminated;
        }

        private static List<(int player0Strategy, int player1Strategy)> NarrowToNashEquilibria(int player0Permutations, int player1Permutations, double[,] player0Utilities, double[,] player1Utilities, bool[] player0StrategyEliminated, bool[] player1StrategyEliminated, bool removePayoffDominatedEquilibria)
        {
            // Eliminate any strategy where a player could improve his score by changing strategies.
            List<int> player0Strategies = player0StrategyEliminated.Select((eliminated, index) => new { eliminated, index }).Where(x => !x.eliminated).Select(x => x.index).ToList();
            List<int> player1Strategies = player1StrategyEliminated.Select((eliminated, index) => new { eliminated, index }).Where(x => !x.eliminated).Select(x => x.index).ToList();
            List<(int player0Strategy, int player1Strategy)> candidates = player0Strategies.SelectMany(x => player1Strategies.Select(y => (x, y))).ToList();
            double[] Payoff(int player0Strategy, int player1Strategy) => new double[] {player0Utilities[player0Strategy, player1Strategy], player1Utilities[player0Strategy, player1Strategy] };
            bool IsPayoffDominant
                ((int player0Strategy, int player1Strategy) firstPayoffs, (int player0Strategy, int player1Strategy) secondPayoffs)
            {
                bool atLeastOneBetter = player0Utilities[firstPayoffs.player0Strategy, firstPayoffs.player1Strategy] > player0Utilities[secondPayoffs.player0Strategy, secondPayoffs.player1Strategy] || player1Utilities[firstPayoffs.player0Strategy, firstPayoffs.player1Strategy] > player1Utilities[secondPayoffs.player0Strategy, secondPayoffs.player1Strategy];
                bool neitherWorse = player0Utilities[firstPayoffs.player0Strategy, firstPayoffs.player1Strategy] >= player0Utilities[secondPayoffs.player0Strategy, secondPayoffs.player1Strategy] && player1Utilities[firstPayoffs.player0Strategy, firstPayoffs.player1Strategy] >= player1Utilities[secondPayoffs.player0Strategy, secondPayoffs.player1Strategy];
                return atLeastOneBetter && neitherWorse;
            }
            bool Player0WillChangeStrategy((int player0Strategy, int player1Strategy) candidate)
            {
                return player0Strategies.Any(p0 => p0 != candidate.player0Strategy && player0Utilities[p0, candidate.player1Strategy] > player0Utilities[candidate.player0Strategy, candidate.player1Strategy]);
            };
            bool Player1WillChangeStrategy((int player0Strategy, int player1Strategy) candidate)
            {
                return player1Strategies.Any(p1 => p1 != candidate.player1Strategy && player1Utilities[candidate.player0Strategy, p1] > player1Utilities[candidate.player0Strategy, candidate.player1Strategy]);
            };
            var nashEquilibria = candidates.Where(x => !Player0WillChangeStrategy(x) && !Player1WillChangeStrategy(x)).ToList();
            if (removePayoffDominatedEquilibria)
                nashEquilibria = nashEquilibria.Where(x => !nashEquilibria.Any(y => IsPayoffDominant(y, x))).ToList();
            return nashEquilibria;
        }

        private void SetPureStrategyBasedOnIndex(List<(CRMInformationSetNodeTally tally, int numPossible)> tallies, int strategyIndex, int totalStrategyPermutations)
        {
            int cumulative = 1;
            foreach (var tally in tallies)
            {
                cumulative *= tally.numPossible;
                int q = totalStrategyPermutations / cumulative;
                int indexForThisDecision = 0;
                while (strategyIndex >= q)
                {
                    strategyIndex -= q;
                    indexForThisDecision++;
                }
                byte action = (byte)(indexForThisDecision + 1);
                tally.tally.SetActionToCertainty(action, (byte) tally.numPossible);
            }
        }

        #endregion
    }
}
