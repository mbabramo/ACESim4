﻿using FluentAssertions;
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

        public double[] ProbabilityEachChanceAction;

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
            SetChanceStrategy();
        }

        private void SetChanceStrategy()
        {
            // we assume that each chance action is equally likely.
            for (int i = 0; i < GameDefinition.DecisionsExecutionOrder.Count(); i++)
                if (GameDefinition.Players[GameDefinition.DecisionsExecutionOrder[i].PlayerNumber].PlayerIsChance)
                    ProbabilityEachChanceAction[i] = 1.0 / (double)GameDefinition.DecisionsExecutionOrder[i].NumPossibleActions;
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

            Type theType = GameFactory.GetSimulationSettingsType();
            InputVariables inputVariables = new InputVariables(CurrentExecutionInformation);
            GameInputs inputs = inputVariables.GetGameInputs(theType, 1, new IterationID(1), CurrentExecutionInformation);

            // Create game trees
            GameHistoryTree = new NWayTreeStorageInternal<object>(GameDefinition.DecisionsExecutionOrder.First().NumPossibleActions);
            foreach (Strategy s in Strategies)
            {
                s.CreateInformationSetTree(GameDefinition.DecisionsExecutionOrder.First(x => x.PlayerNumber == s.PlayerInfo.PlayerNumber).NumPossibleActions);
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
                    if (GameDefinition.Players[informationSetHistory.PlayerMakingDecision].PlayerIsChance)
                    {
                        walkHistoryTree.StoredValue = informationSetHistory.DecisionIndex; // for a chance decision, just store the decision index
                    }
                    else
                    {
                        var decision = GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
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
                                    CRMInformationSetNodeTally nodeInfo = new CRMInformationSetNodeTally(informationSetHistory.DecisionIndex, decision.NumPossibleActions);
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
                    var informationSetNodeReferencedInHistoryNode = ((NWayTreeStorage<object>)walkHistoryTree.StoredValue);
                    var informationSetNode = playersStrategy.GetInformationSetTreeNode(informationSet);
                    CRMInformationSetNodeTally tallyReferencedInHistory = (CRMInformationSetNodeTally)informationSetNodeReferencedInHistoryNode.StoredValue;
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
            return (history.StoredValue is byte);
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
            return (CRMInformationSetNodeTally)history.StoredValue;
        }


        int VanillaCFRIteration; // controlled in SolveVanillaCFR

        // Rather than create a new array of player contributions for each recursion depth, we just store the numbers for the appropriate recursion depth in this array. So PiValues[0] = Pi1 in the vanilla CFR algorithm, i.e. corresponding to the first player. If the first player is the one that we are optimizing, then this represents the player's own probability contribution to the result. If the second player is the one that we are optimizing, however, then PiValues[0] represents the other player AND chance's contribution to the result. That is, it's pi(-1), i.e. everyone else's contribution.
        double[] VanillaCFRPiValues; 

        double[] CurrentRegretMatching;
        double[] CurrentRegrets;

        public double GetPiValue(byte nonChancePlayerIndex, byte decisionNum)
        {
            byte index = (byte)(decisionNum * NumNonChancePlayers + nonChancePlayerIndex);
            return VanillaCFRPiValues[index];
        }

        public void SetPiValue(byte nonChancePlayerIndex, byte decisionNum, double contribution)
        {
            byte index = (byte)(decisionNum * NumNonChancePlayers + nonChancePlayerIndex);
            VanillaCFRPiValues[index] = contribution;
        }

        public void ResetCurrentRegrets(byte numPossibleActions)
        {
            for (byte a = 0; a < numPossibleActions; a++)
                CurrentRegrets[a] = 0;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="history"></param>
        /// <param name="decisionNum"></param>
        /// <param name="nonChancePlayerIndex">0 for first non-chance player, etc. Note that this corresponds in Lanctot to 1, 2, etc. We are using zero-basing for player index (even though we are 1-basing actions).</param>
        /// <returns></returns>
        public double VanillaCFR(NWayTreeStorage<object> history, byte nonChancePlayerIndex)
        {
            if (history.IsLeaf())
                return GetUtilityFromTerminalHistory(history, nonChancePlayerIndex);
            else
            {
                if (NodeIsChanceNode(history))
                    return GetLaterRegretsAfterChance(history, nonChancePlayerIndex);
                else
                {
                    var informationSet = GetInformationSet(history);
                    byte decisionNum = informationSet.DecisionNum;
                    byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
                    byte nextDecisionNum = (byte)(decisionNum + 1);
                    informationSet.SetRegretMatchingProbabilities(CurrentRegretMatching);
                    double currentRegretSum = 0;
                    ResetCurrentRegrets(numPossibleActions);
                }
            }

        }

        private double GetLaterRegretsAfterChance(NWayTreeStorage<object> history, byte nonChancePlayerIndex)
        {
            byte decisionNum = (byte)history.StoredValue;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            byte nextDecisionNum = (byte)(decisionNum + 1);
            double probabilityEachChanceAction = ProbabilityEachChanceAction[decisionNum];
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                double currentPlayerProbabilityContribution = GetPiValue(p, decisionNum);
                double nextPlayerProbabilityContribution;
                if (p == nonChancePlayerIndex)
                    nextPlayerProbabilityContribution = currentPlayerProbabilityContribution;
                else
                    nextPlayerProbabilityContribution = currentPlayerProbabilityContribution * probabilityEachChanceAction;
                SetPiValue(p, nextDecisionNum, nextPlayerProbabilityContribution);
            }
            double sumLaterRegrets = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                sumLaterRegrets += probabilityEachChanceAction * VanillaCFR(GetSubsequentHistory(history, action), nextDecisionNum, nonChancePlayerIndex);
            }

            return sumLaterRegrets;
        }

        public void SolveVanillaCFR()
        {
            const int numIterationsToRun = 1000000;
            InitializeVanillaCFR();

            for (int iteration = 0; iteration < numIterationsToRun; iteration++)
                for (byte p = 0; p < NumNonChancePlayers; p++)
                    VanillaCFR(GameHistoryTree, 0, p);
        }

        private void InitializeVanillaCFR()
        {
            const int maxPlayerContributionsHistoryLength = 30;
            int maxNumActions = GameDefinition.DecisionsExecutionOrder.Max(x => x.NumPossibleActions);
            VanillaCFRPiValues = new double[maxPlayerContributionsHistoryLength];
            for (byte i = 0; i < NumNonChancePlayers; i++)
                SetPiValue(i, 0, 1.0);
            CurrentRegretMatching = new double[maxNumActions];
            CurrentRegrets = new double[maxNumActions];
        }
    }
}
