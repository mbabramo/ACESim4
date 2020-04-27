using ACESim;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public class GameProgressTree : IEnumerable<GameProgress> 
    {
        // DEBUG -- initially, we allocate based on action probabilities. These might be epsilon-probabilities. Then we allocate based on the child cumulative proportions. So, we can generalize the idea of action probabilities to represent child cumulative proportions. And then we should be able to get rid of CumulativeProportion. 

        public class GameProgressTreeAllocationInfo
        {
            /// <summary>
            /// The index in the list of allocations for the tree.
            /// </summary>
            public int AllocationIndex;
            public int? MinDecisionIndexReached;
        }

        /// <summary>
        /// Information on a node in the GameProgressTree, pursuant to particular criteria for allocating observations.  Observations may be allocated (1) pursuant to particular exploration probabilities for each player; OR (2) pursuant to specified allocation proportions. After we initially explore using a set of exploration probabilities (which may be zero, if the players are playing on-policy), we then calculate the next criterion by aggregating up the tree the cumulative reach probability pursuant to the original criterion of all game progresses that reach the first decision not reached in all game paths.
        /// </summary>
        public class GameProgressTreeNodeAllocation
        {
            public (int, int) ObservationRange;
            public int NumObservations => ObservationRange.Item2 - ObservationRange.Item1 + 1;
            /// <summary>
            /// Where encompassing all game progresses (regardless of the decision being played up to), the action probabilities at this game point; these will be set by the algorithm allocating observations. When encompassing only game progresses that reach at least a particular decision index, before the allocation algorithm begins, these are set to reflect the cumulative reach probability of game progresses lower in the tree reach the necessary point, and then when the algorithm elaborates the tree, this will reflect the action probabilities.
            /// </summary>
            public double[] ChildProportions;
            /// <summary>
            /// The cumulative reach probability of this node for the allocation. 
            /// </summary>
            public double CumulativeReachProbability;

            public override string ToString() => $"{ObservationRange}: Reach {CumulativeReachProbability} ChildProportions {String.Join(",", ChildProportions.Select(x => x.ToSignificantFigures_WithSciNotationForVerySmall(4)))}";
        }

        /// <summary>
        /// Information on the probability of playing to and from this node, given the exploration scheme in force.
        /// </summary>
        public class GameProgressTreeNodeProbabilities
        {
            /// <summary>
            /// The probability that each player will play to this node given the exploration probabilities for each player. Note that this does not reflect the child proportions that are used.
            /// </summary>
            public double[] PlayToHereProbabilities;
            /// <summary>
            /// The probability that all players combined will play to this node.
            /// </summary>
            public double PlayToHereCombinedProbability;
            /// <summary>
            /// The probability that the player at this node will choose each of the child actions, taking into account the players' exploration values.
            /// </summary>
            public double[] ActionProbabilities;
            /// <summary>
            /// The allocations of observations pursuant to these probabilities. Each allocation must be narrower than the previous, so once we have an allocation that has null for ChildProportions, there will be no further allocations.
            /// </summary>
            public List<GameProgressTreeNodeAllocation> Allocations = new List<GameProgressTreeNodeAllocation>();

            public string ToString(int allocationIndex) => Allocations[allocationIndex].ToString() + $" action probabilities {String.Join(",", ActionProbabilities.Select(x => x.ToSignificantFigures_WithSciNotationForVerySmall(4)))}";

            public GameProgressTreeNodeProbabilities((int, int) observationRange, double[] playToHereProbabilities, double[] actionProbabilities)
            {
                Allocations.Add(new GameProgressTreeNodeAllocation()
                {
                    ObservationRange = observationRange,
                    ChildProportions = null,
                    CumulativeReachProbability = 1.0
                });
                PlayToHereProbabilities = playToHereProbabilities;
                PlayToHereCombinedProbability = playToHereProbabilities.Aggregate((a, x) => a * x);
                ActionProbabilities = actionProbabilities;
            }

            public GameProgressTreeNodeAllocation GetAllocation(int allocationIndex)
            {
                if (Allocations.Count > allocationIndex)
                    return Allocations[allocationIndex];
                return null;
            }

            public void CreateNextAllocation(int allocationIndexToCreate, double[] childCumulativeReachProbabilities)
            {
                if (!Allocations.Any())
                    throw new Exception();
                int previousAllocationIndex = allocationIndexToCreate - 1;
                GameProgressTreeNodeAllocation previousAllocation = Allocations[previousAllocationIndex];

                double sum = childCumulativeReachProbabilities.Sum();
                if (sum == 0)
                    return; // nothing more to allocate. 
                double[] childProportions = childCumulativeReachProbabilities.Select(x => x / sum).ToArray();

                GameProgressTreeNodeAllocation newAllocation = new GameProgressTreeNodeAllocation()
                {
                    ChildProportions = childProportions,
                    CumulativeReachProbability = sum,
                    // Note that observation range cannot yet be set, since it depends on cumulative reach probabilities elsewhere in the tree. We will thus set this going downward through the tree.
                };
            }
        }

        public class GameProgressTreeNodeInfo
        {
            public IDirectGamePlayer DirectGamePlayer;
            public GameProgress GameProgress => DirectGamePlayer.GameProgress;

            public List<(double[] explorationValues, GameProgressTreeNodeProbabilities probabilitiesInfo)> NodeProbabilityInfos = new List<(double[] explorationValues, GameProgressTreeNodeProbabilities probabilitiesInfo)>();

            public GameProgressTreeNodeInfo(IDirectGamePlayer directGamePlayer, (int, int) observationRange, double[] explorationValues, double[] playToHereProbabilities)
            {
                DirectGamePlayer = directGamePlayer;
                (double[] explorationValues, GameProgressTreeNodeProbabilities) npi = GetNodeProbabilityInfo(observationRange, explorationValues, playToHereProbabilities);
                NodeProbabilityInfos = new List<(double[] explorationValues, GameProgressTreeNodeProbabilities probabilitiesInfo)>()
                {
                    npi
                };
            }

            private (double[] explorationValues, GameProgressTreeNodeProbabilities) GetNodeProbabilityInfo((int, int) observationRange, double[] explorationValues, double[] playToHereProbabilities)
            {
                double[] actionProbabilitiesWithExploration = GetActionProbabilitiesWithExploration(explorationValues);
                (double[] explorationValues, GameProgressTreeNodeProbabilities) npi = (explorationValues, new GameProgressTreeNodeProbabilities(observationRange, playToHereProbabilities, actionProbabilitiesWithExploration)
                {

                });
                return npi;
            }

            private double[] GetActionProbabilitiesWithExploration(double[] explorationValues)
            {
                double[] actionProbabilities = DirectGamePlayer.GetActionProbabilities();
                byte currentPlayer = DirectGamePlayer.CurrentPlayer.PlayerIndex;
                if (explorationValues != null && currentPlayer < explorationValues.Length && explorationValues[currentPlayer] > 0)
                {
                    double explorationValue = explorationValues[currentPlayer];
                    double equalProbabilities = 1.0 / (double)actionProbabilities.Length;
                    return actionProbabilities.Select(x => explorationValue * equalProbabilities + (1.0 - explorationValue) * x).ToArray();
                }

                return actionProbabilities.ToArray();
            }

            public GameProgressTreeNodeProbabilities GetProbabilitiesInfo(double[] explorationValues)
            {
                foreach (var npi in NodeProbabilityInfos)
                {
                    if ((npi.explorationValues == null && explorationValues == null) ||
(npi.explorationValues.SequenceEqual(explorationValues)))
                        return npi.probabilitiesInfo;
                }
                return null;
            }

            public GameProgressTreeNodeProbabilities GetOrAddProbabilitiesInfo((int, int) observationRange, double[] explorationValues, double[] playToHereProbabilities)
            {
                GameProgressTreeNodeProbabilities value = GetProbabilitiesInfo(explorationValues);
                if (value == null)
                {
                    var result = GetNodeProbabilityInfo(observationRange, explorationValues, playToHereProbabilities);
                    NodeProbabilityInfos.Add(result);
                    value = result.Item2;
                }
                return value;
            }

            public override string ToString() => ToString(null, 0);

            public string ToString(double[] explorationValues, int allocationIndex)
            {
                GameProgressTreeNodeProbabilities value = GetProbabilitiesInfo(explorationValues);
                string resultString = GameProgress.GameComplete ? String.Join(",", GameProgress.GetNonChancePlayerUtilities().Select(x => x.ToSignificantFigures(5))) : "incomplete";
                string result = $"{value.ToString(allocationIndex)} Result: {resultString}";
                return result;
            }
        }

        NWayTreeStorageInternal<GameProgressTreeNodeInfo> Tree;
        int InitialRandSeed;

        public GameProgressTree(int randSeed, int totalObservations, IDirectGamePlayer directGamePlayer, double[] explorationValues)
        {
            double[] playToHereProbabilities = Enumerable.Range(1, explorationValues.Length).Select(x => (double)1).ToArray(); // both players always play to beginning of game
            Tree = new NWayTreeStorageInternal<GameProgressTreeNodeInfo>(new NWayTreeStorageInternal<GameProgressTreeNodeInfo>(null))
            {
                StoredValue = new GameProgressTreeNodeInfo(directGamePlayer, (1, totalObservations), explorationValues, playToHereProbabilities)
            };
            InitialRandSeed = randSeed;
        }

        public override string ToString() => Tree?.ToTreeString("Action");

        public async Task CompleteTree(bool doParallel) => await CompleteTree(doParallel, (null, 0));

        public async Task CompleteTree(bool doParallel, (double[] explorationValues, int allocationIndex) allocationInfo)
        {
            await Tree.BranchInParallel(doParallel, allocationInfo, DoBranching);
            // DEBUG CompleteIncompletePlayers(doParallel);
        }

        public (byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)[] DoBranching(NWayTreeStorageInternal<GameProgressTreeNodeInfo> nodeContainer, (double[] explorationValues, int allocationIndex) allocationInfo)
        {
            var sourceNode = nodeContainer.StoredValue;
            var probabilitiesInfo = sourceNode.GetProbabilitiesInfo(allocationInfo.explorationValues);
            var allocation = probabilitiesInfo.GetAllocation(allocationInfo.allocationIndex);
            double[] playToHereProbabilities = probabilitiesInfo.PlayToHereProbabilities;
            if (allocationInfo.allocationIndex == 0)
            {
                // The child proportions have not been set. Thus, we use the action probabilities.
                allocation.ChildProportions = probabilitiesInfo.ActionProbabilities.ToArray();
            }
            int randSeed = sourceNode.DirectGamePlayer.GameProgress.GetActionsPlayedHash(InitialRandSeed);
            (int, int)?[] subranges = DivideRangeIntoSubranges(allocation.ObservationRange, allocation.ChildProportions, randSeed);
            byte numChildren = (byte) subranges.Length;
            (byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)[] children = new (byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)[numChildren];
            for (byte a = 1; a <= numChildren; a++)
            {
                (int, int)? subrange = subranges[a - 1];
                double[] childPlayToHereProbabilities = playToHereProbabilities.ToArray();
                childPlayToHereProbabilities[sourceNode.DirectGamePlayer.CurrentPlayer.PlayerIndex] *= probabilitiesInfo.ActionProbabilities[a - 1];
                if (subrange != null)
                {
                    int numInSubrange = subrange == null ? 0 : subrange.Value.Item2 - subrange.Value.Item1 + 1;
                    var child = nodeContainer.GetBranch(a)?.StoredValue;
                    if (child == null)
                    {

                        IDirectGamePlayer childGamePlayer = sourceNode.DirectGamePlayer.CopyAndPlayAction((byte)a);
                        child = new GameProgressTreeNodeInfo(childGamePlayer, subrange.Value, allocationInfo.explorationValues, childPlayToHereProbabilities);
                    }
                    else
                    {
                        GameProgressTreeNodeProbabilities nodeProbabilities = child.GetOrAddProbabilitiesInfo(subrange.Value, allocationInfo.explorationValues, childPlayToHereProbabilities);
                    }
                    bool isLeaf = child.DirectGamePlayer.GameComplete;
                    children[a - 1] = (a, child, isLeaf);
                }
            }
            return children;
        }

        public (int, int)?[] DivideRangeIntoSubranges((int, int) range, double[] proportion, int randSeed)
        {
            int numSubranges = proportion.Length;
            (int, int)?[] subranges = new (int, int)?[numSubranges];
            int numInRange = range.Item2 - range.Item1 + 1;
            int[] numInEachSubrange = DivideItems(numInRange, proportion, randSeed);
            int startingValue = range.Item1 - 1;
            for (int i = 0; i < numSubranges; i++)
            {
                if (numInEachSubrange[i] > 0)
                {
                    subranges[i] = (startingValue + 1, startingValue + numInEachSubrange[i]);
                    startingValue += numInEachSubrange[i];
                }
            }
            return subranges;
        }

        public int[] DivideItems(int numItems, double[] proportion, int randSeed)
        {
            int[] integerProportions = proportion.Select(x => (int)(numItems * x)).ToArray();
            int initialSum = integerProportions.Sum();
            int remainingItems = numItems - initialSum;
            if (remainingItems > 0)
            {
                double[] remainders = proportion.Select(x => (double)(numItems * x) - (int)(numItems * x)).ToArray();
                bool[] incremented = new bool[remainders.Length]; // we are doing our selections w/out replacement (unless we have already used all options)
                int randomNumberDraws = 0;
                for (int i = 0; i < remainingItems; i++)
                {
                    byte index = 0;
                    do
                    {
                        ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(randSeed, 2_000_000 + randomNumberDraws++);
                        index = r.GetRandomIndex(remainders);
                    }
                    while (incremented[index]); // i.e., if we already used this (and haven't used everything) then find something else to pick
                    integerProportions[index]++;
                    incremented[index] = true;
                    if (incremented.All(x => x == true))
                        incremented = new bool[remainders.Length];
                }
            }
            return integerProportions;
        }

        private void AccumulateCombined(bool doParallel, double[] explorationValues, int newAllocationIndex)
        {
            Tree.WalkTree(treeStorage => { }, treeStorage =>
            {
                GameProgressTreeNodeInfo node = treeStorage.StoredValue;
                byte numPossibleActions = node.DirectGamePlayer.CurrentDecision.NumPossibleActions;
                var childCumulativeReachProbabilities = Enumerable.Range(1, numPossibleActions).Select(a =>
                {
                    var branch = treeStorage.GetBranch((byte)a);
                    if (branch == null)
                        return 0;
                    GameProgressTreeNodeInfo branchNode = branch.StoredValue;
                    GameProgressTreeNodeProbabilities childProbabilitiesInfo = branchNode.GetProbabilitiesInfo(explorationValues);
                    if (childProbabilitiesInfo == null)
                        return 0;
                    GameProgressTreeNodeAllocation childPreviousAllocation = childProbabilitiesInfo.GetAllocation(newAllocationIndex - 1);
                    double probability = childPreviousAllocation.CumulativeReachProbability;
                    return probability;
                }).ToArray();
                GameProgressTreeNodeProbabilities probabilitiesInfo = node.GetProbabilitiesInfo(explorationValues);
                probabilitiesInfo.CreateNextAllocation(newAllocationIndex, childCumulativeReachProbabilities);
            });
        }

        public IEnumerator<GameProgress> GetEnumerator()
        {
            foreach (var treeNode in Tree.GetAllTreeNodes())
            {
                GameProgressTreeNodeInfo treeNodeInfo = treeNode.storedValue;
                GameProgress gameProgress = treeNodeInfo.GameProgress;
                if (gameProgress.GameComplete)
                {
                    (int, int) observationRange = treeNodeInfo.GetProbabilitiesInfo(null).GetAllocation(0).ObservationRange;
                    int observations = observationRange.Item2 - observationRange.Item1 + 1;
                    for (int o = 0; o < observations; o++)
                    {
                        yield return gameProgress;
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
