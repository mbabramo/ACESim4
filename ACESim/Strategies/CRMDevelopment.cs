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

            int numPlayed = 0;
            foreach (var progress in player.PlayAllPaths(inputs))
            {
                numPlayed++;

                // First, add the utilities at the end of the tree.
                var actionsEnumerator = progress.GameHistory.GetActions().GetEnumerator();
                var numPossibleActionsEnumerator = progress.GameHistory.GetNumPossibleActions().GetEnumerator();
                GameHistoryTree.SetValue(actionsEnumerator, true, progress.GetNonChancePlayerUtilities());

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
                                walkHistoryTree.StoredValue = new CRMChanceNodeSettings_UnequalProbabilities() { DecisionNum = informationSetHistory.DecisionIndex, Probabilities = GameDefinition.GetChanceActionProbabilities(informationSetHistory.DecisionIndex, progress) }; // the probabilities depend on the current state of the game
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
                    }
                    walkHistoryTree = walkHistoryTree.GetBranch(informationSetHistory.ActionChosen);
                }
            }

            PrintSameGameResults(player, inputs);
        }

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
                    var informationSet = informationSetHistory.InformationSet.ToList();
                    TabbedText.WriteLine($"Information set: {String.Join(",", informationSet)}");
                    CRMInformationSetNodeTally tallyReferencedInHistory = GetInformationSet(walkHistoryTree);
                    CRMInformationSetNodeTally tallyStoredInInformationSet = (CRMInformationSetNodeTally) playersStrategy.GetInformationSetTreeValue(informationSet);
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
            int numPossibleActions;
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
                numPossibleActions = GameDefinition.DecisionsExecutionOrder[informationSet.DecisionNum].NumPossibleActions;
                probabilities = new double[numPossibleActions];
                if (actionStrategy == ActionStrategies.RegretMatching)
                    informationSet.GetRegretMatchingProbabilities(probabilities); // DEBUG -- might do something else
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
                        GameProgress progress = startingProgress.DeepCopy();
                        player.PlayPath(actions.GetEnumerator(), progress, inputs);
                        report.ProcessGameProgress(progress, probability);
                    },
                    ActionStrategies.AverageStrategy
                    );
                report.GetReport(sb, false);
            }
            return sb.ToString();
        }

        int VanillaCFRIteration; // controlled in SolveVanillaCFR
        

        public double GetPiValue(double[] piValues, byte nonChancePlayerIndex, byte decisionNum)
        {
            byte index = (byte)(decisionNum * NumNonChancePlayers + nonChancePlayerIndex);
            return piValues[index];
        }

        public double GetInversePiValue(double[] piValues, byte nonChancePlayerIndex, byte decisionNum)
        {
            double product = 1.0;
            for (byte p = 0; p < NumNonChancePlayers; p++)
                if (p != nonChancePlayerIndex)
                    product *= piValues[p];
            return product;
        }

        bool TraceVanillaCRM = true;

        /// <summary>
        /// Performs an iteration of vanilla counterfactual regret minimization.
        /// </summary>
        /// <param name="recursionDepth">Recursion depth (starting at 0)</param>
        /// <param name="history">The game tree, pointing to the particular point in the game where we are located</param>
        /// <param name="nonChancePlayerIndex">0 for first non-chance player, etc. Note that this corresponds in Lanctot to 1, 2, etc. We are using zero-basing for player index (even though we are 1-basing actions).</param>
        /// <returns></returns>
        public double VanillaCRM(byte recursionDepth, NWayTreeStorage<object> history, byte nonChancePlayerIndex, double[] piValues)
        {
            if (history.IsLeaf())
                return GetUtilityFromTerminalHistory(history, nonChancePlayerIndex);
            else
            {
                if (NodeIsChanceNode(history))
                    return VanillaCFR_ChanceNode(recursionDepth, history, nonChancePlayerIndex, piValues);
                else
                {
                    return VanillaCFR_DecisionNode(recursionDepth, history, nonChancePlayerIndex, piValues);
                }
            }
        }

        private double VanillaCFR_DecisionNode(byte recursionDepth, NWayTreeStorage<object> history, byte nonChancePlayerIndex, double[] piValues)
        {
            double[] nextPiValues;
            var informationSet = GetInformationSet(history);
            byte decisionNum = informationSet.DecisionNum;
            byte playerMakingDecision = informationSet.NonChancePlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            byte nextRecursionDepth = (byte)(recursionDepth + 1);
            double[] actionProbabilities = new double[numPossibleActions];
            informationSet.GetRegretMatchingProbabilities(actionProbabilities);
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
                expectedValueOfAction[action - 1] = VanillaCRM(nextRecursionDepth, GetSubsequentHistory(history, action), nonChancePlayerIndex, nextPiValues);
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
                    double inversePi = GetInversePiValue(piValues, nonChancePlayerIndex, recursionDepth);
                    double pi = GetPiValue(piValues, nonChancePlayerIndex, recursionDepth);
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

        private double VanillaCFR_ChanceNode(byte recursionDepth, NWayTreeStorage<object> history, byte nonChancePlayerIndex, double[] piValues)
        {
            CRMChanceNodeSettings chanceNodeSettings = (CRMChanceNodeSettings)history.StoredValue;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionNum);
            double[] nextPiValues = null;
            byte nextRecursionDepth =  (byte)(recursionDepth + 1);
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
                double expectedValueParticularAction = VanillaCRM(nextRecursionDepth, GetSubsequentHistory(history, action), nonChancePlayerIndex, nextPiValues);
                expectedValue += actionProbability * expectedValueParticularAction;
                if (TraceVanillaCRM)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"... action {action} expected value {expectedValueParticularAction} cum expected value {expectedValue}");
                }
            }

            return expectedValue;
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

        public void SolveVanillaCFR()
        {
            const int numIterationsToRun = 10000;

            int? reportEveryNIterations = 100;

            for (int iteration = 0; iteration < numIterationsToRun; iteration++)
            {
                for (byte p = 0; p < NumNonChancePlayers; p++)
                    VanillaCRM(0, GameHistoryTree, p, GetInitialPiValues());
                if (reportEveryNIterations != null && iteration % reportEveryNIterations == 0)
                    Debug.WriteLine(GenerateReports());
            }

        }
    }
}
