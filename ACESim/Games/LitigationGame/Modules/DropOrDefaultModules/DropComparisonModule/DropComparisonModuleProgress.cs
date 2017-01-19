using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class DropComparisonModuleProgress : DropOrDefaultModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public bool StartedModuleThisBargainingRound = false;
        public double PUtilityDropping;
        public double PUtilityNotDropping;
        public double DUtilityDefaulting;
        public double DUtilityNotDefaulting;


        static ConcurrentQueue<DropComparisonModuleProgress> RecycledDropComparisonModuleProgressQueue = new ConcurrentQueue<DropComparisonModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledDropComparisonModuleProgressQueue.Enqueue(this);
            }
        }

        public static new DropComparisonModuleProgress GetRecycledOrAllocate()
        {
            DropComparisonModuleProgress recycled = null;
            RecycledDropComparisonModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new DropComparisonModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            StartedModuleThisBargainingRound = false;
            PUtilityDropping = 0;
            PUtilityNotDropping = 0;
            DUtilityDefaulting = 0;
            DUtilityNotDefaulting = 0;
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            DropComparisonModuleProgress copy = new DropComparisonModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(DropComparisonModuleProgress copy)
        {
            copy.StartedModuleThisBargainingRound = StartedModuleThisBargainingRound;
            copy.PUtilityDropping = PUtilityDropping;
            copy.PUtilityNotDropping = PUtilityNotDropping;
            copy.DUtilityDefaulting = DUtilityDefaulting;
            copy.DUtilityNotDefaulting = DUtilityNotDefaulting;
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
