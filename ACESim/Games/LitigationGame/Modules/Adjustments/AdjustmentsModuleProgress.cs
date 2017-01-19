using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class AdjustmentsModuleProgress : GameModuleProgress
    {
        /* NOTE: Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */

        static ConcurrentQueue<AdjustmentsModuleProgress> RecycledAdjustmentsModuleProgressQueue = new ConcurrentQueue<AdjustmentsModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledAdjustmentsModuleProgressQueue.Enqueue(this);
            }
        }

        public static new AdjustmentsModuleProgress GetRecycledOrAllocate()
        {
            AdjustmentsModuleProgress recycled = null;
            RecycledAdjustmentsModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new AdjustmentsModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            AdjustmentsModuleProgress copy = new AdjustmentsModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(AdjustmentsModuleProgress copy)
        {
            base.CopyFieldInfo(copy);
        }
    }
}
