using ACESim.Util.DiscreteProbabilities;
using System;
using System.Collections.Generic;

namespace ACESimBase.Util.DiscreteProbabilities
{
    public sealed class ThreePartyCorrelatedSignalsBayes
    {
        private readonly int _hiddenValueCount;
        private readonly int _party0SignalCount;
        private readonly int _party1SignalCount;
        private readonly int _party2SignalCount;

        private readonly double[] _priorHiddenValues; // length H
        private readonly double[][] _party0SignalProbabilitiesGivenHidden; // [H][S0]
        private readonly double[][] _party1SignalProbabilitiesGivenHidden; // [H][S1]
        private readonly double[][] _party2SignalProbabilitiesGivenHidden; // [H][S2]

        private readonly double[] _party0SignalProbabilitiesUnconditional; // [S0]
        private readonly double[][] _party1SignalProbabilitiesGivenParty0Signal; // [S0][S1]
        private readonly double[][][] _party2SignalProbabilitiesGivenParty0AndParty1Signals; // [S0][S1][S2]

        private readonly double[][] _posteriorHiddenGivenParty0Signal; // [S0][H]
        private readonly double[][][] _posteriorHiddenGivenParty0AndParty1Signals; // [S0][S1][H]

        public int HiddenValueCount => _hiddenValueCount;
        public int Party0SignalCount => _party0SignalCount;
        public int Party1SignalCount => _party1SignalCount;
        public int Party2SignalCount => _party2SignalCount;

        public ThreePartyCorrelatedSignalsBayes(
            double[] priorHiddenValues,
            double[][] party0SignalProbabilitiesGivenHidden,
            double[][] party1SignalProbabilitiesGivenHidden,
            double[][] party2SignalProbabilitiesGivenHidden)
        {
            if (priorHiddenValues == null)
                throw new ArgumentNullException(nameof(priorHiddenValues));
            if (party0SignalProbabilitiesGivenHidden == null)
                throw new ArgumentNullException(nameof(party0SignalProbabilitiesGivenHidden));
            if (party1SignalProbabilitiesGivenHidden == null)
                throw new ArgumentNullException(nameof(party1SignalProbabilitiesGivenHidden));
            if (party2SignalProbabilitiesGivenHidden == null)
                throw new ArgumentNullException(nameof(party2SignalProbabilitiesGivenHidden));

            _hiddenValueCount = priorHiddenValues.Length;
            if (_hiddenValueCount <= 0)
                throw new ArgumentException("Hidden value count must be positive.", nameof(priorHiddenValues));

            RequireConsistentSignalCount(party0SignalProbabilitiesGivenHidden, _hiddenValueCount, out _party0SignalCount, nameof(party0SignalProbabilitiesGivenHidden));
            RequireConsistentSignalCount(party1SignalProbabilitiesGivenHidden, _hiddenValueCount, out _party1SignalCount, nameof(party1SignalProbabilitiesGivenHidden));
            RequireConsistentSignalCount(party2SignalProbabilitiesGivenHidden, _hiddenValueCount, out _party2SignalCount, nameof(party2SignalProbabilitiesGivenHidden));

            _priorHiddenValues = (double[])priorHiddenValues.Clone();
            NormalizeInPlaceOrUniform(_priorHiddenValues);

            _party0SignalProbabilitiesGivenHidden = DeepCloneAndNormalizeConditionals(party0SignalProbabilitiesGivenHidden, _party0SignalCount);
            _party1SignalProbabilitiesGivenHidden = DeepCloneAndNormalizeConditionals(party1SignalProbabilitiesGivenHidden, _party1SignalCount);
            _party2SignalProbabilitiesGivenHidden = DeepCloneAndNormalizeConditionals(party2SignalProbabilitiesGivenHidden, _party2SignalCount);

            _party0SignalProbabilitiesUnconditional = new double[_party0SignalCount];
            _posteriorHiddenGivenParty0Signal = CreateJagged2D(_party0SignalCount, _hiddenValueCount);

            _party1SignalProbabilitiesGivenParty0Signal = CreateJagged2D(_party0SignalCount, _party1SignalCount);
            _posteriorHiddenGivenParty0AndParty1Signals = CreateJagged3D(_party0SignalCount, _party1SignalCount, _hiddenValueCount);

            _party2SignalProbabilitiesGivenParty0AndParty1Signals = CreateJagged3D(_party0SignalCount, _party1SignalCount, _party2SignalCount);

            Precompute();
        }

