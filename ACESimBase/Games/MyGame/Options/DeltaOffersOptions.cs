using System;

namespace ACESim
{
    [Serializable]
    public struct DeltaOffersOptions
    {
        /// <summary>
        /// If true, then the second offer by a party is interpreted as a value relative to the first offer.
        /// </summary>
        public bool SubsequentOffersAreDeltas;

        /// <summary>
        /// When subsequent offers are deltas, this represents the minimum (non-zero) delta. The party making the offer can make an offer +/- this amount.
        /// </summary>
        public double DeltaStartingValue;

        /// <summary>
        /// When subsequent offers are deltas, this represents the maximum delta. The intermediate deltas will be determined relative to this. 
        /// </summary>
        public double MaxDelta;

        public override string ToString()
        {
            if (SubsequentOffersAreDeltas)
            {
                return $"Deltas {DeltaStartingValue}-{MaxDelta}";
            }
            else
                return "N/A";
        }
    }
}