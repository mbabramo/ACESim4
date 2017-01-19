using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class DropOrDefaultChoiceModuleProgress : DropOrDefaultModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public bool StartedModuleThisBargainingRound = false;
        public bool DualDrop = false;


        static ConcurrentQueue<DropOrDefaultChoiceModuleProgress> RecycledDropOrDefaultChoiceModuleProgressQueue = new ConcurrentQueue<DropOrDefaultChoiceModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledDropOrDefaultChoiceModuleProgressQueue.Enqueue(this);
            }
        }

        public static new DropOrDefaultChoiceModuleProgress GetRecycledOrAllocate()
        {
            DropOrDefaultChoiceModuleProgress recycled = null;
            RecycledDropOrDefaultChoiceModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new DropOrDefaultChoiceModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            StartedModuleThisBargainingRound = false;
            DualDrop = false;
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            DropOrDefaultChoiceModuleProgress copy = new DropOrDefaultChoiceModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(DropOrDefaultChoiceModuleProgress copy)
        {
            copy.StartedModuleThisBargainingRound = StartedModuleThisBargainingRound;
            copy.DualDrop = DualDrop;
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
