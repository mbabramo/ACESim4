using System;

namespace ACESimBase.Util.DiscreteProbabilities
{
    /// <summary>
    /// Transforms a conditional signal channel P(signal | hidden) using the hidden prior
    /// as needed for prior-weighted (marginal/unconditional) shaping.
    ///
    /// Inputs and outputs are jagged arrays indexed as:
    ///   signalProbabilitiesGivenHidden[hiddenIndex][signalIndex].
    /// </summary>
    public interface ISignalShapeTransformer
    {
        double[][] Transform(
            double[] hiddenPrior,
            double[][] signalProbabilitiesGivenHidden);
    }

    /// <summary>
    /// Identity/no-op signal shape transformer.
    /// Returns a defensive deep copy of the input conditional table.
    /// </summary>
    [Serializable]
    public sealed class IdentitySignalShapeTransformer : ISignalShapeTransformer
    {
        public static readonly IdentitySignalShapeTransformer Instance = new IdentitySignalShapeTransformer();

        private IdentitySignalShapeTransformer()
        {
        }

        public double[][] Transform(
            double[] hiddenPrior,
            double[][] signalProbabilitiesGivenHidden)
        {
            if (signalProbabilitiesGivenHidden == null)
                throw new ArgumentNullException(nameof(signalProbabilitiesGivenHidden));

            int hiddenValueCount = signalProbabilitiesGivenHidden.Length;
            var clone = new double[hiddenValueCount][];

            for (int h = 0; h < hiddenValueCount; h++)
            {
                double[] row = signalProbabilitiesGivenHidden[h];
                if (row == null)
                    throw new ArgumentException("Conditional probability table contains a null row.", nameof(signalProbabilitiesGivenHidden));

                clone[h] = (double[])row.Clone();
            }

            return clone;
        }
    }
}
