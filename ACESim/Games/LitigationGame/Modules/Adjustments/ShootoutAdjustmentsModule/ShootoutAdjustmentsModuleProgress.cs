using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ShootoutAdjustmentsModuleProgress : AdjustmentsModuleProgress
    {
        public ShootoutAdjustmentsModuleInputs ShootoutInputs;
        public List<LitigationShootout> Shootouts;


        static ConcurrentQueue<ShootoutAdjustmentsModuleProgress> RecycledShootoutAdjustmentsModuleProgressQueue = new ConcurrentQueue<ShootoutAdjustmentsModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledShootoutAdjustmentsModuleProgressQueue.Enqueue(this);
            }
        }

        public static new ShootoutAdjustmentsModuleProgress GetRecycledOrAllocate()
        {
            ShootoutAdjustmentsModuleProgress recycled = null;
            RecycledShootoutAdjustmentsModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new ShootoutAdjustmentsModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            ShootoutInputs = null; // torecycle
            Shootouts = null; // torecycle
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            ShootoutAdjustmentsModuleProgress copy = new ShootoutAdjustmentsModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(ShootoutAdjustmentsModuleProgress copy)
        {
            copy.ShootoutInputs = ShootoutInputs;
            copy.Shootouts = Shootouts;
            base.CopyFieldInfo(copy);
        }
    }
}