        public static ThreePartyCorrelatedSignalsBayes CreateUsingDiscreteValueSignalParameters(
            double[] priorHiddenValues,
            DiscreteValueSignalParameters party0Params,
            DiscreteValueSignalParameters party1Params,
            DiscreteValueSignalParameters party2Params)
        {
            if (priorHiddenValues == null)
                throw new ArgumentNullException(nameof(priorHiddenValues));

            int hiddenValueCount = priorHiddenValues.Length;
            if (hiddenValueCount <= 0)
                throw new ArgumentException("Hidden value count must be positive.", nameof(priorHiddenValues));

            double[][] p0 = BuildConditionalSignalTableFromNoise(hiddenValueCount, party0Params.NumSignals, party0Params.SourcePointsIncludeExtremes, party0Params.StdevOfNormalDistribution);
            double[][] p1 = BuildConditionalSignalTableFromNoise(hiddenValueCount, party1Params.NumSignals, party1Params.SourcePointsIncludeExtremes, party1Params.StdevOfNormalDistribution);
            double[][] p2 = BuildConditionalSignalTableFromNoise(hiddenValueCount, party2Params.NumSignals, party2Params.SourcePointsIncludeExtremes, party2Params.StdevOfNormalDistribution);

            return new ThreePartyCorrelatedSignalsBayes(priorHiddenValues, p0, p1, p2);
        }

        public double[] GetParty0SignalProbabilitiesUnconditional() => _party0SignalProbabilitiesUnconditional;

        public double[] GetParty1SignalProbabilitiesGivenParty0Signal(byte party0Signal)
        {
            if (party0Signal < 1 || party0Signal > _party0SignalCount)
                throw new ArgumentOutOfRangeException(nameof(party0Signal));
            return _party1SignalProbabilitiesGivenParty0Signal[party0Signal - 1];
        }

        public double[] GetParty2SignalProbabilitiesGivenParty0AndParty1Signals(byte party0Signal, byte party1Signal)
        {
            if (party0Signal < 1 || party0Signal > _party0SignalCount)
                throw new ArgumentOutOfRangeException(nameof(party0Signal));
            if (party1Signal < 1 || party1Signal > _party1SignalCount)
                throw new ArgumentOutOfRangeException(nameof(party1Signal));
            return _party2SignalProbabilitiesGivenParty0AndParty1Signals[party0Signal - 1][party1Signal - 1];
        }

        public double[] GetPosteriorHiddenProbabilitiesGivenParty0Signal(byte party0Signal)
        {
            if (party0Signal < 1 || party0Signal > _party0SignalCount)
                throw new ArgumentOutOfRangeException(nameof(party0Signal));
            return _posteriorHiddenGivenParty0Signal[party0Signal - 1];
        }

        public double[] GetPosteriorHiddenProbabilitiesGivenParty0AndParty1Signals(byte party0Signal, byte party1Signal)
        {
            if (party0Signal < 1 || party0Signal > _party0SignalCount)
                throw new ArgumentOutOfRangeException(nameof(party0Signal));
            if (party1Signal < 1 || party1Signal > _party1SignalCount)
                throw new ArgumentOutOfRangeException(nameof(party1Signal));
            return _posteriorHiddenGivenParty0AndParty1Signals[party0Signal - 1][party1Signal - 1];
        }

