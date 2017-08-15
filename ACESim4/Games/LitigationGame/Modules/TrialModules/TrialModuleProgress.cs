using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class TrialModuleProgress : GameModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        // Each of the following two are valued negative if they favor the defendant, and positive if they favor the plaintiff.
        // The value indicates the size of the shift relative to the difference between the more extreme probability and certainty.
        // For example, if p = 0.3 or 0.7, then a value of -0.5 would indicate 0.15 or 0.55, and a value of +0.5 would indicate 0.45 or 0.85
        // In no event can the result be greater than 1.0.
        public double? ShiftBasedOnEffort;
        public double? PEstimatePResultAtTrial;
        public double? DEstimatePResultAtTrial;
        public bool PartialSettlementEnforced;
        //public double? Bias;


        static ConcurrentQueue<TrialModuleProgress> RecycledTrialModuleProgressQueue = new ConcurrentQueue<TrialModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledTrialModuleProgressQueue.Enqueue(this);
            }
        }

        public static new TrialModuleProgress GetRecycledOrAllocate()
        {
            TrialModuleProgress recycled = null;
            RecycledTrialModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new TrialModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            ShiftBasedOnEffort = null;
            PEstimatePResultAtTrial = null;
            DEstimatePResultAtTrial = null;
            PartialSettlementEnforced = false;
            base.CleanAfterRecycling();
        }

        public double GetShiftedValue(double baseProbabilityPWins)
        {
            if (ShiftBasedOnEffort != null)
                baseProbabilityPWins = GetAdjustedProbability(baseProbabilityPWins, (double)ShiftBasedOnEffort);
            //if (Bias != null)
            //    baseProbabilityPWins = GetAdjustedProbability(baseProbabilityPWins, (double)Bias);
            return baseProbabilityPWins;
        }

        private double GetAdjustedProbability(double baseProbabilityPWins, double shiftCoefficient)
        {
            double moreExtremeProbability = Math.Max(baseProbabilityPWins, 1.0 - baseProbabilityPWins);
            double magnitudeOfFullShift = 1.0 - moreExtremeProbability;
            double adjustedMagnitude = shiftCoefficient * magnitudeOfFullShift;
            baseProbabilityPWins += adjustedMagnitude;
            if (baseProbabilityPWins > 1.0)
                baseProbabilityPWins = 1.0;
            else if (baseProbabilityPWins < 0)
                baseProbabilityPWins = 0.0;
            return baseProbabilityPWins;
        }

        public override GameModuleProgress DeepCopy()
        {
            TrialModuleProgress copy = new TrialModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(TrialModuleProgress copy)
        {
            copy.ShiftBasedOnEffort = ShiftBasedOnEffort;
            copy.PEstimatePResultAtTrial = PEstimatePResultAtTrial;
            copy.DEstimatePResultAtTrial = DEstimatePResultAtTrial;
            copy.PartialSettlementEnforced = PartialSettlementEnforced;
            //copy.Bias = Bias;
            base.CopyFieldInfo(copy);
        }
    }
}
