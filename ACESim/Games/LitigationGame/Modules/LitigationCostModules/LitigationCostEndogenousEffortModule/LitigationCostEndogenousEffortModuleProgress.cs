using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class LitigationCostEndogenousEffortModuleProgress : LitigationCostStandardModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public LitigationCostEndogenousEffortInputs LCEEInputs { get { return (LitigationCostEndogenousEffortInputs)LitigationCostInputs; } }
        public double PInvestigationLevel;
        public double DInvestigationLevel;
        public double PTrialPrep;
        public double DTrialPrep;
        public List<double> PAdditionalInvestigativeExpenses;
        public List<double> DAdditionalInvestigativeExpenses;

        static ConcurrentQueue<LitigationCostEndogenousEffortModuleProgress> RecycledLitigationCostEndogenousEffortModuleProgressQueue = new ConcurrentQueue<LitigationCostEndogenousEffortModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledLitigationCostEndogenousEffortModuleProgressQueue.Enqueue(this);
            }
        }

        public static new LitigationCostEndogenousEffortModuleProgress GetRecycledOrAllocate()
        {
            LitigationCostEndogenousEffortModuleProgress recycled = null;
            RecycledLitigationCostEndogenousEffortModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new LitigationCostEndogenousEffortModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            PInvestigationLevel = 0;
            DInvestigationLevel = 0;
            PTrialPrep = 0;
            DTrialPrep = 0;
            PAdditionalInvestigativeExpenses = null; // torecycle
            DAdditionalInvestigativeExpenses = null; // torecycle
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            LitigationCostEndogenousEffortModuleProgress copy = new LitigationCostEndogenousEffortModuleProgress();
            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(LitigationCostEndogenousEffortModuleProgress copy)
        {
            copy.PInvestigationLevel = PInvestigationLevel;
            copy.DInvestigationLevel = DInvestigationLevel;
            copy.PTrialPrep = PTrialPrep;
            copy.DTrialPrep = DTrialPrep;
            copy.PAdditionalInvestigativeExpenses = PAdditionalInvestigativeExpenses == null ? null : PAdditionalInvestigativeExpenses.ToList();
            copy.DAdditionalInvestigativeExpenses = DAdditionalInvestigativeExpenses == null ? null : DAdditionalInvestigativeExpenses.ToList();
            base.CopyFieldInfo(copy);
        }

        public override object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
            object returnObj = base.GetNonFieldValueForReport(variableNameForReport, out found);
            if (found)
                return returnObj;
            switch (variableNameForReport)
            {
                default:
                    break;
            }
            found = false;
            return null;
        }
    }
}
