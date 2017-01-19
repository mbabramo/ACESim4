using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class NegligenceDisputeGeneratorModuleProgress : DisputeGeneratorModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public NegligenceDisputeGeneratorInputs NDGInputs;
        public double CostBenefitRatio;
        public bool TakePrecaution;


        static ConcurrentQueue<NegligenceDisputeGeneratorModuleProgress> RecycledNegligenceDisputeGeneratorModuleProgressQueue = new ConcurrentQueue<NegligenceDisputeGeneratorModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledNegligenceDisputeGeneratorModuleProgressQueue.Enqueue(this);
            }
        }

        public static new NegligenceDisputeGeneratorModuleProgress GetRecycledOrAllocate()
        {
            NegligenceDisputeGeneratorModuleProgress recycled = null;
            RecycledNegligenceDisputeGeneratorModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new NegligenceDisputeGeneratorModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            NDGInputs = null; // torecycle
            CostBenefitRatio = 0;
            TakePrecaution = false;
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            NegligenceDisputeGeneratorModuleProgress copy = new NegligenceDisputeGeneratorModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(NegligenceDisputeGeneratorModuleProgress copy)
        {
            copy.NDGInputs = NDGInputs;
            copy.CostBenefitRatio = CostBenefitRatio;
            copy.TakePrecaution = TakePrecaution;

            base.CopyFieldInfo(copy);
        }

        public override object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
            switch (variableNameForReport)
            {
                case "MagnitudeOfInjury":
                    found = true;
                    return NDGInputs.MagnitudeOfInjury;

                case "ProbabilityInjuryWithPrecaution":
                    found = true;
                    return NDGInputs.ProbabilityInjuryWithPrecaution;

                default:
                    break;
            }
            return base.GetNonFieldValueForReport(variableNameForReport, out found);
        }
    }
}
