using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class SimultaneousOfferSymmetricBargainingModuleProgress : BargainingModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public double? PartysOffer;
        public double? OpponentsOffer;
        public double? DistanceBetweenOffers;


        static ConcurrentQueue<SimultaneousOfferSymmetricBargainingModuleProgress> RecycledSimultaneousOfferSymmetricBargainingModuleProgressQueue = new ConcurrentQueue<SimultaneousOfferSymmetricBargainingModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledSimultaneousOfferSymmetricBargainingModuleProgressQueue.Enqueue(this);
            }
        }

        public static new SimultaneousOfferSymmetricBargainingModuleProgress GetRecycledOrAllocate()
        {
            SimultaneousOfferSymmetricBargainingModuleProgress recycled = null;
            RecycledSimultaneousOfferSymmetricBargainingModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new SimultaneousOfferSymmetricBargainingModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            PartysOffer = null;
            OpponentsOffer = null;
            DistanceBetweenOffers = null;
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            SimultaneousOfferSymmetricBargainingModuleProgress copy = new SimultaneousOfferSymmetricBargainingModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(SimultaneousOfferSymmetricBargainingModuleProgress copy)
        {
            copy.PartysOffer = PartysOffer;
            copy.OpponentsOffer = OpponentsOffer;
            copy.DistanceBetweenOffers = DistanceBetweenOffers;
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
            return base.GetNonFieldValueForReport(variableNameForReport, out found);
        }
    }
}
