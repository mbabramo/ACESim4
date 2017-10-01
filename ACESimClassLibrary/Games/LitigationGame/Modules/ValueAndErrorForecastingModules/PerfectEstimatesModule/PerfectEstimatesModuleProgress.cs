using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class PerfectEstimatesModuleProgress : ValueAndErrorForecastingModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */


        static ConcurrentQueue<PerfectEstimatesModuleProgress> RecycledPerfectEstimatesModuleProgressQueue = new ConcurrentQueue<PerfectEstimatesModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledPerfectEstimatesModuleProgressQueue.Enqueue(this);
            }
        }

        public static new PerfectEstimatesModuleProgress GetRecycledOrAllocate()
        {
            PerfectEstimatesModuleProgress recycled = null;
            RecycledPerfectEstimatesModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new PerfectEstimatesModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            PerfectEstimatesModuleProgress copy = new PerfectEstimatesModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(PerfectEstimatesModuleProgress copy)
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
