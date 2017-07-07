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

namespace ACESim
{
    public class CRMDevelopment : IStrategiesDeveloper
    {
        public List<Strategy> Strategies { get; set; }

        public EvolutionSettings EvolutionSettings { get; set; }

        public GameDefinition GameDefinition { get; set; }

        public IGameFactory GameFactory { get; set; }

        public CurrentExecutionInformation CurrentExecutionInformation { get; set; }

        /// <summary>
        /// A game history tree. On each internal node, the object contained is the information set of the player making the decision (or null for a chance decision). 
        /// On each leaf node, the object contained is an array of the players' terminal utilities.
        /// </summary>
        public NWayTreeStorageInternal<object> GameHistoryTree;

        public int NumNonChancePlayers;
        public int NumChancePlayers; // note that chance players MUST be indexed after nonchance players in the player list

        public const int MaxNumPlayers = 4; // this affects fixed-size stack-allocated buffers
        public const int MaxPossibleActions = 100; // same

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
            GamePlayer player = new GamePlayer(Strategies, GameFactory, EvolutionSettings.ParallelOptimization, GameDefinition);
            GameInputs inputs = GetGameInputs();

            // Create game trees
            GameHistoryTree = new NWayTreeStorageInternal<object>(null, GameDefinition.DecisionsExecutionOrder.First().NumPossibleActions);
            foreach (Strategy s in Strategies)
            {
                s.CreateInformationSetTree(GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.PlayerNumber == s.PlayerInfo.PlayerIndex)?.NumPossibleActions ?? (byte) 1);
            }
            
