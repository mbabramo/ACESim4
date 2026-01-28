using System;
using System.Globalization;
using System.Text;

namespace ACESimBase.Util.DiscreteProbabilities
{
    public static class SignalChannelDiagnostics
    {
        public static double[] GetUnconditionalSignalProbabilities(
            double[] priorHiddenValues,
            double[][] signalProbabilitiesGivenHidden)
        {
            if (priorHiddenValues == null) throw new ArgumentNullException(nameof(priorHiddenValues));
            if (signalProbabilitiesGivenHidden == null) throw new ArgumentNullException(nameof(signalProbabilitiesGivenHidden));
            if (priorHiddenValues.Length <= 0) throw new ArgumentOutOfRangeException(nameof(priorHiddenValues));
            if (signalProbabilitiesGivenHidden.Length != priorHiddenValues.Length)
                throw new ArgumentException("Hidden dimension mismatch.", nameof(signalProbabilitiesGivenHidden));

            if (signalProbabilitiesGivenHidden[0] == null)
                throw new ArgumentException("Conditional table contains a null row.", nameof(signalProbabilitiesGivenHidden));

            int signalCount = signalProbabilitiesGivenHidden[0].Length;
            if (signalCount <= 0)
                throw new ArgumentException("Signal dimension must be positive.", nameof(signalProbabilitiesGivenHidden));

            double[] unconditional = new double[signalCount];

            for (int h = 0; h < priorHiddenValues.Length; h++)
            {
                double[] row = signalProbabilitiesGivenHidden[h];
                if (row == null)
                    throw new ArgumentException("Conditional table contains a null row.", nameof(signalProbabilitiesGivenHidden));
                if (row.Length != signalCount)
                    throw new ArgumentException("Signal dimension mismatch.", nameof(signalProbabilitiesGivenHidden));

                double priorH = priorHiddenValues[h];

                for (int s = 0; s < signalCount; s++)
                    unconditional[s] += priorH * row[s];
            }

            return unconditional;
        }

        public static double[] GetUnconditionalPlaintiffSignalProbabilities(SignalChannelModel channelModel)
        {
            if (channelModel == null) throw new ArgumentNullException(nameof(channelModel));
            return GetUnconditionalSignalProbabilities(channelModel.PriorHiddenValues, channelModel.PlaintiffSignalProbabilitiesGivenHidden);
        }

        public static double[] GetUnconditionalDefendantSignalProbabilities(SignalChannelModel channelModel)
        {
            if (channelModel == null) throw new ArgumentNullException(nameof(channelModel));
            return GetUnconditionalSignalProbabilities(channelModel.PriorHiddenValues, channelModel.DefendantSignalProbabilitiesGivenHidden);
        }

        public static double[] GetUnconditionalCourtSignalProbabilities(SignalChannelModel channelModel)
        {
            if (channelModel == null) throw new ArgumentNullException(nameof(channelModel));
            return GetUnconditionalSignalProbabilities(channelModel.PriorHiddenValues, channelModel.CourtSignalProbabilitiesGivenHidden);
        }

        public static double GetMaximumAbsoluteDeviationFromUniform(double[] probabilities)
        {
            if (probabilities == null) throw new ArgumentNullException(nameof(probabilities));
            if (probabilities.Length <= 0) throw new ArgumentOutOfRangeException(nameof(probabilities));

            double uniform = 1.0 / probabilities.Length;
            double maxAbs = 0.0;

            for (int i = 0; i < probabilities.Length; i++)
            {
                double abs = Math.Abs(probabilities[i] - uniform);
                if (abs > maxAbs) maxAbs = abs;
            }

            return maxAbs;
        }

        public static double[] BuildSymmetricTailDecayTarget(int signalCount, double tailDecay)
        {
            if (signalCount <= 0) throw new ArgumentOutOfRangeException(nameof(signalCount));
            if (double.IsNaN(tailDecay) || double.IsInfinity(tailDecay)) tailDecay = 0.0;

            double center = (signalCount - 1) / 2.0;
            double maxDistance = Math.Max(1.0, center);

            double[] target = new double[signalCount];

            double sum = 0.0;
            for (int i = 0; i < signalCount; i++)
            {
                double normalizedDistance = Math.Abs(i - center) / maxDistance;
                double exponent = -tailDecay * normalizedDistance;
                double value = Math.Exp(exponent);
                target[i] = value;
                sum += value;
            }

            if (!(sum > 0.0) || double.IsNaN(sum) || double.IsInfinity(sum))
            {
                double uniform = 1.0 / signalCount;
                for (int i = 0; i < signalCount; i++)
                    target[i] = uniform;
                return target;
            }

            double inv = 1.0 / sum;
            for (int i = 0; i < signalCount; i++)
                target[i] *= inv;

            return target;
        }

        public static string BuildSummaryString(SignalChannelModel channelModel, string channelLabel = null, int decimalPlaces = 6)
        {
            if (channelModel == null) throw new ArgumentNullException(nameof(channelModel));
            if (decimalPlaces < 0) throw new ArgumentOutOfRangeException(nameof(decimalPlaces));

            double[] pUnconditional = GetUnconditionalPlaintiffSignalProbabilities(channelModel);
            double[] dUnconditional = GetUnconditionalDefendantSignalProbabilities(channelModel);
            double[] cUnconditional = GetUnconditionalCourtSignalProbabilities(channelModel);

            string numberFormat = "0." + new string('#', Math.Max(1, decimalPlaces));

            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(channelLabel))
                sb.AppendLine(channelLabel);

            sb.Append("Hidden prior: ");
            AppendVector(sb, channelModel.PriorHiddenValues, numberFormat);
            sb.AppendLine();

            sb.Append("P(signal) Plaintiff: ");
            AppendVector(sb, pUnconditional, numberFormat);
            sb.AppendLine();

            sb.Append("P(signal) Defendant: ");
            AppendVector(sb, dUnconditional, numberFormat);
            sb.AppendLine();

            sb.Append("P(signal) Court: ");
            AppendVector(sb, cUnconditional, numberFormat);

            return sb.ToString();
        }

        private static void AppendVector(StringBuilder sb, double[] values, string numberFormat)
        {
            sb.Append('[');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(values[i].ToString(numberFormat, CultureInfo.InvariantCulture));
            }
            sb.Append(']');
        }
    }
}
