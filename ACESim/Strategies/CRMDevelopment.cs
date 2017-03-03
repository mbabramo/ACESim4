using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        /// A game history tree. On internal nodes, the object contained is the information set of the player making the decision (or null for a chance decision). On the leaf nodes, the object contained are the players' utilities.
        /// </summary>
        public NWayTreeStorageInternal<object> GameHistoryTree;

        public bool ChancePlayerExists;

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

        public void Initialize()
        {
            GamePlayer player = new GamePlayer(Strategies, GameFactory, EvolutionSettings.ParallelOptimization, GameDefinition);
            GameInputs inputs = GetGameInputs();

            // Create game trees
            GameHistoryTree = new NWayTreeStorageInternal<object>(GameDefinition.DecisionsExecutionOrder.First().NumPossibleActions);
            foreach (Strategy s in Strategies)
            {
                s.CreateInformationSetTree(GameDefinition.DecisionsExecutionOrder.First(x => x.PlayerNumber == s.PlayerInfo.PlayerNumberOverall).NumPossibleActions);
            }

            int DEBUG_Counter = 0;

            int numPlayed = 0;
            foreach (var progress in player.PlayAllPaths(inputs))
            {
                numPlayed++;

                // First, add the utilities at the end of the tree.
                var actionsEnumerator = progress.GameHistory.GetActions().GetEnumerator();
                var numPossibleActionsEnumerator = progress.GameHistory.GetNumPossibleActions().GetEnumerator();
                GameHistoryTree.SetValue(actionsEnumerator, true, progress.GetNonChancePlayerUtilities());

                DEBUG_Counter++;

                NWayTreeStorage<object> walkHistoryTree = GameHistoryTree;
                // Go through each non-chance decision point and make sure that the information set tree extends there. We then store the regrets etc. at these points. 
                foreach (var informationSetHistory in progress.GameHistory.GetInformationSetHistoryItems())
                {
                    var decision = GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
                    var playerInfo = GameDefinition.Players[informationSetHistory.PlayerMakingDecision];
                    if (playerInfo.PlayerIsChance)
                    {
                        if (walkHistoryTree.StoredValue == null)
                        {
                            if (decision.UnevenChanceActions)
                                walkHistoryTree.StoredValue = new CRMChanceNodeSettings_UnequalProbabilities() { DecisionNum = informationSetHistory.DecisionIndex, Probabilities = GameDefinition.GetChanceActionProbabilities(GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex].DecisionByteCode, progress) }; // the probabilities depend on the current state of the game
                            else
                                walkHistoryTree.StoredValue = new CRMChanceNodeSettings_EqualProbabilities() { DecisionNum = informationSetHistory.DecisionIndex, EachProbability = 1.0 / (double)decision.NumPossibleActions };
                        }
                    }
                    else
                    {
                        bool isNecessarilyLast = decision.IsAlwaysPlayersLastDecision || informationSetHistory.IsTerminalAction;
                        var playersStrategy = GetPlayerStrategyFromOverallPlayerNum(informationSetHistory.PlayerMakingDecision);
                        if (walkHistoryTree.StoredValue == null)
                        {
                            // create the information set node if necessary, within initialized tally values
                            var informationSetNode = playersStrategy.SetInformationSetTreeValueIfNotSet(
                                informationSetHistory.InformationSet,
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
                        else
                        {
                            // DEBUG
                            if (GetInformationSet(walkHistoryTree).InformationSetNumber == 31 && informationSetHistory.DecisionIndex != 6)
                            {
                                var DEBUG = 0;
                            }
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

        private void PrintSameGameResults(GamePlayer player, GameInputs inputs)
        {
            double probabilityOfPrint = 0;
            if (probabilityOfPrint == 0)
                return;
            foreach (var progress in player.PlayAllPaths(inputs))
            {
                if (RandomGenerator.NextDouble() < probabilityOfPrint)
                {
                    var path = progress.GameHistory.GetActions().ToList();
                    var utilities = GetUtilities(path);
                    TabbedText.WriteLine($"{String.Join(",", path)} -->  Utilities: P {utilities[0]}, D {utilities[1]}");
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
                var decision = GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
                TabbedText.WriteLine($"Decision {decision.Name} for player {GameDefinition.Players[decision.PlayerNumber].PlayerName}");
                TabbedText.Tabs++;
                TabbedText.WriteLine($"Action chosen: {informationSetHistory.ActionChosen}");
                if (!GameDefinition.Players[informationSetHistory.PlayerMakingDecision].PlayerIsChance)
                {
                    var playersStrategy = GetPlayerStrategyFromOverallPlayerNum(informationSetHistory.PlayerMakingDecision);
                    TabbedText.WriteLine($"Information set: {String.Join(",", informationSetHistory.InformationSet)}");
                    CRMInformationSetNodeTally tallyReferencedInHistory = GetInformationSet(walkHistoryTree);
                    CRMInformationSetNodeTally tallyStoredInInformationSet = (CRMInformationSetNodeTally)playersStrategy.GetInformationSetTreeValue(informationSetHistory.InformationSet);
                    if (tallyReferencedInHistory != tallyStoredInInformationSet)
                        throw new Exception("Tally references do not match.");
                }
                walkHistoryTree = walkHistoryTree.GetBranch(informationSetHistory.ActionChosen);
                TabbedText.Tabs--;
            }
        }

        public double GetUtility(IEnumerable<byte> path, byte player)
        {
            byte index = player;
            if (ChancePlayerExists)
                index--;
            return GetUtilities(path)[index];
        }

        private double[] GetUtilities(IEnumerable<byte> path)
        {
            return (double[]) GameHistoryTree.GetValue(path.GetEnumerator());
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

        public CRMInformationSetNodeTally GetInformationSet(NWayTreeStorage<object> history)
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
            BestResponse
        }

        public void ProcessPossiblePaths(NWayTreeStorage<object> history, List<byte> listSoFar, double probability, Action<List<byte>, double> processor, ActionStrategies actionStrategy)
        {
            // Note that this method is different from GamePlayer.PlayAllPaths, because it relies on the history storage, rather than needing to play the game to discover what the next paths are.
            if (history.IsLeaf())
            {
                processor(listSoFar, probability);
                return;
            }
            double[] probabilities = null;
            byte numPossibleActions;
            if (NodeIsChanceNode(history))
            {
                CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings)history.StoredValue;
                byte decisionIndex = chanceNodeSettings.DecisionNum;
                numPossibleActions = GameDefinition.DecisionsExecutionOrder[decisionIndex].NumPossibleActions;
                probabilities = new double[numPossibleActions];
                for (byte action = 1; action <= numPossibleActions; action++)
                    probabilities[action - 1] = chanceNodeSettings.GetActionProbability(action);
            }
            else
            { // not a chance node or a leaf node
                CRMInformationSetNodeTally informationSet = GetInformationSet(history);
                var decision = GameDefinition.DecisionsExecutionOrder[informationSet.DecisionNum];
                numPossibleActions = decision.NumPossibleActions;
                probabilities = new double[numPossibleActions];
                if (decision.AlwaysDoAction != null)
                    SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions, probabilities, (byte) decision.AlwaysDoAction);
                else if (actionStrategy == ActionStrategies.RegretMatching)
                    informationSet.GetRegretMatchingProbabilities(probabilities);
                else if (actionStrategy == ActionStrategies.AverageStrategy)
                    informationSet.GetAverageStrategies(probabilities);
                else
                    throw new NotImplementedException();
            }
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                if (probabilities[action - 1] > 0)
                {
                    List<byte> nextList = listSoFar.ToList();
                    nextList.Add(action);
                    if (nextList.Count() == 4 && nextList[3] != 4)
                    {
                        var DEBUG = 0;
                    }
                    ProcessPossiblePaths(GetSubsequentHistory(history, action), nextList, probability * probabilities[action - 1], processor, actionStrategy);
                }
            }
        }

        public string GenerateReports()
        {
            GamePlayer player = new GamePlayer(Strategies, GameFactory, EvolutionSettings.ParallelOptimization, GameDefinition);
            GameInputs inputs = GetGameInputs();
            GameProgress startingProgress = GameFactory.CreateNewGameProgress(new IterationID(1));
            StringBuilder sb = new StringBuilder();
            foreach (var reportDefinition in GameDefinition.SimpleReportDefinitions)
            {
                SimpleReport report = new SimpleReport(reportDefinition);
                ProcessPossiblePaths(GameHistoryTree, new List<byte>(), 1.0, (List<byte> actions, double probability) =>
                    {
                        if (actions.Count() > 3 && actions[3] != 4)
                        {
                            var DEBUG = 0;
                        }
                        GameProgress progress = startingProgress.DeepCopy();
                        player.PlayPath(actions.GetEnumerator(), progress, inputs);
                        if (probability > 0)
                            report.ProcessGameProgress(progress, probability);
                    },
                    ActionStrategies.AverageStrategy
                    );
                report.GetReport(sb, false);
            }
            return sb.ToString();
        }

        #endregion

        #region Pi values utility methods

        public double GetPiValue(double[] piValues, byte nonChancePlayerIndex, byte decisionNum)
        {
            return piValues[nonChancePlayerIndex];
        }

        public double GetInversePiValue(double[] piValues, byte nonChancePlayerIndex)
        {
            double product = 1.0;
            for (byte p = 0; p < NumNonChancePlayers; p++)
                if (p != nonChancePlayerIndex)
                    product *= piValues[p];
            return product;
        }

        private double[] GetNextPiValues(double[] currentPiValues, byte nonChancePlayerIndex, double probabilityToMultiplyBy, bool changeOtherPlayers)
        {
            double[] nextPiValues = new double[NumNonChancePlayers];
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
            return nextPiValues;
        }

        private double[] GetInitialPiValues()
        {
            double[] pi = new double[NumNonChancePlayers];
            for (byte p = 0; p < NumNonChancePlayers; p++)
                pi[p] = 1.0;
            return pi;
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
        public double CalculateBestResponse(byte nonChancePlayerIndex)
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
                bestResponseUtility = GEBRPass2(GameHistoryTree, nonChancePlayerIndex, depthToTarget, 1, 1.0);
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
                    var informationSet = GetInformationSet(history);
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

        public double GEBRPass2(NWayTreeStorage<object> history, byte nonChancePlayerIndex, byte depthToTarget, byte depthSoFar, double inversePi)
        {
            if (history.IsLeaf())
                return GetUtilityFromTerminalHistory(history, nonChancePlayerIndex);
            else if (NodeIsChanceNode(history))
                return GEBRPass2_ChanceNode(history, nonChancePlayerIndex, depthToTarget, depthSoFar, inversePi);
            return GEBRPass2_DecisionNode(history, nonChancePlayerIndex, depthToTarget, depthSoFar, inversePi);
        }

        private double GEBRPass2_DecisionNode(NWayTreeStorage<object> history, byte nonChancePlayerIndex, byte depthToTarget, byte depthSoFar, double inversePi)
        {
            var informationSet = GetInformationSet(history);
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
                double expectedValue = GEBRPass2(history.GetBranch(action), nonChancePlayerIndex, depthToTarget, (byte)(depthSoFar + 1), inversePi);
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionNum))
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"Best response action {action} producing expected value {expectedValue}");
                }
                return expectedValue;
            }
            else
            {
                double[] actionProbabilities = new double[numPossibleActions];
                byte? alwaysDoAction = GameDefinition.DecisionsExecutionOrder[decisionNum].AlwaysDoAction;
                if (alwaysDoAction == null)
                    informationSet.GetAverageStrategies(actionProbabilities);
                else
                    SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions, actionProbabilities, (byte)alwaysDoAction);
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
                    double expectedValue = GEBRPass2(history.GetBranch(action), nonChancePlayerIndex, depthToTarget, (byte)(depthSoFar + 1), nextInversePi);
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

        private double GEBRPass2_ChanceNode(NWayTreeStorage<object> history, byte nonChancePlayerIndex, byte depthToTarget, byte depthSoFar, double inversePi)
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
                var valueBelow = GEBRPass2(history.GetBranch(action), nonChancePlayerIndex, depthToTarget, (byte)(depthSoFar + 1), inversePi * probability);
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
        public double VanillaCRM(NWayTreeStorage<object> history, byte nonChancePlayerIndex, double[] piValues)
        {
            if (history.IsLeaf())
                return GetUtilityFromTerminalHistory(history, nonChancePlayerIndex);
            else
            {
                if (NodeIsChanceNode(history))
                    return VanillaCRM_ChanceNode(history, nonChancePlayerIndex, piValues);
                else
                {
                    return VanillaCRM_DecisionNode(history, nonChancePlayerIndex, piValues);
                }
            }
        }

        private double VanillaCRM_DecisionNode(NWayTreeStorage<object> history, byte nonChancePlayerIndex, double[] piValues)
        {
            double[] nextPiValues;
            var informationSet = GetInformationSet(history);
            byte decisionNum = informationSet.DecisionNum;
            byte playerMakingDecision = informationSet.NonChancePlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            // todo: stackalloc or use simple stack based array pool http://stackoverflow.com/questions/1123939/is-c-sharp-compiler-deciding-to-use-stackalloc-by-itself
            double[] actionProbabilities = new double[numPossibleActions];
            byte? alwaysDoAction = GameDefinition.DecisionsExecutionOrder[decisionNum].AlwaysDoAction;
            if (alwaysDoAction == null)
                informationSet.GetRegretMatchingProbabilities(actionProbabilities);
            else
                SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions, actionProbabilities, (byte) alwaysDoAction);
            double[] expectedValueOfAction = new double[numPossibleActions];
            double expectedValue = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                double probabilityOfAction = actionProbabilities[action - 1];
                nextPiValues = GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false);
                if (TraceVanillaCRM)
                {
                    TabbedText.WriteLine($"decisionNum {decisionNum} optimizing player {nonChancePlayerIndex}  own decision {playerMakingDecision == nonChancePlayerIndex} action {action} probability {probabilityOfAction} ...");
                    TabbedText.Tabs++;
                }
                expectedValueOfAction[action - 1] = VanillaCRM(GetSubsequentHistory(history, action), nonChancePlayerIndex, nextPiValues);
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
                        TabbedText.WriteLine($"Regrets: Action {action} regret {regret} prob-adjust {inversePi * regret} new regret {informationSet.GetCumulativeRegret(action)} strategy inc {pi * actionProbabilities[action - 1]} new cum strategy {informationSet.GetCumulativeStrategy(action)}");
                    }
                }
            }
            return expectedValue;
        }

        private static void SetProbabilitiesToAlwaysDoParticularAction(byte numPossibleActions, double[] actionProbabilities, byte alwaysDoAction)
        {
            for (byte action = 1; action <= numPossibleActions; action++)
                if (action == alwaysDoAction)
                    actionProbabilities[action - 1] = 1.0;
                else
                    actionProbabilities[action - 1] = 0;
        }

        private double VanillaCRM_ChanceNode(NWayTreeStorage<object> history, byte nonChancePlayerIndex, double[] piValues)
        {
            CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings)history.StoredValue;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionNum);
            double[] nextPiValues = null;
            bool equalProbabilities = chanceNodeSettings.AllProbabilitiesEqual();
            if (equalProbabilities) // can set next probabilities once for all actions
                nextPiValues = GetNextPiValues(piValues, nonChancePlayerIndex, chanceNodeSettings.GetActionProbability(1), true);
            double expectedValue = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                if (!equalProbabilities) // must set probability separately for each action we take
                    nextPiValues = GetNextPiValues(piValues, nonChancePlayerIndex, chanceNodeSettings.GetActionProbability(action), true);
                double actionProbability = chanceNodeSettings.GetActionProbability(action);
                if (TraceVanillaCRM)
                {
                    TabbedText.WriteLine($"Chance decisionNum {chanceNodeSettings.DecisionNum} action {action} probability {actionProbability} ...");
                    TabbedText.Tabs++;
                }
                double expectedValueParticularAction = VanillaCRM(GetSubsequentHistory(history, action), nonChancePlayerIndex, nextPiValues);
                expectedValue += actionProbability * expectedValueParticularAction;
                if (TraceVanillaCRM)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"... action {action} expected value {expectedValueParticularAction} cum expected value {expectedValue}");
                }
            }

            return expectedValue;
        }

        public void SolveVanillaCFR()
        {
            const int numIterationsToRun = 10000;

            int? reportEveryNIterations = 100;

            double[] utilities = new double[NumNonChancePlayers];

            for (int iteration = 0; iteration < numIterationsToRun; iteration++)
            {
                for (byte p = 0; p < NumNonChancePlayers; p++)
                    utilities[p] = VanillaCRM(GameHistoryTree, p, GetInitialPiValues());
                if (reportEveryNIterations != null && iteration % reportEveryNIterations == 0)
                {
                    Debug.WriteLine(GenerateReports());
                    for (byte p = 0; p < NumNonChancePlayers; p++)
                    {
                        double bestResponseUtility = CalculateBestResponse(p);
                        Debug.WriteLine($"Player {p} utility {utilities[p]} using best response {bestResponseUtility} difference {bestResponseUtility - utilities[p]}");
                    }
                }
            }

        }

        #endregion
    }
}
