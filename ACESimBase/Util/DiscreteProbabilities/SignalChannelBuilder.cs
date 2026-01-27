using ACESim.Util.DiscreteProbabilities;
using System;

namespace ACESimBase.Util.DiscreteProbabilities
{
    public static class SignalChannelBuilder
    {
        public static SignalChannelModel BuildUsingDiscreteValueSignalParameters(
            double[] hiddenPrior,
            DiscreteValueSignalParameters plaintiffSignalParameters,
            DiscreteValueSignalParameters defendantSignalParameters,
            DiscreteValueSignalParameters courtSignalParameters,
            Func<double[], double[][], double[][]> signalShapeTransformer = null)
        {
            if (hiddenPrior == null)
                throw new ArgumentNullException(nameof(hiddenPrior));
            if (hiddenPrior.Length <= 0)
                throw new ArgumentOutOfRangeException(nameof(hiddenPrior), "Hidden prior must have at least one element.");

            int hiddenValueCount = hiddenPrior.Length;

            if (plaintiffSignalParameters.NumPointsInSourceUniformDistribution != hiddenValueCount)
                throw new ArgumentException("Plaintiff parameters hidden-count mismatch with hidden prior length.", nameof(plaintiffSignalParameters));
            if (defendantSignalParameters.NumPointsInSourceUniformDistribution != hiddenValueCount)
                throw new ArgumentException("Defendant parameters hidden-count mismatch with hidden prior length.", nameof(defendantSignalParameters));
            if (courtSignalParameters.NumPointsInSourceUniformDistribution != hiddenValueCount)
                throw new ArgumentException("Court parameters hidden-count mismatch with hidden prior length.", nameof(courtSignalParameters));

            double[][] plaintiffRaw = BuildConditionalSignalTableUsingDiscreteValueSignalParameters(plaintiffSignalParameters);
            double[][] defendantRaw = BuildConditionalSignalTableUsingDiscreteValueSignalParameters(defendantSignalParameters);
            double[][] courtRaw = BuildConditionalSignalTableUsingDiscreteValueSignalParameters(courtSignalParameters);

            Func<double[], double[][], double[][]> transformerToUse = signalShapeTransformer ?? IdentityConditionalTableTransformer;

            double[][] plaintiffTransformed = transformerToUse(hiddenPrior, plaintiffRaw);
            double[][] defendantTransformed = transformerToUse(hiddenPrior, defendantRaw);
            double[][] courtTransformed = transformerToUse(hiddenPrior, courtRaw);

            ValidateConditionalTableShapeOrThrow(hiddenValueCount, plaintiffSignalParameters.NumSignals, plaintiffTransformed, nameof(plaintiffTransformed));
            ValidateConditionalTableShapeOrThrow(hiddenValueCount, defendantSignalParameters.NumSignals, defendantTransformed, nameof(defendantTransformed));
            ValidateConditionalTableShapeOrThrow(hiddenValueCount, courtSignalParameters.NumSignals, courtTransformed, nameof(courtTransformed));

            NormalizeConditionalTableRowsInPlace(plaintiffTransformed);
            NormalizeConditionalTableRowsInPlace(defendantTransformed);
            NormalizeConditionalTableRowsInPlace(courtTransformed);

            return new SignalChannelModel(
                (double[])hiddenPrior.Clone(),
                plaintiffTransformed,
                defendantTransformed,
                courtTransformed);
        }

        public static SignalChannelModel BuildFromNoise(
            double[] hiddenPrior,
            int plaintiffSignalCount,
            double plaintiffNoiseStdev,
            int defendantSignalCount,
            double defendantNoiseStdev,
            int courtSignalCount,
            double courtNoiseStdev,
            bool sourcePointsIncludeExtremes,
            Func<double[], double[][], double[][]> signalShapeTransformer = null)
        {
            if (hiddenPrior == null)
                throw new ArgumentNullException(nameof(hiddenPrior));
            if (hiddenPrior.Length <= 0)
                throw new ArgumentOutOfRangeException(nameof(hiddenPrior), "Hidden prior must have at least one element.");

            if (plaintiffSignalCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(plaintiffSignalCount));
            if (defendantSignalCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(defendantSignalCount));
            if (courtSignalCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(courtSignalCount));

            int hiddenValueCount = hiddenPrior.Length;

            double[][] plaintiffRaw = BuildConditionalSignalTableFromNoise(hiddenValueCount, plaintiffSignalCount, plaintiffNoiseStdev, sourcePointsIncludeExtremes);
            double[][] defendantRaw = BuildConditionalSignalTableFromNoise(hiddenValueCount, defendantSignalCount, defendantNoiseStdev, sourcePointsIncludeExtremes);
            double[][] courtRaw = BuildConditionalSignalTableFromNoise(hiddenValueCount, courtSignalCount, courtNoiseStdev, sourcePointsIncludeExtremes);

            Func<double[], double[][], double[][]> transformerToUse = signalShapeTransformer ?? IdentityConditionalTableTransformer;

            double[][] plaintiffTransformed = transformerToUse(hiddenPrior, plaintiffRaw);
            double[][] defendantTransformed = transformerToUse(hiddenPrior, defendantRaw);
            double[][] courtTransformed = transformerToUse(hiddenPrior, courtRaw);

            ValidateConditionalTableShapeOrThrow(hiddenValueCount, plaintiffSignalCount, plaintiffTransformed, nameof(plaintiffTransformed));
            ValidateConditionalTableShapeOrThrow(hiddenValueCount, defendantSignalCount, defendantTransformed, nameof(defendantTransformed));
            ValidateConditionalTableShapeOrThrow(hiddenValueCount, courtSignalCount, courtTransformed, nameof(courtTransformed));

            NormalizeConditionalTableRowsInPlace(plaintiffTransformed);
            NormalizeConditionalTableRowsInPlace(defendantTransformed);
            NormalizeConditionalTableRowsInPlace(courtTransformed);

            return new SignalChannelModel(
                (double[])hiddenPrior.Clone(),
                plaintiffTransformed,
                defendantTransformed,
                courtTransformed);
        }

