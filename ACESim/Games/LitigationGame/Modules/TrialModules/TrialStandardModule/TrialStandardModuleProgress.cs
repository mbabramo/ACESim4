using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class TrialStandardModuleProgress : TrialModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */


        static ConcurrentQueue<TrialStandardModuleProgress> RecycledTrialStandardModuleProgressQueue = new ConcurrentQueue<TrialStandardModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledTrialStandardModuleProgressQueue.Enqueue(this);
            }
        }

        public static new TrialStandardModuleProgress GetRecycledOrAllocate()
        {
            TrialStandardModuleProgress recycled = null;
            RecycledTrialStandardModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new TrialStandardModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            TrialStandardModuleProgress copy = new TrialStandardModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(TrialStandardModuleProgress copy)
        {
            base.CopyFieldInfo(copy);
        }

        public override object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
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
