using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class OfferThresholdBargainingModuleProgress : BargainingModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public bool? RandomlyPickingOneOffer;
        public bool? ConsiderPlaintiffsOffer;
        public bool? ConsiderDefendantsOffer;
        public double? MostRecentPlaintiffOffer;
        public double? MostRecentDefendantOffer;
        public double? MostRecentPlaintiffThreshold;
        public double? MostRecentDefendantThreshold;
        public double? MostRecentBargainingDistancePlaintiffOffer;
        public double? MostRecentBargainingDistanceDefendantOffer;

        public List<double?> ListPlaintiffOffer;
        public List<double?> ListDefendantOffer;
        public List<double?> ListPlaintiffThreshold;
        public List<double?> ListDefendantThreshold;
        public List<double?> ListBargainingDistancePlaintiffOffer;
        public List<double?> ListBargainingDistanceDefendantOffer;


        public bool?  SettlementIsOnPlaintiffOfferOnly;
        public bool? SettlementIsOnDefendantOfferOnly;
        public bool? SettlementIsOnBothOffers;


        static ConcurrentQueue<OfferThresholdBargainingModuleProgress> RecycledOfferThresholdBargainingModuleProgressQueue = new ConcurrentQueue<OfferThresholdBargainingModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledOfferThresholdBargainingModuleProgressQueue.Enqueue(this);
            }
        }

        public static new OfferThresholdBargainingModuleProgress GetRecycledOrAllocate()
        {
            OfferThresholdBargainingModuleProgress recycled = null;
            RecycledOfferThresholdBargainingModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new OfferThresholdBargainingModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            RandomlyPickingOneOffer = null;
            ConsiderPlaintiffsOffer = null;
            ConsiderDefendantsOffer = null;
            MostRecentPlaintiffOffer = null;
            MostRecentDefendantOffer = null;
            MostRecentPlaintiffThreshold = null;
            MostRecentDefendantThreshold = null;
            MostRecentBargainingDistancePlaintiffOffer = null;
            MostRecentBargainingDistanceDefendantOffer = null;
            // torecycle rest
            ListPlaintiffOffer = null;
            ListDefendantOffer = null;
            ListPlaintiffThreshold = null;
            ListDefendantThreshold = null;
            ListBargainingDistancePlaintiffOffer = null;
            ListBargainingDistanceDefendantOffer = null;
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            OfferThresholdBargainingModuleProgress copy = new OfferThresholdBargainingModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(OfferThresholdBargainingModuleProgress copy)
        {
            copy.RandomlyPickingOneOffer = RandomlyPickingOneOffer;
            copy.ConsiderPlaintiffsOffer = ConsiderPlaintiffsOffer;
            copy.ConsiderDefendantsOffer = ConsiderDefendantsOffer;
            copy.MostRecentPlaintiffOffer = MostRecentPlaintiffOffer;
            copy.MostRecentDefendantOffer = MostRecentDefendantOffer;
            copy.MostRecentPlaintiffThreshold = MostRecentPlaintiffThreshold;
            copy.MostRecentDefendantThreshold = MostRecentDefendantThreshold;
            copy.MostRecentBargainingDistancePlaintiffOffer = MostRecentBargainingDistancePlaintiffOffer;
            copy.MostRecentBargainingDistanceDefendantOffer = MostRecentBargainingDistanceDefendantOffer;
            copy.ListPlaintiffOffer = ListPlaintiffOffer == null ? null : ListPlaintiffOffer.ToList();
            copy.ListDefendantOffer = ListDefendantOffer == null ? null : ListDefendantOffer.ToList();
            copy.ListPlaintiffThreshold = ListPlaintiffThreshold == null ? null : ListPlaintiffThreshold.ToList();
            copy.ListDefendantThreshold = ListDefendantThreshold == null ? null : ListDefendantThreshold.ToList();
            copy.ListBargainingDistancePlaintiffOffer = ListBargainingDistancePlaintiffOffer == null ? null : ListBargainingDistancePlaintiffOffer.ToList();
            copy.ListBargainingDistanceDefendantOffer = ListBargainingDistanceDefendantOffer == null ? null : ListBargainingDistanceDefendantOffer.ToList();
            copy.SettlementIsOnPlaintiffOfferOnly = SettlementIsOnPlaintiffOfferOnly;
            copy.SettlementIsOnDefendantOfferOnly = SettlementIsOnDefendantOfferOnly;
            copy.SettlementIsOnBothOffers = SettlementIsOnBothOffers;
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