        private static double[][] IdentityConditionalTableTransformer(double[] hiddenPrior, double[][] probabilitiesSignalGivenHidden)
        {
            return probabilitiesSignalGivenHidden;
        }

        private static double[][] BuildConditionalSignalTableUsingDiscreteValueSignalParameters(DiscreteValueSignalParameters parameters)
        {
            if (parameters.NumPointsInSourceUniformDistribution <= 0)
                throw new ArgumentOutOfRangeException(nameof(parameters), "NumPointsInSourceUniformDistribution must be positive.");
            if (parameters.NumSignals <= 0)
                throw new ArgumentOutOfRangeException(nameof(parameters), "NumSignals must be positive.");

            int hiddenValueCount = parameters.NumPointsInSourceUniformDistribution;
            int signalCount = parameters.NumSignals;

            var table = new double[hiddenValueCount][];

            for (int h = 1; h <= hiddenValueCount; h++)
            {
                double[] rowFromCache = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h, parameters);
                if (rowFromCache == null || rowFromCache.Length != signalCount)
                    throw new InvalidOperationException("DiscreteValueSignal returned an unexpected row shape.");

                table[h - 1] = (double[])rowFromCache.Clone();
            }

            return table;
        }

        private static double[][] BuildConditionalSignalTableFromNoise(
            int hiddenValueCount,
            int signalCount,
            double noiseStdev,
            bool sourcePointsIncludeExtremes)
        {
            var parameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = hiddenValueCount,
                NumSignals = signalCount,
                StdevOfNormalDistribution = noiseStdev,
                SourcePointsIncludeExtremes = sourcePointsIncludeExtremes,
                SignalBoundaryMode = DiscreteSignalBoundaryMode.EqualWidth
            };

            var table = new double[hiddenValueCount][];

            if (noiseStdev == 0.0)
            {
                for (int h = 1; h <= hiddenValueCount; h++)
                {
                    double location = parameters.MapSourceTo0To1(h);
                    int zeroBasedSignalIndex = DiscreteSignalBoundaries.MapLocationIn0To1ToZeroBasedSignalIndex(
                        location,
                        signalCount,
                        parameters.SignalBoundaryMode);

                    double[] row = new double[signalCount];
                    row[zeroBasedSignalIndex] = 1.0;
                    table[h - 1] = row;
                }
                return table;
            }

            for (int h = 1; h <= hiddenValueCount; h++)
            {
                double[] rowFromCache = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h, parameters);
                if (rowFromCache == null || rowFromCache.Length != signalCount)
                    throw new InvalidOperationException("DiscreteValueSignal returned an unexpected row shape.");

                table[h - 1] = (double[])rowFromCache.Clone();
            }

            return table;
        }

        private static void NormalizeConditionalTableRowsInPlace(double[][] conditionalProbabilityTable)
        {
            if (conditionalProbabilityTable == null)
                throw new ArgumentNullException(nameof(conditionalProbabilityTable));

            for (int h = 0; h < conditionalProbabilityTable.Length; h++)
            {
                double[] row = conditionalProbabilityTable[h];
                if (row == null)
                    throw new ArgumentException("Conditional table contains a null row.", nameof(conditionalProbabilityTable));

                NormalizeRowInPlaceOrUniformIfInvalid(row);
            }
        }

        private static void NormalizeRowInPlaceOrUniformIfInvalid(double[] values)
        {
            const double sumTolerance = 1e-12;

            double sum = 0.0;
            for (int i = 0; i < values.Length; i++)
                sum += values[i];

            if (!(sum > 0.0) || double.IsNaN(sum) || double.IsInfinity(sum))
            {
                double uniform = 1.0 / values.Length;
                for (int i = 0; i < values.Length; i++)
                    values[i] = uniform;
                return;
            }

            if (Math.Abs(sum - 1.0) <= sumTolerance)
                return;

            double inv = 1.0 / sum;
            for (int i = 0; i < values.Length; i++)
                values[i] *= inv;
        }

        private static void ValidateConditionalTableShapeOrThrow(
            int expectedHiddenValueCount,
            int expectedSignalCount,
            double[][] conditionalProbabilityTable,
            string argumentName)
        {
            if (conditionalProbabilityTable == null)
                throw new ArgumentNullException(argumentName);
            if (conditionalProbabilityTable.Length != expectedHiddenValueCount)
                throw new ArgumentException("Hidden dimension mismatch.", argumentName);

            for (int h = 0; h < conditionalProbabilityTable.Length; h++)
            {
                double[] row = conditionalProbabilityTable[h];
                if (row == null)
                    throw new ArgumentException("Conditional table contains a null row.", argumentName);
                if (row.Length != expectedSignalCount)
                    throw new ArgumentException("Signal dimension mismatch.", argumentName);
            }
        }
    }
}
