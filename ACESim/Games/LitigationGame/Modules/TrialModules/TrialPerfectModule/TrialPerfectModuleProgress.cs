using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class TrialPerfectModuleProgress : TrialModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */


        static ConcurrentQueue<TrialPerfectModuleProgress> RecycledTrialPerfectModuleProgressQueue = new ConcurrentQueue<TrialPerfectModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledTrialPerfectModuleProgressQueue.Enqueue(this);
            }
        }

        public static new TrialPerfectModuleProgress GetRecycledOrAllocate()
        {
            TrialPerfectModuleProgress recycled = null;
            RecycledTrialPerfectModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new TrialPerfectModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            TrialPerfectModuleProgress copy = new TrialPerfectModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(TrialPerfectModuleProgress copy)
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
