using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Reflection;

namespace ACESim
{
    [Serializable]
    public class GameModuleProgress : GameProgressReportable
    {

        static ConcurrentQueue<GameModuleProgress> RecycledGameModuleProgressQueue = new ConcurrentQueue<GameModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public virtual void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                RecycledGameModuleProgressQueue.Enqueue(this);
            }
        }

        public static GameModuleProgress GetRecycledOrAllocate()
        {
            GameModuleProgress recycled = null;
            RecycledGameModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new GameModuleProgress();
        }

        public virtual void CleanAfterRecycling()
        {
        }

        public virtual GameModuleProgress DeepCopy()
        {
            GameModuleProgress copy = new GameModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(GameModuleProgress copy)
        {
            // copy.TemporaryInputsStorage = TemporaryInputsStorage == null ? null : TemporaryInputsStorage.ToList();
        }
    }
}
