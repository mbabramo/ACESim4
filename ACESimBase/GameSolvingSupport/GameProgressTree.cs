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
        public class GameProgressTreeNodeInfo
        {
            public IDirectGamePlayer DirectGamePlayer;
            public GameProgress GameProgress => DirectGamePlayer.GameProgress;
            public (int, int) ObservationRange;
            public double[] Probabilities;
            public bool IsLeaf => Probabilities == null;

            public override string ToString()
            {
                string probabilitiesString = Probabilities == null ? null : String.Join(",", Probabilities.Select(x => x.ToSignificantFigures(3)));
                string resultString = GameProgress.GameComplete ? String.Join(",", GameProgress.GetNonChancePlayerUtilities().Select(x => x.ToSignificantFigures(5))) : "incomplete";
                string result = $"Obs: {ObservationRange} Decision: {DirectGamePlayer.CurrentDecision?.Name} Probs: {probabilitiesString} Result: {resultString}";
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
                }
            };
            InitialRandSeed = randSeed;
        }

        public override string ToString() => Tree?.ToTreeString("Action");

        public async Task CompleteTree(bool doParallel)
        {
            await Tree.CreateBranchesParallel(doParallel, CreateSubbranches);
            CompleteIncompletePlayers(doParallel);
        }

        public (byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)[] CreateSubbranches(GameProgressTreeNodeInfo sourceNode)
        {
            double[] probabilities = sourceNode.DirectGamePlayer.GetActionProbabilities();
            sourceNode.Probabilities = probabilities;
            int randSeed = 0;
            unchecked
            {
                randSeed = InitialRandSeed * 37 + sourceNode.ObservationRange.Item1 * 23 + sourceNode.ObservationRange.Item2 * 19;
            }
            (int, int)?[] subranges = DivideRangeIntoSubranges(sourceNode.ObservationRange, probabilities, randSeed);
            IDirectGamePlayer[] children = Enumerable.Range(1, probabilities.Length).Select(a =>
            {
                (int, int)? subrange = subranges[a - 1];
                if (subrange == null)
                    return null;
                IDirectGamePlayer childGamePlayer = sourceNode.DirectGamePlayer.CopyAndPlayAction((byte)a);
                if (!childGamePlayer.GameProgress.GameComplete && subrange.Value.Item1 == subrange.Value.Item2)
                {
                    // This is a leaf of the tree -- so we must play until the game is complete.
                    double[] childProbabilities = childGamePlayer.GetActionProbabilities();
                    ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(++randSeed, 3_000_000);
                    byte childAction = (byte) (1 + r.GetRandomIndex(childProbabilities));
                    childGamePlayer = childGamePlayer.CopyAndPlayAction(childAction);
                };
                return childGamePlayer;
            }).ToArray();
            List<(byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)> subbranches = new List<(byte branchID, GameProgressTreeNodeInfo childBranch, bool isLeaf)>();
            for (byte a = 1; a <= probabilities.Length; a++)
            {
                int numInSubrange = subranges[a - 1] == null ? 0 : subranges[a - 1].Value.Item2 - subranges[a - 1].Value.Item1 + 1;
                IDirectGamePlayer gamePlayer = children[a - 1];
                if (gamePlayer != null)
                {
                    var childBranchItem = new GameProgressTreeNodeInfo() { DirectGamePlayer = gamePlayer, ObservationRange = subranges[a - 1].Value };
                    subbranches.Add((a, childBranchItem, numInSubrange == 1 || gamePlayer.GameProgress.GameComplete)); // If there is only 1 in subrange, we go no further, BUT we will need to complete those games later.
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
