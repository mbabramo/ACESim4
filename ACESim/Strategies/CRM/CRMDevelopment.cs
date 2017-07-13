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
        public List<Strategy> Strategies { get; set; }

        public EvolutionSettings EvolutionSettings { get; set; }

        public GameDefinition GameDefinition { get; set; }

        public IGameFactory GameFactory { get; set; }

        public GamePlayer GamePlayer { get; set; }

        public CurrentExecutionInformation CurrentExecutionInformation { get; set; }

        public InformationSetLookupApproach LookupApproach = InformationSetLookupApproach.CachedGameHistoryOnly;
        bool allowSkipEveryPermutationInitialization = true;
        public bool SkipEveryPermutationInitialization => (allowSkipEveryPermutationInitialization && (Navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || Navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame));

        public HistoryNavigationInfo Navigation;

        public int NumInitializedGamePaths = 0;
        public int NumRandomIterationsForReporting = 1000;

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

        public const int MaxNumPlayers = 4; // this affects fixed-size stack-allocated buffers
        public const int MaxPossibleActions = 100; // same

        const int TotalVanillaCFRIterations = 25;
        int? ReportEveryNIterations = 5;
        int? BestResponseEveryMIterations = 1000;

        public CRMDevelopment()
        {

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
                CurrentExecutionInformation = CurrentExecutionInformation
            };
        }

        #region Initialization

        public void DevelopStrategies()
        {
            Initialize();
            SolveVanillaCFR();
        }

        public unsafe void Initialize()
        {
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition);
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
            var DEBUG3 = ListExtensions.GetPointerAsList_255Terminated(actionsEnumerator);

            // Go through each non-chance decision point on this path and make sure that the information set tree extends there. We then store the regrets etc. at these points. 

            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            IEnumerable<InformationSetHistory> informationSetHistories = gameProgress.GameHistory.GetInformationSetHistoryItems();
            var DEBUG2 = informationSetHistories.ToList();
            foreach (var informationSetHistory in informationSetHistories)
            {
                var DEBUG = informationSetHistory.ToString();
                historyPoint.SetInformationIfNotSet(Navigation, gameProgress, informationSetHistory);
                historyPoint = historyPoint.GetBranch(Navigation, informationSetHistory.ActionChosen);
                var DEBUG_Actions = historyPoint.GetActionsToHereString(Navigation);
            }
            historyPoint.SetFinalUtilitiesAtPoint(Navigation, gameProgress);
        }

        public ICRMGameState GetGameState(HistoryPoint historyPoint)
        {
            var gameState = historyPoint.GetGameStateForCurrentPlayer(Navigation);
            if (gameState == null)
            {
                List<byte> actionsSoFar = historyPoint.GetActionsToHere(Navigation);
                (GameProgress progress, _) = GamePlayer.PlayPath(actionsSoFar, false);
                ProcessInitializedGameProgress(progress);
                gameState = historyPoint.GetGameStateForCurrentPlayer(Navigation);
                if (gameState == null)
                    throw new Exception("Internal error.");
            }
            return gameState;
        }

        #endregion

        #region Printing

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

        SimpleReport[] ReportsBeingGenerated = null;

        public string GenerateReports(Action<GamePlayer> generator)
        {
            Navigation = new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition);
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

        #region Vanilla CRM

        int VanillaCRMIteration; // controlled in SolveVanillaCRM
        bool TraceVanillaCRM = false;

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
            if (gameStateForCurrentPlayer is CRMFinalUtilities finalUtilities)
                return finalUtilities.Utilities[playerBeingOptimized];
            else if (gameStateForCurrentPlayer is CRMChanceNodeSettings)
                return VanillaCRM_ChanceNode(historyPoint, playerBeingOptimized, piValues, usePruning);
            else
                return VanillaCRM_DecisionNode(historyPoint, playerBeingOptimized, piValues, usePruning);
        }
        
        private unsafe double VanillaCRM_DecisionNode(HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues, bool usePruning)
        {
            double* nextPiValues = stackalloc double[MaxNumPlayers];
            var DEBUG = historyPoint.GetActionsToHere(Navigation);
            var DEBUG2 = historyPoint.ToString();

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
                    //TabbedText.WriteLine($"History point: {historyPoint}"); // DEBUG
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
                for (int i = 0; i < NumNonChancePlayers + 1; i++) // DEBUG -- do we need + 1?
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
                //TabbedText.WriteLine($"History point: {historyPoint}"); // DEBUG
                //TabbedText.WriteLine($"Next history point: {nextHistoryPoint}"); // DEBUG
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


        public unsafe void SolveVanillaCFR()
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
            GenerateReports(iteration, s);
        }

        private unsafe void GenerateReports(int iteration, Stopwatch s)
        {
            if (ReportEveryNIterations != null && iteration % ReportEveryNIterations == 0)
            {
                bool useRandomPaths = SkipEveryPermutationInitialization || NumInitializedGamePaths > NumRandomIterationsForReporting;
                Debug.WriteLine("");
                Debug.WriteLine($"Iteration {iteration} Milliseconds per iteration {(s.ElapsedMilliseconds / ((double)iteration + 1.0))}");
                MainReport(useRandomPaths);
                CompareBestResponse(iteration, useRandomPaths);
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
        }

        private unsafe void CompareBestResponse(int iteration, bool useRandomPaths)
        {
            if (BestResponseEveryMIterations != null && iteration % BestResponseEveryMIterations == 0)
                for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
                {
                    double bestResponseUtility = CalculateBestResponse(playerBeingOptimized, ActionStrategy);
                    double bestResponseImprovement = bestResponseUtility - UtilityCalculations[playerBeingOptimized].Average();
                    if (!useRandomPaths && bestResponseImprovement < -1E-15)
                        throw new Exception("Best response function worse."); // it can be slightly negative as a result of rounding error or if we are using random paths as a result of sampling error
                    Debug.WriteLine($"Player {playerBeingOptimized} utility with regret matching {UtilityCalculations[playerBeingOptimized].Average()} using best response against regret matching {bestResponseUtility} best response improvement {bestResponseImprovement}");
                }
        }



        #endregion
    }
}
