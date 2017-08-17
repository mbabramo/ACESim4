using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class SimpleGameProgress : GameProgress
    {
        public byte P1Decision, P2Decision, ChanceDecision;

        public override GameProgress DeepCopy()
        {
            SimpleGameProgress copy = new SimpleGameProgress();

            // copy.GameComplete = this.GameComplete;
            CopyFieldInfo(copy);

            return copy;
        }

        internal override void CopyFieldInfo(GameProgress copy)
        {
            base.CopyFieldInfo(copy);
            SimpleGameProgress simpleGameProgress = (SimpleGameProgress)copy;
            simpleGameProgress.P1Decision = P1Decision;
            simpleGameProgress.P2Decision = P2Decision;
            simpleGameProgress.ChanceDecision = ChanceDecision;
        }

        public override double[] GetNonChancePlayerUtilities()
        {
            /*
             * 2/3 of the time, the payoff is as follows:
            +------+---------+---------+
            |      |  P2=1   |  P2=2   |
            +------+---------+---------+
            | P1=1 | (6,6)   | (2,2)   |
            | P1=2 | (1,1)   | (3,3)   |
            +------+---------+---------+

            There are two Nash equilibrium strategies. But on the principle of payoff dominance, both parties should play 1. 

            Meanwhile, 1/3 of the time, we have the reverse where both parties should play 2.
            +------+---------+---------+
            |      |  P2=1   |  P2=2   |
            +------+---------+---------+
            | P1=1 | (0,0)   | (1,1)   |
            | P1=2 | (0,0)   | (3,3)   |
            +------+---------+---------+

            In expected value terms, both parties should still play 1, since that is worth (4,4) in expected value (higher than any other single cell).
             */
            if (ChanceDecision == 1)
            {
                if (P1Decision == 1 && P2Decision == 1)
                    return new double[] { 6, 6 };
                if (P1Decision == 2 && P2Decision == 1)
                    return new double[] { 1, 1 };
                if (P1Decision == 1 && P2Decision == 2)
                    return new double[] { 2, 2 };
                if (P1Decision == 2 && P2Decision == 2)
                    return new double[] { 3, 3 };
            }
            else
            {
                if (P1Decision == 1 && P2Decision == 1)
                    return new double[] { 0, 0 };
                if (P1Decision == 2 && P2Decision == 1)
                    return new double[] { 0, 0 };
                if (P1Decision == 1 && P2Decision == 2)
                    return new double[] { 1, 1 };
                if (P1Decision == 2 && P2Decision == 2)
                    return new double[] { 3, 3 };
            }
            throw new NotImplementedException();
        }
    }
}
