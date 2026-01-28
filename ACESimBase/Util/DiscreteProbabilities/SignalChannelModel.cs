using System;

namespace ACESimBase.Util.DiscreteProbabilities
{
    [Serializable]
    public sealed class SignalChannelModel
    {
        public double[] PriorHiddenValues { get; }
        public double[][] PlaintiffSignalProbabilitiesGivenHidden { get; }
        public double[][] DefendantSignalProbabilitiesGivenHidden { get; }
        public double[][] CourtSignalProbabilitiesGivenHidden { get; }

        public int HiddenValueCount => PriorHiddenValues.Length;

        public int PlaintiffSignalCount => PlaintiffSignalProbabilitiesGivenHidden.Length == 0 ? 0 : PlaintiffSignalProbabilitiesGivenHidden[0].Length;
        public int DefendantSignalCount => DefendantSignalProbabilitiesGivenHidden.Length == 0 ? 0 : DefendantSignalProbabilitiesGivenHidden[0].Length;
        public int CourtSignalCount => CourtSignalProbabilitiesGivenHidden.Length == 0 ? 0 : CourtSignalProbabilitiesGivenHidden[0].Length;

        public SignalChannelModel(
            double[] priorHiddenValues,
            double[][] plaintiffSignalProbabilitiesGivenHidden,
            double[][] defendantSignalProbabilitiesGivenHidden,
            double[][] courtSignalProbabilitiesGivenHidden)
        {
            if (priorHiddenValues == null) throw new ArgumentNullException(nameof(priorHiddenValues));
            if (plaintiffSignalProbabilitiesGivenHidden == null) throw new ArgumentNullException(nameof(plaintiffSignalProbabilitiesGivenHidden));
            if (defendantSignalProbabilitiesGivenHidden == null) throw new ArgumentNullException(nameof(defendantSignalProbabilitiesGivenHidden));
            if (courtSignalProbabilitiesGivenHidden == null) throw new ArgumentNullException(nameof(courtSignalProbabilitiesGivenHidden));

            if (priorHiddenValues.Length <= 0) throw new ArgumentOutOfRangeException(nameof(priorHiddenValues));

            int hiddenValueCount = priorHiddenValues.Length;
            if (plaintiffSignalProbabilitiesGivenHidden.Length != hiddenValueCount) throw new ArgumentException("Hidden dimension mismatch.", nameof(plaintiffSignalProbabilitiesGivenHidden));
            if (defendantSignalProbabilitiesGivenHidden.Length != hiddenValueCount) throw new ArgumentException("Hidden dimension mismatch.", nameof(defendantSignalProbabilitiesGivenHidden));
            if (courtSignalProbabilitiesGivenHidden.Length != hiddenValueCount) throw new ArgumentException("Hidden dimension mismatch.", nameof(courtSignalProbabilitiesGivenHidden));

            const double sumTolerance = 1e-10;

            double[] priorClone = (double[])priorHiddenValues.Clone();
            double priorSum = 0.0;
            for (int i = 0; i < priorClone.Length; i++)
            {
                double v = priorClone[i];
                if (double.IsNaN(v) || double.IsInfinity(v))
                    throw new ArgumentException("Hidden prior contains NaN or Infinity.", nameof(priorHiddenValues));
                if (v < 0.0)
                    throw new ArgumentException("Hidden prior contains a negative probability.", nameof(priorHiddenValues));
                priorSum += v;
            }

            if (!(priorSum > 0.0) || double.IsNaN(priorSum) || double.IsInfinity(priorSum))
                throw new ArgumentException("Hidden prior sum is invalid.", nameof(priorHiddenValues));
            if (Math.Abs(priorSum - 1.0) > sumTolerance)
                throw new ArgumentException("Hidden prior probabilities must sum to 1 within tolerance.", nameof(priorHiddenValues));

            PriorHiddenValues = priorClone;
            PlaintiffSignalProbabilitiesGivenHidden = DeepCloneJagged2D(plaintiffSignalProbabilitiesGivenHidden, nameof(plaintiffSignalProbabilitiesGivenHidden));
            DefendantSignalProbabilitiesGivenHidden = DeepCloneJagged2D(defendantSignalProbabilitiesGivenHidden, nameof(defendantSignalProbabilitiesGivenHidden));
            CourtSignalProbabilitiesGivenHidden = DeepCloneJagged2D(courtSignalProbabilitiesGivenHidden, nameof(courtSignalProbabilitiesGivenHidden));
        }


        private static double[][] DeepCloneJagged2D(double[][] table, string paramName)
        {
            if (table.Length == 0) throw new ArgumentOutOfRangeException(paramName, "Table must have at least one row.");
            if (table[0] == null) throw new ArgumentException("Null row.", paramName);

            int signalCount = table[0].Length;
            if (signalCount <= 0) throw new ArgumentException("Signal dimension must be positive.", paramName);

            const double sumTolerance = 1e-10;

            var clone = new double[table.Length][];
            for (int h = 0; h < table.Length; h++)
            {
                if (table[h] == null) throw new ArgumentException("Null row.", paramName);
                if (table[h].Length != signalCount) throw new ArgumentException("Inconsistent signal dimension.", paramName);

                double[] rowClone = (double[])table[h].Clone();

                double rowSum = 0.0;
                for (int s = 0; s < signalCount; s++)
                {
                    double v = rowClone[s];
                    if (double.IsNaN(v) || double.IsInfinity(v))
                        throw new ArgumentException($"Conditional table contains NaN or Infinity. HiddenIndex={h}.", paramName);
                    if (v < 0.0)
                        throw new ArgumentException($"Conditional table contains a negative probability. HiddenIndex={h}.", paramName);
                    rowSum += v;
                }

                if (!(rowSum > 0.0) || double.IsNaN(rowSum) || double.IsInfinity(rowSum))
                    throw new ArgumentException($"Conditional probability row sum is invalid. HiddenIndex={h}.", paramName);
                if (Math.Abs(rowSum - 1.0) > sumTolerance)
                    throw new ArgumentException($"Conditional probability row must sum to 1 within tolerance. HiddenIndex={h}, Sum={rowSum}.", paramName);

                clone[h] = rowClone;
            }

            return clone;
        }

    }
}