            int numPathsPlayed = player.PlayAllPaths(inputs, ProcessInitializedGameProgress);
            Debug.WriteLine($"Initialized. Total paths: {numPathsPlayed}");
            PrintSameGameResults(player, inputs);
        }

        unsafe void ProcessInitializedGameProgress(GameProgress progress)
        {
            // First, add the utilities at the end of the tree for this path.
            byte* actionsEnumerator = stackalloc byte[GameHistory.MaxNumActions];
            progress.GameHistory.GetActions(actionsEnumerator);


            double[] playerUtilities = progress.GetNonChancePlayerUtilities();
            // We are going to save this information in the GameHistoryTree, if applicable, and always in the resolution set's resolution tree.
            GameHistoryTree.SetValue(actionsEnumerator, true, playerUtilities); 
            var resolutionStrategy = Strategies[GameDefinition.PlayerIndex_ResolutionPlayer];
            byte* resolutionInformationSet = stackalloc byte[GameHistory.MaxInformationSetLengthPerPlayer];
            progress.GameHistory.GetPlayerInformation(GameDefinition.PlayerIndex_ResolutionPlayer, null /* get entire resolution set */, resolutionInformationSet);
            resolutionStrategy.SetInformationSetTreeValueIfNotSet(resolutionInformationSet, true, () => playerUtilities);

            // Go through each non-chance decision point on this path and make sure that the information set tree extends there. We then store the regrets etc. at these points. 
            NWayTreeStorage<object> historyPoint = GameHistoryTree;
            foreach (var informationSetHistory in progress.GameHistory.GetInformationSetHistoryItems())
            {
                var informationSetHistoryCopy = informationSetHistory;
                var decision = GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
                var playerInfo = GameDefinition.Players[informationSetHistory.PlayerIndex];
                bool isNecessarilyLast = decision.IsAlwaysPlayersLastDecision || informationSetHistory.IsTerminalAction;
                var playersStrategy = Strategies[informationSetHistory.PlayerIndex];
                if (historyPoint.StoredValue == null)
                {
                    NWayTreeStorage<object> informationSetNode = playersStrategy.SetInformationSetTreeValueIfNotSet(
                            informationSetHistoryCopy.InformationSet,
                            isNecessarilyLast,
                            () =>
                            {
                                if (playerInfo.PlayerIsChance)
                                {
                                    CRMChanceNodeSettings chanceNodeSettings;
                                    if (decision.UnevenChanceActions)
                                        chanceNodeSettings = new CRMChanceNodeSettings_UnequalProbabilities()
                                        {
                                            DecisionNum = informationSetHistory.DecisionIndex,
                                            Probabilities = GameDefinition.GetChanceActionProbabilities(decision.DecisionByteCode, progress) // the probabilities depend on the current state of the game
                                        };
                                    else
                                        chanceNodeSettings = new CRMChanceNodeSettings_EqualProbabilities()
                                        {
                                            DecisionNum = informationSetHistory.DecisionIndex,
                                            EachProbability = 1.0 / (double)decision.NumPossibleActions
                                        };
                                    return chanceNodeSettings;
                                }
                                else
                                {
                                    CRMInformationSetNodeTally nodeInfo = new CRMInformationSetNodeTally(informationSetHistory.DecisionIndex, playerInfo.PlayerIndex, decision.NumPossibleActions);
                                    return nodeInfo;
                                }
                            }
                            );
                    historyPoint.StoredValue = informationSetNode;
                }
                historyPoint = historyPoint.GetBranch(informationSetHistory.ActionChosen);
            }
        }
        #endregion

        #region Utility methods

        private GameInputs GetGameInputs()
        {
            Type theType = GameFactory.GetSimulationSettingsType();
            InputVariables inputVariables = new InputVariables(CurrentExecutionInformation);
            GameInputs inputs = inputVariables.GetGameInputs(theType, 1, new IterationID(1), CurrentExecutionInformation);
            return inputs;
        }

        double printProbability = 0.0;
        bool processIfNotPrinting = true;
        private unsafe void PrintSameGameResults(GamePlayer player, GameInputs inputs)
        {
            if (printProbability == 0 && !processIfNotPrinting)
                return;
            player.PlayAllPaths(inputs, PrintGameProbabilistically);
        }

        private unsafe void PrintGameProbabilistically(GameProgress progress)
        {
            byte* path = stackalloc byte[GameHistory.MaxNumActions];
            bool overridePrint = false;
            string actionsList = progress.GameHistory.GetActionsAsListString();
            if (actionsList == "INSERT_LIST_HERE")
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
            NWayTreeStorage<object> historyPoint = GameHistoryTree;
            // Go through each non-chance decision point 
            foreach (var informationSetHistory in progress.GameHistory.GetInformationSetHistoryItems())
            {
                var informationSetHistoryCopy = informationSetHistory;
                var decision = GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
                TabbedText.WriteLine($"Decision {decision.Name} for player {GameDefinition.Players[decision.PlayerNumber].PlayerName}");
                TabbedText.Tabs++;
                TabbedText.WriteLine($"Action chosen: {informationSetHistory.ActionChosen}");
                bool playerIsChance = GameDefinition.Players[informationSetHistory.PlayerIndex].PlayerIsChance;
                var playersStrategy = Strategies[informationSetHistory.PlayerIndex];
                unsafe
                {
                    List<byte> informationSetList;
                    informationSetList = ListExtensions.GetPointerAsList_255Terminated(informationSetHistoryCopy.InformationSet);
                    TabbedText.WriteLine($"Information set: {String.Join(",", informationSetList)}");
                    VerifyInformationSetMatchesExpectation(historyPoint, playerIsChance, playersStrategy, informationSetHistoryCopy);
                }
                historyPoint = historyPoint.GetBranch(informationSetHistory.ActionChosen);
                TabbedText.Tabs--;
            }
            double[] utilitiesStoredInTree = (double[])historyPoint.StoredValue;
            VerifyUtilitiesMatchExpectation(progress.GameHistory, utilitiesStoredInTree);
            TabbedText.WriteLine($"--> Utilities: { String.Join(",", utilitiesStoredInTree)}");
        }

        private unsafe void VerifyUtilitiesMatchExpectation(GameHistory gameHistory, double[] utilities)
        {
            double[] utilitiesInInformationSet = GetUtilitiesFromResolutionSet(gameHistory);
            if (utilitiesInInformationSet.Length != utilities.Length || !utilities.SequenceEqual(utilitiesInInformationSet))
                throw new Exception("Internal error. Resolution set is not adequately defined. Utilities failed to match expectation.");
        }

        private unsafe double[] GetUtilitiesFromResolutionSet(GameHistory gameHistory)
        {
            byte* resolutionInformationSet = stackalloc byte[GameHistory.MaxInformationSetLengthPerPlayer];
            gameHistory.GetPlayerInformation(GameDefinition.PlayerIndex_ResolutionPlayer, null /* get entire resolution set */, resolutionInformationSet);
            Strategy resolutionStrategy = Strategies[GameDefinition.PlayerIndex_ResolutionPlayer];
            double[] utilitiesInInformationSet = (double[])resolutionStrategy.GetInformationSetTreeValue(resolutionInformationSet);
            return utilitiesInInformationSet;
        }

        private unsafe void VerifyInformationSetMatchesExpectation(NWayTreeStorage<object> historyPoint, bool playerIsChance, Strategy playersStrategy, InformationSetHistory informationSetHistory)
        {
            if (playerIsChance)
            {
                CRMChanceNodeSettings chanceNodeSettingsInHistory = GetInformationSetChanceSettings(historyPoint);
                CRMChanceNodeSettings chanceNodeSettingsInInformationSet = (CRMChanceNodeSettings)playersStrategy.GetInformationSetTreeValue(informationSetHistory.InformationSet);
                if (chanceNodeSettingsInHistory != chanceNodeSettingsInInformationSet)
                    throw new Exception("Chance node settings do not match");
            }
            else
            {
                CRMInformationSetNodeTally tallyReferencedInHistory = GetInformationSetNodeTally(historyPoint);
                CRMInformationSetNodeTally tallyStoredInInformationSet = (CRMInformationSetNodeTally)playersStrategy.GetInformationSetTreeValue(informationSetHistory.InformationSet);
                if (tallyReferencedInHistory != tallyStoredInInformationSet)
                    throw new Exception("Tally references do not match.");
            }
        }

        public unsafe double GetUtility(byte* path, byte playerIndex)
        {
            byte index = playerIndex;
            return GetUtilities(path)[index];
        }

        private unsafe double[] GetUtilities(byte* path)
        {
            return (double[]) GameHistoryTree.GetValue(path);
        }

        public double GetUtilityFromTerminalHistory(NWayTreeStorage<object> history, byte playerIndex)
        {
            double[] utilities = ((double[])history.StoredValue);
            byte index = playerIndex;
            return utilities[index];
        }

        public void PreSerialize()
        {
        }

        public void UndoPreSerialize()
        {
        }

        public byte NumPossibleActionsAtDecision(byte decisionNum)
        {
            return GameDefinition.DecisionsExecutionOrder[decisionNum].NumPossibleActions;
        }

        public NWayTreeStorage<object> GetSubsequentHistory(NWayTreeStorage<object> history, byte action)
        {
            var returnVal = history.GetBranch(action);
            if (returnVal == null)
                throw new Exception("Internal error. Missing subsequent history.");
            return returnVal;
        }

        public bool NodeIsChanceNode(NWayTreeStorage<object> history)
        {
            var informationSetNodeReferencedInHistoryNode = ((NWayTreeStorage<object>)history.StoredValue);
            return (informationSetNodeReferencedInHistoryNode.StoredValue is CRMChanceNodeSettings);
        }

        public CRMInformationSetNodeTally GetInformationSetNodeTally(NWayTreeStorage<object> history)
        {
            var informationSetNodeReferencedInHistoryNode = ((NWayTreeStorage<object>)history.StoredValue);
            CRMInformationSetNodeTally tallyReferencedInHistory = (CRMInformationSetNodeTally)informationSetNodeReferencedInHistoryNode.StoredValue;
            return tallyReferencedInHistory;
        }


        public CRMChanceNodeSettings GetInformationSetChanceSettings(NWayTreeStorage<object> history)
        {
            var informationSetNodeReferencedInHistoryNode = ((NWayTreeStorage<object>)history.StoredValue);
            CRMChanceNodeSettings tallyReferencedInHistory = (CRMChanceNodeSettings)informationSetNodeReferencedInHistoryNode.StoredValue;
            return tallyReferencedInHistory;
        }

        #endregion

        #region Game play and reporting

        public enum ActionStrategies
        {
            RegretMatching,
            AverageStrategy,
            BestResponse,
            RegretMatchingWithPruning
        }

        public void ProcessPossiblePaths(NWayTreeStorage<object> history, List<byte> historySoFar, double probability, Action<NWayTreeStorage<object>, List<byte>, double> leafProcessor, ActionStrategies actionStrategy)
        {
            // Note that this method is different from GamePlayer.PlayAllPaths, because it relies on the history storage, rather than needing to play the game to discover what the next paths are.
            if (history.IsLeaf())
            {
                leafProcessor(history, historySoFar, probability);
                return;
            }
            ProcessPossiblePaths_Helper(history, historySoFar, probability, leafProcessor, actionStrategy);
        }

        private unsafe void ProcessPossiblePaths_Helper(NWayTreeStorage<object> historyPoint, List<byte> historySoFar, double probability, Action<NWayTreeStorage<object>, List<byte>, double> leafProcessor, ActionStrategies actionStrategy)
        {
            double* probabilities = stackalloc double[MaxPossibleActions];
            byte numPossibleActions;
            if (NodeIsChanceNode(historyPoint))
            {
                CRMChanceNodeSettings chanceNodeSettings = GetInformationSetChanceSettings(historyPoint);
                byte decisionIndex = chanceNodeSettings.DecisionNum;
                numPossibleActions = GameDefinition.DecisionsExecutionOrder[decisionIndex].NumPossibleActions;
                for (byte action = 1; action <= numPossibleActions; action++)
                    probabilities[action - 1] = chanceNodeSettings.GetActionProbability(action);
            }
            else
            { // not a chance node or a leaf node
                CRMInformationSetNodeTally nodeTally = GetInformationSetNodeTally(historyPoint);
                var decision = GameDefinition.DecisionsExecutionOrder[nodeTally.DecisionNum];
                numPossibleActions = decision.NumPossibleActions;
                if (decision.AlwaysDoAction != null)
                    SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions, probabilities, (byte)decision.AlwaysDoAction);
                else if (actionStrategy == ActionStrategies.RegretMatching)
                    nodeTally.GetRegretMatchingProbabilities(probabilities);
                else if (actionStrategy == ActionStrategies.RegretMatchingWithPruning)
                    nodeTally.GetRegretMatchingProbabilities_WithPruning(probabilities);
                else if (actionStrategy == ActionStrategies.AverageStrategy)
                    nodeTally.GetAverageStrategies(probabilities);
                else
                    throw new NotImplementedException();
            }
            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, 2, 1, (byte) (numPossibleActions + 1), (action) =>
            {
                if (probabilities[action - 1] > 0)
                {
                    List<byte> nextHistory = new List<byte>();
                    foreach (byte b in historySoFar)
                        nextHistory.Add(b);
                    nextHistory.Add(action);
                    ProcessPossiblePaths(GetSubsequentHistory(historyPoint, action), nextHistory, probability * probabilities[action - 1], leafProcessor, actionStrategy);
                }
            });
        }

        SimpleReport[] ReportsBeingGenerated = null;

        public string GenerateReports(ActionStrategies actionStrategy)
        {
            GamePlayer player = new GamePlayer(Strategies, GameFactory, EvolutionSettings.ParallelOptimization, GameDefinition);
            GameInputs inputs = GetGameInputs();
            GameProgress startingProgress = GameFactory.CreateNewGameProgress(new IterationID(1));
            StringBuilder sb = new StringBuilder();
            ReportsBeingGenerated = new SimpleReport[GameDefinition.SimpleReportDefinitions.Count()];
            for (int i = 0; i < GameDefinition.SimpleReportDefinitions.Count(); i++)
                ReportsBeingGenerated[i] = new SimpleReport(GameDefinition.SimpleReportDefinitions[i], GameDefinition.SimpleReportDefinitions[i].DivideColumnFiltersByImmediatelyEarlierReport ? ReportsBeingGenerated[i - 1] : null);
            GenerateReports_Parallel(player, inputs, startingProgress, actionStrategy);
            for (int i = 0; i < GameDefinition.SimpleReportDefinitions.Count(); i++)
                ReportsBeingGenerated[i].GetReport(sb, false);
            ReportsBeingGenerated = null;
            return sb.ToString();
        }

        StatCollector[] UtilityCalculations;

        public unsafe class BytePointerContainer
        {
            public byte* bytes;
        }

        private void GenerateReports_Parallel(GamePlayer player, GameInputs inputs, GameProgress startingProgress, ActionStrategies actionStrategy)
        {
            UtilityCalculations = new StatCollector[NumNonChancePlayers];
            for (int p = 0; p < NumNonChancePlayers; p++)
                UtilityCalculations[p] = new StatCollector();
            // start Task Parallel Library consumer/producer pattern
            var resultsBuffer = new BufferBlock<Tuple<GameProgress, double>>(new DataflowBlockOptions { BoundedCapacity = 10000 });
            var consumer = ProcessCompletedGameProgresses(resultsBuffer);
            // play each path and then asynchronously consume the result
            void leafProcessor(NWayTreeStorage<object> leafNode, List<byte> actions, double probability) 
            {
                GameProgress progress = startingProgress.DeepCopy();
                player.PlayPath(actions, progress, inputs, false);
                // do the simple aggregation of utilities. note that this is different from the value returned by vanilla, since that uses regret matching, instead of average strategies.
                double[] utilities = (double[])leafNode.StoredValue;
                for (int p = 0; p < NumNonChancePlayers; p++)
                    UtilityCalculations[p].Add(utilities[p], probability);
                                                                           // consume the result for reports
                resultsBuffer.SendAsync(new Tuple<GameProgress, double>(progress, probability));
            };
            ProcessPossiblePaths(GameHistoryTree, new List<byte>(), 1.0, leafProcessor, actionStrategy);

            //for (int p = 0; p < NumNonChancePlayers; p++)
            //    if (Math.Abs(UtilityCalculations[p].sumOfWeights - 1.0) > 0.0001)
            //        throw new Exception("Imperfect sampling.");

            resultsBuffer.Complete(); // tell consumer nothing more to be produced
            consumer.Wait(); // wait until all have been processed
        }

        async Task ProcessCompletedGameProgresses(ISourceBlock<Tuple<GameProgress, double>> source)
        {
            while (await source.OutputAvailableAsync())
            {
                Tuple<GameProgress, double> toProcess = source.Receive();
                if (toProcess.Item2 > 0) // probability
                    for (int i = 0; i < GameDefinition.SimpleReportDefinitions.Count(); i++)
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
            GEBRPass1(GameHistoryTree, playerIndex, 1, depthsOfPlayerDecisions); // setup counting first decision as depth 1
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
                bestResponseUtility = GEBRPass2(GameHistoryTree, playerIndex, depthToTarget, 1, 1.0, opponentsActionStrategy);
                if (TraceGEBR)
                    TabbedText.Tabs--;
            }
            return bestResponseUtility;
        }

        public void GEBRPass1(NWayTreeStorage<object> historyPoint, byte playerIndex, byte depth, HashSet<byte> depthOfPlayerDecisions)
        {
            if (historyPoint.IsLeaf())
                return;
            else
            {
                byte numPossibleActions;
                if (NodeIsChanceNode(historyPoint))
                {
                    CRMChanceNodeSettings chanceNodeSettings = GetInformationSetChanceSettings(historyPoint);
                    numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionNum);
                }
                else
                {
                    var informationSet = GetInformationSetNodeTally(historyPoint);
                    byte decisionNum = informationSet.DecisionNum;
                    numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
                    byte playerMakingDecision = informationSet.PlayerIndex;
                    if (playerMakingDecision == playerIndex)
                    {
                        if (!depthOfPlayerDecisions.Contains(depth))
                            depthOfPlayerDecisions.Add(depth);
                        informationSet.ResetBestResponseData();
                    }
                }
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    var nextHistory = historyPoint.GetBranch(action);
                    GEBRPass1(nextHistory, playerIndex, (byte)(depth + 1), depthOfPlayerDecisions);
                }
            }
        }

        public double GEBRPass2(NWayTreeStorage<object> historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            if (historyPoint.IsLeaf())
                return GetUtilityFromTerminalHistory(historyPoint, playerIndex);
            else if (NodeIsChanceNode(historyPoint))
                return GEBRPass2_ChanceNode(historyPoint, playerIndex, depthToTarget, depthSoFar, inversePi, opponentsActionStrategy);
            return GEBRPass2_DecisionNode(historyPoint, playerIndex, depthToTarget, depthSoFar, inversePi, opponentsActionStrategy);
        }

        private unsafe double GEBRPass2_DecisionNode(NWayTreeStorage<object> historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            var informationSet = GetInformationSetNodeTally(historyPoint);
            byte decisionNum = informationSet.DecisionNum;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            byte playerMakingDecision = informationSet.PlayerIndex;
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
            {
                TabbedText.WriteLine($"Decision {decisionNum} {GameDefinition.DecisionsExecutionOrder[decisionNum].Name} playerMakingDecision {playerMakingDecision} information set {informationSet.InformationSetNumber} inversePi {inversePi} depthSoFar {depthSoFar} ");
            }
            if (playerMakingDecision == playerIndex && depthSoFar > depthToTarget)
            {
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
                    TabbedText.Tabs++;
                byte action = GameDefinition.DecisionsExecutionOrder[decisionNum].AlwaysDoAction ?? informationSet.GetBestResponseAction();
                double expectedValue = GEBRPass2(historyPoint.GetBranch(action), playerIndex, depthToTarget, (byte)(depthSoFar + 1), inversePi, opponentsActionStrategy);
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"Best response action {action} producing expected value {expectedValue}");
                }
                return expectedValue;
            }
            else
            {
                double* actionProbabilities = stackalloc double[numPossibleActions];
                byte? alwaysDoAction = GameDefinition.DecisionsExecutionOrder[decisionNum].AlwaysDoAction;
                if (alwaysDoAction != null)
                    SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions, actionProbabilities, (byte)alwaysDoAction);
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
                    if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
                    {
                        TabbedText.WriteLine($"action {action} for playerMakingDecision {playerMakingDecision}...");
                        TabbedText.Tabs++; 
                    }
                    double expectedValue = GEBRPass2(historyPoint.GetBranch(action), playerIndex, depthToTarget, (byte)(depthSoFar + 1), nextInversePi, opponentsActionStrategy);
                    double product = actionProbabilities[action - 1] * expectedValue;
                    if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
                    {
                        TabbedText.Tabs--;
                        TabbedText.WriteLine($"... action {action} producing expected value {expectedValue} * probability {actionProbabilities[action - 1]} = product {product}");
                    }
                    if (playerMakingDecision != playerIndex)
                        expectedValueSum += product;
                    else if (playerMakingDecision == playerIndex && depthToTarget == depthSoFar)
                    {
                        informationSet.IncrementBestResponse(action, inversePi, expectedValue);
                        if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
                        {
                            TabbedText.WriteLine($"Incrementing best response for information set {informationSet.InformationSetNumber} for action {action} inversePi {inversePi} expectedValue {expectedValue}");
                        }
                    }
                }
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
                {
                    if (playerMakingDecision != playerIndex)
                        TabbedText.WriteLine($"Returning from other player node expectedvaluesum {expectedValueSum}");
                    else if (playerMakingDecision == playerIndex && depthToTarget == depthSoFar)
                        TabbedText.WriteLine($"Returning 0 (from own decision not yet fully developed)");
                }
                return expectedValueSum;
            }
        }

        private double GEBRPass2_ChanceNode(NWayTreeStorage<object> historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            CRMChanceNodeSettings chanceNodeSettings = GetInformationSetChanceSettings(historyPoint); 
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionNum);
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionNum))
            {
                TabbedText.WriteLine($"Num chance actions {numPossibleActions} for decision {chanceNodeSettings.DecisionNum} {GameDefinition.DecisionsExecutionOrder[chanceNodeSettings.DecisionNum].Name}");
            }
            double expectedValueSum = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionNum))
                    TabbedText.WriteLine($"chance action {action} for decision {chanceNodeSettings.DecisionNum} {GameDefinition.DecisionsExecutionOrder[chanceNodeSettings.DecisionNum].Name} ... ");
                double probability = chanceNodeSettings.GetActionProbability(action);
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionNum))
                    TabbedText.Tabs++;
                var valueBelow = GEBRPass2(historyPoint.GetBranch(action), playerIndex, depthToTarget, (byte)(depthSoFar + 1), inversePi * probability, opponentsActionStrategy);
                double expectedValue = probability * valueBelow;
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionNum))
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"... chance action {action} probability {probability} * valueBelow {valueBelow} = expectedValue {expectedValue}");
                }
                expectedValueSum += expectedValue;
            }
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionNum))
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
        /// <param name="playerIndex">0 for first player, etc. Note that this corresponds in Lanctot to 1, 2, etc. We are using zero-basing for player index (even though we are 1-basing actions).</param>
        /// <returns></returns>
        public unsafe double VanillaCRM(NWayTreeStorage<object> historyPoint, byte playerIndex, double* piValues, bool usePruning)
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
            if (historyPoint.IsLeaf())
                return GetUtilityFromTerminalHistory(historyPoint, playerIndex);
            else
            {
                if (NodeIsChanceNode(historyPoint))
                    return VanillaCRM_ChanceNode(historyPoint, playerIndex, piValues, usePruning);
                else
                    return VanillaCRM_DecisionNode(historyPoint, playerIndex, piValues, usePruning);
            }
        }
        
        private unsafe double VanillaCRM_DecisionNode(NWayTreeStorage<object> historyPoint, byte playerIndex, double* piValues, bool usePruning)
        {
            double* nextPiValues = stackalloc double[MaxNumPlayers];
            var informationSet = GetInformationSetNodeTally(historyPoint);
            byte decisionNum = informationSet.DecisionNum;
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
                SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions, actionProbabilities, (byte) alwaysDoAction);
            double* expectedValueOfAction = stackalloc double[numPossibleActions];
            double expectedValue = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                double probabilityOfAction = actionProbabilities[action - 1];
                GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false, nextPiValues); // reduce probability associated with player being optimized, without changing probabilities for other players
                if (TraceVanillaCRM)
                {
                    TabbedText.WriteLine($"decisionNum {decisionNum} optimizing player {playerIndex}  own decision {playerMakingDecision == playerIndex} action {action} probability {probabilityOfAction} ...");
                    TabbedText.Tabs++;
                }
                NWayTreeStorage<object> nextHistoryPoint = GetSubsequentHistory(historyPoint, action);
                expectedValueOfAction[action - 1] = VanillaCRM(nextHistoryPoint, playerIndex, nextPiValues, usePruning);
                expectedValue += probabilityOfAction * expectedValueOfAction[action - 1];

                if (TraceVanillaCRM)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"... action {action} expected value {expectedValueOfAction[action - 1]} cum expected value {expectedValue}");
                }
            }
            if (playerMakingDecision == playerIndex)
            {
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double inversePi = GetInversePiValue(piValues, playerIndex);
                    double pi = piValues[playerIndex];
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

        private static unsafe void SetProbabilitiesToAlwaysDoParticularAction(byte numPossibleActions, double* actionProbabilities, byte alwaysDoAction)
        {
            for (byte action = 1; action <= numPossibleActions; action++)
                if (action == alwaysDoAction)
                    actionProbabilities[action - 1] = 1.0;
                else
                    actionProbabilities[action - 1] = 0;
        }

        private unsafe double VanillaCRM_ChanceNode(NWayTreeStorage<object> historyPoint, byte playerIndex, double* piValues, bool usePruning)
        {
            double* equalProbabilityNextPiValues = stackalloc double[MaxNumPlayers];
            CRMChanceNodeSettings chanceNodeSettings = GetInformationSetChanceSettings(historyPoint);
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionNum);
            bool equalProbabilities = chanceNodeSettings.AllProbabilitiesEqual();
            if (equalProbabilities) // can set next probabilities once for all actions
                GetNextPiValues(piValues, playerIndex, chanceNodeSettings.GetActionProbability(1), true, equalProbabilityNextPiValues);
            else
                equalProbabilityNextPiValues = null;
            double expectedValue = 0;
            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, 2 /* TODO: Make this an evolution setting */, 1, (byte)(numPossibleActions + 1),
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
                    double probabilityAdjustedExpectedValueParticularAction = VanillaCRM_ChanceNode_NextAction(historyPoint, playerIndex, piValues, chanceNodeSettings, equalProbabilityNextPiValues, expectedValue, action, usePruning);
                    expectedValue += probabilityAdjustedExpectedValueParticularAction;
                });

            return expectedValue;
        }

        private unsafe double VanillaCRM_ChanceNode_NextAction(NWayTreeStorage<object> historyPoint, byte playerIndex, double* piValues, CRMChanceNodeSettings chanceNodeSettings, double* equalProbabilityNextPiValues, double expectedValue, byte action, bool usePruning)
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
                GetNextPiValues(piValues, playerIndex, chanceNodeSettings.GetActionProbability(action), true, nextPiValues);
            double actionProbability = chanceNodeSettings.GetActionProbability(action);
            if (TraceVanillaCRM)
            {
                TabbedText.WriteLine($"Chance decisionNum {chanceNodeSettings.DecisionNum} action {action} probability {actionProbability} ...");
                TabbedText.Tabs++;
            }
            double expectedValueParticularAction = VanillaCRM(GetSubsequentHistory(historyPoint, action), playerIndex, nextPiValues, usePruning);
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
            const int numIterationsToRun = 100000;

            int? reportEveryNIterations = 100;
            int? bestResponseEveryMIterations = 1000;

            double[] lastUtilities = new double[NumNonChancePlayers];

            Stopwatch s = new Stopwatch();
            unsafe void VanillaCFRIteration(int iteration)
            {
                // NOTE: This is in a helper method because otherwise the stackallocs keep accumulating, causing a stackoverflowexception
                bool usePruning = iteration >= 100;
                ActionStrategies actionStrategy = usePruning ? ActionStrategies.RegretMatchingWithPruning : ActionStrategies.RegretMatching;
                for (byte playerIndex = 0; playerIndex < NumNonChancePlayers; playerIndex++)
                {
                    double* initialPiValues = stackalloc double[MaxNumPlayers];
                    GetInitialPiValues(initialPiValues);
                    if (TraceVanillaCRM)
                        TabbedText.WriteLine($"Iteration {iteration} Player {playerIndex}");
                    s.Start();
                    lastUtilities[playerIndex] = VanillaCRM(GameHistoryTree, playerIndex, initialPiValues, usePruning);
                    s.Stop();
                }
                if (reportEveryNIterations != null && iteration % reportEveryNIterations == 0)
                {
                    Debug.WriteLine("");
                    Debug.WriteLine($"Iteration {iteration} Milliseconds per iteration {(s.ElapsedMilliseconds / ((double)iteration + 1.0))}");
                    Debug.WriteLine($"{GenerateReports(actionStrategy)}");

                    if (bestResponseEveryMIterations != null && iteration % bestResponseEveryMIterations == 0)
                        for (byte playerIndex = 0; playerIndex < NumNonChancePlayers; playerIndex++)
                        {
                            double bestResponseUtility = CalculateBestResponse(playerIndex, actionStrategy);
                            double bestResponseImprovement = bestResponseUtility - UtilityCalculations[playerIndex].Average();
                            if (bestResponseImprovement < -1E-15)
                                throw new Exception("Best response function worse."); // it can be slightly negative as a result of rounding errors
                            Debug.WriteLine($"Player {playerIndex} utility with regret matching {UtilityCalculations[playerIndex].Average()} using best response against regret matching {bestResponseUtility} best response improvement {bestResponseImprovement}");
                        }
                }
            }
            for (int iteration = 0; iteration < numIterationsToRun; iteration++)
            {
                VanillaCFRIteration(iteration); 
            }

        }

        

        #endregion
    }
}
