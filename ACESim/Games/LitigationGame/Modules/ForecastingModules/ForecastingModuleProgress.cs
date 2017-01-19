using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ForecastingModuleProgress : GameModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public double? Forecast;
        public bool ForecastConditionIsMet; // if the condition is not met, then this will not affect the score
        public double EventualOutcome;


        static ConcurrentQueue<ForecastingModuleProgress> RecycledForecastingModuleProgressQueue = new ConcurrentQueue<ForecastingModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledForecastingModuleProgressQueue.Enqueue(this);
            }
        }

        public static new ForecastingModuleProgress GetRecycledOrAllocate()
        {
            ForecastingModuleProgress recycled = null;
            RecycledForecastingModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new ForecastingModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            Forecast = null;
            ForecastConditionIsMet = false;
            EventualOutcome = 0;
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            ForecastingModuleProgress copy = new ForecastingModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(ForecastingModuleProgress copy)
        {
            copy.Forecast = Forecast;
            copy.ForecastConditionIsMet = ForecastConditionIsMet;
            copy.EventualOutcome = EventualOutcome;
            base.CopyFieldInfo(copy);
        }
    }
}
