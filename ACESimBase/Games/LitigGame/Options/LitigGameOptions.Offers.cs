using System;
using System.Linq;

namespace ACESim
{
    public partial class LitigGameOptions
    {
        private double[] explicitOfferValues;

        /// <summary>
        /// Optional absolute offers, in action order. Null retains the original uniform
        /// action conversion, including its floating-point operation order.
        /// </summary>
        public double[] ExplicitOfferValues
        {
            get => explicitOfferValues?.ToArray();
            set => explicitOfferValues = value?.ToArray();
        }

        public double GetOfferValue(int action)
        {
            if (action < 1 || action > NumOffers)
                throw new ArgumentOutOfRangeException(nameof(action));
            return explicitOfferValues == null
                ? Game.ConvertActionToUniformDistributionDraw(action, NumOffers, IncludeEndpointsForOffers)
                : explicitOfferValues[action - 1];
        }

        public double[] GetOfferValues() => Enumerable.Range(1, NumOffers).Select(GetOfferValue).ToArray();

        public void ValidateOfferValues()
        {
            if (explicitOfferValues == null)
                return;
            if (NumOffers < 2 || explicitOfferValues.Length != NumOffers)
                throw new ArgumentException("Explicit offers must contain NumOffers entries and at least two actions.");
            if (DeltaOffersOptions.SubsequentOffersAreDeltas)
                throw new ArgumentException("Explicit absolute offers cannot be combined with delta offers.");
            for (int i = 0; i < explicitOfferValues.Length; i++)
                if (!double.IsFinite(explicitOfferValues[i]) || explicitOfferValues[i] < 0 || explicitOfferValues[i] > 1 ||
                    (i > 0 && explicitOfferValues[i] <= explicitOfferValues[i - 1]))
                    throw new ArgumentException("Explicit offers must be finite, strictly increasing values between zero and one.");
        }

        /// <summary>Creates an evenly spaced grid on declared fixed support.</summary>
        public static double[] CreateFixedSupportOffers(int count, double minimum, double maximum)
        {
            if (count < 2 || count > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum < 0 || maximum > 1 || minimum >= maximum)
                throw new ArgumentException("Offer support must satisfy 0 <= minimum < maximum <= 1.");
            // The established baseline is reproduced bit for bit, not recomputed with
            // a mathematically equivalent expression with different binary rounding.
            if (count == 10 && minimum == 0.05 && maximum == 0.95)
                return Enumerable.Range(1, count).Select(a => Game.ConvertActionToUniformDistributionDraw(a, count, false)).ToArray();
            var values = Enumerable.Range(0, count).Select(i => minimum + (maximum - minimum) * i / (count - 1)).ToArray();
            values[0] = minimum;
            values[count - 1] = maximum;
            return values;
        }
    }
}
