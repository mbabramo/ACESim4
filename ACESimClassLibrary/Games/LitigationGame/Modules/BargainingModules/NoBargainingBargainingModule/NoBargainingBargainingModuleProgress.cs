using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class NoBargainingBargainingModuleProgress : BargainingModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */


        static ConcurrentQueue<NoBargainingBargainingModuleProgress> RecycledNoBargainingBargainingModuleProgressQueue = new ConcurrentQueue<NoBargainingBargainingModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledNoBargainingBargainingModuleProgressQueue.Enqueue(this);
            }
        }

        public static new NoBargainingBargainingModuleProgress GetRecycledOrAllocate()
        {
            NoBargainingBargainingModuleProgress recycled = null;
            RecycledNoBargainingBargainingModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new NoBargainingBargainingModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            NoBargainingBargainingModuleProgress copy = new NoBargainingBargainingModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(NoBargainingBargainingModuleProgress copy)
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
