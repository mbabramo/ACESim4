using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class NeverGiveUpModuleProgress : DropOrDefaultModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */


        static ConcurrentQueue<NeverGiveUpModuleProgress> RecycledNeverGiveUpModuleProgressQueue = new ConcurrentQueue<NeverGiveUpModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledNeverGiveUpModuleProgressQueue.Enqueue(this);
            }
        }

        public static new NeverGiveUpModuleProgress GetRecycledOrAllocate()
        {
            NeverGiveUpModuleProgress recycled = null;
            RecycledNeverGiveUpModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new NeverGiveUpModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            NeverGiveUpModuleProgress copy = new NeverGiveUpModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(NeverGiveUpModuleProgress copy)
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
