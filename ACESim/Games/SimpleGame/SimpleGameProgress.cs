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
             * 99% of the time, the payoff is a prisoner's dilemma as follows:
            +------+---------+---------+
            |      |  P2=1   |  P2=2   |
            +------+---------+---------+
            | P1=1 | (-4,-4) | (-1,-8) |
            | P1=2 | (-8,-1) | (-3,-3) |
            +------+---------+---------+

            Both parties should play 1. But 1% of the time, we have a prisoner's dilemma where both parties should play 2.
            +------+---------+---------+
            |      |  P2=1   |  P2=2   |
            +------+---------+---------+
            | P1=1 | (-3,-3) | (-8,-1) |
            | P1=2 | (-1,-8) | (-4,-4) |
            +------+---------+---------+

            In expected value terms, both parties should still play 1.
             */
            if (ChanceDecision == 1)
            {
                if (P1Decision == 1 && P2Decision == 1)
                    return new double[] { -4, -4 };
                if (P1Decision == 2 && P2Decision == 1)
                    return new double[] { -8, -1 };
                if (P1Decision == 1 && P2Decision == 2)
                    return new double[] { -1, -8 };
                if (P1Decision == 2 && P2Decision == 2)
                    return new double[] { -3, -3 };
            }
            else
            {
                if (P1Decision == 1 && P2Decision == 1)
                    return new double[] { -3, -3 };
                if (P1Decision == 2 && P2Decision == 1)
                    return new double[] { -1, -8 };
                if (P1Decision == 1 && P2Decision == 2)
                    return new double[] { -8, -1 };
                if (P1Decision == 2 && P2Decision == 2)
                    return new double[] { -4, -4 };
            }
            throw new NotImplementedException();
        }
    }
}
