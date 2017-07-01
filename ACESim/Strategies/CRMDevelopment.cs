using ACESim.Util;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

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

        public bool ChancePlayerExists;

        public const int MaxNumPlayers = 4; // this affects fixed-size stack-allocated buffers
        public const int MaxPossibleActions = 100; // same

        public int NumNonChancePlayers;

        public byte NonChancePlayerIndexFromPlayerIndex(byte overallPlayerNum) => ChancePlayerExists ? (byte)(overallPlayerNum - 1) : overallPlayerNum;

        public Strategy GetPlayerStrategyFromOverallPlayerNum(byte overallPlayerNum) => Strategies[NonChancePlayerIndexFromPlayerIndex(overallPlayerNum)];

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
            ChancePlayerExists = GameDefinition.Players.Any(x => x.PlayerIsChance);
            NumNonChancePlayers = GameDefinition.Players.Count(x => !x.PlayerIsChance);
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
            GameHistoryTree = new NWayTreeStorageInternal<object>(GameDefinition.DecisionsExecutionOrder.First().NumPossibleActions);
            foreach (Strategy s in Strategies)
            {
                s.CreateInformationSetTree(GameDefinition.DecisionsExecutionOrder.First(x => x.PlayerNumber == s.PlayerInfo.PlayerNumberOverall).NumPossibleActions);
            }

            int numPlayed = 0;
            byte* actionsEnumerator = stackalloc byte[GameHistory.MaxNumActions];
            byte* numPossibleActionsEnumerator = stackalloc byte[GameHistory.MaxNumActions];
            foreach (GameProgress progress in player.PlayAllPaths(inputs))
            {
                numPlayed++;

                // First, add the utilities at the end of the tree for this path.
                progress.GameHistory.GetActions(actionsEnumerator);
                progress.GameHistory.GetNumPossibleActions(numPossibleActionsEnumerator);
                GameHistoryTree.SetValue(actionsEnumerator, true, progress.GetNonChancePlayerUtilities());

                NWayTreeStorage<object> walkHistoryTree = GameHistoryTree;
                
                // Go through each non-chance decision point on this path and make sure that the information set tree extends there. We then store the regrets etc. at these points. 
                foreach (var informationSetHistory in progress.GameHistory.GetInformationSetHistoryItems())
                {
                    var informationSetHistoryCopy = informationSetHistory;
                    var decision = GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
                    var playerInfo = GameDefinition.Players[informationSetHistory.PlayerMakingDecision];
                    if (playerInfo.PlayerIsChance)
                    {
                        if (walkHistoryTree.StoredValue == null)
                        {
                            if (decision.UnevenChanceActions)
                                walkHistoryTree.StoredValue = new CRMChanceNodeSettings_UnequalProbabilities()
                                {
                                    DecisionNum = informationSetHistory.DecisionIndex,
                                    Probabilities = GameDefinition.GetChanceActionProbabilities(GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex].DecisionByteCode, progress) // the probabilities depend on the current state of the game
                                }; 
                            else
                                walkHistoryTree.StoredValue = new CRMChanceNodeSettings_EqualProbabilities()
                                {
                                    DecisionNum = informationSetHistory.DecisionIndex,
                                    EachProbability = 1.0 / (double)decision.NumPossibleActions
                                };
                        }
                    }
                    else
                    {
                        bool isNecessarilyLast = decision.IsAlwaysPlayersLastDecision || informationSetHistory.IsTerminalAction;
                        var playersStrategy = GetPlayerStrategyFromOverallPlayerNum(informationSetHistory.PlayerMakingDecision);
                        if (walkHistoryTree.StoredValue == null)
                        {
                            // create the information set node if necessary, with initialized tally values
                            var informationSetNode = playersStrategy.SetInformationSetTreeValueIfNotSet(
                                informationSetHistoryCopy.InformationSet,
                                isNecessarilyLast,
                                () =>
                                {
                                    CRMInformationSetNodeTally nodeInfo = new CRMInformationSetNodeTally(informationSetHistory.DecisionIndex, playerInfo.NonChancePlayerIndex, decision.NumPossibleActions);
                                    return nodeInfo;
                                }
                                );
                            // Now, we want to store in the game history tree a quick reference to the correct point in the information set tree.
                            walkHistoryTree.StoredValue = informationSetNode;
                        }
                    }
                    walkHistoryTree = walkHistoryTree.GetBranch(informationSetHistory.ActionChosen);
                }
            }

            PrintSameGameResults(player, inputs);
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

        private unsafe void PrintSameGameResults(GamePlayer player, GameInputs inputs)
        {
            double probabilityOfPrint = 0;
            if (probabilityOfPrint == 0)
                return;
            byte* path = stackalloc byte[GameHistory.MaxNumActions];
            foreach (var progress in player.PlayAllPaths(inputs))
            {
                if (RandomGenerator.NextDouble() < probabilityOfPrint)
                {
                    progress.GameHistory.GetActions(path);
                    List<byte> path2 = new List<byte>();
                    while (*path != 255)
                    {
                        path2.Add(*path);
                        path++;
                    }
                    var utilities = GetUtilities(path);
                    TabbedText.WriteLine($"{String.Join(",", path2)} -->  Utilities: P {utilities[0]}, D {utilities[1]}");
                    TabbedText.Tabs++;
                    PrintGenericGameProgress(progress);
                    TabbedText.Tabs--;
                }
            }
        }

        public void PrintGenericGameProgress(GameProgress progress)
        {
            NWayTreeStorage<object> walkHistoryTree = GameHistoryTree;
            // Go through each non-chance decision point 
            foreach (var informationSetHistory in progress.GameHistory.GetInformationSetHistoryItems())
            {
                var informationSetHistoryCopy = informationSetHistory;
                var decision = GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
                TabbedText.WriteLine($"Decision {decision.Name} for player {GameDefinition.Players[decision.PlayerNumber].PlayerName}");
                TabbedText.Tabs++;
                TabbedText.WriteLine($"Action chosen: {informationSetHistory.ActionChosen}");
                if (!GameDefinition.Players[informationSetHistory.PlayerMakingDecision].PlayerIsChance)
                {
                    var playersStrategy = GetPlayerStrategyFromOverallPlayerNum(informationSetHistory.PlayerMakingDecision);
                    unsafe
                    {
                        List<byte> informationSetList;
                        informationSetList = ListExtensions.GetPointerAsList(informationSetHistoryCopy.InformationSet);
                        TabbedText.WriteLine($"Information set: {String.Join(",", informationSetList)}");
                        CRMInformationSetNodeTally tallyReferencedInHistory = GetInformationSetNodeTally(walkHistoryTree);
                        CRMInformationSetNodeTally tallyStoredInInformationSet = (CRMInformationSetNodeTally)playersStrategy.GetInformationSetTreeValue(informationSetHistoryCopy.InformationSet);
                        if (tallyReferencedInHistory != tallyStoredInInformationSet)
                            throw new Exception("Tally references do not match.");
                    }
                }
                walkHistoryTree = walkHistoryTree.GetBranch(informationSetHistory.ActionChosen);
                TabbedText.Tabs--;
            }
        }

        public unsafe double GetUtility(byte* path, byte player)
        {
            byte index = player;
            if (ChancePlayerExists)
                index--;
            return GetUtilities(path)[index];
        }

        private unsafe double[] GetUtilities(byte* path)
        {
            return (double[]) GameHistoryTree.GetValue(path);
        }

        public void PreSerialize()
        {
        }

        public void UndoPreSerialize()
        {
        }

        public double GetUtilityFromTerminalHistory(NWayTreeStorage<object> history, byte nonChancePlayerIndex)
        {
            double[] utilities = ((double[])history.StoredValue);
            return utilities[nonChancePlayerIndex];
        }

        public bool NodeIsChanceNode(NWayTreeStorage<object> history)
        {
            return (history.StoredValue is CRMChanceNodeSettings);
        }

        public byte NumPossibleActionsAtDecision(byte decisionNum)
        {
            return GameDefinition.DecisionsExecutionOrder[decisionNum].NumPossibleActions;
        }

        public NWayTreeStorage<object> GetSubsequentHistory(NWayTreeStorage<object> history, byte action)
        {
            return history.GetBranch(action);
        }

        public CRMInformationSetNodeTally GetInformationSetNodeTally(NWayTreeStorage<object> history)
        {
            var informationSetNodeReferencedInHistoryNode = ((NWayTreeStorage<object>)history.StoredValue);
            CRMInformationSetNodeTally tallyReferencedInHistory = (CRMInformationSetNodeTally)informationSetNodeReferencedInHistoryNode.StoredValue;
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

        private unsafe void ProcessPossiblePaths_Helper(NWayTreeStorage<object> history, List<byte> historySoFar, double probability, Action<NWayTreeStorage<object>, List<byte>, double> leafProcessor, ActionStrategies actionStrategy)
        {
            double* probabilities = stackalloc double[MaxPossibleActions];
            byte numPossibleActions;
            if (NodeIsChanceNode(history))
            {
                CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings)history.StoredValue;
                byte decisionIndex = chanceNodeSettings.DecisionNum;
                numPossibleActions = GameDefinition.DecisionsExecutionOrder[decisionIndex].NumPossibleActions;
                for (byte action = 1; action <= numPossibleActions; action++)
                    probabilities[action - 1] = chanceNodeSettings.GetActionProbability(action);
            }
            else
            { // not a chance node or a leaf node
                CRMInformationSetNodeTally nodeTally = GetInformationSetNodeTally(history);
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
                    ProcessPossiblePaths(GetSubsequentHistory(history, action), nextHistory, probability * probabilities[action - 1], leafProcessor, actionStrategy);
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
                player.PlayPath(actions, progress, inputs);
                // do the simple aggregation of utilities. note that this is different from the value returned by vanilla, since that uses regret matching, instead of average strategies.
                double[] utilities = (double[])leafNode.StoredValue;
                for (int p = 0; p < NumNonChancePlayers; p++)
                    UtilityCalculations[p].Add(utilities[p]); // DEBUG: weight by probability?
                                                                           // consume the result for reports
                resultsBuffer.SendAsync(new Tuple<GameProgress, double>(progress, probability));
            };
            ProcessPossiblePaths(GameHistoryTree, new List<byte>(), 1.0, leafProcessor, actionStrategy);
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

        public double GetPiValue(double[] piValues, byte nonChancePlayerIndex, byte decisionNum)
        {
            return piValues[nonChancePlayerIndex];
        }

        public unsafe double GetInversePiValue(double* piValues, byte nonChancePlayerIndex)
        {
            double product = 1.0;
            for (byte p = 0; p < NumNonChancePlayers; p++)
                if (p != nonChancePlayerIndex)
                    product *= piValues[p];
            return product;
        }

        private unsafe void GetNextPiValues(double* currentPiValues, byte nonChancePlayerIndex, double probabilityToMultiplyBy, bool changeOtherPlayers, double* nextPiValues)
        {
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                double currentPiValue = currentPiValues[p];
                double nextPiValue;
                if (p == nonChancePlayerIndex)
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
        /// <param name="nonChancePlayerIndex"></param>
        /// <returns></returns>
        public double CalculateBestResponse(byte nonChancePlayerIndex, ActionStrategies opponentsActionStrategy)
        {
            HashSet<byte> depthsOfPlayerDecisions = new HashSet<byte>();
            GEBRPass1(GameHistoryTree, nonChancePlayerIndex, 1, depthsOfPlayerDecisions); // setup counting first decision as depth 1
            List<byte> depthsOrdered = depthsOfPlayerDecisions.OrderByDescending(x => x).ToList();
            depthsOrdered.Add(0); // last depth to play should return outcome
            double bestResponseUtility = 0;
            foreach (byte depthToTarget in depthsOrdered)
            {
                if (TraceGEBR)
                {
                    TabbedText.WriteLine($"Optimizing {nonChancePlayerIndex} depthToTarget {depthToTarget}: ");
                    TabbedText.Tabs++;
                }
                bestResponseUtility = GEBRPass2(GameHistoryTree, nonChancePlayerIndex, depthToTarget, 1, 1.0, opponentsActionStrategy);
                if (TraceGEBR)
                    TabbedText.Tabs--;
            }
            return bestResponseUtility;
        }

        public void GEBRPass1(NWayTreeStorage<object> history, byte nonChancePlayerIndex, byte depth, HashSet<byte> depthOfPlayerDecisions)
        {
            if (history.IsLeaf())
                return;
            else
            {
                byte numPossibleActions;
                if (NodeIsChanceNode(history))
                {
                    CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings)history.StoredValue;
                    numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionNum);
                }
                else
                {
                    var informationSet = GetInformationSetNodeTally(history);
                    byte decisionNum = informationSet.DecisionNum;
                    numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
                    byte playerMakingDecision = informationSet.NonChancePlayerIndex;
                    if (playerMakingDecision == nonChancePlayerIndex)
                    {
                        if (!depthOfPlayerDecisions.Contains(depth))
                            depthOfPlayerDecisions.Add(depth);
                        informationSet.ResetBestResponseData();
                    }
                }
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    var nextHistory = history.GetBranch(action);
                    GEBRPass1(nextHistory, nonChancePlayerIndex, (byte)(depth + 1), depthOfPlayerDecisions);
                }
            }
        }

        public double GEBRPass2(NWayTreeStorage<object> history, byte nonChancePlayerIndex, byte depthToTarget, byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            if (history.IsLeaf())
                return GetUtilityFromTerminalHistory(history, nonChancePlayerIndex);
            else if (NodeIsChanceNode(history))
                return GEBRPass2_ChanceNode(history, nonChancePlayerIndex, depthToTarget, depthSoFar, inversePi, opponentsActionStrategy);
            return GEBRPass2_DecisionNode(history, nonChancePlayerIndex, depthToTarget, depthSoFar, inversePi, opponentsActionStrategy);
        }

        private unsafe double GEBRPass2_DecisionNode(NWayTreeStorage<object> history, byte nonChancePlayerIndex, byte depthToTarget, byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            var informationSet = GetInformationSetNodeTally(history);
            byte decisionNum = informationSet.DecisionNum;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            byte playerMakingDecision = informationSet.NonChancePlayerIndex;
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
            {
                TabbedText.WriteLine($"Decision {decisionNum} {GameDefinition.DecisionsExecutionOrder[decisionNum].Name} playerMakingDecision {playerMakingDecision} information set {informationSet.InformationSetNumber} inversePi {inversePi} depthSoFar {depthSoFar} ");
            }
            if (playerMakingDecision == nonChancePlayerIndex && depthSoFar > depthToTarget)
            {
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
                    TabbedText.Tabs++;
                byte action = GameDefinition.DecisionsExecutionOrder[decisionNum].AlwaysDoAction ?? informationSet.GetBestResponseAction();
                double expectedValue = GEBRPass2(history.GetBranch(action), nonChancePlayerIndex, depthToTarget, (byte)(depthSoFar + 1), inversePi, opponentsActionStrategy);
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
                    if (playerMakingDecision != nonChancePlayerIndex)
                        nextInversePi *= actionProbabilities[action - 1];
                    if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
                    {
                        TabbedText.WriteLine($"action {action} for playerMakingDecision {playerMakingDecision}...");
                        TabbedText.Tabs++; 
                    }
                    double expectedValue = GEBRPass2(history.GetBranch(action), nonChancePlayerIndex, depthToTarget, (byte)(depthSoFar + 1), nextInversePi, opponentsActionStrategy);
                    double product = actionProbabilities[action - 1] * expectedValue;
                    if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
                    {
                        TabbedText.Tabs--;
                        TabbedText.WriteLine($"... action {action} producing expected value {expectedValue} * probability {actionProbabilities[action - 1]} = product {product}");
                    }
                    if (playerMakingDecision != nonChancePlayerIndex)
                        expectedValueSum += product;
                    else if (playerMakingDecision == nonChancePlayerIndex && depthToTarget == depthSoFar)
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
                    if (playerMakingDecision != nonChancePlayerIndex)
                        TabbedText.WriteLine($"Returning from other player node expectedvaluesum {expectedValueSum}");
                    else if (playerMakingDecision == nonChancePlayerIndex && depthToTarget == depthSoFar)
                        TabbedText.WriteLine($"Returning 0 (from own decision not yet fully developed)");
                }
                return expectedValueSum;
            }
        }

        private double GEBRPass2_ChanceNode(NWayTreeStorage<object> history, byte nonChancePlayerIndex, byte depthToTarget, byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings)history.StoredValue;
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
                var valueBelow = GEBRPass2(history.GetBranch(action), nonChancePlayerIndex, depthToTarget, (byte)(depthSoFar + 1), inversePi * probability, opponentsActionStrategy);
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
        /// <param name="history">The game tree, pointing to the particular point in the game where we are located</param>
        /// <param name="nonChancePlayerIndex">0 for first non-chance player, etc. Note that this corresponds in Lanctot to 1, 2, etc. We are using zero-basing for player index (even though we are 1-basing actions).</param>
        /// <returns></returns>
        public unsafe double VanillaCRM(NWayTreeStorage<object> history, byte nonChancePlayerIndex, double* piValues, bool usePruning)
        {
            if (usePruning)
            {
                bool allZero = true;
                for (int i = 0; i < NumNonChancePlayers; i++)
                    if (*piValues != 0)
                    {
                        allZero = false;
                        break;
                    }
                if (allZero)
                    return 0; // this is zero probability, so the result doesn't matter
            }
            if (history.IsLeaf())
                return GetUtilityFromTerminalHistory(history, nonChancePlayerIndex);
            else
            {
                if (NodeIsChanceNode(history))
                    return VanillaCRM_ChanceNode(history, nonChancePlayerIndex, piValues, usePruning);
                else
                    return VanillaCRM_DecisionNode(history, nonChancePlayerIndex, piValues, usePruning);
            }
        }

        int DEBUG = 0;
        private unsafe double VanillaCRM_DecisionNode(NWayTreeStorage<object> history, byte nonChancePlayerIndex, double* piValues, bool usePruning)
        {
            DEBUG++;
            double* nextPiValues = stackalloc double[MaxNumPlayers];
            var informationSet = GetInformationSetNodeTally(history);
            byte decisionNum = informationSet.DecisionNum;
            byte playerMakingDecision = informationSet.NonChancePlayerIndex;
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
                    TabbedText.WriteLine($"decisionNum {decisionNum} optimizing player {nonChancePlayerIndex}  own decision {playerMakingDecision == nonChancePlayerIndex} action {action} probability {probabilityOfAction} ...");
                    TabbedText.Tabs++;
                }
                NWayTreeStorage<object> nextHistory = GetSubsequentHistory(history, action);
                expectedValueOfAction[action - 1] = VanillaCRM(nextHistory, nonChancePlayerIndex, nextPiValues, usePruning);
                expectedValue += probabilityOfAction * expectedValueOfAction[action - 1];

                if (TraceVanillaCRM)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"... action {action} expected value {expectedValueOfAction[action - 1]} cum expected value {expectedValue}");
                }
            }
            if (playerMakingDecision == nonChancePlayerIndex)
            {
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double inversePi = GetInversePiValue(piValues, nonChancePlayerIndex);
                    double pi = piValues[nonChancePlayerIndex];
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

        private unsafe double VanillaCRM_ChanceNode(NWayTreeStorage<object> history, byte nonChancePlayerIndex, double* piValues, bool usePruning)
        {
            double* equalProbabilityNextPiValues = stackalloc double[MaxNumPlayers];
            CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings)history.StoredValue;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionNum);
            bool equalProbabilities = chanceNodeSettings.AllProbabilitiesEqual();
            if (equalProbabilities) // can set next probabilities once for all actions
                GetNextPiValues(piValues, nonChancePlayerIndex, chanceNodeSettings.GetActionProbability(1), true, equalProbabilityNextPiValues);
            double expectedValue = 0;
            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, 2 /* TODO: Make this an evolution setting */, 1, (byte)(numPossibleActions + 1),
                action =>
                {
                    // to do: use parallelizer or something like it for byte. But make sure that it will work at multiple tree levels.
                    double probabilityAdjustedExpectedValueParticularAction = VanillaCRM_ChanceNode_NextAction(history, nonChancePlayerIndex, piValues, chanceNodeSettings, equalProbabilityNextPiValues, expectedValue, action, usePruning);
                    expectedValue += probabilityAdjustedExpectedValueParticularAction;
                });

            return expectedValue;
        }

        private unsafe double VanillaCRM_ChanceNode_NextAction(NWayTreeStorage<object> history, byte nonChancePlayerIndex, double* piValues, CRMChanceNodeSettings chanceNodeSettings, double* equalProbabilityNextPiValues, double expectedValue, byte action, bool usePruning)
        {
            double* nextPiValues = stackalloc double[MaxNumPlayers];
            if (equalProbabilityNextPiValues != null)
            {
                double* locTarget = nextPiValues;
                double* locSource = equalProbabilityNextPiValues;
                for (int i = 0; i < NumNonChancePlayers + 1; i++)
                {
                    (*locTarget) = (*locSource);
                    locTarget++;
                    locSource++;
                }
            }
            else // must set probability separately for each action we take
                GetNextPiValues(piValues, nonChancePlayerIndex, chanceNodeSettings.GetActionProbability(action), true, nextPiValues);
            double actionProbability = chanceNodeSettings.GetActionProbability(action);
            if (TraceVanillaCRM)
            {
                TabbedText.WriteLine($"Chance decisionNum {chanceNodeSettings.DecisionNum} action {action} probability {actionProbability} ...");
                TabbedText.Tabs++;
            }
            double expectedValueParticularAction = VanillaCRM(GetSubsequentHistory(history, action), nonChancePlayerIndex, nextPiValues, usePruning);
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
            const int numIterationsToRun = 10;

            int? reportEveryNIterations = 1; // DEBUG

            double[] lastUtilities = new double[NumNonChancePlayers];

            DateTime startTime = DateTime.Now;


            for (int iteration = 0; iteration < numIterationsToRun; iteration++)
            {
                bool usePruning = iteration >= 100;
                ActionStrategies actionStrategy = usePruning ? ActionStrategies.RegretMatchingWithPruning : ActionStrategies.RegretMatching;
                for (byte p = 0; p < NumNonChancePlayers; p++)
                {
                    double* initialPiValues = stackalloc double[MaxNumPlayers];
                    GetInitialPiValues(initialPiValues);
                    if (TraceVanillaCRM)
                        TabbedText.WriteLine($"Iteration {iteration} Player {p}");
                    lastUtilities[p] = VanillaCRM(GameHistoryTree, p, initialPiValues, usePruning);
                }
                if (reportEveryNIterations != null && iteration % reportEveryNIterations == 0)
                {
                    Debug.WriteLine("");
                    Debug.WriteLine($"Iteration {iteration} Milliseconds per iteration {((DateTime.Now - startTime).TotalMilliseconds / (double) iteration)}");
                    Debug.WriteLine($"{GenerateReports(actionStrategy)}");
                    for (byte p = 0; p < NumNonChancePlayers; p++)
                    {
                        double bestResponseUtility = CalculateBestResponse(p, actionStrategy);
                        Debug.WriteLine($"Player {p} utility with regret matching {UtilityCalculations[p].Average()} using best response against regret matching {bestResponseUtility} best response improvement {bestResponseUtility - UtilityCalculations[p].Average()}");
                    }
                }
            }

        }

        #endregion
    }
}
