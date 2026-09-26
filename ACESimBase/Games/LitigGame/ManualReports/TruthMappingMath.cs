using System;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    /// <summary>Ex-post reporting only. This class does not modify any game primitive.</summary>
    public static class TruthMappingMath
    {
        public static double Map(double quality, double exponent)
        {
            if (!double.IsFinite(quality) || quality < 0 || quality > 1)
                throw new ArgumentOutOfRangeException(nameof(quality));
            if (!double.IsFinite(exponent) || exponent <= 0)
                throw new ArgumentOutOfRangeException(nameof(exponent));
            if (exponent == 1 || quality == 0 || quality == 1)
                return quality;
            // Stable logistic form avoids 0/0 for large positive exponents.
            double odds = exponent * (Math.Log(quality) - Math.Log(1 - quality));
            if (odds >= 0)
                return 1 / (1 + Math.Exp(-odds));
            double exp = Math.Exp(odds);
            return exp / (1 + exp);
        }

        public static double Integrate(double[] quality, double[] weights, double exponent)
        {
            if (quality == null || weights == null || quality.Length == 0 || quality.Length != weights.Length)
                throw new ArgumentException("Supply matching nonempty quadrature arrays.");
            double total = 0, weight = 0;
            for (int i = 0; i < quality.Length; i++)
            {
                if (!double.IsFinite(weights[i]) || weights[i] < 0)
                    throw new ArgumentException("Quadrature weights must be finite and nonnegative.");
                weight += weights[i];
                total += weights[i] * Map(quality[i], exponent);
            }
            if (Math.Abs(weight - 1) > 1e-12)
                throw new ArgumentException("Posterior weights must already sum to one; implicit renormalization is not allowed.");
            return Math.Clamp(total, 0, 1);
        }
    }
}
