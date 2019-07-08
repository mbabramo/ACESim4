using ACESim;
using ACESimBase.GameSolvingSupport;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ACESim
{
    [Serializable]
    public abstract partial class StrategiesDeveloperBase : IStrategiesDeveloper
    {
        #region Options

        public static bool StoreGameStateNodesInLists = true;

        public const int MaxNumMainPlayers = 4; // this affects fixed-size stack-allocated buffers // TODO: Set to 2
        public const int MaxPossibleActions = 100; // same

        public InformationSetLookupApproach LookupApproach { get; set; } = InformationSetLookupApproach.CachedGameHistoryOnly;

        bool AllowSkipEveryPermutationInitialization = true;
        public bool SkipEveryPermutationInitialization => 
            AllowSkipEveryPermutationInitialization  
            && EvolutionSettings.Algorithm != GameApproximationAlgorithm.PureStrategyFinder;

        [NonSerialized]
        public Stopwatch StrategiesDeveloperStopwatch = new Stopwatch();

        public int IterationNum;
        public double IterationNumDouble;

        public int GameNumber;


        public StrategiesDeveloperBase(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition)
        {
            Navigation = Navigation.WithGameStateFunction(GetGameState);
            Strategies = existingStrategyState;
            EvolutionSettings = evolutionSettings;
            GameDefinition = gameDefinition;
            GameFactory = GameDefinition.GameFactory;
            NumNonChancePlayers = (byte)GameDefinition.Players.Count(x => !x.PlayerIsChance);
            NumChancePlayers = (byte)GameDefinition.Players.Count(x => x.PlayerIsChance);
        }


        #endregion

        #region Implementation of interface


        public abstract Task<ReportCollection> RunAlgorithm(string reportName);

        public async Task<ReportCollection> DevelopStrategies(string reportName)
        {
            await Initialize();
            ReportCollection reportCollection = new ReportCollection();
            for (int s = 0; s < GameDefinition.NumScenariosToDevelop; s++)
            {
                if (GameDefinition.NumScenariosToDevelop > 0)
                    Console.WriteLine($@"Scenario {s}");
                if (s > 0)
                    ReinitializeForScenario(s, IterationsForWarmupScenario() != null);
                var result = await RunAlgorithm(reportName);
                if (EvolutionSettings.SerializeResults && !(this is PlaybackOnly))
                {
                    string filename = Path.Combine(FolderFinder.GetFolderToWriteTo("Strategies").FullName, EvolutionSettings.SerializeResultsPrefixPlus(s, GameDefinition.NumScenariosToDevelop));
                    if (EvolutionSettings.SerializeInformationSetDataOnly)
                        StrategySerialization.SerializeInformationSets(InformationSets, filename);
                    else
                        StrategySerialization.SerializeStrategies(Strategies.ToArray(), filename);
                }
                reportCollection.Add(result);
            }
            return reportCollection;
        }

        public abstract IStrategiesDeveloper DeepCopy();

        public void DeepCopyHelper(IStrategiesDeveloper target)
        {

            target.Strategies = Strategies.Select(x => x.DeepCopy()).ToList();
            target.EvolutionSettings = EvolutionSettings;
            target.GameDefinition = GameDefinition;
            target.GameFactory = GameFactory;
            target.Navigation = Navigation;
            target.LookupApproach = LookupApproach;
        }

        #endregion

        #region Class variables

        public List<Strategy> Strategies { get; set; }

        public EvolutionSettings EvolutionSettings { get; set; }

        public List<InformationSetNode> InformationSets { get; set; }

        public List<ChanceNode> ChanceNodes { get; set; }

        public List<FinalUtilitiesNode> FinalUtilitiesNodes { get; set; }

        public GameDefinition GameDefinition { get; set; }

        public IGameFactory GameFactory { get; set; }

        public GamePlayer GamePlayer { get; set; }

        public HistoryNavigationInfo Navigation { get; set; }

        // Note: The ActionStrategy selected does not affect the learning process. It affects reporting after learning, including the calculation of best response scores. Convergence bounds guarantees depend on all players' using the AverageStrategies ActionStrategy. It may seem surprising that we can have convergence guarantees when a player is using the average strategy, thus continuing to make what appears to be some mistakes from the past. But the key is that the other players are also using their average strategies. Thus, even if new information has changed the best move for a player under current circumstances, the player will be competing against a player that continues to employ some of the old strategies. In other words, the opponents' average strategy changes extremely slowly, and the no-regret learning convergence guarantees at a single information set are based on this concept of the player and the opponent playing their average strategies. But the average strategy is not the strategy that the player has "learned." 

        public bool BestBecomesResult = false; // NOTE: The argument against doing this is that exploitability is not the only consideration; we also want to be close to a sequential equilibrium.
        public double BestExploitability = int.MaxValue; // initialize to worst possible score (i.e., highest possible exploitability)
        public int BestIteration = -1;

        public double[] MinScore, MaxScore, ScoreRange;
        public bool ScoreRangeExists => (ScoreRange == null || ScoreRange.Any(x => x == 0));

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

        public byte NumNonChancePlayers;
        public byte NumChancePlayers; // note that chance players MUST be indexed after nonchance players in the player list

        public int NumInitializedGamePaths = 0; // Because of how PlayAllPaths works, this will be much higher for parallel implementations

        #endregion

        #region Initialization

        public void InitializeInformationSets()
        {
            int numInformationSets = InformationSets.Count;
            Parallel.For(0, numInformationSets, n => InformationSets[n].Initialize());
        }

        public virtual void ReinitializeForScenario(int scenario, bool warmupVersion)
        {
            GameDefinition.SetScenario(scenario, warmupVersion);
            if (scenario > 0)
            {
                foreach (var node in FinalUtilitiesNodes)
                    node.CurrentScenario = scenario;
                CalculateMinMax();
            }
        }

        public virtual int? IterationsForWarmupScenario()
        {
            return null; // not supported by default
        }

        public void Reinitialize()
        {
            foreach (Strategy s in Strategies)
            {
                if (!s.PlayerInfo.PlayerIsChance)
                {
                    s.InformationSetTree?.WalkTree(node =>
                    {
                        InformationSetNode t = (InformationSetNode)node.StoredValue;
                        t?.Reinitialize();
                    });
                }
            }
        }

        public virtual Task Initialize()
        {
            if (StoreGameStateNodesInLists)
            {
                InformationSets = new List<InformationSetNode>();
                ChanceNodes = new List<ChanceNode>();
                FinalUtilitiesNodes = new List<FinalUtilitiesNode>();
            }
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, InformationSets, ChanceNodes, FinalUtilitiesNodes, GetGameState, EvolutionSettings);
            foreach (Strategy strategy in Strategies)
                strategy.Navigation = Navigation;

            GamePlayer = new GamePlayer(Strategies, EvolutionSettings.ParallelOptimization, GameDefinition);

            foreach (Strategy s in Strategies)
            {
                if (s.InformationSetTree == null)
                    s.CreateInformationSetTree(GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.PlayerNumber == s.PlayerInfo.PlayerIndex)?.NumPossibleActions ?? (byte)1, s.PlayerInfo.PlayerIndex <= NumNonChancePlayers || s.PlayerInfo.PlayerIndex == GameDefinition.PlayerIndex_ResolutionPlayer);
            }

            // Create game trees
            GameHistoryTree = new NWayTreeStorageInternal<IGameState>(null, GameDefinition.DecisionsExecutionOrder.First().NumPossibleActions);

            if (!SkipEveryPermutationInitialization)
            {
                TabbedText.WriteLine("Initializing all game paths...");
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                if (StoreGameStateNodesInLists && GamePlayer.PlayAllPathsIsParallel)
                    throw new NotImplementedException();
                // slower approach commented out
                //NumInitializedGamePaths = 0;
                //var originalLookup = Navigation.LookupApproach;
                //Navigation = Navigation.WithLookupApproach(InformationSetLookupApproach.PlayUnderlyingGame);
                //await ProcessAllPathsAsync(GetStartOfGameHistoryPoint(), (historyPoint, probability) => ProcessInitializedGameProgressAsync(historyPoint, probability));
                //Navigation = Navigation.WithLookupApproach(originalLookup);
                NumInitializedGamePaths = GamePlayer.PlayAllPaths(ProcessInitializedGameProgress);
                stopwatch.Stop();
                string parallelString = GamePlayer.PlayAllPathsIsParallel ? " (higher number in parallel)" : "";
                string informationSetsString = StoreGameStateNodesInLists ? $" Total information sets: {InformationSets.Count()} chance nodes: {ChanceNodes.Count()} final nodes: {FinalUtilitiesNodes.Count()}" : "";
                TabbedText.WriteLine($"... Initialized. Total paths{parallelString}: {NumInitializedGamePaths}{informationSetsString} Initialization milliseconds {stopwatch.ElapsedMilliseconds}");
            }

            DistributeChanceDecisions();
            PrepareAcceleratedBestResponse();
            PrintSameGameResults();
            CalculateMinMax();

            return Task.CompletedTask;
        }

        public void CalculateMinMax()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            TabbedText.WriteLine("Calculating min-max...");
            foreach (bool isMax in new bool[] { false, true })
                // foreach (byte? playerIndex in Enumerable.Range(0, NumNonChancePlayers).Select(x => (byte?) x))
                foreach (byte? playerIndex in new byte?[] { null }) // uncomment above to avoid distributing distributable distributor inputs
                {
                    CalculateMinMax c = new CalculateMinMax(isMax, NumNonChancePlayers, playerIndex);
                    double[] result = TreeWalk_Tree(c);
                    if (playerIndex == null)
                    {
                        if (!isMax)
                            MinScore = result;
                        else
                        {
                            MaxScore = result;
                            ScoreRange = MinScore.Zip(MaxScore, (min, max) => max - min).ToArray();
                        }
                    }
                }
            TabbedText.WriteLine($"... complete {stopwatch.ElapsedMilliseconds} milliseconds");
        }

        Task ProcessInitializedGameProgressAsync(HistoryPoint completedGame, double probability)
        {
            List<byte> actions = completedGame.GetActionsToHere(Navigation);
            (GameProgress progress, _) = GamePlayer.PlayPath(actions, false);
            lock (this)
            {
                ProcessInitializedGameProgress(progress);
                NumInitializedGamePaths++;
            }
            return Task.CompletedTask;
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
                if (GameProgressLogger.DetailedLogging)
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
            for (int i = 0; i < 20; i++) // shouldn't be necessary to do more than once, but maybe some parallelism issue is causing the need for that
            {
                gameState = ProcessProgress(ref historyPoint, navigationSettings, progress);
                if (gameState != null)
                    return gameState;
            }
            throw new Exception("Internal error. The path was not processed correctly. Try using CachedBothMethods to try to identify where there is a problem with information sets.");
        }

        private unsafe IGameState ProcessProgress(ref HistoryPoint historyPoint, in HistoryNavigationInfo navigationSettings, GameProgress progress)
        {
            IGameState gameState;
            ProcessInitializedGameProgress(progress);
            NumInitializedGamePaths++; // Note: This may not be exact if we initialize the same game path twice (e.g., if we are playing in parallel)
            gameState = historyPoint.GetGameStateForCurrentPlayer(navigationSettings);
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
                            InformationSetNode t = (InformationSetNode)node.StoredValue;
                            if (t != null &&
                                EvolutionSettings.RestrictToTheseInformationSets.Contains(t.InformationSetNodeNumber))
                            {
                                TabbedText.WriteLine($"{t}");
                            }
                        });
                    }
                    else
                    {
                        TabbedText.WriteLine($"{s.PlayerInfo}");
                        string tree = s.GetInformationSetTreeString();
                        TabbedText.WriteLine(tree);
                    }
                }
            }
        }

        public void AnalyzeInformationSets()
        {
            foreach (var infoSet in InformationSets)
                infoSet.PastValuesAnalyze();
        }

        public void PrintGameTree()
        {
            bool original = TraceTreeWalk;
            TraceTreeWalk = true;
            TreeWalk_Tree(new WalkOnly());
            TraceTreeWalk = original;
            //var startHistoryPoint = GetStartOfGameHistoryPoint();
            //PrintGameTree_Helper_Manual(ref startHistoryPoint);
            // uncomment to print from particular poitn
            //HistoryPoint historyPoint = GetHistoryPointFromActions(new List<byte>() {3, 10, 5, 9, 4, 8});
            //PrintGameTree_Helper_Manual(historyPoint);
        }

        public unsafe double[] PrintGameTree_Helper_Manual(ref HistoryPoint historyPoint)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            //if (TraceCFR)
            //    TabbedText.WriteLine($"Probe optimizing player {playerBeingOptimized}");
            if (gameStateForCurrentPlayer is FinalUtilitiesNode finalUtilities)
            {
                TabbedText.WriteLine($"--> {String.Join(",", finalUtilities.Utilities.Select(x => $"{x:N2}"))}");
                return finalUtilities.Utilities;
            }
            else
            {
                if (gameStateForCurrentPlayer is ChanceNode chanceNode)
                {
                    byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
                    double[] cumUtilities = null;
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        double chanceProbability = chanceNode.GetActionProbability(action);
                        TabbedText.WriteLine($"{action} (C): p={chanceProbability:N2}");
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
                        TabbedText.Tabs++;
                        double[] utilitiesAtNextHistoryPoint = PrintGameTree_Helper_Manual(ref nextHistoryPoint);
                        TabbedText.Tabs--;
                        if (cumUtilities == null)
                            cumUtilities = new double[utilitiesAtNextHistoryPoint.Length];
                        for (int i = 0; i < cumUtilities.Length; i++)
                            cumUtilities[i] += utilitiesAtNextHistoryPoint[i] * chanceProbability;
                    }
                    TabbedText.WriteLine($"--> {String.Join(",", cumUtilities.Select(x => $"{x:N2}"))}");
                    return cumUtilities;
                }
                else if (gameStateForCurrentPlayer is InformationSetNode informationSet)
                {
                    byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
                    double[] cumUtilities = null;
                    double* actionProbabilities = stackalloc double[numPossibleActions];
                    informationSet.GetCurrentProbabilities(actionProbabilities);
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        TabbedText.WriteLine($"{action} (P{informationSet.PlayerIndex}): p={actionProbabilities[action - 1]:N2} ({informationSet.ToStringAbbreviated()})");
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                        TabbedText.Tabs++;
                        double[] utilitiesAtNextHistoryPoint = PrintGameTree_Helper_Manual(ref nextHistoryPoint);
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
                    List<byte> informationSetList = informationSetHistoryCopy.GetInformationSetForPlayerAsList();
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

        internal void RememberBest(int iteration)
        {
            if (BestBecomesResult && iteration >= 3)
            {
                double exploitability = BestResponseImprovement.Sum();
                if (exploitability < BestExploitability || iteration == 3)
                {
                    Parallel.ForEach(InformationSets, informationSet => informationSet.CreateBackup());
                    BestExploitability = exploitability;
                    BestIteration = iteration;
                    LastBestResponseImprovement = BestResponseImprovement.ToArray();
                }
                if (iteration == EvolutionSettings.TotalIterations)
                {
                    Parallel.ForEach(InformationSets, informationSet => informationSet.RestoreBackup());
                    BestResponseImprovement = LastBestResponseImprovement.ToArray();
                    TabbedText.WriteLine($"Best iteration {BestIteration} best exploitability {BestExploitability}");
                }
            }
        }

        public unsafe HistoryPoint GetStartOfGameHistoryPoint()
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


        public void WalkAllInformationSetTrees(Action<InformationSetNode> action)
        {
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                var playerRegrets = Strategies[p].InformationSetTree;
                playerRegrets.WalkTree(node =>
                {
                    InformationSetNode tally = (InformationSetNode)node.StoredValue;
                    if (tally != null)
                        action(tally);
                });
            }
        }

        #endregion

        #region Game play and reporting

        public async Task<ReportCollection> GenerateReports(int iteration, Func<string> prefaceFn)
        {
            ReportCollection reportCollection = new ReportCollection();
            bool doBestResponse = (EvolutionSettings.BestResponseEveryMIterations != null && iteration % EvolutionSettings.BestResponseEveryMIterations == 0 && EvolutionSettings.BestResponseEveryMIterations != EvolutionSettings.EffectivelyNever && iteration != 0);
            bool doReports = EvolutionSettings.ReportEveryNIterations != null && iteration % EvolutionSettings.ReportEveryNIterations == 0;
            if (doReports || doBestResponse)
            {
                TabbedText.WriteLine("");
                TabbedText.WriteLine(prefaceFn());
                if (doBestResponse)
                {
                    CalculateBestResponse(false);
                    RememberBest(iteration);
                }

                if (doReports)
                {
                    Br.eak.Add("Report");
                    bool useRandomPaths = EvolutionSettings.UseRandomPathsForReporting
                        //&& (SkipEveryPermutationInitialization ||
                        //   NumInitializedGamePaths > EvolutionSettings.NumRandomIterationsForSummaryTable)
                        ;
                    ActionStrategies previous = ActionStrategy;
                    var actionStrategiesToUse = EvolutionSettings.ActionStrategiesToUseInReporting;
                    if (actionStrategiesToUse != null)
                        foreach (var actionStrategy in actionStrategiesToUse)
                        {
                            ActionStrategy = actionStrategy;
                            ActionStrategyLastReport = ActionStrategy.ToString();
                            if (EvolutionSettings.GenerateReportsByPlaying)
                            {
                                var result = await GenerateReportsByPlaying(useRandomPaths);
                                reportCollection.Add(result);
                            }
                        }
                    ActionStrategy = previous;
                    Br.eak.Remove("Report");
                }
                if (doBestResponse)
                    CompareBestResponse();
                if (iteration % EvolutionSettings.CorrelatedEquilibriumCalculationsEveryNIterations == 0)
                    DoCorrelatedEquilibriumCalculations(iteration);
                if (EvolutionSettings.PrintGameTree)
                    PrintGameTree();
                if (EvolutionSettings.PrintInformationSets)
                    PrintInformationSets();
                if (EvolutionSettings.AnalyzeInformationSets)
                    AnalyzeInformationSets();
            }

            return reportCollection;
        }

        string ActionStrategyLastReport;
        private async Task<ReportCollection> GenerateReportsByPlaying(bool useRandomPaths)
        {
            Func<GamePlayer, Func<Decision, GameProgress, byte>, Task> reportGenerator;
            if (useRandomPaths)
            {
                TabbedText.WriteLine($"Result using {EvolutionSettings.NumRandomIterationsForSummaryTable} randomly chosen paths playing {ActionStrategy}");
                reportGenerator = GenerateReports_RandomPaths;
            }
            else
            {
                TabbedText.WriteLine($"Result using all paths playing {ActionStrategy}");
                reportGenerator = GenerateReports_AllPaths;
            }
            var reports = await GenerateReportsByPlaying(reportGenerator);
            if (!EvolutionSettings.SuppressReportPrinting)
            {
                Debug.WriteLine($"{reports.standardReport}");
                TabbedText.WriteLine($"{reports.standardReport}");
            }
            return reports;
        }

        public double[] BestResponseUtilities;
        public double[] AverageStrategyUtilities;
        public double[] BestResponseImprovement;
        public double[] LastBestResponseImprovement;
        public double[] BestResponseImprovementAdj => ScoreRangeExists ? BestResponseImprovement : BestResponseImprovement.Zip(ScoreRange, (bri, sr) => bri / sr).ToArray();

        long BestResponseCalculationTime;

        bool BestResponseIsToAverageStrategy = true; // usually, this should be true, since the average strategy is the least exploitable strategy
        string BestResponseOpponentString => BestResponseIsToAverageStrategy ? "average strategy" : ActionStrategy.ToString();

        List<List<InformationSetNode>> InformationSetsByDecisionIndex;
        List<NodeActionsMultipleHistories> AcceleratedBestResponsePrepResult;

        private void PrepareAcceleratedBestResponse()
        {
            if (!EvolutionSettings.UseAcceleratedBestResponse)
                return;
            TabbedText.WriteLine($"Prepping accelerated best response...");
            Stopwatch s = new Stopwatch();
            s.Start();
            AcceleratedBestResponsePrep prepWalk = new AcceleratedBestResponsePrep(EvolutionSettings.DistributeChanceDecisions, (byte)NumNonChancePlayers, TraceTreeWalk);
            AcceleratedBestResponsePrepResult = TreeWalk_Tree(prepWalk, new NodeActionsHistory());
            InformationSetsByDecisionIndex = InformationSets.GroupBy(x => x.DecisionIndex).Select(x => x.ToList()).ToList();
            s.Stop();
            TabbedText.WriteLine($"... {s.ElapsedMilliseconds} milliseconds. Total information sets: {InformationSets.Count()}");
        }

        private void ExecuteAcceleratedBestResponse(bool determineWhetherReachable)
        {
            // index through information sets by decision (note that i is not the same as the actual decision index). First, calculate reach probabilities going forward. Second, calculate best response values going backward.
            CalculateReachProbabilitiesAndPrunability();
            for (int i = InformationSetsByDecisionIndex.Count - 1; i >= 0; i--)
            {
                List<InformationSetNode> informationSetsForDecision = InformationSetsByDecisionIndex[i];
                Parallel.ForEach(informationSetsForDecision, informationSet => informationSet.AcceleratedBestResponse_CalculateBestResponseValues(NumNonChancePlayers));
            }
            if (determineWhetherReachable)
                for (int i = 0; i < InformationSetsByDecisionIndex.Count; i++)
                {
                    List<InformationSetNode> informationSetsForDecision = InformationSetsByDecisionIndex[i];
                    Parallel.ForEach(informationSetsForDecision, informationSet => informationSet.AcceleratedBestResponse_DetermineWhetherReachable());
                }
            // Finally, we need to calculate the final values by looking at the first information sets for each player.
            if (BestResponseUtilities == null || BestResponseImprovement == null || AverageStrategyUtilities == null)
            {
                BestResponseUtilities = new double[NumNonChancePlayers];
                BestResponseImprovement = new double[NumNonChancePlayers];
                AverageStrategyUtilities = new double[NumNonChancePlayers];
            }
            for (byte playerIndex = 0; playerIndex < NumNonChancePlayers; playerIndex++)
            {
                var resultForPlayer = AcceleratedBestResponsePrepResult[playerIndex];
                (double bestResponseResult, double averageStrategyResult) = resultForPlayer.GetProbabilityAdjustedValueOfPaths(playerIndex);
                BestResponseUtilities[playerIndex] = bestResponseResult;
                AverageStrategyUtilities[playerIndex] = averageStrategyResult;
                BestResponseImprovement[playerIndex] = bestResponseResult - averageStrategyResult;
            }
        }

        public void CalculateReachProbabilitiesAndPrunability()
        {
            for (int i = 0; i < InformationSetsByDecisionIndex.Count; i++)
            {
                List<InformationSetNode> informationSetsForDecision = InformationSetsByDecisionIndex[i];
                Parallel.ForEach(informationSetsForDecision, informationSet => informationSet.AcceleratedBestResponse_CalculateReachProbabilities(EvolutionSettings.PredeterminePrunabilityBasedOnRelativeContributions));
            }
        }

        public unsafe void CalculateBestResponse(bool determineWhetherReachable)
        {
            BestResponseUtilities = new double[NumNonChancePlayers];
            ActionStrategies actionStrategy = ActionStrategy;
            if (actionStrategy == ActionStrategies.CorrelatedEquilibrium)
                actionStrategy = ActionStrategies.AverageStrategy; // best response against average strategy is same as against correlated equilibrium
            if (BestResponseIsToAverageStrategy)
                actionStrategy = ActionStrategies.AverageStrategy;

            Stopwatch s = new Stopwatch();
            s.Start();
            if (EvolutionSettings.UseAcceleratedBestResponse)
            {
                if (actionStrategy != ActionStrategies.AverageStrategy)
                    throw new NotSupportedException();
                if (AcceleratedBestResponsePrepResult == null)
                    PrepareAcceleratedBestResponse();
                ExecuteAcceleratedBestResponse(determineWhetherReachable);
            }
            else
            {
                var calculator = new AverageStrategyCalculator(EvolutionSettings.DistributeChanceDecisions);
                AverageStrategyUtilities = TreeWalk_Tree(calculator, true);
                BestResponseImprovement = new double[NumNonChancePlayers];
                for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
                {
                    double bestResponse = CalculateBestResponse(playerBeingOptimized, actionStrategy);
                    BestResponseUtilities[playerBeingOptimized] = bestResponse;
                    BestResponseImprovement[playerBeingOptimized] = bestResponse - AverageStrategyUtilities[playerBeingOptimized];
                }
            }
            s.Stop();
            BestResponseCalculationTime = s.ElapsedMilliseconds;
        }

        private unsafe void CompareBestResponse()
        {
            // This is comparing (1) Best response vs. average strategy; to (2) most recently calculated average strategy
            bool averageStrategyUtilitiesRecorded = AverageStrategyUtilities != null;
            if (!averageStrategyUtilitiesRecorded)
            {
                if (UtilityCalculationsArray == null)
                    return; // nothing to compare BR to
                else
                    AverageStrategyUtilities = UtilityCalculationsArray.StatCollectors.Select(x => x.Average()).ToArray();
            }
            ActionStrategies actionStrategy = ActionStrategy;
            if (actionStrategy == ActionStrategies.CorrelatedEquilibrium)
                actionStrategy = ActionStrategies.AverageStrategy; // best response against average strategy is same as against correlated equilibrium
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                TabbedText.WriteLine($"U(P{playerBeingOptimized}) {ActionStrategyLastReport}: {AverageStrategyUtilities[playerBeingOptimized]} Best response vs. {BestResponseOpponentString} {BestResponseUtilities[playerBeingOptimized]} Best response improvement: {BestResponseImprovementAdj?[playerBeingOptimized]}");
            }
            if (!averageStrategyUtilitiesRecorded)
                AverageStrategyUtilities = null; // we may be using approximations, so set to null to avoid confusion
            TabbedText.WriteLine($"Total best response calculation time: {BestResponseCalculationTime} milliseconds");
            TabbedText.WriteLine("");
        }

        public IEnumerable<GameProgress> GetRandomCompleteGames(GamePlayer player, int numIterations, Func<Decision, GameProgress, byte> actionOverride)
        {
            return player.PlayMultipleIterations(null, numIterations, null, actionOverride);
        }

        private async Task GenerateReports_RandomPaths(GamePlayer player, Func<Decision, GameProgress, byte> actionOverride)
        {
            UtilityCalculationsArray = new StatCollectorArray();
            UtilityCalculationsArray.Initialize(NumNonChancePlayers);
            // start Task Parallel Library consumer/producer pattern
            // we'll set up step1, step2, and step3 (but not in that order, since step 1 triggers step 2)
            var step2_buffer = new BufferBlock<Tuple<GameProgress, double>>(new DataflowBlockOptions { BoundedCapacity = 10000 });
            var step3_consumer = AddGameProgressToReport(step2_buffer);
            await player.PlayMultipleIterationsAndProcess(EvolutionSettings.NumRandomIterationsForSummaryTable, actionOverride, step2_buffer);
            step2_buffer.Complete(); // tell consumer nothing more to be produced
            await step3_consumer; // wait until all have been processed
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
                //TabbedText.WriteLine($"{gameActions} {gameProgress1.GetNonChancePlayerUtilities()[0]}");
            }
            foreach (var item in CountPaths.AsEnumerable().OrderBy(x => x.Key))
                TabbedText.WriteLine($"{item.Key} => {((double)item.Value) / (double)EvolutionSettings.NumRandomIterationsForSummaryTable}");
        }

        public async Task ProcessAllPathsAsync(HistoryPoint history, Func<HistoryPoint, double, Task> pathPlayer)
        {
            await ProcessAllPaths_Recursive(history, pathPlayer, ActionStrategy, 1.0);
        }

        public async Task ProcessAllPaths(HistoryPoint history, Func<HistoryPoint, double, Task> pathPlayer)
        {
            await ProcessAllPaths_Recursive(history, pathPlayer, ActionStrategy, 1.0);
        }

        private async Task ProcessAllPaths_Recursive(HistoryPoint history, Func<HistoryPoint, double, Task> pathPlayer, ActionStrategies actionStrategy, double probability, byte action = 0, byte nextDecisionIndex = 0)
        {
            // The last two parameters are included to facilitate debugging.
            // Note that this method is different from GamePlayer.PlayAllPaths, because it relies on the cached history, rather than needing to play the game to discover what the next paths are.
            if (history.IsComplete(Navigation))
            {
                await pathPlayer(history, probability);
                return;
            }
            await ProcessAllPaths_Helper(history, probability, pathPlayer, actionStrategy);
        }

        private async Task ProcessAllPaths_Helper(HistoryPoint historyPoint, double probability, Func<HistoryPoint, double, Task> completedGameProcessor, ActionStrategies actionStrategy)
        {
            double[] probabilities = new double[GameFullHistory.MaxNumActions];
            byte nextDecisionIndex = historyPoint.GetNextDecisionIndex(Navigation);
            byte numPossibleActions = NumPossibleActionsAtDecision(nextDecisionIndex);
            IGameState gameState = GetGameState(ref historyPoint);
            if (actionStrategy == ActionStrategies.CorrelatedEquilibrium)
            {
                actionStrategy = ActionStrategies.AverageStrategy;
                TabbedText.WriteLine("Correlated equilibrium not supported in process all paths, since some iteration must be chosen at random anyway, so using average strategy.");
            }
            ActionProbabilityUtilities.GetActionProbabilitiesAtHistoryPoint(gameState, actionStrategy, 0 /* ignored */, probabilities, numPossibleActions, null, Navigation);
            var historyPointCopy = historyPoint;

            bool includeZeroProbabilityActions = true; // may be relevant for counts
            bool roundOffPlayerActionsNearZeroOrOne = false; 

            await Parallelizer.GoAsync(EvolutionSettings.ParallelOptimization, 1, (byte)(numPossibleActions + 1), async (action) =>
            {
                byte actionByte = (byte)action;
                double actionProbability = probabilities[action - 1];
                if (roundOffPlayerActionsNearZeroOrOne && gameState is InformationSetNode)
                {
                    if (actionProbability < 0.005)
                        actionProbability = 0;
                    else if (actionProbability > 0.995)
                        actionProbability = 1.0;
                }
                if (includeZeroProbabilityActions || actionProbability > 0)
                {
                    var nextHistoryPoint = historyPointCopy.GetBranch(Navigation, actionByte, GameDefinition.DecisionsExecutionOrder[nextDecisionIndex], nextDecisionIndex); // must use a copy because it's an anonymous method (but this won't be executed much so it isn't so costly). Note that we couldn't use switch-to-branch approach here because all threads are sharing historyPointCopy variable.
                    double nextProbability = probability * actionProbability;
                    await ProcessAllPaths_Recursive(nextHistoryPoint, completedGameProcessor, actionStrategy, nextProbability, actionByte, nextDecisionIndex);
                }
            });
        }

        public double[] GetAverageUtilities()
        {
            double[] cumulated = new double[NumNonChancePlayers];
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, InformationSets, ChanceNodes, FinalUtilitiesNodes, GetGameState, EvolutionSettings);
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            GetAverageUtilities_Helper(ref historyPoint, cumulated, 1.0);
            return cumulated;
        }

        public unsafe void GetAverageUtilities_Helper(ref HistoryPoint historyPoint, double[] cumulated, double prob)
        {
            IGameState gameState = historyPoint.GetGameStateForCurrentPlayer(Navigation);
            if (gameState is FinalUtilitiesNode finalUtilities)
            {
                for (byte p = 0; p < NumNonChancePlayers; p++)
                    cumulated[p] += finalUtilities.Utilities[p] * prob;
            }
            else if (gameState is ChanceNode chanceNode)
            {
                byte numPossibilities = GameDefinition.DecisionsExecutionOrder[chanceNode.DecisionIndex].NumPossibleActions;
                for (byte action = 1; action <= numPossibilities; action++)
                {
                    double actionProb = chanceNode.GetActionProbability(action);
                    if (actionProb > 0)
                    {
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
                        GetAverageUtilities_Helper(ref nextHistoryPoint, cumulated, prob * actionProb);
                    }
                }
            }
            else if (gameState is InformationSetNode informationSet)
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

        public async Task<ReportCollection> GenerateReportsByPlaying(Func<GamePlayer, Func<Decision, GameProgress, byte>, Task> generator)
        {
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, InformationSets, ChanceNodes, FinalUtilitiesNodes, GetGameState, EvolutionSettings);
            var simpleReportDefinitions = GameDefinition.GetSimpleReportDefinitions();
            int simpleReportDefinitionsCount = simpleReportDefinitions.Count();
            ReportsBeingGenerated = new SimpleReport[simpleReportDefinitionsCount];
            GamePlayer.ReportingMode = true;
            ReportCollection reportCollection = new ReportCollection();
            for (int i = 0; i < simpleReportDefinitionsCount; i++)
            {
                ReportsBeingGenerated[i] = new SimpleReport(simpleReportDefinitions[i], simpleReportDefinitions[i].DivideColumnFiltersByImmediatelyEarlierReport ? ReportsBeingGenerated[i - 1] : null);
                await generator(GamePlayer, simpleReportDefinitions[i].ActionsOverride);
                ReportCollection result = ReportsBeingGenerated[i].BuildReport();
                reportCollection.Add(result);
                ReportsBeingGenerated[i] = null; // so we don't keep adding GameProgress to this report
            }
            GamePlayer.ReportingMode = false;
            ReportsBeingGenerated = null;
            return reportCollection;
        }

        private StatCollectorArray UtilityCalculationsArray;

        private async Task GenerateReports_AllPaths(GamePlayer player, Func<Decision, GameProgress, byte> actionOverride)
        {
            UtilityCalculationsArray = new StatCollectorArray();
            UtilityCalculationsArray.Initialize(NumNonChancePlayers);
            // start Task Parallel Library consumer/producer pattern
            // we'll set up step1, step2, and step3 (but not in that order, since step 1 triggers step 2)
            var step2_buffer = new BufferBlock<Tuple<GameProgress, double>>(new DataflowBlockOptions { BoundedCapacity = 10_000 });
            var step3_consumer = AddGameProgressToReport(step2_buffer);
            async Task step1_playPath(HistoryPoint completedGame, double probabilityOfPath)
            {
                // play each path and then asynchronously consume the result, including the probability of the game path
                List<byte> actions = completedGame.GetActionsToHere(Navigation);
                (GameProgress progress, _) = player.PlayPath(actions, false);
                // do the simple aggregation of utilities. note that this is different from the value returned by vanilla, since that uses regret matching, instead of average strategies.
                double[] utilities = GetUtilities(ref completedGame);
                UtilityCalculationsArray.Add(utilities, probabilityOfPath);
                // consume the result for reports
                bool messageAccepted;
                do
                {
                    // whether the message was accepted (though it will be rejected then only in unusual circumstances, not just because the buffer
                    // is full).
                    messageAccepted = await step2_buffer.SendAsync(new Tuple<GameProgress, double>(progress, probabilityOfPath));
                } while (!messageAccepted);
            };
            // Now, we have to send the paths through all of these steps and make sure that step 3 is completely finished.
            var startHistoryPoint = GetStartOfGameHistoryPoint();
            await ProcessAllPathsAsync(startHistoryPoint, (historyPoint, pathProbability) => step1_playPath(historyPoint, pathProbability));
            step2_buffer.Complete(); // tell consumer nothing more to be produced
            await step3_consumer; // wait until all have been processed

            for (int p = 0; p < NumNonChancePlayers; p++)
                if (Math.Abs(UtilityCalculationsArray.StatCollectors[p].sumOfWeights - 1.0) > 0.001)
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
                    {
                        ReportsBeingGenerated[i]?.ProcessGameProgress(toProcess.Item1, toProcess.Item2);
                    }
            }
        }

        private double[] GetBestResponseImprovements_RandomPaths()
        {
            throw new NotImplementedException();
            // basic idea: for each player, WalkTree, try to override each action, play random paths, and see what score is. 
        }

        private async Task<double[]> CalculateUtility_RandomPaths(GamePlayer player, Func<Decision, GameProgress, byte> actionOverride)
        {
            var gameProgresses = GetRandomCompleteGames(player, EvolutionSettings.NumRandomIterationsForUtilityCalculation, actionOverride);
            // start Task Parallel Library consumer/producer pattern
            // we'll set up step1, step2, and step3 (but not in that order, since step 1 triggers step 2)
            var step2_buffer = new BufferBlock<Tuple<GameProgress, double>>(new DataflowBlockOptions { BoundedCapacity = 10000 });
            var step3_consumer = ProcessUtilities(step2_buffer);
            foreach (var gameProgress in gameProgresses)
            {
                bool result = false;
                while (!result)
                    result = await step2_buffer.SendAsync(new Tuple<GameProgress, double>(gameProgress, 1.0));
            }
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

        public unsafe void GetNextPiValues(double* currentPiValues, byte playerIndex, double probabilityToMultiplyBy, bool changeOtherPlayers, double* nextPiValues)
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

        public unsafe void GetInitialPiValues(double* initialPiValues)
        {
            for (byte p = 0; p < NumNonChancePlayers; p++)
                initialPiValues[p] = 1.0;
        }

        #endregion

        #region Distribution of chance decisions

        // This allows for the distribution of chance decisions to economize on optimization time. The idea is best explained through an example: Suppose that a chance decision produces a "true value" and then other chance decisions produce estimates of the true value for each player. Later, in some circumstances, another chance decision determines a payout for players based in part on that true value. Ordinarily, optimization would require us to go through each possible true value, plus each permutation of estimates of the true value. The goal here is to make it so that we can traverse the tree just once, playing a dummy true value (action = 1). All that needs to be changed are the chance probabilities that ultimately determine payouts, so that these probabilities are weighted by the probabilities that would obtain with true values. For example, if the estimates are 1 then the probabilities that would obtain if the true value is 1 will be given greater weight than the probabilities that would obtain if the true value is 10.
        // The principal challenge is to determine the probabilities that must be played in the distributor chance decision. The approach here is to initialize by walking once through the entire tree (skipping some intermediate non-chance decisions once we have gotten to the chance decisions). We keep track of the probability that chance plays to each chance point. When we arrive at a nondistributed chance decision (such as the estimates), we aggregate a measure that will be unique for every tuple of such estimates. When we arrive at a distributor chance decision (such as the one that determines ultimate payouts), we find the corresponding distributor chance decision node with action = 1 for the nondistributed chance decisions. At that node, we keep a table linking the nondistributed chance decisions aggregate measure to probabilities. At the corresponding node, we increment the probabilities that would obtain on the nondistributed chance decision reached multiplied by the probability of playing to that point. 
        // An additional challenge is to determine the probability of each nondistributed action. We use essentially the same approach, incrementing these probabilities in the first chance settings node corresponding to the nondistributed action. 

        public void DistributeChanceDecisions()
        {
            if (!EvolutionSettings.DistributeChanceDecisions || !GameDefinition.DecisionsExecutionOrder.Any(x => x.DistributorChanceDecision))
                return; // nothing to do
            Stopwatch s = new Stopwatch();
            s.Start();
            TabbedText.WriteLine($"Distributing chance decisions...");
            var chanceNodeAggregationDictionary = new Dictionary<string, ChanceNodeUnequalProbabilities>();
            var informationSetAggregationDictionary = new Dictionary<string, InformationSetNode>();
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            DistributeChanceDecisions_WalkNode(ref historyPoint, 1.0 /* 100% probability of playing to beginning */, 0 /* no nondistributed actions yet */, "", chanceNodeAggregationDictionary, informationSetAggregationDictionary);
            foreach (var chanceNode in Navigation.ChanceNodes)
                if (chanceNode is ChanceNodeUnequalProbabilities unequal)
                    unequal.NormalizeDistributorChanceInputProbabilities();
            TabbedText.WriteLine($"...{s.ElapsedMilliseconds} milliseconds");
        }

        private unsafe bool DistributeChanceDecisions_WalkNode(ref HistoryPoint historyPoint, double piChance, int distributorChanceInputs, string distributedActionsString, Dictionary<string, ChanceNodeUnequalProbabilities> chanceNodeAggregationDictionary, Dictionary<string, InformationSetNode> informationSetAggregationDictionary)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            GameStateTypeEnum gameStateTypeEnum = gameStateForCurrentPlayer.GetGameStateType();
            bool result = false;
            TabbedText.Tabs++;
            if (gameStateTypeEnum == GameStateTypeEnum.Chance)
                result = DistributeChanceDecisions_ChanceNode(ref historyPoint, piChance, distributorChanceInputs, distributedActionsString, chanceNodeAggregationDictionary, informationSetAggregationDictionary);
            else if (gameStateTypeEnum == GameStateTypeEnum.InformationSet)
                result = DistributeChanceDecisions_DecisionNode(ref historyPoint, piChance, distributorChanceInputs, distributedActionsString, chanceNodeAggregationDictionary, informationSetAggregationDictionary);
            else
                result = false; // don't stop non-chance decisions; we need to backtrack and then move forwards to get to a chance decision
            TabbedText.Tabs--;
            return result;
        }

        private unsafe bool DistributeChanceDecisions_DecisionNode(ref HistoryPoint historyPoint, double piChance, int distributorChanceInputs, string distributedActionsString, Dictionary<string, ChanceNodeUnequalProbabilities> chanceNodeAggregationDictionary, Dictionary<string, InformationSetNode> informationSetAggregationDictionary)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            var informationSet = (InformationSetNode)gameStateForCurrentPlayer;
            //TabbedText.WriteLine($"Information set {informationSet.Decision.Name} ({informationSet.InformationSetNumber})");
            byte decisionNum = informationSet.DecisionIndex;
            byte numPossibleActions = (byte)informationSet.NumPossibleActions;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                int distributorChanceInputsNext = distributorChanceInputs;
                if (informationSet.Decision.DistributorChanceInputDecision)
                    distributorChanceInputsNext += action * informationSet.Decision.DistributorChanceInputDecisionMultiplier;
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                bool stopNonChanceDecisions = DistributeChanceDecisions_WalkNode(ref nextHistoryPoint, piChance, distributorChanceInputsNext, distributedActionsString, chanceNodeAggregationDictionary, informationSetAggregationDictionary);
                if (stopNonChanceDecisions && !informationSet.Decision.DistributorChanceInputDecision) // once we have returned from a distributor decision and are working backwards, we just need to get back to the previous chance decision, so we don't need to walk every possible player action in the tree
                    return stopNonChanceDecisions;
            }
            return false; // don't stop non-chance decisions
        }

        private unsafe bool DistributeChanceDecisions_ChanceNode(ref HistoryPoint historyPoint, double piChance, int distributorChanceInputs, string distributedActionsString, Dictionary<string, ChanceNodeUnequalProbabilities> chanceNodeAggregationDictionary, Dictionary<string, InformationSetNode> informationSetAggregationDictionary)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            ChanceNode chanceNode = (ChanceNode)gameStateForCurrentPlayer;
            //TabbedText.WriteLine($"Chance node {chanceNode.Decision.Name}"); 
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
            var historyPointCopy = historyPoint; // can't use historyPoint in anonymous method below. This is costly, so it might be worth optimizing if we use MultiplicativeWeightsVanillaCFR much.

            Decision decision = chanceNode.Decision;
            if (decision.DistributorChanceDecision || decision.DistributorChanceInputDecision)
            {
                // We need first to figure out what the uneven probabilities are in this chance node (i.e., where the distributed actions can be anything)
                var unequal = (ChanceNodeUnequalProbabilities)chanceNode; // required to be unequal if distributor
                var probabilities = unequal.Probabilities; // this should be already set as the standard unequal chance probabilities (independent of the nondistributed decisions)
                // Now we need to register this set of probabilities with the corresponding chance node where the distributed actions are 1. The idea is that when optimizing, we'll only have to use action=1 for the distributed decisions (we'll still have to visit all nondistributed decisions).
                ChanceNodeUnequalProbabilities correspondingNode;
                string key = distributedActionsString + "decision" + decision.DecisionByteCode + (decision.DistributorChanceInputDecision ? "_distributorchanceinputs:" + distributorChanceInputs.ToString() : "");
                if (chanceNodeAggregationDictionary.ContainsKey(key))
                    correspondingNode = chanceNodeAggregationDictionary[key];
                else
                {
                    correspondingNode = unequal; // this must be the flattened one
                    chanceNodeAggregationDictionary[key] = unequal;
                }
                //TabbedText.WriteLine($"Registering decision {decision.Name} with probability {piChance}: distributor chance inputs {distributorChanceInputs} key {key} probabilities to distribute: {String.Join(",", probabilities)}");
                correspondingNode.RegisterProbabilityForDistributorChanceInput(piChance, distributorChanceInputs, probabilities);
                if (decision.DistributorChanceDecision)
                {
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
                        DistributeChanceDecisions_WalkNode(ref nextHistoryPoint, piChance /* because one distributor decision will not depend on another, we won't adjust piChance */, distributorChanceInputs, distributedActionsString, chanceNodeAggregationDictionary, informationSetAggregationDictionary);
                    };

                    return true; // we're going back up the tree after a distributor decision, so we don'tneed to walk through all other action decisions
                }
            }
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                int distributorChanceInputsNext = distributorChanceInputs;
                if (chanceNode.Decision.DistributorChanceInputDecision)
                    distributorChanceInputsNext += action * chanceNode.Decision.DistributorChanceInputDecisionMultiplier;
                double piChanceNext = piChance;
                double piChanceNextExcludingNondistributed = piChance;
                double actionProbability = chanceNode.GetActionProbability(action);
                piChanceNext *= actionProbability;
                if (!chanceNode.Decision.DistributorChanceInputDecision)
                    piChanceNextExcludingNondistributed *= actionProbability;
                //if (chanceNode.Decision.Name.Contains("PostPrimary") || chanceNode.Decision.Name.Contains("LiabilitySignal") || chanceNode.Decision.Name.Contains("LiabilityStrength") || chanceNode.Decision.Name.Contains("PrePrimary"))
                //    TabbedText.WriteLine($"{chanceNode.Decision.Name}: action: {action} probability: {actionProbability} cumulative probability {piChanceNext}");
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
                bool stopNonChanceDecisions = DistributeChanceDecisions_WalkNode(ref nextHistoryPoint, piChanceNext, distributorChanceInputsNext, chanceNode.Decision.DistributedChanceDecision ? distributedActionsString + chanceNode.DecisionByteCode + ":1;" : distributedActionsString, chanceNodeAggregationDictionary, informationSetAggregationDictionary);
                if (stopNonChanceDecisions && chanceNode.Decision.NumPossibleActions == 1)
                    return true; // this is just a dummy chance decision, so we need to backtrack to a real chance decision
            };

            return false;
        }



        #endregion

        #region Game value calculation

        [NonSerialized]
        Type CorrelatedEquilibriumCalculatorType = null;
        bool CorrelatedEquilibriumCodeIsPrecompiled = false;

        public void DoCorrelatedEquilibriumCalculations(int currentIterationNumber)
        {
            if (CorrelatedEquilibriumCalculatorType == null)
            {
                if (CorrelatedEquilibriumCodeIsPrecompiled)
                {
                    TabbedText.WriteLine($"Using precompiled code for correlated equilibrium.");
                    CorrelatedEquilibriumCalculatorType = Type.GetType("CorrEqCalc.AutogeneratedCalculator");
                }
                else
                    CorrelatedEquilibriumCalculations_GenerateCode();
            }
            CorrelatedEquilibriumCalculations_RunGeneratedCode(currentIterationNumber);
        }

        public void CorrelatedEquilibriumCalculations_GenerateCode()
        {
            StringBuilder codeGenerationBuilder = new StringBuilder();
            codeGenerationBuilder.AppendLine($@"using System;
    using System.Collections.Generic;
    using ACESim;

    namespace CorrEqCalc
    {{
    public static class AutogeneratedCalculator
    {{");
            CorrelatedEquilibriumCalculation_SwitchOnByteFunctions(codeGenerationBuilder, 15);
            // We want to calculate the following:
            // both players' utilities for correlated equilibrium vs. correlated equilibrium
            // plaintiff's utility for best response vs. correlated equilibrium
            // defendant's utility for correlated equilibrium vs. best response
            // We will prepare to do this by preparing a single very long expression for each of the four numbers that we want, and then we'll have a function that will return each of them.
            codeGenerationBuilder.AppendLine("public static void DoCalc(int cei, List<FinalUtilitiesNode> f, List<ChanceNode> c, List<InformationSetNode> n, out double p0CvC, out double p1CvC, out double p0BRvC, out double p1CvBR)");
            codeGenerationBuilder.AppendLine($"{{");
            codeGenerationBuilder.Append("p0CvC = ");
            CorrelatedEquilibriumCalculationString_Tree(codeGenerationBuilder, 0, ActionStrategies.CorrelatedEquilibrium);
            codeGenerationBuilder.AppendLine(";");
            codeGenerationBuilder.Append("p1CvC = ");
            CorrelatedEquilibriumCalculationString_Tree(codeGenerationBuilder, 1, ActionStrategies.CorrelatedEquilibrium);
            codeGenerationBuilder.AppendLine(";");
            codeGenerationBuilder.Append("p0BRvC = ");
            CorrelatedEquilibriumCalculationString_Tree(codeGenerationBuilder, 0, ActionStrategies.BestResponseVsCorrelatedEquilibrium);
            codeGenerationBuilder.AppendLine(";");
            codeGenerationBuilder.Append("p1CvBR = ");
            CorrelatedEquilibriumCalculationString_Tree(codeGenerationBuilder, 1, ActionStrategies.CorrelatedEquilibriumVsBestResponse);
            codeGenerationBuilder.AppendLine(";");
            codeGenerationBuilder.AppendLine($"}}");

            codeGenerationBuilder.AppendLine($@"}}
}}");
            string code = codeGenerationBuilder.ToString();
            CorrelatedEquilibriumCalculatorType = StringToCode.LoadCode(code, "CorrEqCalc.AutogeneratedCalculator", new List<Type>() { typeof(CounterfactualRegretMinimization), typeof(System.Collections.ArrayList) });


        }

        public void CorrelatedEquilibriumCalculations_RunGeneratedCode(int currentIterationNumber)
        {
            int numPastValues = InformationSets.First().LastPastValueIndexRecorded + 1;
            const int p0CvC_Index = 0;
            const int p1CvC_Index = 1;
            const int p0BRvC_Index = 2;
            const int p1CvBR_Index = 3;
            const int avgStratContribution_Index = 4;
            double[,] correlatedEquilibriumResults = new double[numPastValues, 5];
            double[,] correlatedEquilibriumResults_Cumulative = new double[numPastValues, 5];
            double[,] correlatedEquilibriumResults_ReverseCumulative = new double[numPastValues, 5];

            for (int correlatedEquilibriumIterationIndex = 0; correlatedEquilibriumIterationIndex < numPastValues; correlatedEquilibriumIterationIndex++)
            {
                var method = CorrelatedEquilibriumCalculatorType.GetMethod("DoCalc");
                object[] parameters = new object[] { correlatedEquilibriumIterationIndex, FinalUtilitiesNodes, ChanceNodes, InformationSets, null, null, null, null };
                method.Invoke(null, parameters);
                correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, p0CvC_Index] = (double)parameters[4];
                correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, p1CvC_Index] = (double)parameters[5];
                correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, p0BRvC_Index] = (double)parameters[6];
                correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, p1CvBR_Index] = (double)parameters[7];
                int correspondingIteration = (int)(((double)(correlatedEquilibriumIterationIndex + 1) / (double)numPastValues) * (double)(EvolutionSettings.TotalIterations));
                double averageStrategyAdjustment = EvolutionSettings.Discounting_Gamma_ForIteration(correspondingIteration);
                double averageStrategyAdjustmentAsPct = EvolutionSettings.Discounting_Gamma_AsPctOfMax(correspondingIteration);
                correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, avgStratContribution_Index] = averageStrategyAdjustment;
            }
            // calculate reverse cumulative -- this shows aggregates from here on. The point of this is to compare the correlated equilibrium strategy from some point forward to the best response. If the correlated equilibrium strategy performs well at this point but not later, this may indicate that there is a cycle and that as the number of iterations -> infinity, the correlated equilibrium strategy performs well.
            for (int j = 0; j < 4; j++)
            {
                double cumWeight = 0;
                double cumValueTimesWeight = 0;
                for (int correlatedEquilibriumIterationIndex = numPastValues - 1; correlatedEquilibriumIterationIndex >= 0; correlatedEquilibriumIterationIndex--)
                {
                    double weightThisItem = correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, avgStratContribution_Index];
                    double valueThisItem = correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, j];
                    cumWeight += weightThisItem;
                    cumValueTimesWeight += valueThisItem * weightThisItem;
                    correlatedEquilibriumResults_ReverseCumulative[correlatedEquilibriumIterationIndex, avgStratContribution_Index] = weightThisItem;
                    correlatedEquilibriumResults_ReverseCumulative[correlatedEquilibriumIterationIndex, j] = cumValueTimesWeight / cumWeight;
                }
                cumWeight = 0;
                cumValueTimesWeight = 0;
                for (int correlatedEquilibriumIterationIndex = 0; correlatedEquilibriumIterationIndex < numPastValues; correlatedEquilibriumIterationIndex++)
                {
                    double weightThisItem = correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, avgStratContribution_Index];
                    double valueThisItem = correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, j];
                    cumWeight += weightThisItem;
                    cumValueTimesWeight += valueThisItem * weightThisItem;
                    correlatedEquilibriumResults_Cumulative[correlatedEquilibriumIterationIndex, avgStratContribution_Index] = weightThisItem;
                    correlatedEquilibriumResults_Cumulative[correlatedEquilibriumIterationIndex, j] = cumValueTimesWeight / cumWeight;
                }
            }

            string resultString(int correlatedEquilibriumIterationIndex, string iterString)
            {
                double p0CvC = correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, p0CvC_Index];
                double p1CvC = correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, p1CvC_Index];
                double p0BRvC = correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, p0BRvC_Index];
                double p1CvBR = correlatedEquilibriumResults[correlatedEquilibriumIterationIndex, p1CvBR_Index];

                double p0CvC_ToHere = correlatedEquilibriumResults_Cumulative[correlatedEquilibriumIterationIndex, p0CvC_Index];
                double p1CvC_ToHere = correlatedEquilibriumResults_Cumulative[correlatedEquilibriumIterationIndex, p1CvC_Index];
                double p0BRvC_ToHere = correlatedEquilibriumResults_Cumulative[correlatedEquilibriumIterationIndex, p0BRvC_Index];
                double p1CvBR_ToHere = correlatedEquilibriumResults_Cumulative[correlatedEquilibriumIterationIndex, p1CvBR_Index];

                double p0CvC_FromHere = correlatedEquilibriumResults_ReverseCumulative[correlatedEquilibriumIterationIndex, p0CvC_Index];
                double p1CvC_FromHere = correlatedEquilibriumResults_ReverseCumulative[correlatedEquilibriumIterationIndex, p1CvC_Index];
                double p0BRvC_FromHere = correlatedEquilibriumResults_ReverseCumulative[correlatedEquilibriumIterationIndex, p0BRvC_Index];
                double p1CvBR_FromHere = correlatedEquilibriumResults_ReverseCumulative[correlatedEquilibriumIterationIndex, p1CvBR_Index];

                return $"Correlated eq. utilities ({iterString}): {p0CvC.ToSignificantFigures(6)} vs. {p1CvC.ToSignificantFigures(6)} p0BR: {p0BRvC.ToSignificantFigures(6)} p1BR: {p1CvBR.ToSignificantFigures(6)} p0BR improvement: {(p0BRvC - p0CvC).ToSignificantFigures(6)} p1BR improvement {(p1CvBR - p1CvC).ToSignificantFigures(6)} TO HERE: {p0CvC_ToHere.ToSignificantFigures(6)} vs. {p1CvC_ToHere.ToSignificantFigures(6)} p0BR: {p0BRvC_ToHere.ToSignificantFigures(6)} p1BR: {p1CvBR_ToHere.ToSignificantFigures(6)} p0BR improvement: {(p0BRvC_ToHere - p0CvC_ToHere).ToSignificantFigures(6)} p1BR improvement {(p1CvBR_ToHere - p1CvC_ToHere).ToSignificantFigures(6)} FROM HERE: {p0CvC_FromHere.ToSignificantFigures(6)} vs. {p1CvC_FromHere.ToSignificantFigures(6)} p0BR: {p0BRvC_FromHere.ToSignificantFigures(6)} p1BR: {p1CvBR_FromHere.ToSignificantFigures(6)} p0BR improvement: {(p0BRvC_FromHere - p0CvC_FromHere).ToSignificantFigures(6)} p1BR improvement {(p1CvBR_FromHere - p1CvC_FromHere).ToSignificantFigures(6)} ";
            }

            for (int correlatedEquilibriumIterationIndex = 0; correlatedEquilibriumIterationIndex < numPastValues; correlatedEquilibriumIterationIndex++)
            {
                int correspondingIteration = (int)(((double)(correlatedEquilibriumIterationIndex + 1) / (double)numPastValues) * (double)(currentIterationNumber));
                string result = resultString(correlatedEquilibriumIterationIndex, correspondingIteration.ToString());
                TabbedText.WriteLine(result);
            }
        }

        public void CorrelatedEquilibriumCalculation_SwitchOnByteFunctions(
            StringBuilder codeGenerationBuilder, byte maxNumActionsUsed)
        {
            for (byte b = (byte)1; b <= maxNumActionsUsed; b++)
                CorrelatedEquilibriumCalculation_SwitchOnByteFunction(codeGenerationBuilder, b);
        }

        public void CorrelatedEquilibriumCalculation_SwitchOnByteFunction(StringBuilder codeGenerationBuilder, byte numActions)
        {
            codeGenerationBuilder.Append($"public static double SwitchOnByte{numActions}(byte b");
            for (int i = 1; i <= numActions; i++)
                codeGenerationBuilder.Append($", double c{i}");
            codeGenerationBuilder.AppendLine(")");
            codeGenerationBuilder.AppendLine("{");
            for (int i = 1; i <= numActions; i++)
                codeGenerationBuilder.AppendLine($"if (b == {i}) return c{i};");
            codeGenerationBuilder.AppendLine("throw new NotSupportedException();");
            codeGenerationBuilder.AppendLine("}");
        }

        public void CorrelatedEquilibriumCalculationString_Tree(StringBuilder codeGenerationBuilder, byte player, ActionStrategies actionStrategy)
        {
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            CorrelatedEquilibriumCalculation_Node(codeGenerationBuilder, ref historyPoint, player, actionStrategy, 0);
        }

        public void CorrelatedEquilibriumCalculation_Node(StringBuilder codeGenerationBuilder, ref HistoryPoint historyPoint, byte player, ActionStrategies actionStrategy, int distributorChanceInputs)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilitiesNode finalUtilities = (FinalUtilitiesNode)gameStateForCurrentPlayer;
                codeGenerationBuilder.Append($"f[{finalUtilities.FinalUtilitiesNodeNumber}].Utilities[{player}]");
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                CorrelatedEquilibriumCalculation_ChanceNode(codeGenerationBuilder, ref historyPoint, player, actionStrategy, distributorChanceInputs);
            }
            else
                CorrelatedEquilibriumCalculation_DecisionNode(codeGenerationBuilder, ref historyPoint, player, actionStrategy, distributorChanceInputs);
        }

        public void CorrelatedEquilibriumCalculation_ChanceNode(StringBuilder codeGenerationBuilder, ref HistoryPoint historyPoint, byte player, ActionStrategies actionStrategy, int distributorChanceInputs)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            ChanceNode chanceNode = (ChanceNode)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
            byte numPossibleActionsToExplore = numPossibleActions;
            if (EvolutionSettings.DistributeChanceDecisions && chanceNode.Decision.DistributedChanceDecision)
                numPossibleActionsToExplore = 1;
            for (byte action = 1; action <= numPossibleActionsToExplore; action++)
            {
                int distributorChanceInputsNext = distributorChanceInputs;
                if (chanceNode.Decision.DistributorChanceInputDecision)
                    distributorChanceInputsNext += action * chanceNode.Decision.DistributorChanceInputDecisionMultiplier;
                bool isDistributed = (EvolutionSettings.DistributeChanceDecisions && chanceNode.Decision.DistributedChanceDecision);
                // if it is distributed, action probability is 1
                if (!isDistributed)
                {
                    string distributorChanceInputsString = EvolutionSettings.DistributeChanceDecisions ? $", {distributorChanceInputs}" : "";
                    codeGenerationBuilder.Append($"c[{chanceNode.ChanceNodeNumber}].GetActionProbability({action}{distributorChanceInputsString}) * ");
                }
                codeGenerationBuilder.Append(" ( ");
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
                CorrelatedEquilibriumCalculation_Node(codeGenerationBuilder, ref nextHistoryPoint, player, actionStrategy, distributorChanceInputsNext);
                codeGenerationBuilder.Append(" ) ");
                if (action < numPossibleActionsToExplore)
                    codeGenerationBuilder.Append(" + ");
            }
        }

        public void CorrelatedEquilibriumCalculation_DecisionNode(StringBuilder codeGenerationBuilder, ref HistoryPoint historyPoint, byte player, ActionStrategies actionStrategy, int distributorChanceInputs)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            InformationSetNode informationSetNode = (InformationSetNode)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(informationSetNode.DecisionIndex);
            byte numPossibleActionsToExplore = numPossibleActions;
            byte playerAtNode = informationSetNode.PlayerIndex; // note that player is the player whose utility we are seeking

            bool isBestResponse = actionStrategy == ActionStrategies.BestResponse || (actionStrategy == ActionStrategies.BestResponseVsCorrelatedEquilibrium && playerAtNode == 0) || (actionStrategy == ActionStrategies.CorrelatedEquilibriumVsBestResponse && playerAtNode == 1);
            bool isCorrelatedEquilibrium = actionStrategy == ActionStrategies.CorrelatedEquilibrium || (actionStrategy == ActionStrategies.BestResponseVsCorrelatedEquilibrium && playerAtNode == 1) || (actionStrategy == ActionStrategies.CorrelatedEquilibriumVsBestResponse && playerAtNode == 0);
            if (NumNonChancePlayers > 2 || (!isBestResponse && !isCorrelatedEquilibrium))
                throw new NotSupportedException(); // right now, using this just for correlated equilibrium & best response calculations after the game tree has already been defined

            string nodeString = $"n[{informationSetNode.InformationSetNodeNumber}]";

            if (numPossibleActionsToExplore == 1)
            {
                int distributorChanceInputsNext = distributorChanceInputs;
                if (informationSetNode.Decision.DistributorChanceInputDecision)
                    distributorChanceInputsNext += 1 * informationSetNode.Decision.DistributorChanceInputDecisionMultiplier;
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, 1, informationSetNode.Decision, informationSetNode.DecisionIndex);
                codeGenerationBuilder.Append(" ( ");
                CorrelatedEquilibriumCalculation_Node(codeGenerationBuilder, ref nextHistoryPoint, player, actionStrategy, distributorChanceInputsNext);
                codeGenerationBuilder.Append(" ) ");
                return;
            }

            if (isBestResponse)
            {
                codeGenerationBuilder.Append($"SwitchOnByte{numPossibleActionsToExplore}({nodeString}.BackupBestResponseAction");
                //codeGenerationBuilder.Append($"{nodeString}.BackupBestResponseAction switch {{ ");
                for (byte action = 1; action <= numPossibleActionsToExplore; action++)
                {
                    int distributorChanceInputsNext = distributorChanceInputs;
                    if (informationSetNode.Decision.DistributorChanceInputDecision)
                        distributorChanceInputsNext += action * informationSetNode.Decision.DistributorChanceInputDecisionMultiplier;
                    codeGenerationBuilder.Append($", ( ");
                    HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSetNode.Decision, informationSetNode.DecisionIndex);
                    CorrelatedEquilibriumCalculation_Node(codeGenerationBuilder, ref nextHistoryPoint, player, actionStrategy, distributorChanceInputsNext);
                    codeGenerationBuilder.Append(" ) ");
                }

                codeGenerationBuilder.Append($") ");
            }
            else
            {
                for (byte action = 1; action <= numPossibleActionsToExplore; action++)
                {
                    int distributorChanceInputsNext = distributorChanceInputs;
                    if (informationSetNode.Decision.DistributorChanceInputDecision)
                        distributorChanceInputsNext += action * informationSetNode.Decision.DistributorChanceInputDecisionMultiplier;
                    codeGenerationBuilder.Append($"({nodeString}.PVP(cei, {action}, () => (");
                    HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSetNode.Decision, informationSetNode.DecisionIndex);
                    CorrelatedEquilibriumCalculation_Node(codeGenerationBuilder, ref nextHistoryPoint, player, actionStrategy, distributorChanceInputsNext);
                    codeGenerationBuilder.Append(")))"); // end function within PVP and PVP itself, plus surrounding parens
                    if (action < numPossibleActionsToExplore)
                        codeGenerationBuilder.Append(" + ");
                }
            }
        }

        #endregion

        #region General tree walk

        bool TraceTreeWalk = false; 

        public Back TreeWalk_Tree<Forward, Back>(ITreeNodeProcessor<Forward, Back> processor, Forward forward = default)
        {
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            return TreeWalk_Node(processor, null, 0, 0, forward, 0, ref historyPoint);
        }

        public Back TreeWalk_Node<Forward, Back>(ITreeNodeProcessor<Forward, Back> processor, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, Forward forward, int distributorChanceInputs, ref HistoryPoint historyPoint)
        {
            TabbedText.Tabs++;
            IGameState gameState = GetGameState(ref historyPoint);
            Back b = default;
            switch (gameState)
            {
                case ChanceNode c:
                    b = TreeWalk_ChanceNode(processor, c, predecessor, predecessorAction, predecessorDistributorChanceInputs, forward, distributorChanceInputs, ref historyPoint);
                    break;
                case InformationSetNode n:
                    b = TreeWalk_DecisionNode(processor, n, predecessor, predecessorAction, predecessorDistributorChanceInputs, forward, distributorChanceInputs, ref historyPoint);
                    break;
                case FinalUtilitiesNode f:
                    if (TraceTreeWalk)
                        TabbedText.WriteLine($"{f}");
                    b = processor.FinalUtilities_TurnAround(f, predecessor, predecessorAction, predecessorDistributorChanceInputs, forward);
                    break;
                default:
                    throw new NotSupportedException();
            }
            TabbedText.Tabs--;
            return b;
        }

        public Back TreeWalk_ChanceNode<Forward, Back>(ITreeNodeProcessor<Forward, Back> processor, ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, Forward forward, int distributorChanceInputs, ref HistoryPoint historyPoint)
        {
            Forward nextForward = processor.ChanceNode_Forward(chanceNode, predecessor, predecessorAction, predecessorDistributorChanceInputs, forward, distributorChanceInputs);
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
            byte numPossibleActionsToExplore = numPossibleActions;
            bool isDistributedChanceDecision = EvolutionSettings.DistributeChanceDecisions && chanceNode.Decision.DistributedChanceDecision;
            if (isDistributedChanceDecision)
                numPossibleActionsToExplore = 1;
            List<Back> fromSuccessors = new List<Back>();
            for (byte action = 1; action <= numPossibleActionsToExplore; action++)
            {
                if (TraceTreeWalk)
                    TabbedText.WriteLine($"{chanceNode.Decision.Name} (C{chanceNode.ChanceNodeNumber}): {action} ({chanceNode.GetActionProbabilityString(distributorChanceInputs)})");
                bool isDistributorChanceInputDecision = EvolutionSettings.DistributeChanceDecisions && chanceNode.Decision.DistributorChanceInputDecision;
                int distributorChanceInputsNext = isDistributorChanceInputDecision ? distributorChanceInputs + action * chanceNode.Decision.DistributorChanceInputDecisionMultiplier : distributorChanceInputs;

                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
                var fromSuccessor = TreeWalk_Node(processor, chanceNode, action, distributorChanceInputs, nextForward, distributorChanceInputsNext, ref nextHistoryPoint);
                fromSuccessors.Add(fromSuccessor);

                if (isDistributedChanceDecision)
                    break;
            }
            return processor.ChanceNode_Backward(chanceNode, fromSuccessors, distributorChanceInputs);
        }

        public Back TreeWalk_DecisionNode<Forward, Back>(ITreeNodeProcessor<Forward, Back> processor, InformationSetNode informationSetNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, Forward forward, int distributorChanceInputs, ref HistoryPoint historyPoint)
        {
            Forward nextForward = processor.InformationSet_Forward(informationSetNode, predecessor, predecessorAction, predecessorDistributorChanceInputs, forward);
            byte numPossibleActions = NumPossibleActionsAtDecision(informationSetNode.DecisionIndex);
            byte numPossibleActionsToExplore = numPossibleActions;


            List<Back> fromSuccessors = new List<Back>();
            if (TraceTreeWalk)
                TabbedText.WriteLine($"{informationSetNode}");
            for (byte action = 1; action <= numPossibleActionsToExplore; action++)
            {
                if (TraceTreeWalk)
                    TabbedText.WriteLine($"{informationSetNode.Decision.Name} ({informationSetNode.InformationSetNodeNumber}): {action} (best response value = {informationSetNode.BestResponseOptions?[action - 1]}{(informationSetNode.LastBestResponseValue == action ? "*" : "")})");
                if (informationSetNode.Decision.DistributorChanceInputDecision)
                    throw new NotSupportedException(); // currently, we are only passing forward an array of distributor chance inputs from chance decisions, but we could adapt this to player decisions.
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSetNode.Decision, informationSetNode.DecisionIndex);
                var fromSuccessor = TreeWalk_Node(processor, informationSetNode, action, predecessorDistributorChanceInputs, nextForward, distributorChanceInputs, ref nextHistoryPoint);
                fromSuccessors.Add(fromSuccessor);
            }
            return processor.InformationSet_Backward(informationSetNode, fromSuccessors);
        }

        #endregion
    }
}
