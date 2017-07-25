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
             * We generate the following expected utilities (because chance decision == 1 happens with p = 2/3):
                +------+-------+-------+
                |      | P2=1  | P2=2  |
                +------+-------+-------+
                | P1=1 | (2,2) | (0,1) |
                | P1=2 | (0,0) | (0,1) |
                +------+-------+-------+
                Note that top-left cell is the Nash equilibrium. 
             */
            if (ChanceDecision == 1)
            {
                if (P1Decision == 1 && P2Decision == 1)
                    return new double[] { 3.0, 3.0 };
                if (P1Decision == 2 && P2Decision == 1)
                    return new double[] { 0.0, 0.0 };
                if (P1Decision == 1 && P2Decision == 2)
                    return new double[] { 0.0, 0.0 };
                if (P1Decision == 2 && P2Decision == 2)
                    return new double[] { 0.0, 0.0 };
            }
            else
            {
                if (P1Decision == 1 && P2Decision == 1)
                    return new double[] { 0.0, 0.0 };
                if (P1Decision == 2 && P2Decision == 1)
                    return new double[] { 0.0, 0.0 };
                if (P1Decision == 1 && P2Decision == 2)
                    return new double[] { 0.0, 3.0 };
                if (P1Decision == 2 && P2Decision == 2)
                    return new double[] { 0.0, 3.0 };
            }
            throw new NotImplementedException();
        }
    }
}
