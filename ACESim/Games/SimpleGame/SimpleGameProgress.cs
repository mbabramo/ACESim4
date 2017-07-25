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
            if (ChanceDecision == 1)
            {
                if (P1Decision == 1 && P2Decision == 1)
                    return new double[] { 1.0, 1.0 };
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
                    return new double[] { 1.0, 1.0 };
                if (P1Decision == 1 && P2Decision == 2)
                    return new double[] { 1.0, 1.0 };
                if (P1Decision == 2 && P2Decision == 2)
                    return new double[] { 1.0, 1.0 };
            }
            throw new NotImplementedException();
        }
    }
}
