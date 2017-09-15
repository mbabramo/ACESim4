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

        // Note: The ActionStrategy selected does not affect the learning process. It affects reporting after learning, including the calculation of best response scores. Convergence bounds guarantees depend on all players' using the AverageStrategies ActionStrategy. It may seem surprising that we can have convergence guarantees when a player is using the average strategy, thus continuing to make what appears to be some mistakes from the past. But the key is that the other players are also using their average strategies. Thus, even if new information has changed the best move for a player under current circumstances, the player will be competing against a player that continues to employ some of the old strategies. In other words, the opponents' average strategy changes extremely slowly, and the no-regret learning convergence guarantees at a single information set are based on this concept of the player and the opponent playing their average strategies. But the average strategy is not the strategy that the player has "learned." 

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

        public int NumInitializedGamePaths = 0; // Because of how EnumerateIfNotRedundant works, this will be much higher for parallel implementations

        #endregion

        #region Construction

        public CounterfactualRegretMaximization()
        {
            Navigation.SetGameStateFunction(GetGameState);
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

        public string DevelopStrategies()
        {
            string report = null;
            Initialize();
            switch (EvolutionSettings.Algorithm)
            {
                case GameApproximationAlgorithm.AverageStrategySampling:
                    SolveAvgStrategySamplingCFR();
                    break;
                case GameApproximationAlgorithm.GibsonProbing:
                    report = SolveGibsonProbingCFR();
                    break;
                case GameApproximationAlgorithm.AbramowiczProbing:
                    report = SolveAbramowiczProbingCFR();
                    break;
                case GameApproximationAlgorithm.Vanilla:
                    report = SolveVanillaCFR();
                    break;
                case GameApproximationAlgorithm.PureStrategyFinder:
                    FindPureStrategies();
                    break;
                default:
                    throw new NotImplementedException();
            }
            return report;
        }

        public unsafe void Initialize()
        {
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, GetGameState);
            foreach (Strategy strategy in Strategies)
                strategy.Navigation = Navigation;

            GamePlayer = new GamePlayer(Strategies, EvolutionSettings.ParallelOptimization, GameDefinition);

            foreach (Strategy s in Strategies)
            {
                s.CreateInformationSetTree(GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.PlayerNumber == s.PlayerInfo.PlayerIndex)?.NumPossibleActions ?? (byte)1, s.PlayerInfo.PlayerIndex <= NumNonChancePlayers || s.PlayerInfo.PlayerIndex == GameDefinition.PlayerIndex_ResolutionPlayer);
            }

            if (SkipEveryPermutationInitialization)
                return; // no initialization needed (that's a benefit of using GameHistory -- we can initialize information sets on the fly, which may be much faster than playing every game permutation)

            // Create game trees
            GameHistoryTree = new NWayTreeStorageInternal<IGameState>(null, GameDefinition.DecisionsExecutionOrder.First().NumPossibleActions);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            NumInitializedGamePaths = GamePlayer.PlayAllPaths(ProcessInitializedGameProgress);
            stopwatch.Stop();
            Console.WriteLine($"Initialized. Total paths (higher number in parallel): {NumInitializedGamePaths} Total initialization milliseconds {stopwatch.ElapsedMilliseconds}");
            PrintSameGameResults();
        }

        unsafe void ProcessInitializedGameProgress(GameProgress gameProgress)
        {
            
            // First, add the utilities at the end of the tree for this path.
            byte* actions = stackalloc byte[GameFullHistory.MaxNumActions];
            gameProgress.GameFullHistory.GetActions(actions);
            //var actionsAsList = ListExtensions.GetPointerAsList_255Terminated(actions);

            // Go through each non-chance decision point on this path and make sure that the information set tree extends there. We then store the regrets etc. at these points. 

            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            IEnumerable<InformationSetHistory> informationSetHistories = gameProgress.GetInformationSetHistoryItems();
            //GameProgressLogger.Log(() => "Processing information set histories");
            //if (GameProgressLogger.LoggingOn)
            //{
            //    GameProgressLogger.Tabs++;
            //}
            int i = 1;
            foreach (var informationSetHistory in informationSetHistories)
            {
                GameProgressLogger.Log(() => $"Setting information set point based on player's information set: {informationSetHistory}");
                GameProgressLogger.Tabs++;
                //var informationSetHistoryString = informationSetHistory.ToString();
                historyPoint.SetInformationIfNotSet(Navigation, gameProgress, informationSetHistory);
                var decision = GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
                historyPoint = historyPoint.GetBranch(Navigation, informationSetHistory.ActionChosen, decision, informationSetHistory.DecisionIndex);
                i++;
                GameProgressLogger.Tabs--;
                //GameProgressLogger.Log(() => "Actions processed: " + historyPoint.GetActionsToHereString(Navigation));
                // var actionsToHere = historyPoint.GetActionsToHereString(Navigation); 
            }
            //if (GameProgressLogger.LoggingOn)
            //{
            //    GameProgressLogger.Tabs--;
            //}
            historyPoint.SetFinalUtilitiesAtPoint(Navigation, gameProgress);
            //var checkMatch1 = (FinalUtilities) historyPoint.GetGameStateForCurrentPlayer(Navigation);
            //var checkMatch2 = gameProgress.GetNonChancePlayerUtilities();
            //if (!checkMatch2.SequenceEqual(checkMatch1.Utilities))
            //    throw new Exception(); // could it be that something is not in the resolution set that should be?
            //var playAgain = false;
            //if (playAgain)
            //{
            //    MyGameProgress p = (MyGameProgress) GamePlayer.PlayPathAndStop(historyPoint.GetActionsToHere(Navigation));
            //}
        }

        public IGameState GetGameState(ref HistoryPoint historyPoint, HistoryNavigationInfo? navigation = null)
        {
            HistoryNavigationInfo navigationSettings = navigation ?? Navigation;
            return historyPoint.GetGameStateForCurrentPlayer(navigationSettings) ?? GetGameStateByPlayingUnderlyingGame(ref historyPoint, navigationSettings);
        }

        private unsafe IGameState GetGameStateByPlayingUnderlyingGame(ref HistoryPoint historyPoint, HistoryNavigationInfo navigationSettings)
        {
            IGameState gameState;
            List<byte> actionsSoFar = historyPoint.GetActionsToHere(navigationSettings);
            (GameProgress progress, _) = GamePlayer.PlayPath(actionsSoFar, false);
            ProcessInitializedGameProgress(progress);
            NumInitializedGamePaths++; // Note: This may not be exact if we initialize the same game path twice (e.g., if we are playing in parallel)
            gameState = historyPoint.GetGameStateForCurrentPlayer(navigationSettings);
            if (gameState == null)
                throw new Exception("Internal error. Try using CachedBothMethods to try to identify where there is a problem with information sets.");
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
                    if (EvolutionSettings.RestrictToTheseInformationSets != null)
                    {
                        s.InformationSetTree.WalkTree(node =>
                        {
                            InformationSetNodeTally t = (InformationSetNodeTally) node.StoredValue;
                            if (t != null &&
                                EvolutionSettings.RestrictToTheseInformationSets.Contains(t.InformationSetNumber))
                            {
                                Console.WriteLine($"{t}");
                            }
                        });
                    }
                    else
                    {
                        Console.WriteLine($"{s.PlayerInfo}");
                        string tree = s.GetInformationSetTreeString();
                        Console.WriteLine(tree);
                    }
                }
            }
        }

        private void PrintInformationSets(NWayTreeStorageInternal<IGameState> informationSetTree)
        {
            throw new NotImplementedException();
        }

        public void PrintGameTree()
        {
            //HistoryPoint historyPoint = GetHistoryPointFromActions(new List<byte>() {3, 10, 5, 9, 4, 8});
            //PrintGameTree_Helper(historyPoint);
            var startHistoryPoint = GetStartOfGameHistoryPoint();
            PrintGameTree_Helper(ref startHistoryPoint);
        }

        public unsafe double[] PrintGameTree_Helper(ref HistoryPoint historyPoint)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
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
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
                        TabbedText.Tabs++;
                        double[] utilitiesAtNextHistoryPoint = PrintGameTree_Helper(ref nextHistoryPoint);
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
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                        TabbedText.Tabs++;
                        double[] utilitiesAtNextHistoryPoint = PrintGameTree_Helper(ref nextHistoryPoint);
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
            byte* path = stackalloc byte[GameFullHistory.MaxNumActions];
            bool overridePrint = false;
            string actionsList = progress.GameFullHistory.GetActionsAsListString();
            if (actionsList == "INSERT_PATH_HERE") // use this to print a single path
            {
                overridePrint = true;
            }
            if (overridePrint || RandomGenerator.NextDouble() < printProbability)
            {
                lock (this)
                {

                    progress.GameFullHistory.GetActions(path);
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
            foreach (var informationSetHistory in progress.GetInformationSetHistoryItems())
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
                    IGameState gameState = GetGameState(ref historyPoint);
                    TabbedText.WriteLine($"Game state before action: {gameState}");
                }
                TabbedText.WriteLine($"==> Action chosen: {informationSetHistory.ActionChosen}");
                TabbedText.Tabs--;
                historyPoint = historyPoint.GetBranch(Navigation, informationSetHistory.ActionChosen, decision, informationSetHistory.DecisionIndex);
            }
            double[] finalUtilities = historyPoint.GetFinalUtilities(Navigation);
            TabbedText.WriteLine($"--> Utilities: { String.Join(",", finalUtilities)}");
        }

        #endregion

        #region Utility methods

        private unsafe HistoryPoint GetStartOfGameHistoryPoint()
        {
            GameHistory gameHistory = new GameHistory();
            gameHistory.Initialize();
            switch (Navigation.LookupApproach)
            {
                case InformationSetLookupApproach.PlayUnderlyingGame:
                    GameProgress startingProgress = GameFactory.CreateNewGameProgress(new IterationID(1));
                    return new HistoryPoint(null, startingProgress.GameHistory, startingProgress);
                case InformationSetLookupApproach.CachedGameTreeOnly:
                    return new HistoryPoint(GameHistoryTree, gameHistory, null);
                case InformationSetLookupApproach.CachedGameHistoryOnly:
                    return new HistoryPoint(null, gameHistory, null);
                case InformationSetLookupApproach.CachedBothMethods:
                    return new HistoryPoint(GameHistoryTree, gameHistory, null);
                default:
                    throw new Exception(); // unexpected lookup approach -- won't be called
            }
        }

        //private unsafe HistoryPoint GetHistoryPointFromActions(List<byte> actions)
        //{
        //    HistoryPoint hp = GetStartOfGameHistoryPoint();
        //    foreach (byte action in actions)
        //        hp = hp.GetBranch(Navigation, action);
        //    return hp;
        //}

        //private unsafe HistoryPoint GetHistoryPointBasedOnProgress(GameProgress gameProgress)
        //{
        //    if (Navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
        //        return new HistoryPoint(null, gameProgress.GameHistory, gameProgress);
        //    if (Navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly)
        //        return new HistoryPoint(null, gameProgress.GameHistory, null);
        //    HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
        //    foreach (var informationSetHistory in gameProgress.GetInformationSetHistoryItems())
        //    {
        //        historyPoint = historyPoint.GetBranch(Navigation, informationSetHistory.ActionChosen);
        //    }
        //    return historyPoint;
        //}

        public double GetUtilityFromTerminalHistory(ref HistoryPoint historyPoint, byte playerIndex)
        {
            double[] utilities = GetUtilities(ref historyPoint);
            return utilities[playerIndex];
        }

        public double[] GetUtilities(ref HistoryPoint completedGame)
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


        public void WalkAllInformationSetTrees(Action<InformationSetNodeTally> action)
        {
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                var playerRegrets = Strategies[p].InformationSetTree;
                playerRegrets.WalkTree(node =>
                {
                    InformationSetNodeTally tally = (InformationSetNodeTally)node.StoredValue;
                    if (tally != null)
                        action(tally);
                });
            }
        }

        #endregion

        #region Game play and reporting

        private unsafe string GenerateReports(int iteration, Func<string> prefaceFn)
        {
            string reportString = null;
            if (EvolutionSettings.ReportEveryNIterations != null && iteration % EvolutionSettings.ReportEveryNIterations == 0)
            {
                ActionStrategies previous = ActionStrategy;
                if (EvolutionSettings.AlwaysUseAverageStrategyInReporting)
                    ActionStrategy = ActionStrategies.AverageStrategy;
                bool useRandomPaths = SkipEveryPermutationInitialization || NumInitializedGamePaths > EvolutionSettings.NumRandomIterationsForSummaryTable;
                bool doBestResponse = (EvolutionSettings.BestResponseEveryMIterations != null && iteration % EvolutionSettings.BestResponseEveryMIterations == 0 && EvolutionSettings.BestResponseEveryMIterations != EvolutionSettings.EffectivelyNever && iteration != 0);
                if (doBestResponse)
                    useRandomPaths = false;
                Console.WriteLine("");
                Console.WriteLine(prefaceFn());
                if (EvolutionSettings.Algorithm == GameApproximationAlgorithm.AverageStrategySampling)
                    Console.WriteLine($"{NumberAverageStrategySamplingExplorations / (double)EvolutionSettings.ReportEveryNIterations}");
                NumberAverageStrategySamplingExplorations = 0;
                if (EvolutionSettings.PrintSummaryTable)
                    reportString = PrintSummaryTable(useRandomPaths);
                MeasureRegretMatchingChanges();
                if (ShouldEstimateImprovementOverTime)
                    ReportEstimatedImprovementsOverTime();
                if (doBestResponse)
                    CompareBestResponse(iteration, false);
                if (EvolutionSettings.AlwaysUseAverageStrategyInReporting)
                    ActionStrategy = previous;
                if (EvolutionSettings.PrintGameTree)
                    PrintGameTree();
                if (EvolutionSettings.PrintInformationSets)
                    PrintInformationSets();
                ActionStrategy = previous;
            }
            return reportString;
        }

        private unsafe string PrintSummaryTable(bool useRandomPaths)
        {
            Action<GamePlayer, Func<Decision, GameProgress, byte>> reportGenerator;
            if (useRandomPaths)
            {
                Console.WriteLine($"Result using {EvolutionSettings.NumRandomIterationsForSummaryTable} randomly chosen paths");
                reportGenerator = GenerateReports_RandomPaths;
            }
            else
            {
                Console.WriteLine($"Result using all paths");
                reportGenerator = GenerateReports_AllPaths;
            }
            var reports = GenerateReports(reportGenerator);
            Console.WriteLine($"{reports.standardReport}");
            return reports.csvReport;
            //Console.WriteLine($"Number initialized game paths: {NumInitializedGamePaths}");
        }

        private unsafe void CompareBestResponse(int iteration, bool useRandomPaths)
        {
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                double bestResponseUtility = CalculateBestResponse(playerBeingOptimized, ActionStrategy);
                double bestResponseImprovement = bestResponseUtility - UtilityCalculations[playerBeingOptimized].Average();
                if (!useRandomPaths && bestResponseImprovement < 0 && Math.Abs(bestResponseImprovement) > Math.Abs(bestResponseUtility)/1E-8)
                    throw new Exception("Best response function worse."); // it can be slightly negative as a result of rounding error or if we are using random paths as a result of sampling error
                Console.WriteLine($"Player {playerBeingOptimized} utility with {(EvolutionSettings.AlwaysUseAverageStrategyInReporting ? "average strategy" : "regret matching")} against opponent using average strategy {UtilityCalculations[playerBeingOptimized].Average()} using best response {bestResponseUtility} best response improvement {bestResponseImprovement}");
            }
        }


        public List<GameProgress> GetRandomCompleteGames(GamePlayer player, int numIterations, Func<Decision, GameProgress, byte> actionOverride)
        {
            return player.PlayMultipleIterations(null, numIterations, null, actionOverride).ToList();
        }

        private void GenerateReports_RandomPaths(GamePlayer player, Func<Decision, GameProgress, byte> actionOverride)
        {
            var gameProgresses = GetRandomCompleteGames(player, EvolutionSettings.NumRandomIterationsForSummaryTable, actionOverride);
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
            for (int i = 0; i < EvolutionSettings.NumRandomIterationsForSummaryTable; i++)
            {
                GameProgress gameProgress1 = gameProgresses[i];
                string gameActions = gameProgress1.GameFullHistory.GetActionsAsListString();
                if (!CountPaths.ContainsKey(gameActions))
                    CountPaths[gameActions] = 1;
                else
                    CountPaths[gameActions] = CountPaths[gameActions] + 1;
                //Console.WriteLine($"{gameActions} {gameProgress1.GetNonChancePlayerUtilities()[0]}");
            }
            foreach (var item in CountPaths.AsEnumerable().OrderBy(x => x.Key))
                Console.WriteLine($"{item.Key} => {((double)item.Value) / (double)EvolutionSettings.NumRandomIterationsForSummaryTable}");
        }

        public void ProcessAllPaths(ref HistoryPoint history, Action<HistoryPoint, double> pathPlayer)
        {
            ProcessAllPaths_Recursive(ref history, pathPlayer, ActionStrategy, 1.0);
        }

        private void ProcessAllPaths_Recursive(ref HistoryPoint history, Action<HistoryPoint, double> pathPlayer, ActionStrategies actionStrategy, double probability, byte action = 0, byte nextDecisionIndex = 0)
        {
            // The last two parameters are included to facilitate debugging.
            // Note that this method is different from GamePlayer.PlayAllPaths, because it relies on the cached history, rather than needing to play the game to discover what the next paths are.
            if (history.IsComplete(Navigation))
            {
                pathPlayer(history, probability);
                return;
            }
            ProcessAllPaths_Helper(ref history, probability, pathPlayer, actionStrategy);
        }

        private unsafe void ProcessAllPaths_Helper(ref HistoryPoint historyPoint, double probability, Action<HistoryPoint, double> completedGameProcessor, ActionStrategies actionStrategy)
        {
            double* probabilities = stackalloc double[GameFullHistory.MaxNumActions];
            byte nextDecisionIndex = historyPoint.GetNextDecisionIndex(Navigation);
            byte numPossibleActions = NumPossibleActionsAtDecision(nextDecisionIndex);
            IGameState gameState = GetGameState(ref historyPoint);
            ActionProbabilityUtilities.GetActionProbabilitiesAtHistoryPoint(gameState, actionStrategy, probabilities, numPossibleActions, null, Navigation);
            var historyPointCopy = historyPoint;

            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, EvolutionSettings.MaxParallelDepth, 1, (byte)(numPossibleActions + 1), (action) =>
            {
                if (probabilities[action - 1] > 0)
                {
                    var nextHistoryPoint = historyPointCopy.GetBranch(Navigation, action, GameDefinition.DecisionsExecutionOrder[0], 0); // must use a copy because it's an anonymous method (but this won't be executed much so it isn't so costly). Note that we couldn't use switch-to-branch approach here because all threads are sharing historyPointCopy variable.
                    ProcessAllPaths_Recursive(ref nextHistoryPoint, completedGameProcessor, actionStrategy, probability * probabilities[action - 1], action, nextDecisionIndex);
                }
            });
        }

        public double[] GetAverageUtilities()
        {
            double[] cumulated = new double[NumNonChancePlayers];
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, GetGameState);
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            GetAverageUtilities_Helper(ref historyPoint, cumulated, 1.0);
            return cumulated;
        }

        public unsafe void GetAverageUtilities_Helper(ref HistoryPoint historyPoint, double[] cumulated, double prob)
        {
            IGameState gameState = historyPoint.GetGameStateForCurrentPlayer(Navigation);
            if (gameState is FinalUtilities finalUtilities)
            {
                for (byte p = 0; p < NumNonChancePlayers; p++)
                    cumulated[p] += finalUtilities.Utilities[p] * prob;
            }
            else if (gameState is ChanceNodeSettings chanceNodeSettings)
            {
                byte numPossibilities = GameDefinition.DecisionsExecutionOrder[chanceNodeSettings.DecisionIndex].NumPossibleActions;
                for (byte action = 1; action <= numPossibilities; action++)
                {
                    double actionProb = chanceNodeSettings.GetActionProbability(action);
                    if (actionProb > 0)
                    {
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
                        GetAverageUtilities_Helper(ref nextHistoryPoint, cumulated, prob * actionProb);
                    }
                }
            }
            else if (gameState is InformationSetNodeTally informationSet)
            {
                byte numPossibilities = GameDefinition.DecisionsExecutionOrder[informationSet.DecisionIndex].NumPossibleActions;
                double* actionProbabilities = stackalloc double[numPossibilities];
                informationSet.GetRegretMatchingProbabilities(actionProbabilities);
                for (byte action = 1; action <= numPossibilities; action++)
                {
                    if (actionProbabilities[action - 1] > 0)
                    {
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                        GetAverageUtilities_Helper(ref nextHistoryPoint, cumulated, prob * actionProbabilities[action - 1]);
                    }
                }
            }
        }

        SimpleReport[] ReportsBeingGenerated = null;

        public (string standardReport, string csvReport) GenerateReports(Action<GamePlayer, Func<Decision, GameProgress, byte>> generator)
        {
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, GetGameState);
            StringBuilder standardReport = new StringBuilder();
            StringBuilder csvReport = new StringBuilder();
            var simpleReportDefinitions = GameDefinition.GetSimpleReportDefinitions();
            int simpleReportDefinitionsCount = simpleReportDefinitions.Count();
            ReportsBeingGenerated = new SimpleReport[simpleReportDefinitionsCount];
            for (int i = 0; i < simpleReportDefinitionsCount; i++)
            {
                ReportsBeingGenerated[i] = new SimpleReport(simpleReportDefinitions[i], simpleReportDefinitions[i].DivideColumnFiltersByImmediatelyEarlierReport ? ReportsBeingGenerated[i - 1] : null);
                generator(GamePlayer, simpleReportDefinitions[i].ActionsOverride);
                ReportsBeingGenerated[i].GetReport(standardReport, csvReport);
                ReportsBeingGenerated[i] = null; // so we don't keep adding GameProgress to this report
            }
            ReportsBeingGenerated = null;
            return (standardReport.ToString(), csvReport.ToString());
        }

        StatCollector[] UtilityCalculations;
        private StatCollectorArray UtilityCalculationsArray;

        private void GenerateReports_AllPaths(GamePlayer player, Func<Decision, GameProgress, byte> actionOverride)
        {
            UtilityCalculations = new StatCollector[NumNonChancePlayers];
            for (int p = 0; p < NumNonChancePlayers; p++)
                UtilityCalculations[p] = new StatCollector();
            // start Task Parallel Library consumer/producer pattern
            // we'll set up step1, step2, and step3 (but not in that order, since step 1 triggers step 2)
            var step2_buffer = new BufferBlock<Tuple<GameProgress, double>>(new DataflowBlockOptions { BoundedCapacity = 10000 });
            var step3_consumer = AddGameProgressToReport(step2_buffer);
            void step1_playPath(ref HistoryPoint completedGame, double probabilityOfPath)
            {
                // play each path and then asynchronously consume the result, including the probability of the game path
                List<byte> actions = completedGame.GetActionsToHere(Navigation);
                (GameProgress progress, _) = player.PlayPath(actions, false);
                // do the simple aggregation of utilities. note that this is different from the value returned by vanilla, since that uses regret matching, instead of average strategies.
                double[] utilities = GetUtilities(ref completedGame);
                for (int p = 0; p < NumNonChancePlayers; p++)
                {
                    UtilityCalculations[p].Add(utilities[p], probabilityOfPath);
                }
                // consume the result for reports
                step2_buffer.SendAsync(new Tuple<GameProgress, double>(progress, probabilityOfPath));
            };
            // Now, we have to send the paths through all of these steps and make sure that step 3 is completely finished.
            var startHistoryPoint = GetStartOfGameHistoryPoint();
            ProcessAllPaths(ref startHistoryPoint, (historyPoint, pathProbability) => step1_playPath(ref historyPoint, pathProbability));
            step2_buffer.Complete(); // tell consumer nothing more to be produced
            step3_consumer.Wait(); // wait until all have been processed

            for (int p = 0; p < NumNonChancePlayers; p++)
                if (Math.Abs(UtilityCalculations[p].sumOfWeights - 1.0) > 0.001)
                    throw new Exception("Imperfect sampling.");
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
                        ReportsBeingGenerated[i]?.ProcessGameProgress(toProcess.Item1, toProcess.Item2);
            }
        }

        private double[] GetBestResponseImprovements_RandomPaths()
        {
            throw new NotImplementedException();
            // basic idea: for each player, WalkTree, try to override each action, play random paths, and see what score is. 
        }

        private double[] CalculateUtility_RandomPaths(GamePlayer player, Func<Decision, GameProgress, byte> actionOverride)
        {
            var gameProgresses = GetRandomCompleteGames(player, EvolutionSettings.NumRandomIterationsForUtilityCalculation, actionOverride);
            // start Task Parallel Library consumer/producer pattern
            // we'll set up step1, step2, and step3 (but not in that order, since step 1 triggers step 2)
            var step2_buffer = new BufferBlock<Tuple<GameProgress, double>>(new DataflowBlockOptions { BoundedCapacity = 10000 });
            var step3_consumer = ProcessUtilities(step2_buffer);
            foreach (var gameProgress in gameProgresses)
                step2_buffer.SendAsync(new Tuple<GameProgress, double>(gameProgress, 1.0));
            step2_buffer.Complete(); // tell consumer nothing more to be produced
            step3_consumer.Wait(); // wait until all have been processed
            return UtilityCalculationsArray.Average().ToArray();
        }

        async Task ProcessUtilities(ISourceBlock<Tuple<GameProgress, double>> source)
        {
            UtilityCalculationsArray = new StatCollectorArray();
            UtilityCalculationsArray.Initialize(NumNonChancePlayers);
            while (await source.OutputAvailableAsync())
            {
                Tuple<GameProgress, double> toProcess = source.Receive();
                if (toProcess.Item2 > 0) // probability
                {
                    double[] utilities = toProcess.Item1.GetNonChancePlayerUtilities();
                    UtilityCalculationsArray.Add(utilities, toProcess.Item2);
                }
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
        
        #region Estimate improvement over time

        double[] MostRecentUtilities;
        Queue<double>[] MostRecentImprovements;
        double[] AggregateRecentImprovements;
        const int SizeOfMovingAverage = 100;
        long PseudorandomSpot = 1;

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
            var startHistoryPoint = GetStartOfGameHistoryPoint();
            double[] returnVal = GibsonProbe(ref startHistoryPoint, randomProducer, GameDefinition.DecisionsExecutionOrder[0], 0);
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
                Console.WriteLine($"Estimated average improvement per iteration over last {SizeOfMovingAverage} iterations for player {p}: {AggregateRecentImprovements[p]/numItems}");
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
                    Console.WriteLine($"Change size for player {p} change {totalChange} proportion mixed {proportionMixed}");
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
