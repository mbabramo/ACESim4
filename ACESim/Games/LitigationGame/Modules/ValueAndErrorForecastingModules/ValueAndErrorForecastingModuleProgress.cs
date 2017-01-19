using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ValueAndErrorForecastingModuleProgress : GameModuleProgress
    {

        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public bool IsProbabilityNotDamages;
        /// <summary>
        /// P's strength on an issue (probability or damages) that the parties are trying to predict. For example, with probability of winning, this is the underlying evidentiary strength, not the corresponding probability. With damages, this is the percent of the claim that P actually would receive.
        /// </summary>
        public double? ActualPIssueStrength;
        /// <summary>
        /// P's strength on an issue (probability or damages) that the parties are trying to predict, where strength on a probabilistic issue is translated into the probability that P would win.
        /// </summary>
        public double? ActualPResultTransformed;
        public double? PEstimatePResult;
        public double? PEstimateDResult;
        public double? DEstimatePResult;
        public double? DEstimateDResult;
        public double? PEstimatePError;
        //public double? PEstimateDError;
        // public double? DEstimatePError;
        public double? DEstimateDError;
        public double? CurrentEquivalentPNoiseLevel;
        public double? CurrentEquivalentDNoiseLevel;
        public int PNumRandomSeedsUsed;
        public int DNumRandomSeedsUsed;

        static ConcurrentQueue<ValueAndErrorForecastingModuleProgress> RecycledValueAndErrorForecastingModuleProgressQueue = new ConcurrentQueue<ValueAndErrorForecastingModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledValueAndErrorForecastingModuleProgressQueue.Enqueue(this);
            }
        }

        public static new ValueAndErrorForecastingModuleProgress GetRecycledOrAllocate()
        {
            ValueAndErrorForecastingModuleProgress recycled = null;
            RecycledValueAndErrorForecastingModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new ValueAndErrorForecastingModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            IsProbabilityNotDamages = false;
            ActualPIssueStrength = null;
            ActualPResultTransformed = null;
            PEstimatePResult = null;
            PEstimateDResult = null;
            DEstimatePResult = null;
            DEstimateDResult = null;
            PEstimatePError = null;
            DEstimateDError = null;
            CurrentEquivalentPNoiseLevel = null;
            CurrentEquivalentDNoiseLevel = null;
            PNumRandomSeedsUsed = 0;
            DNumRandomSeedsUsed = 0;
            base.CleanAfterRecycling();
        }


        public override GameModuleProgress DeepCopy()
        {
            ValueAndErrorForecastingModuleProgress copy = new ValueAndErrorForecastingModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(ValueAndErrorForecastingModuleProgress copy)
        {
            copy.IsProbabilityNotDamages = IsProbabilityNotDamages;
            copy.ActualPIssueStrength = ActualPIssueStrength;
            copy.ActualPResultTransformed = ActualPResultTransformed;
            copy.PEstimatePResult = PEstimatePResult;
            copy.PEstimateDResult = PEstimateDResult;
            copy.DEstimatePResult = DEstimatePResult;
            copy.DEstimateDResult = DEstimateDResult;
            copy.PEstimatePError = PEstimatePError;
            //copy.PEstimateDError = PEstimateDError;
            //copy.DEstimatePError = DEstimatePError;
            copy.DEstimateDError = DEstimateDError;
            copy.CurrentEquivalentPNoiseLevel = CurrentEquivalentPNoiseLevel;
            copy.CurrentEquivalentDNoiseLevel = CurrentEquivalentDNoiseLevel;
            copy.PNumRandomSeedsUsed = PNumRandomSeedsUsed;
            copy.DNumRandomSeedsUsed = DNumRandomSeedsUsed;
            base.CopyFieldInfo(copy);
        }

        public override object GetFieldValueForReport(string variableNameForReport, int? listIndex, out bool found)
        {
            if (IsProbabilityNotDamages && variableNameForReport.StartsWith("Probability."))
                variableNameForReport = variableNameForReport.Replace("Probability.", "");
            else if (!IsProbabilityNotDamages && variableNameForReport.StartsWith("Damages."))
                variableNameForReport = variableNameForReport.Replace("Damages.", "");

            if (variableNameForReport == "PEstimatePActualError")
            {
                found = true;
                if (ActualPResultTransformed == null)
                    return (double?) null;
                return Math.Abs((double)PEstimatePResult - (double)ActualPResultTransformed);
            }
            if (variableNameForReport == "DEstimatePActualError")
            {
                found = true;
                if (ActualPResultTransformed == null)
                    return (double?)null;
                return Math.Abs((double)DEstimatePResult - (double)ActualPResultTransformed);
            }
            if (variableNameForReport == "AbsDiffBetweenPAndDResultEstimates")
            {
                found = true;
                if (DEstimatePResult == null)
                    return (double?)null;
                return Math.Abs((double)DEstimatePResult - (double)PEstimatePResult);
            }
            

            return base.GetFieldValueForReport(variableNameForReport, listIndex, out found);
        }
    }
}
