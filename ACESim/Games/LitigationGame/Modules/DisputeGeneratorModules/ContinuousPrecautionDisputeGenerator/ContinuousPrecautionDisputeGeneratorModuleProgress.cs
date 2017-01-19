using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ContinuousPrecautionDisputeGeneratorModuleProgress : DisputeGeneratorModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public ContinuousPrecautionDisputeGeneratorInputs CPDGInputs;
        public double MarginalCostMarginalBenefitRatio;
        public double LevelOfPrecaution;
        public double ProbabilityOfInjury;


        static ConcurrentQueue<ContinuousPrecautionDisputeGeneratorModuleProgress> RecycledContinuousPrecautionDisputeGeneratorModuleProgressQueue = new ConcurrentQueue<ContinuousPrecautionDisputeGeneratorModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledContinuousPrecautionDisputeGeneratorModuleProgressQueue.Enqueue(this);
            }
        }

        public static new ContinuousPrecautionDisputeGeneratorModuleProgress GetRecycledOrAllocate()
        {
            ContinuousPrecautionDisputeGeneratorModuleProgress recycled = null;
            RecycledContinuousPrecautionDisputeGeneratorModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new ContinuousPrecautionDisputeGeneratorModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            CPDGInputs = null; // torecycle
            MarginalCostMarginalBenefitRatio = 0;
            LevelOfPrecaution = 0;
            ProbabilityOfInjury = 0;
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            ContinuousPrecautionDisputeGeneratorModuleProgress copy = new ContinuousPrecautionDisputeGeneratorModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(ContinuousPrecautionDisputeGeneratorModuleProgress copy)
        {
            copy.CPDGInputs = CPDGInputs;
            copy.MarginalCostMarginalBenefitRatio = MarginalCostMarginalBenefitRatio;
            copy.LevelOfPrecaution = LevelOfPrecaution;
            copy.ProbabilityOfInjury = ProbabilityOfInjury;

            base.CopyFieldInfo(copy);
        }

        public override object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
            switch (variableNameForReport)
            {
                case "MagnitudeOfInjury":
                    found = true;
                    return CPDGInputs.MagnitudeOfInjury;

                case "PrecautionLevelThatProducesHalfOfGainsOfInfinitePrecaution":
                    found = true;
                    return CPDGInputs.PrecautionLevelThatProducesHalfOfGainsOfInfinitePrecaution;

                default:
                    break;
            }
            return base.GetNonFieldValueForReport(variableNameForReport, out found);
        }
    }
}
