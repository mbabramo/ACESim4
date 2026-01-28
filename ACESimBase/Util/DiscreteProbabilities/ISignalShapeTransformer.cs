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

    /// <summary>
    /// Shapes a channel so the prior-weighted unconditional (marginal) distribution of signal labels
    /// is (approximately) uniform, while preserving informativeness as much as possible via a
    /// global per-signal multiplicative reweighting shared across hidden values.
    /// </summary>
    [Serializable]
    public sealed class EqualMarginalSignalShapeTransformer : ISignalShapeTransformer
    {
        public static readonly EqualMarginalSignalShapeTransformer Instance = new EqualMarginalSignalShapeTransformer();

        private EqualMarginalSignalShapeTransformer()
        {
        }

        public double[][] Transform(double[] hiddenPrior, double[][] signalProbabilitiesGivenHidden)
        {
            if (hiddenPrior == null)
                throw new ArgumentNullException(nameof(hiddenPrior));
            if (signalProbabilitiesGivenHidden == null)
                throw new ArgumentNullException(nameof(signalProbabilitiesGivenHidden));

            int signalCount = SignalShapeTransformerUtilities.GetSignalCountOrThrow(signalProbabilitiesGivenHidden, nameof(signalProbabilitiesGivenHidden));
            if (signalCount <= 0)
                throw new InvalidOperationException("Signal count must be positive.");

            var target = new double[signalCount];
            double uniform = 1.0 / signalCount;
            for (int s = 0; s < signalCount; s++)
                target[s] = uniform;

            return SignalShapeTransformerUtilities.TransformByMatchingUnconditionalMarginal(
                hiddenPrior,
                signalProbabilitiesGivenHidden,
                target);
        }
    }

    /// <summary>
    /// Shapes a channel so the prior-weighted unconditional (marginal) distribution of signal labels
    /// follows a symmetric tail-decay profile: extreme labels are less likely as TailDecay increases.
    /// TailDecay = 0 corresponds to a uniform marginal (same target as EqualMarginal).
    /// </summary>
    [Serializable]
    public sealed class TailDecaySignalShapeTransformer : ISignalShapeTransformer
    {
        private readonly double _tailDecay;

        public TailDecaySignalShapeTransformer(double tailDecay)
        {
            if (double.IsNaN(tailDecay) || double.IsInfinity(tailDecay))
                throw new ArgumentOutOfRangeException(nameof(tailDecay), "TailDecay must be a finite number.");

            _tailDecay = tailDecay;
        }

        public double[][] Transform(double[] hiddenPrior, double[][] signalProbabilitiesGivenHidden)
        {
            if (hiddenPrior == null)
                throw new ArgumentNullException(nameof(hiddenPrior));
            if (signalProbabilitiesGivenHidden == null)
                throw new ArgumentNullException(nameof(signalProbabilitiesGivenHidden));

            int signalCount = SignalShapeTransformerUtilities.GetSignalCountOrThrow(signalProbabilitiesGivenHidden, nameof(signalProbabilitiesGivenHidden));
            if (signalCount <= 0)
                throw new InvalidOperationException("Signal count must be positive.");

            var target = new double[signalCount];

            if (signalCount == 1)
            {
                target[0] = 1.0;
            }
            else
            {
                double center = (signalCount - 1) / 2.0;
                double maxDistance = center;
                double sum = 0.0;

                for (int s = 0; s < signalCount; s++)
                {
                    double normalizedDistance = maxDistance > 0.0 ? Math.Abs(s - center) / maxDistance : 0.0;
                    double exponent = -_tailDecay * normalizedDistance;

                    if (exponent < -745.0) exponent = -745.0;
                    if (exponent > 709.0) exponent = 709.0;

                    double w = Math.Exp(exponent);
                    target[s] = w;
                    sum += w;
                }

                if (!(sum > 0.0) || double.IsNaN(sum) || double.IsInfinity(sum))
                {
                    double uniform = 1.0 / signalCount;
                    for (int s = 0; s < signalCount; s++)
                        target[s] = uniform;
                }
                else
                {
                    double inv = 1.0 / sum;
                    for (int s = 0; s < signalCount; s++)
                        target[s] *= inv;
                }
            }

            return SignalShapeTransformerUtilities.TransformByMatchingUnconditionalMarginal(
                hiddenPrior,
                signalProbabilitiesGivenHidden,
                target);
        }
    }

    internal static class SignalShapeTransformerUtilities
    {
        private const int DefaultMaxIterations = 2000;
        private const double DefaultConvergenceTolerance = 1e-12;

        internal static int GetSignalCountOrThrow(double[][] conditionalTable, string paramName)
        {
            if (conditionalTable == null)
                throw new ArgumentNullException(paramName);
            if (conditionalTable.Length <= 0)
                throw new ArgumentOutOfRangeException(paramName, "Conditional table must have at least one row.");
            if (conditionalTable[0] == null)
                throw new ArgumentException("Conditional table contains a null row.", paramName);

            int signalCount = conditionalTable[0].Length;
            if (signalCount <= 0)
                throw new ArgumentException("Conditional table must have at least one signal column.", paramName);

            for (int h = 0; h < conditionalTable.Length; h++)
            {
                if (conditionalTable[h] == null)
                    throw new ArgumentException("Conditional table contains a null row.", paramName);
                if (conditionalTable[h].Length != signalCount)
                    throw new ArgumentException("Conditional table has inconsistent signal dimension.", paramName);
            }

            return signalCount;
        }

        internal static double[][] TransformByMatchingUnconditionalMarginal(
            double[] hiddenPrior,
            double[][] signalProbabilitiesGivenHidden,
            double[] targetSignalProbabilitiesUnconditional)
        {
            return TransformByMatchingUnconditionalMarginal(
                hiddenPrior,
                signalProbabilitiesGivenHidden,
                targetSignalProbabilitiesUnconditional,
                DefaultMaxIterations,
                DefaultConvergenceTolerance);
        }

        internal static double[][] TransformByMatchingUnconditionalMarginal(
            double[] hiddenPrior,
            double[][] signalProbabilitiesGivenHidden,
            double[] targetSignalProbabilitiesUnconditional,
            int maxIterations,
            double convergenceTolerance)
        {
            if (hiddenPrior == null)
                throw new ArgumentNullException(nameof(hiddenPrior));
            if (signalProbabilitiesGivenHidden == null)
                throw new ArgumentNullException(nameof(signalProbabilitiesGivenHidden));
            if (targetSignalProbabilitiesUnconditional == null)
                throw new ArgumentNullException(nameof(targetSignalProbabilitiesUnconditional));
            if (maxIterations <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxIterations));
            if (!(convergenceTolerance > 0.0) || double.IsNaN(convergenceTolerance) || double.IsInfinity(convergenceTolerance))
                throw new ArgumentOutOfRangeException(nameof(convergenceTolerance));

            int hiddenCount = hiddenPrior.Length;
            if (hiddenCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(hiddenPrior), "Hidden prior must have at least one element.");
            if (signalProbabilitiesGivenHidden.Length != hiddenCount)
                throw new ArgumentException("Hidden dimension mismatch.", nameof(signalProbabilitiesGivenHidden));

            int signalCount = GetSignalCountOrThrow(signalProbabilitiesGivenHidden, nameof(signalProbabilitiesGivenHidden));
            if (targetSignalProbabilitiesUnconditional.Length != signalCount)
                throw new ArgumentException("Target signal dimension mismatch.", nameof(targetSignalProbabilitiesUnconditional));

            double[] prior = NormalizeCopyOrThrow(hiddenPrior, nameof(hiddenPrior));
            double[] target = NormalizeCopyOrThrow(targetSignalProbabilitiesUnconditional, nameof(targetSignalProbabilitiesUnconditional));

            double[] baseSupport = new double[signalCount];
            for (int h = 0; h < hiddenCount; h++)
            {
                double pH = prior[h];
                if (pH == 0.0)
                    continue;

                double[] row = signalProbabilitiesGivenHidden[h];
                for (int s = 0; s < signalCount; s++)
                    baseSupport[s] += pH * row[s];
            }

            for (int s = 0; s < signalCount; s++)
            {
                if (target[s] > 0.0 && baseSupport[s] == 0.0)
                    throw new InvalidOperationException("Requested signal shaping is infeasible because at least one signal label has zero prior-weighted probability under the base channel.");
            }

            double[] columnWeights = new double[signalCount];
            for (int s = 0; s < signalCount; s++)
                columnWeights[s] = 1.0;

            double[] rowNormalizers = new double[hiddenCount];
            double[] marginal = new double[signalCount];

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                for (int h = 0; h < hiddenCount; h++)
                {
                    double[] row = signalProbabilitiesGivenHidden[h];

                    double z = 0.0;
                    for (int s = 0; s < signalCount; s++)
                        z += row[s] * columnWeights[s];

                    if (!(z > 0.0) || double.IsNaN(z) || double.IsInfinity(z))
                        throw new InvalidOperationException("Signal shaping failed because at least one hidden row collapsed to a non-positive normalizer.");

                    rowNormalizers[h] = z;
                }

                for (int s = 0; s < signalCount; s++)
                    marginal[s] = 0.0;

                for (int h = 0; h < hiddenCount; h++)
                {
                    double pH = prior[h];
                    if (pH == 0.0)
                        continue;

                    double invZ = 1.0 / rowNormalizers[h];
                    double[] row = signalProbabilitiesGivenHidden[h];

                    for (int s = 0; s < signalCount; s++)
                        marginal[s] += pH * row[s] * columnWeights[s] * invZ;
                }

                NormalizeRowInPlaceOrUniform(marginal);

                double maxAbsError = 0.0;
                for (int s = 0; s < signalCount; s++)
                {
                    double e = Math.Abs(marginal[s] - target[s]);
                    if (e > maxAbsError)
                        maxAbsError = e;
                }

                if (maxAbsError <= convergenceTolerance)
                    break;

                for (int s = 0; s < signalCount; s++)
                {
                    double m = marginal[s];
                    double t = target[s];

                    if (!(m > 0.0))
                    {
                        if (t == 0.0)
                            continue;

                        throw new InvalidOperationException("Requested signal shaping is infeasible because the induced unconditional distribution assigns zero probability to a signal label that has positive target probability.");
                    }

                    double ratio = t / m;
                    if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0.0)
                        throw new InvalidOperationException("Signal shaping failed due to an invalid multiplicative update.");

                    double updated = columnWeights[s] * ratio;

                    if (updated < 1e-300) updated = 1e-300;
                    if (updated > 1e300) updated = 1e300;

                    columnWeights[s] = updated;
                }

                RenormalizeWeightsByGeometricMeanInPlace(columnWeights);
            }

            var result = new double[hiddenCount][];
            for (int h = 0; h < hiddenCount; h++)
            {
                double[] row = signalProbabilitiesGivenHidden[h];
                var shapedRow = new double[signalCount];

                for (int s = 0; s < signalCount; s++)
                {
                    double v = row[s] * columnWeights[s];
                    if (double.IsNaN(v) || double.IsInfinity(v) || v < 0.0)
                        v = 0.0;
                    shapedRow[s] = v;
                }

                NormalizeRowInPlaceOrUniform(shapedRow);
                result[h] = shapedRow;
            }

            return result;
        }

        private static double[] NormalizeCopyOrThrow(double[] values, string paramName)
        {
            if (values == null)
                throw new ArgumentNullException(paramName);
            if (values.Length <= 0)
                throw new ArgumentOutOfRangeException(paramName, "Array must have at least one element.");

            var copy = (double[])values.Clone();

            double sum = 0.0;
            for (int i = 0; i < copy.Length; i++)
            {
                double v = copy[i];
                if (double.IsNaN(v) || double.IsInfinity(v))
                    throw new ArgumentException("Array contains NaN or Infinity.", paramName);
                if (v < 0.0)
                    throw new ArgumentException("Array contains a negative value.", paramName);

                sum += v;
            }

            if (!(sum > 0.0) || double.IsNaN(sum) || double.IsInfinity(sum))
                throw new ArgumentException("Array sum is invalid.", paramName);

            double inv = 1.0 / sum;
            for (int i = 0; i < copy.Length; i++)
                copy[i] *= inv;

            return copy;
        }

        private static void NormalizeRowInPlaceOrUniform(double[] values)
        {
            double sum = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                double v = values[i];
                if (double.IsNaN(v) || double.IsInfinity(v) || v < 0.0)
                    v = 0.0;

                values[i] = v;
                sum += v;
            }

            if (!(sum > 0.0) || double.IsNaN(sum) || double.IsInfinity(sum))
            {
                double uniform = 1.0 / values.Length;
                for (int i = 0; i < values.Length; i++)
                    values[i] = uniform;
                return;
            }

            double inv = 1.0 / sum;
            for (int i = 0; i < values.Length; i++)
                values[i] *= inv;
        }

        private static void RenormalizeWeightsByGeometricMeanInPlace(double[] weights)
        {
            double logSum = 0.0;
            for (int i = 0; i < weights.Length; i++)
            {
                double w = weights[i];
                if (!(w > 0.0) || double.IsNaN(w) || double.IsInfinity(w))
                    return;

                logSum += Math.Log(w);
            }

            double logMean = logSum / weights.Length;
            double scale = Math.Exp(logMean);

            if (!(scale > 0.0) || double.IsNaN(scale) || double.IsInfinity(scale))
                return;

            double inv = 1.0 / scale;
            for (int i = 0; i < weights.Length; i++)
            {
                double w = weights[i] * inv;

                if (w < 1e-300) w = 1e-300;
                if (w > 1e300) w = 1e300;

                weights[i] = w;
            }
        }
    }
}
