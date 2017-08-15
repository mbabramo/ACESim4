using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class SimultaneousOfferBargainingModuleProgress : BargainingModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public double? MostRecentPlaintiffOffer;
        public double? MostRecentDefendantOffer;
        public double? MostRecentBargainingDistance;
        public bool PAcceptsDOffer;
        public bool DAcceptsPOffer;
        public List<double?> BargainingDistanceList;


        static ConcurrentQueue<SimultaneousOfferBargainingModuleProgress> RecycledSimultaneousOfferBargainingModuleProgressQueue = new ConcurrentQueue<SimultaneousOfferBargainingModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledSimultaneousOfferBargainingModuleProgressQueue.Enqueue(this);
            }
        }

        public static new SimultaneousOfferBargainingModuleProgress GetRecycledOrAllocate()
        {
            SimultaneousOfferBargainingModuleProgress recycled = null;
            RecycledSimultaneousOfferBargainingModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new SimultaneousOfferBargainingModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            MostRecentPlaintiffOffer = null;
            MostRecentDefendantOffer = null;
            MostRecentBargainingDistance = null;
            PAcceptsDOffer = false;
            DAcceptsPOffer = false;
            BargainingDistanceList = null; // torecycle
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            SimultaneousOfferBargainingModuleProgress copy = new SimultaneousOfferBargainingModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(SimultaneousOfferBargainingModuleProgress copy)
        {
            copy.MostRecentPlaintiffOffer = MostRecentPlaintiffOffer;
            copy.MostRecentDefendantOffer = MostRecentDefendantOffer;
            copy.MostRecentBargainingDistance = MostRecentBargainingDistance;
            copy.PAcceptsDOffer = PAcceptsDOffer;
            copy.DAcceptsPOffer = DAcceptsPOffer;
            copy.BargainingDistanceList = BargainingDistanceList == null ? null : BargainingDistanceList.ToList();
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
