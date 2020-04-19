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
        public class GameProgressTreeNode
        {
            public GameProgress GameProgress;
            public (int, int) ObservationRange;
            public double[] Probabilities;
        }

        NWayTreeStorageInternal<GameProgressTreeNode> Tree;

        Func<GameProgress, double[]> GetChildProbabilities;
        Func<GameProgress, byte, GameProgress> GetChildGameProgress;
        int InitialRandSeed;

        public GameProgressTree(int randSeed, int totalObservations, GameProgress initialGameProgress, Func<GameProgress, double[]> getChildProbabilities, Func<GameProgress, byte, GameProgress> getChild)
        {
            Tree = new NWayTreeStorageInternal<GameProgressTreeNode>(new NWayTreeStorageInternal<GameProgressTreeNode>(null)
            {
                StoredValue = new GameProgressTreeNode()
                {
                    GameProgress = initialGameProgress,
                    ObservationRange = (1, totalObservations),
                }
            });
            InitialRandSeed = randSeed;
            GetChildProbabilities = getChildProbabilities;
            GetChildGameProgress = getChild;
        }

        public async Task CompleteTree(bool doParallel)
        {
            await Tree.CreateBranchesParallel(doParallel, CreateSubbranches);
        }

        public (byte branchID, GameProgressTreeNode childBranch, bool isLeaf)[] CreateSubbranches(GameProgressTreeNode sourceNode)
        {
            GameProgress startingGameProgress = sourceNode.GameProgress;
            double[] probabilities = GetChildProbabilities(startingGameProgress);
            sourceNode.Probabilities = probabilities;
            int randSeed = 0;
            unchecked
            {
                randSeed = InitialRandSeed * 37 + sourceNode.ObservationRange.Item1 * 23 + sourceNode.ObservationRange.Item2 * 19;
            }
            (int, int)?[] subranges = DivideRangeIntoSubranges(sourceNode.ObservationRange, probabilities, randSeed);
            GameProgress[] children = Enumerable.Range(1, probabilities.Length).Select(a =>
            {
                (int, int)? subrange = subranges[a - 1];
                if (subrange == null)
                    return null;
                GameProgress childGameProgress = GetChildGameProgress(startingGameProgress, (byte)a);
                if (!childGameProgress.GameComplete && subrange.Value.Item1 == subrange.Value.Item2)
                {
                    // This is a leaf of the tree -- so we must play until the game is complete.
                    double[] childProbabilities = GetChildProbabilities(childGameProgress);
                    ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(++randSeed, 3_000_000);
                    byte childAction = GetRandomIndex(childProbabilities, r.NextDouble());
                    childGameProgress = GetChildGameProgress(childGameProgress, childAction);
                };
                return childGameProgress;
            }).ToArray();
            List<(byte branchID, GameProgressTreeNode childBranch, bool isLeaf)> subbranches = new List<(byte branchID, GameProgressTreeNode childBranch, bool isLeaf)>();
            for (byte a = 1; a <= probabilities.Length; a++)
            {
                int numInSubrange = subranges[a - 1] == null ? 0 : subranges[a - 1].Value.Item2 - subranges[a - 1].Value.Item1 + 1;
                GameProgress gameProgress = children[a - 1];
                if (gameProgress != null)
                {
                    var childBranchItem = new GameProgressTreeNode() { GameProgress = gameProgress, ObservationRange = subranges[a - 1].Value };
                    subbranches.Add((a, childBranchItem, numInSubrange == 1 || gameProgress.GameComplete));
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
                for (int i = 0; i < numItems; i++)
                {
                    ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(randSeed, 2_000_000 + i);
                    byte index = GetRandomIndex(remainders, r.NextDouble());
                    integerProportions[index]++;
                }
            }
            return integerProportions;
        }

        private byte GetRandomIndex(double[] probabilities, double randSeed)
        {
            double target = probabilities.Sum() * randSeed;
            double towardTarget = 0;
            for (byte a = 0; a < probabilities.Length - 1; a++)
            {
                towardTarget += probabilities[a];
                if (towardTarget > target)
                    return a;
            }
            return (byte)(probabilities.Length - 1);
        }

        public IEnumerator<GameProgress> GetEnumerator()
        {
            foreach (var treeNode in Tree.GetAllTreeNodes())
            {
                GameProgress gameProgress = treeNode.storedValue.GameProgress;
                if (gameProgress.GameComplete)
                {
                    int observations = treeNode.storedValue.ObservationRange.Item2 - treeNode.storedValue.ObservationRange.Item1 + 1;
                    for (int o = 0; o < observations; o++)
                        yield return gameProgress;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
