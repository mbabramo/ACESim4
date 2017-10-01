using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class IndependentEstimatesModuleProgress : ValueAndErrorForecastingModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public double? PlaintiffProxy;
        public double? DefendantProxy;
        public double? PNoiseLevel;
        public double? DNoiseLevel;
        public ValueFromSignalEstimator PEstimator;
        public ValueFromSignalEstimator DEstimator;


        static ConcurrentQueue<IndependentEstimatesModuleProgress> RecycledIndependentEstimatesModuleProgressQueue = new ConcurrentQueue<IndependentEstimatesModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledIndependentEstimatesModuleProgressQueue.Enqueue(this);
            }
        }

        public static new IndependentEstimatesModuleProgress GetRecycledOrAllocate()
        {
            IndependentEstimatesModuleProgress recycled = null;
            RecycledIndependentEstimatesModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new IndependentEstimatesModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            PlaintiffProxy = null;
            DefendantProxy = null;
            PNoiseLevel = null;
            DNoiseLevel = null;
            PEstimator = null; // torecycle
            DEstimator = null; // torecycle
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            IndependentEstimatesModuleProgress copy = new IndependentEstimatesModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(IndependentEstimatesModuleProgress copy)
        {
            copy.PlaintiffProxy = PlaintiffProxy;
            copy.DefendantProxy = DefendantProxy;
            copy.PNoiseLevel = PNoiseLevel;
            copy.DNoiseLevel = DNoiseLevel;
            copy.PEstimator = PEstimator == null ? null : (ValueFromSignalEstimator) PEstimator.DeepCopy();
            copy.DEstimator = DEstimator == null ? null : (ValueFromSignalEstimator) DEstimator.DeepCopy();
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
