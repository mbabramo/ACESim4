using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class SimplisticBargainingModuleProgress : BargainingModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */


        static ConcurrentQueue<SimplisticBargainingModuleProgress> RecycledSimplisticBargainingModuleProgressQueue = new ConcurrentQueue<SimplisticBargainingModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledSimplisticBargainingModuleProgressQueue.Enqueue(this);
            }
        }

        public static new SimplisticBargainingModuleProgress GetRecycledOrAllocate()
        {
            SimplisticBargainingModuleProgress recycled = null;
            RecycledSimplisticBargainingModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new SimplisticBargainingModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            SimplisticBargainingModuleProgress copy = new SimplisticBargainingModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(SimplisticBargainingModuleProgress copy)
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
            return base.GetNonFieldValueForReport(variableNameForReport, out found);
        }
    }
}
