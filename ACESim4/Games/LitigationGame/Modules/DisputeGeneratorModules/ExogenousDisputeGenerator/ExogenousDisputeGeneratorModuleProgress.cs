using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ExogenousDisputeGeneratorModuleProgress : DisputeGeneratorModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public ExogenousDisputeGeneratorInputs ExogenousInputs;


        static ConcurrentQueue<ExogenousDisputeGeneratorModuleProgress> RecycledExogenousDisputeGeneratorModuleProgressQueue = new ConcurrentQueue<ExogenousDisputeGeneratorModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledExogenousDisputeGeneratorModuleProgressQueue.Enqueue(this);
            }
        }

        public static new ExogenousDisputeGeneratorModuleProgress GetRecycledOrAllocate()
        {
            ExogenousDisputeGeneratorModuleProgress recycled = null;
            RecycledExogenousDisputeGeneratorModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new ExogenousDisputeGeneratorModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            ExogenousInputs = null; // torecycle
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            ExogenousDisputeGeneratorModuleProgress copy = new ExogenousDisputeGeneratorModuleProgress();
            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(ExogenousDisputeGeneratorModuleProgress copy)
        {
            copy.ExogenousInputs = ExogenousInputs;

            base.CopyFieldInfo(copy);
        }

        public override object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
            switch (variableNameForReport)
            {
                default:
                    break;
            }
            return base.GetNonFieldValueForReport(variableNameForReport, out found);
        }
    }
}
