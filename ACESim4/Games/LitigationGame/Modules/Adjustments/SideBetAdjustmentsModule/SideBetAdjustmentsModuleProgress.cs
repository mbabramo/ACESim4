using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class SideBetAdjustmentsModuleProgress : AdjustmentsModuleProgress
    {
        public bool StartedModule;
        public SideBetAdjustmentsModuleInputs SideBetInputs;
        public bool PChallengesD;
        public bool DChallengesP;
        public List<LitigationSideBet> SideBets;

        static ConcurrentQueue<SideBetAdjustmentsModuleProgress> RecycledSideBetAdjustmentsModuleProgressQueue = new ConcurrentQueue<SideBetAdjustmentsModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledSideBetAdjustmentsModuleProgressQueue.Enqueue(this);
            }
        }

        public static new SideBetAdjustmentsModuleProgress GetRecycledOrAllocate()
        {
            SideBetAdjustmentsModuleProgress recycled = null;
            RecycledSideBetAdjustmentsModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new SideBetAdjustmentsModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            StartedModule = false;
            SideBetInputs = null;
            PChallengesD = false;
            DChallengesP = false;
            SideBets = null; // torecycle
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            SideBetAdjustmentsModuleProgress copy = new SideBetAdjustmentsModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(SideBetAdjustmentsModuleProgress copy)
        {
            copy.StartedModule = StartedModule;
            copy.SideBetInputs = SideBetInputs;
            copy.PChallengesD = PChallengesD;
            copy.DChallengesP = DChallengesP;
            copy.SideBets = SideBets;
            base.CopyFieldInfo(copy);
        }
    }
}
