using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public class GameProgressTree
    {
        public class GameProgressTreeNode
        {
            public GameProgress GameProgress;
            public (int, int) ObservationRange;
            public double[] Probabilities;
        }

        NWayTreeStorageInternal<GameProgressTreeNode> Tree;

        Func<GameProgress, double[]> GetChildProbabilities;
        Func<GameProgress, byte, GameProgress> GetChild;
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
            GetChild = getChild;
        }

        public async Task CompleteTree()
        {
            await Tree.CreateBranchesParallel(node =>
            {
                CreateSubbranches(node);
            })
        }

        public void CreateSubbranches(NWayTreeStorageInternal<GameProgressTreeNode> sourceBranch)
        {
            GameProgress startingGameProgress = sourceBranch.StoredValue.GameProgress;
            double[] probabilities = GetChildProbabilities(startingGameProgress);
            sourceBranch.StoredValue.Probabilities = probabilities;
            int randSeed = 0;
            unchecked
            {
                randSeed = InitialRandSeed * 37 + sourceBranch.StoredValue.ObservationRange.Item1 * 23 + sourceBranch.StoredValue.ObservationRange.Item2 * 19;
            }
            (int, int)?[] subranges = DivideRangeIntoSubranges(sourceBranch.StoredValue.ObservationRange, probabilities, randSeed);
            GameProgress[] children = Enumerable.Range(1, probabilities.Length).Select(a => subranges[a - 1] == null ? null : GetChild(startingGameProgress, (byte) a)).ToArray();
            for (byte a = 1; a <= probabilities.Length; a++)
            {
                int numInSubrange = subranges[a - 1] == null ? 0 : subranges[a - 1].Value.Item2 - subranges[a - 1].Value.Item1 + 1;
                GameProgress gameProgress = children[a - 1];
                if (gameProgress != null)
                {
                    sourceBranch.SetBranch((byte) (a - 1), numInSubrange switch
                    {
                        1 => new NWayTreeStorage<GameProgressTreeNode>(sourceBranch) { StoredValue = new GameProgressTreeNode() { GameProgress = gameProgress, ObservationRange = subranges[a - 1].Value } },
                        _ => new NWayTreeStorageInternal<GameProgressTreeNode>(sourceBranch) { StoredValue = new GameProgressTreeNode() { GameProgress = gameProgress, ObservationRange = subranges[a - 1].Value } }
                    });
                }
            }
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
    }
}
