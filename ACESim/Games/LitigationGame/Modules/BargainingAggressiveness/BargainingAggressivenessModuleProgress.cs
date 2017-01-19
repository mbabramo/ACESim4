using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class BargainingAggressivenessOverrideModuleProgress : GameModuleProgress
    {
        /* NOTE: Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */

        static ConcurrentQueue<BargainingAggressivenessOverrideModuleProgress> RecycledBargainingAggressivenessOverrideModuleProgressQueue = new ConcurrentQueue<BargainingAggressivenessOverrideModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledBargainingAggressivenessOverrideModuleProgressQueue.Enqueue(this);
            }
        }

        public static new BargainingAggressivenessOverrideModuleProgress GetRecycledOrAllocate()
        {
            BargainingAggressivenessOverrideModuleProgress recycled = null;
            RecycledBargainingAggressivenessOverrideModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new BargainingAggressivenessOverrideModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            base.CleanAfterRecycling();
        }


        public override GameModuleProgress DeepCopy()
        {
            BargainingAggressivenessOverrideModuleProgress copy = new BargainingAggressivenessOverrideModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(BargainingAggressivenessOverrideModuleProgress copy)
        {
            base.CopyFieldInfo(copy);
        }

    }
}
