using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{

    public class GameProgressTreeNode
    {
        public GameProgress GameProgress;
        public (int, int) IterationNumRange;
        public double[] Probabilities;
    }

    public class GameProgressTree
    {
        NWayTreeStorageInternal<GameProgressTreeNode> Tree = new NWayTreeStorageInternal<GameProgressTreeNode>(null);

        Func<GameProgress, double[]> GetChildProbabilities;
        Func<GameProgress, byte, GameProgress> GetChild;
        Func<double> Randomizer;

        public void Subbranch(NWayTreeStorageInternal<GameProgressTreeNode> branch)
        {
            GameProgress startingGameProgress = branch.StoredValue.GameProgress;
            double[] probabilities = GetChildProbabilities(startingGameProgress);
            (int, int)?[] subranges = DivideRangeIntoSubranges(branch.StoredValue.IterationNumRange, probabilities);
            GameProgress[] children = Enumerable.Range(1, probabilities.Length).Select(a => subranges[a - 1] == null ? null : GetChild(startingGameProgress, (byte) a)).ToArray();
            for (byte a = 1; a <= probabilities.Length; a++)
            {
                int numInSubrange = subranges[a - 1] == null ? 0 : subranges[a - 1].Value.Item2 - subranges[a - 1].Value.Item1 + 1;
                GameProgress gameProgress = children[a - 1];
                if (gameProgress != null)
                {
                    branch.SetBranch((byte) (a - 1), numInSubrange switch
                    {
                        1 => new NWayTreeStorage<GameProgressTreeNode>(branch) { StoredValue = new GameProgressTreeNode() { GameProgress = gameProgress, IterationNumRange = subranges[a - 1].Value } },
                        _ => new NWayTreeStorageInternal<GameProgressTreeNode>(branch) { StoredValue = new GameProgressTreeNode() { GameProgress = gameProgress, IterationNumRange = subranges[a - 1].Value } }
                    });
                }
            }
        }

        public (int, int)?[] DivideRangeIntoSubranges((int, int) range, double[] proportion)
        {
            int numSubranges = proportion.Length;
            (int, int)?[] subranges = new (int, int)?[numSubranges];
            int numInRange = range.Item2 - range.Item1 + 1;
            int[] numInEachSubrange = DivideItems(numInRange, proportion);
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

        public int[] DivideItems(int numItems, double[] proportion)
        {
            int[] integerProportions = proportion.Select(x => (int)(numItems * x)).ToArray();
            int initialSum = integerProportions.Sum();
            int remainingItems = numItems - initialSum;
            if (remainingItems > 0)
            {
                double[] remainders = proportion.Select(x => (double)(numItems * x) - (int)(numItems * x)).ToArray();
                for (int i = 0; i < numItems; i++)
                {
                    byte index = GetRandomIndex(remainders, Randomizer());
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
