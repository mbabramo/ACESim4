using ACESim;
using ACESimBase.Util;
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
            /// This is stored temporarily to determine the child proportions of the parent node when
            /// creating a new allocation. For nodes at the decision index being used to generate the
            /// new allocation, the ChildProportionContribution will be equal to the PlayToHereCombinedProbability.
            /// For nodes earlier in the tree, the ChildProportionContribution will be the sum of the child
            /// ChildProportionContributions. Meanwhile, ChildProportions will be set to the relative 
            /// weight of the children's ChildProportionContribution values.
            /// </summary>
            public double ChildProportionContribution;

            public override string ToString() => $"{ObservationRange}: ChildProportions {ChildProportions.ToSignificantFigures_WithSciNotationForVerySmall(4)} ChildProportionContribution {ChildProportionContribution.ToSignificantFigures_WithSciNotationForVerySmall(4)}";
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

            public string ToString(int allocationIndex, bool includeActionProbabilities)
            {
                string s = (Allocations[allocationIndex]?.ToString() ?? "");
                if (includeActionProbabilities)
                    return s + $" action probabilities {ActionProbabilities.ToSignificantFigures_WithSciNotationForVerySmall(4)}";
                return s;
            }
                    
            
            public override string ToString()
            {
                StringBuilder s = new StringBuilder();
                s.AppendLine($"Action probabilities {ActionProbabilities.ToSignificantFigures_WithSciNotationForVerySmall(4)}");
                for (int i = 0; i < Allocations.Count(); i++)
                    s.AppendLine("Allocation " + i + ": " + ToString(i, false));
                return s.ToString();
            }

            public GameProgressTreeNodeProbabilities((int, int) observationRange, double[] playToHereProbabilities, double[] actionProbabilities, byte numDecisionIndices)
            {
                Allocations.Add(new GameProgressTreeNodeAllocation()
                {
                    ObservationRange = observationRange,
                    ChildProportions = null,
                    ChildProportionContribution = 0.0
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

            internal GameProgressTreeNodeAllocation CreateNextAllocation_BasedOnChildren(int allocationIndexToCreate, double[] childReachProbabilities, double? reachProbabilityAtDecision)
            {
                if (Allocations.Count() != allocationIndexToCreate)
                    throw new Exception();
                int previousAllocationIndex = allocationIndexToCreate - 1;
                GameProgressTreeNodeAllocation previousAllocation = Allocations[previousAllocationIndex];

                double sum = childReachProbabilities.Sum();
                if (sum == 0 && reachProbabilityAtDecision == null)
                {
                    Allocations.Add(null);
                    return null; // nothing more to allocate. 
                }
                double[] childProportions = null;
                if (reachProbabilityAtDecision == null)
                    childProportions = childReachProbabilities.Select(x => x / sum).ToArray();

                GameProgressTreeNodeAllocation newAllocation = new GameProgressTreeNodeAllocation()
                {
                    ChildProportions = childProportions,
                    ChildProportionContribution = reachProbabilityAtDecision ?? sum,
                    // Note that observation range cannot yet be set, since it depends on cumulative reach probabilities elsewhere in the tree. We will thus set this going downward through the tree.
                };
                Allocations.Add(newAllocation);
                return newAllocation;
            }

            internal GameProgressTreeNodeAllocation CreateNextAllocation_BasedOnParent(int allocationIndexToCreate, (int, int) observationRange)
            {
                GameProgressTreeNodeAllocation newAllocation = new GameProgressTreeNodeAllocation()
                {
                    ObservationRange = observationRange
                };
                if (Allocations.Count() != allocationIndexToCreate)
                {
                    if (allocationIndexToCreate < Allocations.Count() && Allocations[allocationIndexToCreate] == null)
                        Allocations[allocationIndexToCreate] = newAllocation;
                    else
                        throw new Exception();
                }
                else
                    Allocations.Add(newAllocation);
                return newAllocation;
            }
        }

        public class GameProgressTreeNodeInfo
        {
            public IDirectGamePlayer DirectGamePlayer;
            public GameProgress GameProgress => DirectGamePlayer.GameProgress;

            public List<(double[] explorationValues, GameProgressTreeNodeProbabilities probabilitiesInfo)> NodeProbabilityInfos = new List<(double[] explorationValues, GameProgressTreeNodeProbabilities probabilitiesInfo)>();
            byte NumDecisionIndices;

            public GameProgressTreeNodeInfo(IDirectGamePlayer directGamePlayer, (int, int) observationRange, double[] explorationValues, double[] playToHereProbabilities, byte numDecisionIndices)
            {
                DirectGamePlayer = directGamePlayer;
                (double[] explorationValues, GameProgressTreeNodeProbabilities) npi = GetNodeProbabilityInfo(observationRange, explorationValues, playToHereProbabilities, directGamePlayer.GameComplete, numDecisionIndices);
                NodeProbabilityInfos = new List<(double[] explorationValues, GameProgressTreeNodeProbabilities probabilitiesInfo)>()
                {
                    npi
                };
                NumDecisionIndices = numDecisionIndices;
            }

            private (double[] explorationValues, GameProgressTreeNodeProbabilities) GetNodeProbabilityInfo((int, int) observationRange, double[] explorationValues, double[] playToHereProbabilities, bool gameComplete, byte numDecisionIndices)
            {
                double[] actionProbabilitiesWithExploration = gameComplete ? null : GetActionProbabilitiesWithExploration(explorationValues);
                (double[] explorationValues, GameProgressTreeNodeProbabilities) npi = (explorationValues, new GameProgressTreeNodeProbabilities(observationRange, playToHereProbabilities, actionProbabilitiesWithExploration, numDecisionIndices)
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

            public GameProgressTreeNodeProbabilities GetOrAddProbabilitiesInfo((int, int) observationRange, double[] explorationValues, double[] playToHereProbabilities, bool gameComplete)
            {
                GameProgressTreeNodeProbabilities value = GetProbabilitiesInfo(explorationValues);
                if (value == null)
                {
                    var result = GetNodeProbabilityInfo(observationRange, explorationValues, playToHereProbabilities, gameComplete, NumDecisionIndices);
                    NodeProbabilityInfos.Add(result);
                    value = result.Item2;
                }
                return value;
            }

            public override string ToString()
            {
                bool basicTreeOnly = false;
                if (basicTreeOnly)
                    return ToString(null, 0);
                StringBuilder s = new StringBuilder();
                s.AppendLine(GetStatusString());
                foreach (var npi in NodeProbabilityInfos)
                {
                    if (npi.explorationValues != null)
                        s.Append(npi.explorationValues.ToSignificantFigures(4) + ": ");
                    s.AppendLine(npi.probabilitiesInfo.ToString());
                }
                return s.ToString();
            }

            public string ToString(double[] explorationValues, int allocationIndex)
            {
                GameProgressTreeNodeProbabilities value = GetProbabilitiesInfo(explorationValues);
                string statusString = GetStatusString();
                string result = $"{value.ToString(allocationIndex, true)} {statusString}";
                return result;
            }

            private string GetStatusString()
            {
                return GameProgress.GameComplete ? "Result: " + String.Join(",", GameProgress.GetNonChancePlayerUtilities().Select(x => x.ToSignificantFigures(5))) : "Incomplete"; //  $"Next decision: {DirectGamePlayer.CurrentDecision.Name} ({GameProgress.CurrentDecisionIndex})";
            }
        }

        NWayTreeStorageInternal<GameProgressTreeNodeInfo> Tree;
        int InitialRandSeed;
        byte NumDecisionIndices;
        public Dictionary<(DoubleList, byte), byte> AllocationIndexForExplorationProbabilityAndDecisionIndex;

        public GameProgressTree(int randSeed, int totalObservations, IDirectGamePlayer directGamePlayer, double[] explorationValues, byte numNonChancePlayers, byte numDecisionIndices)
        {
            double[] playToHereProbabilities = Enumerable.Range(1, numNonChancePlayers).Select(x => (double)1).ToArray(); // all players always play to beginning of game
            NumDecisionIndices = numDecisionIndices;
            Tree = new NWayTreeStorageInternal<GameProgressTreeNodeInfo>(new NWayTreeStorageInternal<GameProgressTreeNodeInfo>(null))
            {
                StoredValue = new GameProgressTreeNodeInfo(directGamePlayer, (1, totalObservations), explorationValues, playToHereProbabilities, numDecisionIndices)
            };
            InitialRandSeed = randSeed;
            AllocationIndexForExplorationProbabilityAndDecisionIndex = new Dictionary<(DoubleList, byte), byte>();
        }

        public override string ToString() => Tree?.ToTreeString(x => $"{x.DirectGamePlayer.CurrentDecision.Name} ({x.DirectGamePlayer.CurrentDecisionIndex})");

        public async Task CompleteTree(bool doParallel, bool oversample)
        {
            await CompleteTree(doParallel, null, oversample);
        }

        public async Task CompleteTree(bool doParallel, double[] explorationValues, bool oversample)
        {
            DoubleList explorationValuesList = explorationValues == null ? null : new DoubleList(explorationValues.ToList());
            byte allocationIndex = 0;
            await CompleteTree(doParallel, (explorationValues, allocationIndex));

            if (oversample)
            {

                bool done = false;
                while (!done)
                {
                    var gameProgressesForPreviousAllocation = GetGameProgressesForAllocationIndex(explorationValues, allocationIndex);
                    int gameProgressesForPreviousAllocationCount = gameProgressesForPreviousAllocation.Count();
                    Dictionary<byte, (byte allocation, int numGameProgresses)> decisionIndexInfo = new Dictionary<byte, (byte allocation, int numGameProgresses)>();
                    byte? lowestDecisionIndexNotYetAssignedAllocationIndex = null;

                    // For each decision index, if that decision index has not already been assigned an allocation index,
                    // then: If the decision index is included in all game progresses for the previous allocation, then
                    // assign it the previous allocation index. Otherwise, the lowest decision index appearing in at least
                    // one GameProgress will serve as the basis for the next allocation. 
                    // However, the allocation it is based on will not necessarily be the immediately preceding
                    // allocation. Rather, it will be the allocation for which it has the most GameProgresses.

                    var DEBUG2 = gameProgressesForPreviousAllocation.Select(x => (MyGameProgress)x).Where(x => x.PFiles == false).ToList();
                    for (byte decisionIndex = 0; decisionIndex < NumDecisionIndices; decisionIndex++)
                    {
                        bool includedInPreviousAllocation = AllocationIndexForExplorationProbabilityAndDecisionIndex.ContainsKey((explorationValuesList, decisionIndex));
                        if (!includedInPreviousAllocation)
                        {
                            int numGameProgressesIncludedIn = gameProgressesForPreviousAllocation.Count(x => x.GetDecisionIndicesCompleted().Contains(decisionIndex));
                            if (!decisionIndexInfo.ContainsKey(decisionIndex) || decisionIndexInfo[decisionIndex].numGameProgresses < numGameProgressesIncludedIn)
                                decisionIndexInfo[decisionIndex] = (allocationIndex, numGameProgressesIncludedIn);
                            bool includedInAllGameProgresses = numGameProgressesIncludedIn == gameProgressesForPreviousAllocationCount;
                            if (includedInAllGameProgresses)
                                AllocationIndexForExplorationProbabilityAndDecisionIndex[(explorationValuesList, decisionIndex)] = allocationIndex;
                            else if (lowestDecisionIndexNotYetAssignedAllocationIndex == null)
                            {
                                bool includedInAtLeastOneGameProgress = numGameProgressesIncludedIn > 0;
                                if (includedInAtLeastOneGameProgress)
                                    lowestDecisionIndexNotYetAssignedAllocationIndex = decisionIndex;
                            }
                        }
                    }
                    done = lowestDecisionIndexNotYetAssignedAllocationIndex == null;
                    if (!done)
                    {
                        byte decisionIndexForAllocation = (byte)lowestDecisionIndexNotYetAssignedAllocationIndex;
                        byte previousAllocationIndex = decisionIndexInfo[decisionIndexForAllocation].allocation;
                        allocationIndex++;
                        PrepareNewAllocation(explorationValues, decisionIndexForAllocation, previousAllocationIndex, allocationIndex);
                        await CompleteTree(doParallel, (explorationValues, allocationIndex));
                        var DEBUG = ToString();
                    }
                }
            }
        }

        public async Task CompleteTree(bool doParallel, (double[] explorationValues, int allocationIndex) allocationInfo)
        {
            await Tree.BranchInParallel(doParallel, allocationInfo, DoBranching);
        }



        private void PrepareNewAllocation(double[] explorationValues, byte decisionIndex, byte previousAllocationIndex, byte newAllocationIndex)
        {
            // Look in the old allocation for all points where decision decisionIndex is reached. 
            // It doesn't matter yet for our purposes what happens after this point, including how many 
            // branches come from decisionIndex, because all branches we develop from here will include
            // decisionIndex. What we do need to do is set the ChildProportionContribution in the new 
            // allocation based on the probability both players will play to that point. Then, we aggregate
            // these values up the tree.
            Tree.WalkTree(treeStorage => { }, treeStorage =>
            {
                GameProgressTreeNodeInfo node = treeStorage.StoredValue;
                if (node.GameProgress.GameComplete)
                    return;
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
                    GameProgressTreeNodeAllocation childNewAllocation = childProbabilitiesInfo.GetAllocation(newAllocationIndex);
                    if (childNewAllocation == null)
                        return 0;
                    double probability = childNewAllocation.ChildProportionContribution;
                    return probability;
                }).ToArray();
                double? reachProbabilityAtDecision = null;
                GameProgressTreeNodeProbabilities probabilitiesInfo = node.GetProbabilitiesInfo(explorationValues);
                if (node.GameProgress.CurrentDecisionIndex == decisionIndex)
                    reachProbabilityAtDecision = probabilitiesInfo.PlayToHereCombinedProbability;
                probabilitiesInfo.CreateNextAllocation_BasedOnChildren(newAllocationIndex, childCumulativeReachProbabilities, reachProbabilityAtDecision);
            });
        }

        public (byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)[] DoBranching(NWayTreeStorageInternal<GameProgressTreeNodeInfo> nodeContainer, (double[] explorationValues, int allocationIndex) allocationInfo)
        {
            var sourceNode = nodeContainer.StoredValue;
            var probabilitiesInfo = sourceNode.GetProbabilitiesInfo(allocationInfo.explorationValues);
            var allocation = probabilitiesInfo.GetAllocation(allocationInfo.allocationIndex);
            if (allocation == null)
                return new (byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)[0];
            if (allocation.ObservationRange.Item1 == 0 && allocation.ObservationRange.Item2 == 0)
                allocation.ObservationRange = probabilitiesInfo.GetAllocation(allocationInfo.allocationIndex - 1).ObservationRange;
            if (allocation.ObservationRange.Item1 == 1 && allocation.ObservationRange.Item2 == 485 && allocationInfo.allocationIndex == 1)
            {
                var DEBUG = 0;
            }
            double[] playToHereProbabilities = probabilitiesInfo.PlayToHereProbabilities;
            if (allocationInfo.allocationIndex == 0)
            {
                // The child proportions have not been set. Thus, we use the action probabilities.
                allocation.ChildProportions = probabilitiesInfo.ActionProbabilities.ToArray();
            }
            int randSeed = sourceNode.DirectGamePlayer.GameProgress.GetActionsPlayedHash(InitialRandSeed);
            (int, int)?[] subranges = DivideRangeIntoSubranges(allocation.ObservationRange, allocation.ChildProportions ?? probabilitiesInfo.ActionProbabilities, randSeed);
            byte numChildren = (byte) subranges.Length;
            (byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)[] children = new (byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)[numChildren];
            for (byte a = 1; a <= numChildren; a++)
            {
                (int, int)? subrange = subranges[a - 1];
                if (subrange != null)
                {
                    double[] childPlayToHereProbabilities = playToHereProbabilities.ToArray();
                    PlayerInfo currentPlayer = sourceNode.DirectGamePlayer.CurrentPlayer;
                    if (!currentPlayer.PlayerIsChance)
                    {
                        byte playerIndex = currentPlayer.PlayerIndex;
                        childPlayToHereProbabilities[playerIndex] *= probabilitiesInfo.ActionProbabilities[a - 1];
                    }
                    var child = nodeContainer.GetBranch(a)?.StoredValue;
                    if (child == null)
                    {

                        IDirectGamePlayer childGamePlayer = sourceNode.DirectGamePlayer.CopyAndPlayAction((byte)a);
                        child = new GameProgressTreeNodeInfo(childGamePlayer, subrange.Value, allocationInfo.explorationValues, childPlayToHereProbabilities, NumDecisionIndices);
                    }
                    else
                    {
                        GameProgressTreeNodeProbabilities nodeProbabilities = child.GetOrAddProbabilitiesInfo(subrange.Value, allocationInfo.explorationValues, childPlayToHereProbabilities, child.DirectGamePlayer.GameComplete);
                        var childAllocation = nodeProbabilities.GetAllocation(allocationInfo.allocationIndex);
                        if (childAllocation == null)
                            nodeProbabilities.CreateNextAllocation_BasedOnParent(allocationInfo.allocationIndex, subrange.Value);
                        else
                            childAllocation.ObservationRange = subrange.Value;
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
            while (remainingItems >= integerProportions.Length)
            { 
                // might occur because of rounding
                integerProportions = integerProportions.Select(x => x + 1).ToArray();
                remainingItems -= integerProportions.Length;
            }
            if (remainingItems > 0)
            {
                double[] remainders = proportion.Select(x => (double)(numItems * x) - (int)(numItems * x)).ToArray();
                ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(randSeed, 2_000_000);
                for (int i = 0; i < remainingItems; i++)
                {
                    byte index = r.GetRandomIndex(remainders);
                    integerProportions[index]++;
                    remainders[index] = 0; // we are picking without replacement, so set the weight on this item to 0, so that we don't pick this item again. 
                }
            }
            return integerProportions;
        }

        public IEnumerable<GameProgress> GetGameProgressesForDecisionIndex(double[] explorationValues, byte decisionIndex)
        {
            byte allocationIndex = AllocationIndexForExplorationProbabilityAndDecisionIndex[(new DoubleList(explorationValues), decisionIndex)];
            return GetGameProgressesForAllocationIndex(explorationValues, allocationIndex);
        }

        public IEnumerable<GameProgress> GetGameProgressesForAllocationIndex(double[] explorationValues, byte allocationIndex)
        {
            var nodesStorage = Tree.EnumerateNodes(
                nodeStorage =>
                {
                    // Should this node be enumerated?
                    GameProgressTreeNodeInfo treeNodeInfo = nodeStorage.StoredValue;
                    if (treeNodeInfo.GameProgress.GameComplete == false)
                        return false;
                    GameProgressTreeNodeAllocation allocation = GetGameProgressTreeNodeAllocation(explorationValues, allocationIndex, treeNodeInfo);
                    if (allocation == null)
                        return false;
                    return allocation.NumObservations >= 1;
                },
                nodeStorage =>
                {
                    // Which branches should be enumerated?
                    var branches = nodeStorage.Branches;
                    if (branches == null || !branches.Any())
                        return new bool[] { };
                    GameProgressTreeNodeInfo treeNodeInfo = nodeStorage.StoredValue;
                    GameProgressTreeNodeAllocation allocation = GetGameProgressTreeNodeAllocation(explorationValues, allocationIndex, treeNodeInfo);
                    if (allocation == null)
                        return new bool[] { };
                    bool[] results = new bool[branches.Length];
                    for (int i = 0; i < branches.Length; i++)
                    {
                        results[i] = branches[i] != null && allocation.ChildProportions[i] > 0;
                    }
                    return results;
                }
            );
            foreach (var nodeStorage in nodesStorage)
            {
                GameProgressTreeNodeInfo treeNodeInfo = nodeStorage.StoredValue;
                GameProgressTreeNodeAllocation allocation = GetGameProgressTreeNodeAllocation(explorationValues, allocationIndex, treeNodeInfo);
                int numObservations = allocation.NumObservations;
                for (int i = 0; i < numObservations; i++)
                    yield return nodeStorage.StoredValue.GameProgress;
            }
        }

        private static GameProgressTreeNodeAllocation GetGameProgressTreeNodeAllocation(double[] explorationValues, byte allocationIndex, GameProgressTreeNodeInfo treeNodeInfo)
        {
            GameProgressTreeNodeProbabilities probabilities = treeNodeInfo.GetProbabilitiesInfo(explorationValues);
            GameProgressTreeNodeAllocation allocation = probabilities.GetAllocation(allocationIndex);
            return allocation;
        }

        public IEnumerator<GameProgress> GetEnumerator()
        {
            foreach (GameProgress gameProgress in GetGameProgressesForAllocationIndex(null, 0))
                yield return gameProgress;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
