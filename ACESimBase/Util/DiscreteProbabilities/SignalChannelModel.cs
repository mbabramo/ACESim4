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

            PriorHiddenValues = (double[])priorHiddenValues.Clone();
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

            var clone = new double[table.Length][];
            for (int h = 0; h < table.Length; h++)
            {
                if (table[h] == null) throw new ArgumentException("Null row.", paramName);
                if (table[h].Length != signalCount) throw new ArgumentException("Inconsistent signal dimension.", paramName);
                clone[h] = (double[])table[h].Clone();
            }

            return clone;
        }
    }
}
