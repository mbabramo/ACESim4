using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class DummyModuleProgress : GameModuleProgress
    {

        static ConcurrentQueue<DummyModuleProgress> RecycledDummyModuleProgressQueue = new ConcurrentQueue<DummyModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledDummyModuleProgressQueue.Enqueue(this);
            }
        }

        public static new DummyModuleProgress GetRecycledOrAllocate()
        {
            DummyModuleProgress recycled = null;
            RecycledDummyModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new DummyModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            base.CleanAfterRecycling();
        }
    }
}
