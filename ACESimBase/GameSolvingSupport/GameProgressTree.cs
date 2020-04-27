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
            public double CombinedProbability;
            /// <summary>
            /// The probability that the player at this node will choose each of the child actions, taking into account the players' exploration values.
            /// </summary>
            public double[] ActionProbabilities;
            /// <summary>
            /// The allocations of observations pursuant to these probabilities. Each allocation must be narrower than the previous, so once we have an allocation that has null for ChildProportions, there will be no further allocations.
            /// </summary>
            public List<GameProgressTreeNodeAllocation> Allocations = new List<GameProgressTreeNodeAllocation>();

            public void CreateNextAllocation(int allocationIndexToCreate, List<double> childReachProbabilities)
            {
                if (!Allocations.Any())
                    throw new Exception();
                int previousAllocationIndex = allocationIndexToCreate - 1;
                GameProgressTreeNodeAllocation previousAllocation = Allocations[previousAllocationIndex];

                double sum = childReachProbabilities.Sum();
                if (sum == 0)
                    return; // nothing more to allocate. 
                double[] childProportions = childReachProbabilities.Select(x => x / sum).ToArray();

                GameProgressTreeNodeAllocation newAllocation = new GameProgressTreeNodeAllocation()
                {
                    ChildProportions = childProportions,
                    CumulativeReachProbability = sum
                };
            }
        }

        public class GameProgressTreeNodeInfo
        {
            public IDirectGamePlayer DirectGamePlayer;
            public GameProgress GameProgress => DirectGamePlayer.GameProgress;

            public GameProgressTreeNodeAllocation Allocation = new GameProgressTreeNodeAllocation();

            public override string ToString()
            {
                string childProportionsString = ChildProportions == null ? null : String.Join(",", ChildProportions.Select(x => x.ToSignificantFigures(3)));
                string resultString = GameProgress.GameComplete ? String.Join(",", GameProgress.GetNonChancePlayerUtilities().Select(x => x.ToSignificantFigures(5))) : "incomplete";
                string result = $"Obs: {ObservationRange} Cumulative proportion: {PlayToHereProbabilities.ToSignificantFigures_WithSciNotationForVerySmall(4)} Decision: {DirectGamePlayer.CurrentDecision?.Name} Probs: {childProportionsString} Result: {resultString}";
                return result;
            }
        }

        NWayTreeStorageInternal<GameProgressTreeNodeInfo> Tree;
        int InitialRandSeed;

        public GameProgressTree(int randSeed, int totalObservations, IDirectGamePlayer directGamePlayer)
        {
            Tree = new NWayTreeStorageInternal<GameProgressTreeNodeInfo>(new NWayTreeStorageInternal<GameProgressTreeNodeInfo>(null))
            {
                StoredValue = new GameProgressTreeNodeInfo()
                {
                    DirectGamePlayer = directGamePlayer,
                    ObservationRange = (1, totalObservations),
                    PlayToHereProbabilities = 1.0,
                }
            };
            InitialRandSeed = randSeed;
        }

        public override string ToString() => Tree?.ToTreeString("Action");

        public async Task CompleteTree(bool doParallel)
        {
            await Tree.CreateBranchesParallel(doParallel, CreateSubbranches);
            // DEBUG CompleteIncompletePlayers(doParallel);
        }

        private void AccumulateCombined(bool doParallel)
        {
            Tree.WalkTree(treeStorage => { }, treeStorage =>
            {
                var node = treeStorage.StoredValue;
                byte numPossibleActions = node.DirectGamePlayer.CurrentDecision.NumPossibleActions;
                var results = Enumerable.Range(1, numPossibleActions).Select(a =>
                {
                    var branch = treeStorage.GetBranch((byte)a);
                    if (branch == null)
                        return 0;
                    var branchNode = branch.StoredValue;
                    double probability = branchNode.CombinedProbability;
                    return probability;
                }).Sum();
                node.CombinedProbability = probability;
            });
        }

        public (byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)[] CreateSubbranches(GameProgressTreeNodeInfo sourceNode)
        {
            if (sourceNode.ChildProportions == null)
            {
                // The child proportions have not been set. Thus, we use the action probabilities.
                double[] actionProbabilities = sourceNode.DirectGamePlayer.GetActionProbabilities();
                sourceNode.ChildProportions = actionProbabilities;
            }
            int randSeed = sourceNode.DirectGamePlayer.GameProgress.GetActionsPlayedHash(InitialRandSeed);
            (int, int)?[] subranges = DivideRangeIntoSubranges(sourceNode.ObservationRange, sourceNode.ChildProportions, randSeed);
            if (sourceNode.ChildProportions == null)
            {
                double numObservations = ((double)sourceNode.NumObservations);
                sourceNode.ChildProportions = subranges.Select(x =>
                {
                    return x == null ? 0 : ((double)x.Value.Item2 - x.Value.Item1 + 1) / numObservations;
                }).ToArray();
            }
            IDirectGamePlayer[] children = Enumerable.Range(1, sourceNode.ChildProportions.Length).Select(a =>
            {
                (int, int)? subrange = subranges[a - 1];
                if (subrange == null)
                    return null;
                IDirectGamePlayer childGamePlayer = sourceNode.DirectGamePlayer.CopyAndPlayAction((byte)a);
                //if (!childGamePlayer.GameProgress.GameComplete && subrange.Value.Item1 == subrange.Value.Item2)
                //{
                //    // DEBUG -- it seems to me that this only takes us one further level. Should it be needed at all? When we have one observation to divide probabilistically, we just pick one child in which to divide it, and we then keep building the tree as we normally would. Also, why isn't this a while loop?
                //    // This is a leaf of the tree -- so we must play until the game is complete.
                //    double[] childProbabilities = childGamePlayer.GetActionProbabilities();
                //    // DEBUG -- we want random numbers to be based on the hash
                //    ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(++randSeed, 3_000_000);
                //    byte childAction = (byte) (1 + r.GetRandomIndex(childProbabilities));
                //    childGamePlayer = childGamePlayer.CopyAndPlayAction(childAction);
                //};
                return childGamePlayer;
            }).ToArray();
            List<(byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)> subbranches = new List<(byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)>();
            for (byte a = 1; a <= sourceNode.ChildProportions.Length; a++)
            {
                int numInSubrange = subranges[a - 1] == null ? 0 : subranges[a - 1].Value.Item2 - subranges[a - 1].Value.Item1 + 1;
                IDirectGamePlayer gamePlayer = children[a - 1];
                if (gamePlayer != null)
                {
                    (int, int) childObservationRange = subranges[a - 1].Value;
                    int childNumObservations = childObservationRange.Item2 - childObservationRange.Item1 + 1;
                    var childBranchItem = new GameProgressTreeNodeInfo() { DirectGamePlayer = gamePlayer, ObservationRange = childObservationRange, PlayToHereProbabilities = sourceNode.ChildObservationsToChildCumulativeProportion(childNumObservations) };
                    subbranches.Add((a, childBranchItem, gamePlayer.GameProgress.GameComplete)); // If there is only 1 in subrange, we go no further, BUT we will need to complete those games later. DEBUG -- no, we should go further, until the end; we go no further only where the game is complete
                }
            }
            return subbranches.ToArray();
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

        private IEnumerable<IDirectGamePlayer> GetIncompletePlayers()
        {
            foreach (var treeNode in Tree.GetAllTreeNodes())
            {
                GameProgressTreeNodeInfo treeNodeInfo = treeNode.storedValue;
                GameProgress gameProgress = treeNodeInfo.GameProgress;
                if (treeNodeInfo.IsLeaf && !gameProgress.GameComplete)
                {
                    yield return treeNodeInfo.DirectGamePlayer;
                }
            }
        }

        public void CompleteIncompletePlayers(bool doParallel)
        {
            // Wherever there is just one game, we have left it incomplete above. We now strive to complete those efficiently, by doing a bunch on the same thread.

            var incompletes = GetIncompletePlayers().ToList();

            if (!incompletes.Any())
                return;
            int totalNumberIncompletes = incompletes.Count();


            int randSeed = 0;
            unchecked
            {
                randSeed = InitialRandSeed * 3917;
            }

            int GetNumObservationsToDoTogether(int totalNumberObservations)
            {
                return doParallel ? 1 + totalNumberObservations / (Environment.ProcessorCount * 5) : totalNumberObservations;
            }
            int numObservationsToDoTogether = GetNumObservationsToDoTogether(totalNumberIncompletes);
            int numPlaybacks = totalNumberIncompletes / numObservationsToDoTogether;
            int extraObservationsDueToRounding = numPlaybacks * numObservationsToDoTogether - totalNumberIncompletes;
            int numObservationsToDoTogetherLastSet = numPlaybacks - extraObservationsDueToRounding;
            DeepCFRProbabilitiesCache probabilitiesCache = new DeepCFRProbabilitiesCache(); // shared across threads
            Parallelizer.Go(doParallel, 0, numPlaybacks, o =>
            {
                int firstObservation = numObservationsToDoTogether * o;
                int numObservationsToDoTogetherLastSetThisTime = o == totalNumberIncompletes - 1 ? numObservationsToDoTogetherLastSet : numObservationsToDoTogether;
                var toDoTogether = incompletes.Skip(firstObservation).ToList();
                toDoTogether.First().SynchronizeForSameThread(toDoTogether.Skip(1));
                int randSeed2;
                unchecked
                {
                    randSeed2 = randSeed * o;
                }
                foreach (var toDo in toDoTogether)
                    toDo.PlayUntilComplete(randSeed2++);
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
                    (int, int) observationRange = treeNodeInfo.ObservationRange;
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
