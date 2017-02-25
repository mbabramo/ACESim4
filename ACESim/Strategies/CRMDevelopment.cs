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

            Type theType = GameFactory.GetSimulationSettingsType();
            InputVariables inputVariables = new InputVariables(CurrentExecutionInformation);
            GameInputs inputs = inputVariables.GetGameInputs(theType, 1, new IterationID(1), CurrentExecutionInformation);

            // Create game trees
            GameHistoryTree = new NWayTreeStorageInternal<object>(GameDefinition.DecisionsExecutionOrder.First().NumPossibleActions);
            foreach (Strategy s in Strategies)
            {
                if (!s.PlayerInfo.PlayerIsChance)
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
                    if (!GameDefinition.Players[informationSetHistory.PlayerMakingDecision].PlayerIsChance)
                    {
                        var decision = GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
                        bool isNecessarilyLast = decision.IsAlwaysPlayersLastDecision || informationSetHistory.IsTerminalAction;
                        var playersStrategy = Strategies[informationSetHistory.PlayerMakingDecision];
                        if (walkHistoryTree.StoredValue == null)
                        {
                            // create the information set node if necessary, within initialized tally values
                            var informationSetNode = playersStrategy.SetInformationSetTreeValueIfNotSet(
                                informationSetHistory.InformationSet,
                                isNecessarilyLast,
                                () =>
                                {
                                    CRMInformationSetNodeTally nodeInfo = new CRMInformationSetNodeTally(decision.NumPossibleActions);
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
                    var playersStrategy = Strategies[informationSetHistory.PlayerMakingDecision];
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

        public double GetNonChancePlayerContribution(double[] nonChancePlayerProbabilityContributions, byte playerNum)
        {
            if (ChancePlayerExists)
                return nonChancePlayerProbabilityContributions[playerNum - 1]; // i.e., first player is given index 1 but is in index 0 of array
            return nonChancePlayerProbabilityContributions[playerNum]; // i.e., first player is given index 0 and is in index 0 of array
        }

        public double GetUtilityFromTerminalHistory(NWayTreeStorage<object> history, byte playerNum)
        {
            double[] utilities = ((double[])history.StoredValue);
            if (ChancePlayerExists)
                return utilities[playerNum - 1]; // i.e., first player is given index 1 but is in index 0 of array
            return utilities[playerNum]; // i.e., first player is given index 0 and is in index 0 of array
        }

        public bool NodeIsChanceNode(NWayTreeStorage<object> history)
        {
            return history.StoredValue == null;
        }

        public byte NumPossibleActionsAtDecision(byte decisionNum)
        {
            return GameDefinition.DecisionsExecutionOrder[decisionNum].NumPossibleActions;
        }

        public NWayTreeStorage<object> GetSubsequentHistory(NWayTreeStorage<object> history, byte action)
        {
            return history.GetBranch(action);
        }


        int VanillaCFRIteration; // controlled in SolveVanillaCFR

        double[] VanillaCFRPlayerProbabilityContributions; // Rather than create a new array of player contributions for each recursion depth, we just store the numbers for the appropriate recursion depth in this array

        public double GetPlayerProbabilityContribution(byte playerNum, byte decisionNum)
        {
            byte index = (byte)(decisionNum * NumNonChancePlayers + playerNum - (ChancePlayerExists ? 1 : 0));
            return VanillaCFRPlayerProbabilityContributions[index];
        }

        public void SetPlayerProbabilityContribution(byte playerNum, byte decisionNum, double contribution)
        {
            byte index = (byte)(decisionNum * NumNonChancePlayers + playerNum - (ChancePlayerExists ? 1 : 0));
            VanillaCFRPlayerProbabilityContributions[index] = contribution;
        }

        public double VanillaCFR(NWayTreeStorage<object> history, byte decisionNum, byte playerNum, int iteration, byte )
        {
            if (history.IsLeaf())
                return GetUtilityFromTerminalHistory(history, playerNum);
            else
            {
                int numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
                if (NodeIsChanceNode(history))
                {
                    double sumLaterRegrets = 0;
                    double probabilityEachChanceDecision = 1.0 / (double)numPossibleActions; // Note: Could optimize by calculating once
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        double[] nextContributions = new double[nonChancePlayerProbabilityContributions.Length]; // Note: Could optimize by just having 
                    }
                }
            }

        }

        public void SolveVanillaCFR()
        {
            const int numIterationsToRun = 1000000;
            const int maxPlayerContributionsHistoryLength = 30;
            VanillaCFRPlayerProbabilityContributions = new double[maxPlayerContributionsHistoryLength];
            for (byte i = 0; i < NumNonChancePlayers; i++)
                SetPlayerProbabilityContribution();

            for (int iteration = 0; iteration < numIterationsToRun; iteration++)
            {

            }
        }
    }
}