        public double[] GetPosteriorHiddenProbabilitiesGivenSignals(byte party0Signal, byte party1Signal, byte? party2Signal)
        {
            if (!party2Signal.HasValue)
                return GetPosteriorHiddenProbabilitiesGivenParty0AndParty1Signals(party0Signal, party1Signal);

            byte p2 = party2Signal.Value;
            if (p2 < 1 || p2 > _party2SignalCount)
                throw new ArgumentOutOfRangeException(nameof(party2Signal));

            double[] posterior01 = GetPosteriorHiddenProbabilitiesGivenParty0AndParty1Signals(party0Signal, party1Signal);
            double[] result = new double[_hiddenValueCount];

            double sum = 0.0;
            for (int h = 0; h < _hiddenValueCount; h++)
            {
                double v = posterior01[h] * _party2SignalProbabilitiesGivenHidden[h][p2 - 1];
                if (v < 0.0) v = 0.0;
                result[h] = v;
                sum += v;
            }

            if (!(sum > 0.0) || double.IsNaN(sum) || double.IsInfinity(sum))
            {
                double uniform = 1.0 / _hiddenValueCount;
                for (int h = 0; h < _hiddenValueCount; h++)
                    result[h] = uniform;
                return result;
            }

            double inv = 1.0 / sum;
            for (int h = 0; h < _hiddenValueCount; h++)
                result[h] *= inv;

            return result;
        }

        private void Precompute()
        {
            for (int s0 = 0; s0 < _party0SignalCount; s0++)
            {
                double unconditional = 0.0;
                for (int h = 0; h < _hiddenValueCount; h++)
                    unconditional += _priorHiddenValues[h] * _party0SignalProbabilitiesGivenHidden[h][s0];

                if (unconditional < 0.0) unconditional = 0.0;
                _party0SignalProbabilitiesUnconditional[s0] = unconditional;

                if (unconditional > 0.0)
                {
                    double inv = 1.0 / unconditional;
                    for (int h = 0; h < _hiddenValueCount; h++)
                        _posteriorHiddenGivenParty0Signal[s0][h] = (_priorHiddenValues[h] * _party0SignalProbabilitiesGivenHidden[h][s0]) * inv;
                }
                else
                {
                    double uniform = 1.0 / _hiddenValueCount;
                    for (int h = 0; h < _hiddenValueCount; h++)
                        _posteriorHiddenGivenParty0Signal[s0][h] = uniform;
                }

                for (int s1 = 0; s1 < _party1SignalCount; s1++)
                {
                    double conditional = 0.0;
                    for (int h = 0; h < _hiddenValueCount; h++)
                        conditional += _posteriorHiddenGivenParty0Signal[s0][h] * _party1SignalProbabilitiesGivenHidden[h][s1];

                    if (conditional < 0.0) conditional = 0.0;
                    _party1SignalProbabilitiesGivenParty0Signal[s0][s1] = conditional;
                }
                NormalizeInPlaceOrUniform(_party1SignalProbabilitiesGivenParty0Signal[s0]);

                for (int s1 = 0; s1 < _party1SignalCount; s1++)
                {
                    double pS1GivenS0 = _party1SignalProbabilitiesGivenParty0Signal[s0][s1];
                    if (pS1GivenS0 > 0.0)
                    {
                        double inv = 1.0 / pS1GivenS0;
                        for (int h = 0; h < _hiddenValueCount; h++)
                            _posteriorHiddenGivenParty0AndParty1Signals[s0][s1][h] = (_posteriorHiddenGivenParty0Signal[s0][h] * _party1SignalProbabilitiesGivenHidden[h][s1]) * inv;
                    }
                    else
                    {
                        double uniform = 1.0 / _hiddenValueCount;
                        for (int h = 0; h < _hiddenValueCount; h++)
                            _posteriorHiddenGivenParty0AndParty1Signals[s0][s1][h] = uniform;
                    }

                    for (int s2 = 0; s2 < _party2SignalCount; s2++)
                    {
                        double pS2GivenS0S1 = 0.0;
                        for (int h = 0; h < _hiddenValueCount; h++)
                            pS2GivenS0S1 += _posteriorHiddenGivenParty0AndParty1Signals[s0][s1][h] * _party2SignalProbabilitiesGivenHidden[h][s2];

                        if (pS2GivenS0S1 < 0.0) pS2GivenS0S1 = 0.0;
                        _party2SignalProbabilitiesGivenParty0AndParty1Signals[s0][s1][s2] = pS2GivenS0S1;
                    }
                    NormalizeInPlaceOrUniform(_party2SignalProbabilitiesGivenParty0AndParty1Signals[s0][s1]);
                }
            }

            NormalizeInPlaceOrUniform(_party0SignalProbabilitiesUnconditional);
        }

