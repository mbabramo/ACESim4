using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class DropOrDefaultModuleProgress : GameModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public DropOrDefaultPeriod DropOrDefaultPeriod; // when this drop/default opportunity occurred
        public int MidDropRoundsCompleted;
        public bool PDropsCase;
        public bool DDefaultsCase;

        static ConcurrentQueue<DropOrDefaultModuleProgress> RecycledDropOrDefaultModuleProgressQueue = new ConcurrentQueue<DropOrDefaultModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledDropOrDefaultModuleProgressQueue.Enqueue(this);
            }
        }

        public static new DropOrDefaultModuleProgress GetRecycledOrAllocate()
        {
            DropOrDefaultModuleProgress recycled = null;
            RecycledDropOrDefaultModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new DropOrDefaultModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            DropOrDefaultPeriod = default(DropOrDefaultPeriod); // torecycle
            MidDropRoundsCompleted = 0;
            PDropsCase = false;
            DDefaultsCase = false;
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            DropOrDefaultModuleProgress copy = new DropOrDefaultModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(DropOrDefaultModuleProgress copy)
        {
            copy.DropOrDefaultPeriod = DropOrDefaultPeriod;
            copy.MidDropRoundsCompleted = MidDropRoundsCompleted;
            copy.PDropsCase = PDropsCase;
            copy.DDefaultsCase = DDefaultsCase;
            base.CopyFieldInfo(copy);
        }

        public override object GetFieldValueForReport(string variableNameForReport, int? listIndex, out bool found)
        {
            string variableNameForReport2 = variableNameForReport;
            if (DropOrDefaultPeriod == DropOrDefaultPeriod.Beginning && variableNameForReport.StartsWith("Beginning."))
                variableNameForReport2 = variableNameForReport.Replace("Beginning.", "");
            else if (DropOrDefaultPeriod == DropOrDefaultPeriod.Mid && variableNameForReport.StartsWith("Mid."))
                variableNameForReport2 = variableNameForReport.Replace("Mid.", "");
            else if (DropOrDefaultPeriod == DropOrDefaultPeriod.End && variableNameForReport.StartsWith("End."))
                variableNameForReport2 = variableNameForReport.Replace("End.", "");

            object result = base.GetFieldValueForReport(variableNameForReport2, listIndex, out found);
            if (!found && variableNameForReport.StartsWith("Mid.") && DropOrDefaultPeriod == DropOrDefaultPeriod.End)
            { // we must be omitting the mid round
                result = null; 
                found = true;
            }
            return result;
        }
    }
}
