using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class SeparateEstimatesModuleProgress : ValueAndErrorForecastingModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public double? PlaintiffProxy;
        public double? DefendantProxy;
        public double? PNoiseLevel;
        public double? DNoiseLevel;
        public double? GenericNoiseLevel;
        public double? GenericNoiseLevel2;
        public double? GenericProxy;
        public double? GenericProxy2;
        public double? GenericEstimateResult;
        public double? GenericEstimateResult2;
        public double? GenericEstimateError;
        public double? GenericEstimateError2;
        public double? GenericEstimateOtherError;
        public double? GenericCombinedEstimateResult;
        public double? GenericCombinedEstimateError;
        public int CalcResultDecisionNumber = -1;
        public int CalcErrorDecisionNumber = -1;
        public int CalcOtherErrorDecisionNumber = -1;
        public int CalcCombinedResultDecisionNumber = -1;
        public int CalcCombinedErrorDecisionNumber = -1;


        static ConcurrentQueue<SeparateEstimatesModuleProgress> RecycledSeparateEstimatesModuleProgressQueue = new ConcurrentQueue<SeparateEstimatesModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledSeparateEstimatesModuleProgressQueue.Enqueue(this);
            }
        }

        public static new SeparateEstimatesModuleProgress GetRecycledOrAllocate()
        {
            SeparateEstimatesModuleProgress recycled = null;
            RecycledSeparateEstimatesModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new SeparateEstimatesModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            base.CleanAfterRecycling();
            throw new Exception("Must fill this in if using this module.");
        }

        public override GameModuleProgress DeepCopy()
        {
            SeparateEstimatesModuleProgress copy = new SeparateEstimatesModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(SeparateEstimatesModuleProgress copy)
        {
            copy.PlaintiffProxy = PlaintiffProxy;
            copy.DefendantProxy = DefendantProxy;
            copy.PNoiseLevel = PNoiseLevel;
            copy.DNoiseLevel = DNoiseLevel;
            copy.GenericNoiseLevel = GenericNoiseLevel;
            copy.GenericNoiseLevel2 = GenericNoiseLevel2;
            copy.GenericProxy = GenericProxy;
            copy.GenericProxy2 = GenericProxy2;
            copy.GenericEstimateResult = GenericEstimateResult;
            copy.GenericEstimateResult2 = GenericEstimateResult2;
            copy.GenericEstimateError = GenericEstimateError;
            copy.GenericEstimateError2 = GenericEstimateError2;
            copy.GenericEstimateOtherError = GenericEstimateOtherError;
            copy.GenericCombinedEstimateResult = GenericCombinedEstimateResult;
            copy.GenericCombinedEstimateError = GenericCombinedEstimateError;
            copy.CalcResultDecisionNumber = CalcResultDecisionNumber;
            copy.CalcErrorDecisionNumber = CalcErrorDecisionNumber;
            copy.CalcOtherErrorDecisionNumber = CalcOtherErrorDecisionNumber;
            copy.CalcCombinedResultDecisionNumber = CalcCombinedResultDecisionNumber;
            copy.CalcCombinedErrorDecisionNumber = CalcCombinedErrorDecisionNumber;
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