        private static void RequireConsistentSignalCount(double[][] table, int expectedRowCount, out int signalCount, string paramName)
        {
            if (table.Length != expectedRowCount)
                throw new ArgumentException("Conditional table must have one row per hidden value.", paramName);

            signalCount = -1;
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i] == null)
                    throw new ArgumentException("Conditional table contains a null row.", paramName);

                if (signalCount < 0)
                    signalCount = table[i].Length;
                else if (table[i].Length != signalCount)
                    throw new ArgumentException("Conditional table rows must have consistent length.", paramName);
            }

            if (signalCount <= 0)
                throw new ArgumentException("Signal count must be positive.", paramName);
        }

        private static double[][] DeepCloneAndNormalizeConditionals(double[][] source, int expectedSignalCount)
        {
            double[][] clone = new double[source.Length][];
            for (int i = 0; i < source.Length; i++)
            {
                double[] row = source[i];
                if (row.Length != expectedSignalCount)
                    throw new ArgumentException("Conditional table row length mismatch.", nameof(source));

                double[] rowClone = (double[])row.Clone();
                NormalizeInPlaceOrUniform(rowClone);
                clone[i] = rowClone;
            }
            return clone;
        }

        private static void NormalizeInPlaceOrUniform(double[] values)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("Array must be non-empty.", nameof(values));

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

        private static double[][] CreateJagged2D(int dim0, int dim1)
        {
            var a = new double[dim0][];
            for (int i = 0; i < dim0; i++)
                a[i] = new double[dim1];
            return a;
        }

        private static double[][][] CreateJagged3D(int dim0, int dim1, int dim2)
        {
            var a = new double[dim0][][];
            for (int i = 0; i < dim0; i++)
            {
                a[i] = new double[dim1][];
                for (int j = 0; j < dim1; j++)
                    a[i][j] = new double[dim2];
            }
            return a;
        }

        private static double[][] BuildConditionalSignalTableFromNoise(int hiddenValueCount, int signalCount, bool sourcePointsIncludeExtremes, double noiseStdev)
        {
            var parameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = hiddenValueCount,
                NumSignals = signalCount,
                StdevOfNormalDistribution = noiseStdev,
                SourcePointsIncludeExtremes = sourcePointsIncludeExtremes
            };

            var table = new double[hiddenValueCount][];

            if (noiseStdev == 0.0)
            {
                for (int h = 1; h <= hiddenValueCount; h++)
                {
                    double location = parameters.MapSourceTo0To1(h);
                    if (location < 0.0) location = 0.0;
                    if (location > 1.0) location = 1.0;

                    int zeroBasedSignalIndex = (int)Math.Floor(location * signalCount);
                    if (zeroBasedSignalIndex < 0) zeroBasedSignalIndex = 0;
                    if (zeroBasedSignalIndex >= signalCount) zeroBasedSignalIndex = signalCount - 1;

                    double[] row = new double[signalCount];
                    row[zeroBasedSignalIndex] = 1.0;
                    table[h - 1] = row;
                }
                return table;
            }

            for (int h = 1; h <= hiddenValueCount; h++)
                table[h - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h, parameters);

            return table;
        }
    }
}
