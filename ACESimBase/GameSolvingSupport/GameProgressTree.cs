using ACESim;
using ACESimBase.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public partial class GameProgressTree : IEnumerable<GameProgress>
    {

        NWayTreeStorageInternal<GameProgressTreeNodeInfo> Tree;
        int InitialRandSeed;
        byte? LimitToPlayer;
        byte NumDecisionIndices;
        List<Decision> DecisionsList;
        public Dictionary<string, byte> AllocationIndexForExplorationProbabilityAndDecisionIndex;

        public GameProgressTree(int randSeed, int totalObservations, IDirectGamePlayer directGamePlayer, double[] explorationValues, byte numNonChancePlayers, List<Decision> decisionsList, byte? limitToPlayer)
        {
            double[] playToHereProbabilities = Enumerable.Range(1, numNonChancePlayers).Select(x => (double)1).ToArray(); // all players always play to beginning of game
            DecisionsList = decisionsList;
            NumDecisionIndices = (byte) DecisionsList.Count();
            LimitToPlayer = limitToPlayer;
            Tree = new NWayTreeStorageInternal<GameProgressTreeNodeInfo>(new NWayTreeStorageInternal<GameProgressTreeNodeInfo>(null))
            {
                StoredValue = new GameProgressTreeNodeInfo(directGamePlayer, (1, totalObservations), explorationValues, 0, playToHereProbabilities, NumDecisionIndices)
            };
            InitialRandSeed = randSeed;
            AllocationIndexForExplorationProbabilityAndDecisionIndex = new Dictionary<string, byte>();
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
                Dictionary<byte, (byte allocation, int numGameProgresses)> decisionIndexInfo = new Dictionary<byte, (byte allocation, int numGameProgresses)>();
                while (!done)
                {
                    var gameProgressesForPreviousAllocation = GetGameProgressesForAllocationIndex(explorationValues, allocationIndex);
                    int gameProgressesForPreviousAllocationCount = gameProgressesForPreviousAllocation.Count();
                    byte? nextDecisionIndexForAllocation = null;

                    // For each decision index, if that decision index has not already been assigned an allocation index,
                    // then: If the decision index is included in all game progresses for the previous allocation, then
                    // assign it the previous allocation index. Otherwise, the lowest decision index appearing in at least
                    // one GameProgress will serve as the basis for the next allocation. 
                    // However, the allocation it is based on will not necessarily be the immediately preceding
                    // allocation. Rather, it will be the allocation for which it has the most GameProgresses.

                    for (byte decisionIndex = 0; decisionIndex < NumDecisionIndices; decisionIndex++)
                    {
                        bool allocationNotNeeded = DecisionsList[decisionIndex].IsChance || (LimitToPlayer != null && DecisionsList[decisionIndex].PlayerIndex != LimitToPlayer) || AllocationIndexForExplorationProbabilityAndDecisionIndex.ContainsKey((explorationValuesList, decisionIndex).ToString());
                        if (!allocationNotNeeded)
                        {
                            int numGameProgressesIncludedInFromPreviousAllocation = gameProgressesForPreviousAllocation.Count(x => x.GetDecisionIndicesCompleted().Contains(decisionIndex));
                            bool includedInAllGameProgresses = numGameProgressesIncludedInFromPreviousAllocation == gameProgressesForPreviousAllocationCount;
                            if (includedInAllGameProgresses)
                            {
                                // We're done with this decision index. No need to do it again.
                                decisionIndexInfo[decisionIndex] = (allocationIndex, numGameProgressesIncludedInFromPreviousAllocation);
                                AllocationIndexForExplorationProbabilityAndDecisionIndex[(explorationValuesList, decisionIndex).ToString()] = allocationIndex;
                            }
                            else
                            {
                                // We haven't yet had an allocation in which every game progress includes this decision index.
                                // If this is the most game progresses that we've had in any allocation, remember that.
                                if (numGameProgressesIncludedInFromPreviousAllocation > 0 && (!decisionIndexInfo.ContainsKey(decisionIndex) || decisionIndexInfo[decisionIndex].numGameProgresses <= numGameProgressesIncludedInFromPreviousAllocation))
                                    decisionIndexInfo[decisionIndex] = (allocationIndex, numGameProgressesIncludedInFromPreviousAllocation);
                            }
                        }
                    }
                    if (decisionIndexInfo.Any())
                    {
                        IOrderedEnumerable<KeyValuePair<byte, (byte allocation, int numGameProgresses)>> candidates = decisionIndexInfo.Where(x => !AllocationIndexForExplorationProbabilityAndDecisionIndex.ContainsKey((explorationValuesList, x.Key).ToString())).OrderByDescending(x => x.Value.numGameProgresses).ThenBy(x => x.Key);
                        if (candidates.Any())
                        {
                            nextDecisionIndexForAllocation = candidates.First().Key;
                        }
                    }
                    done = nextDecisionIndexForAllocation == null;
                    if (!done)
                    {
                        byte decisionIndexForAllocation = (byte)nextDecisionIndexForAllocation;
                        byte previousAllocationIndex = decisionIndexInfo[decisionIndexForAllocation].allocation;
                        allocationIndex++;
                        //TabbedText.WriteLine($"Oversampling starting with decision index {decisionIndexForAllocation} (allocation index {allocationIndex}");
                        PrepareNewAllocation(explorationValues, decisionIndexForAllocation, previousAllocationIndex, allocationIndex);

                        await CompleteTree(doParallel, (explorationValues, allocationIndex));
                    }
                }
            }

            bool verifyCompleteTree = false;
            if (verifyCompleteTree)
                VerifyCompleteTree(explorationValues);
        }

        private void VerifyCompleteTree(double[] explorationValues)
        {
            List<Decision> decisionsExecutionOrder = Tree.StoredValue.DirectGamePlayer.GameProgress.GameDefinition.DecisionsExecutionOrder;
            int numObservations = Tree.StoredValue.GetProbabilitiesInfo(explorationValues).Allocations[0].NumObservations;
            for (int i = 0; i < decisionsExecutionOrder.Count(); i++)
            {
                if (!decisionsExecutionOrder[i].IsChance)
                {
                    var gameProgressesForDecisionIndex = GetGameProgressesForDecisionIndex(explorationValues, (byte)i);
                    if (gameProgressesForDecisionIndex.Count() != numObservations || gameProgressesForDecisionIndex.Any(x => !x.IncludesDecisionIndex((byte)i)))
                        throw new Exception("Verification failed");
                }
            }
        }

        public async Task CompleteTree(bool doParallel, (double[] explorationValues, int allocationIndex) allocationInfo)
        {
            if (allocationInfo.allocationIndex > 0)
            {
                var sourceNode = Tree.StoredValue;
                var probabilitiesInfo = sourceNode.GetProbabilitiesInfo(allocationInfo.explorationValues);
                var previousAllocation = probabilitiesInfo.GetAllocation(allocationInfo.allocationIndex - 1);
                var allocation = probabilitiesInfo.GetAllocation(allocationInfo.allocationIndex);
                allocation.ObservationRange = previousAllocation.ObservationRange;
            }
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
            if (allocation == null || (allocation.ObservationRange.Item1 == 0 && allocation.ObservationRange.Item2 == 0))
                return new (byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)[0];
            double[] playToHereProbabilities = probabilitiesInfo.PlayToHereProbabilities;
            if (allocationInfo.allocationIndex == 0)
            {
                // The child proportions have not been set. Thus, we use the action probabilities.
                allocation.ChildProportions = probabilitiesInfo.ActionProbabilities.ToArray();
            }
            int randSeed = sourceNode.DirectGamePlayer.GameProgress.GetActionsPlayedHash(InitialRandSeed);
            (int, int)?[] subranges = DivideRangeIntoSubranges(allocation.ObservationRange, allocation.ChildProportions ?? probabilitiesInfo.ActionProbabilities, randSeed);
            byte numChildren = (byte)subranges.Length;
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
                        child = new GameProgressTreeNodeInfo(childGamePlayer, subrange.Value, allocationInfo.explorationValues, (byte)allocationInfo.allocationIndex, childPlayToHereProbabilities, NumDecisionIndices);
                    }
                    else
                    {
                        GameProgressTreeNodeProbabilities nodeProbabilities = child.GetOrAddProbabilitiesInfo((byte)allocationInfo.allocationIndex, subrange.Value, allocationInfo.explorationValues, childPlayToHereProbabilities, child.DirectGamePlayer.GameComplete);
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

        private (int, int)?[] DivideRangeIntoSubranges((int, int) range, double[] proportion, int randSeed)
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

        private int[] DivideItems(int numItems, double[] proportion, int randSeed)
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
            byte allocationIndex = AllocationIndexForExplorationProbabilityAndDecisionIndex[(new DoubleList(explorationValues), decisionIndex).ToString()];
            return GetGameProgressesForAllocationIndex(explorationValues, allocationIndex);
        }

        public IEnumerable<GameProgress> GetGameProgressesForAllocationIndex(double[] explorationValues, byte allocationIndex)
        {
            IEnumerable<NWayTreeStorage<GameProgressTreeNodeInfo>> nodesStorage = GetNodesForAllocationIndex(explorationValues, allocationIndex);
            foreach (var nodeStorage in nodesStorage)
            {
                GameProgressTreeNodeInfo treeNodeInfo = nodeStorage.StoredValue;
                GameProgressTreeNodeAllocation allocation = GetGameProgressTreeNodeAllocation(explorationValues, allocationIndex, treeNodeInfo);
                int numObservations = allocation.NumObservations; // could be 0 in case of a node that was included in a previous allocation despite having a low probability of occurring, but excluded from this one
                for (int i = 0; i < numObservations; i++)
                    yield return nodeStorage.StoredValue.GameProgress;
            }
        }

        public (IDirectGamePlayer gamePlayer, int numObservations)[][] GetDirectGamePlayersForEachDecision(double[] explorationValues, List<Decision> decisions, int[] targetObservations)
        {
            (IDirectGamePlayer gamePlayer, int numObservations)[][] results = new (IDirectGamePlayer gamePlayer, int numObservations)[decisions.Count()][];
            for (byte decisionIndex = 0; decisionIndex < decisions.Count(); decisionIndex++)
            {
                if (decisions[decisionIndex].IsChance || targetObservations[decisionIndex] == 0)
                    continue;
                var directGamePlayersWithCounts = GetDirectGamePlayersForDecisionIndex(explorationValues, decisionIndex);
                int totalAvailableCount = directGamePlayersWithCounts.Sum(x => x.numObservations);
                int adjustedTarget = targetObservations[decisionIndex] / decisions[decisionIndex].NumPossibleActions; // we have one observation for each action, so we need to determine how many to make
                if (adjustedTarget > totalAvailableCount)
                    throw new Exception("Insufficient number of game players to meet the target number of observations.");
                if (adjustedTarget == totalAvailableCount)
                    results[decisionIndex] = directGamePlayersWithCounts.ToArray();
                else
                {
                    // we need to truncate the list, either by lowering observations or by eliminating all together
                    List<(IDirectGamePlayer gamePlayer, int numObservations)> truncatedList = new List<(IDirectGamePlayer gamePlayer, int numObservations)>();
                    ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(0, decisionIndex);
                    bool[] keepIndices = RandomSubset.SampleExactly(adjustedTarget, totalAvailableCount, () => r.NextDouble()).ToArray();
                    int i = 0;
                    foreach (var directGamePlayerWithCount in directGamePlayersWithCounts.ToList())
                    {
                        int numToKeep = 0;
                        for (int j = 0; j < directGamePlayerWithCount.numObservations; j++)
                            if (keepIndices[i++])
                                numToKeep++;
                        if (numToKeep > 0)
                            truncatedList.Add((directGamePlayerWithCount.gamePlayer, numToKeep));
                    }
                    results[decisionIndex] = truncatedList.ToArray();
                }
            }
            return results;
        }

        public IEnumerable<(IDirectGamePlayer gamePlayer, int numObservations)> GetDirectGamePlayersForDecisionIndex(double[] explorationValues, byte decisionIndex)
        {
            byte allocationIndex = AllocationIndexForExplorationProbabilityAndDecisionIndex[(new DoubleList(explorationValues), decisionIndex).ToString()];
            IEnumerable<NWayTreeStorage<GameProgressTreeNodeInfo>> nodesStorage = GetNodesForAllocationIndex(explorationValues, allocationIndex, decisionIndex);

            foreach (var nodeStorage in nodesStorage)
            {
                GameProgressTreeNodeInfo treeNodeInfo = nodeStorage.StoredValue;
                GameProgressTreeNodeAllocation allocation = GetGameProgressTreeNodeAllocation(explorationValues, allocationIndex, treeNodeInfo);
                int numObservations = allocation.NumObservations; // could be 0 in case of a node that was included in a previous allocation despite having a low probability of occurring, but excluded from this one
                yield return (nodeStorage.StoredValue.DirectGamePlayer, numObservations);
            }
        }

        public IEnumerable<NWayTreeStorage<GameProgressTreeNodeInfo>> GetNodesForAllocationIndex(double[] explorationValues, byte allocationIndex, byte? decisionIndex = null)
        {
            var nodesStorage = Tree.EnumerateNodes(
                            nodeStorage =>
                            {
                                // Should this node be enumerated?
                                GameProgressTreeNodeInfo treeNodeInfo = nodeStorage.StoredValue;
                                if (decisionIndex == null && treeNodeInfo.GameProgress.GameComplete == false)
                                    return false;
                                GameProgressTreeNodeAllocation allocation = GetGameProgressTreeNodeAllocation(explorationValues, allocationIndex, treeNodeInfo);
                                if (allocation == null)
                                    return false;
                                bool hasObservations = allocation.NumObservations >= 1;
                                if (decisionIndex is byte decisionIndexNonNull)
                                    return (treeNodeInfo.CurrentDecisionIndex == decisionIndexNonNull && hasObservations);

                                return hasObservations;
                            },
                            nodeStorage =>
                            {
                                // Which branches should be enumerated?
                                var branches = nodeStorage.Branches;
                                if (branches == null || !branches.Any())
                                    return new bool[] { };
                                GameProgressTreeNodeInfo treeNodeInfo = nodeStorage.StoredValue;
                                if (treeNodeInfo.GameProgress.GameComplete)
                                    return new bool[] { };
                                GameProgressTreeNodeAllocation allocation = GetGameProgressTreeNodeAllocation(explorationValues, allocationIndex, treeNodeInfo);
                                if (allocation == null || allocation.NumObservations == 0)
                                    return new bool[] { };
                                if (decisionIndex is byte decisionIndexNonNull && treeNodeInfo.CurrentDecisionIndex == decisionIndexNonNull)
                                    return new bool[] { }; // enumerating this intermediate node -- go no further
                                bool[] results = new bool[branches.Length];
                                for (int i = 0; i < branches.Length; i++)
                                {
                                    results[i] = branches[i] != null && (allocation.ChildProportions == null || allocation.ChildProportions[i] > 0);
                                }
                                return results;
                            }
                        );
            return nodesStorage;
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
