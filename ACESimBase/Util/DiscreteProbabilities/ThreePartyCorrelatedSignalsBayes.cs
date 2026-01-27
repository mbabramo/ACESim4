using ACESim.Util.DiscreteProbabilities;
using System;

namespace ACESimBase.Util.DiscreteProbabilities
{
    [Serializable]
    public sealed class ThreePartyCorrelatedSignalsBayes
    {
        private readonly int _hiddenValueCount;
        private readonly int _party0SignalCount;
        private readonly int _party1SignalCount;
        private readonly int _party2SignalCount;

        private readonly double[] _priorHiddenValues; // [h] sums to 1
        private readonly double[][] _party0SignalProbabilitiesGivenHidden; // [h][s0]
        private readonly double[][] _party1SignalProbabilitiesGivenHidden; // [h][s1]
        private readonly double[][] _party2SignalProbabilitiesGivenHidden; // [h][s2]

        private readonly double[] _party0SignalProbabilitiesUnconditional; // [s0]
        private readonly double[][] _party1SignalProbabilitiesGivenParty0Signal; // [s0][s1]
        private readonly double[][][] _party2SignalProbabilitiesGivenParty0AndParty1Signals; // [s0][s1][s2]

        private readonly double[][] _posteriorHiddenGivenParty0Signal; // [s0][h]
        private readonly double[][][] _posteriorHiddenGivenParty0AndParty1Signals; // [s0][s1][h]

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
            if (priorHiddenValues == null) throw new ArgumentNullException(nameof(priorHiddenValues));
            if (party0SignalProbabilitiesGivenHidden == null) throw new ArgumentNullException(nameof(party0SignalProbabilitiesGivenHidden));
            if (party1SignalProbabilitiesGivenHidden == null) throw new ArgumentNullException(nameof(party1SignalProbabilitiesGivenHidden));
            if (party2SignalProbabilitiesGivenHidden == null) throw new ArgumentNullException(nameof(party2SignalProbabilitiesGivenHidden));

            _hiddenValueCount = priorHiddenValues.Length;
            if (_hiddenValueCount <= 0) throw new ArgumentOutOfRangeException(nameof(priorHiddenValues));

            if (party0SignalProbabilitiesGivenHidden.Length != _hiddenValueCount) throw new ArgumentException("Hidden dimension mismatch.", nameof(party0SignalProbabilitiesGivenHidden));
            if (party1SignalProbabilitiesGivenHidden.Length != _hiddenValueCount) throw new ArgumentException("Hidden dimension mismatch.", nameof(party1SignalProbabilitiesGivenHidden));
            if (party2SignalProbabilitiesGivenHidden.Length != _hiddenValueCount) throw new ArgumentException("Hidden dimension mismatch.", nameof(party2SignalProbabilitiesGivenHidden));

            _party0SignalCount = RequireConsistentSignalCount(party0SignalProbabilitiesGivenHidden, nameof(party0SignalProbabilitiesGivenHidden));
            _party1SignalCount = RequireConsistentSignalCount(party1SignalProbabilitiesGivenHidden, nameof(party1SignalProbabilitiesGivenHidden));
            _party2SignalCount = RequireConsistentSignalCount(party2SignalProbabilitiesGivenHidden, nameof(party2SignalProbabilitiesGivenHidden));

            _priorHiddenValues = (double[])priorHiddenValues.Clone();
            NormalizeInPlaceOrUniform(_priorHiddenValues);

            _party0SignalProbabilitiesGivenHidden = DeepCloneAndNormalizeConditionals(party0SignalProbabilitiesGivenHidden);
            _party1SignalProbabilitiesGivenHidden = DeepCloneAndNormalizeConditionals(party1SignalProbabilitiesGivenHidden);
            _party2SignalProbabilitiesGivenHidden = DeepCloneAndNormalizeConditionals(party2SignalProbabilitiesGivenHidden);

            _party0SignalProbabilitiesUnconditional = new double[_party0SignalCount];
            _posteriorHiddenGivenParty0Signal = CreateJagged2D(_party0SignalCount, _hiddenValueCount);

            _party1SignalProbabilitiesGivenParty0Signal = CreateJagged2D(_party0SignalCount, _party1SignalCount);
            _posteriorHiddenGivenParty0AndParty1Signals = CreateJagged3D(_party0SignalCount, _party1SignalCount, _hiddenValueCount);

            _party2SignalProbabilitiesGivenParty0AndParty1Signals = CreateJagged3D(_party0SignalCount, _party1SignalCount, _party2SignalCount);

            Precompute();
        }

        public static ThreePartyCorrelatedSignalsBayes CreateUsingDiscreteValueSignalParameters(
            double[] priorHiddenValues,
            int party0SignalCount,
            double party0NoiseStdev,
            int party1SignalCount,
            double party1NoiseStdev,
            int party2SignalCount,
            double party2NoiseStdev,
            bool sourcePointsIncludeExtremes = true)
        {
            if (priorHiddenValues == null) throw new ArgumentNullException(nameof(priorHiddenValues));
            if (party0SignalCount <= 0) throw new ArgumentOutOfRangeException(nameof(party0SignalCount));
            if (party1SignalCount <= 0) throw new ArgumentOutOfRangeException(nameof(party1SignalCount));
            if (party2SignalCount <= 0) throw new ArgumentOutOfRangeException(nameof(party2SignalCount));

            int hiddenValueCount = priorHiddenValues.Length;
            if (hiddenValueCount <= 0) throw new ArgumentOutOfRangeException(nameof(priorHiddenValues));

            var party0 = BuildConditionalSignalTableFromNoise(hiddenValueCount, party0SignalCount, party0NoiseStdev, sourcePointsIncludeExtremes);
            var party1 = BuildConditionalSignalTableFromNoise(hiddenValueCount, party1SignalCount, party1NoiseStdev, sourcePointsIncludeExtremes);
            var party2 = BuildConditionalSignalTableFromNoise(hiddenValueCount, party2SignalCount, party2NoiseStdev, sourcePointsIncludeExtremes);

            return new ThreePartyCorrelatedSignalsBayes(priorHiddenValues, party0, party1, party2);
        }

        public double[] GetParty0SignalProbabilitiesUnconditional()
        {
            return _party0SignalProbabilitiesUnconditional;
        }


        public double[] GetParty1SignalProbabilitiesUnconditional()
        {
            var result = new double[_party1SignalCount];
            for (int s1 = 0; s1 < _party1SignalCount; s1++)
            {
                double unconditional = 0.0;
                for (int h = 0; h < _hiddenValueCount; h++)
                    unconditional += _priorHiddenValues[h] * _party1SignalProbabilitiesGivenHidden[h][s1];
                result[s1] = Math.Max(0.0, unconditional);
            }

            NormalizeInPlaceOrUniform(result);
            return result;
        }


        public double[] GetParty2SignalProbabilitiesUnconditional()
        {
            var result = new double[_party2SignalCount];
            for (int s2 = 0; s2 < _party2SignalCount; s2++)
            {
                double unconditional = 0.0;
                for (int h = 0; h < _hiddenValueCount; h++)
                    unconditional += _priorHiddenValues[h] * _party2SignalProbabilitiesGivenHidden[h][s2];
                result[s2] = Math.Max(0.0, unconditional);
            }

            NormalizeInPlaceOrUniform(result);
            return result;
        }


        public double[] GetParty1SignalProbabilitiesGivenParty0Signal(byte party0Signal)
        {
            int s0 = party0Signal - 1;
            if ((uint)s0 >= (uint)_party0SignalCount) throw new ArgumentOutOfRangeException(nameof(party0Signal));
            return _party1SignalProbabilitiesGivenParty0Signal[s0];
        }

        public double[] GetParty2SignalProbabilitiesGivenParty0AndParty1Signals(byte party0Signal, byte party1Signal)
        {
            int s0 = party0Signal - 1;
            int s1 = party1Signal - 1;
            if ((uint)s0 >= (uint)_party0SignalCount) throw new ArgumentOutOfRangeException(nameof(party0Signal));
            if ((uint)s1 >= (uint)_party1SignalCount) throw new ArgumentOutOfRangeException(nameof(party1Signal));
            return _party2SignalProbabilitiesGivenParty0AndParty1Signals[s0][s1];
        }

        public double[] GetPosteriorHiddenProbabilitiesGivenSignals(byte party0Signal, byte party1Signal, byte? party2Signal)
        {
            int s0 = party0Signal - 1;
            int s1 = party1Signal - 1;
            if ((uint)s0 >= (uint)_party0SignalCount) throw new ArgumentOutOfRangeException(nameof(party0Signal));
            if ((uint)s1 >= (uint)_party1SignalCount) throw new ArgumentOutOfRangeException(nameof(party1Signal));

            if (party2Signal is null)
                return _posteriorHiddenGivenParty0AndParty1Signals[s0][s1];

            int s2 = ((byte)party2Signal) - 1;
            if ((uint)s2 >= (uint)_party2SignalCount) throw new ArgumentOutOfRangeException(nameof(party2Signal));

            double[] posterior = new double[_hiddenValueCount];
            double sum = 0.0;

            double[] posterior01 = _posteriorHiddenGivenParty0AndParty1Signals[s0][s1];
            for (int h = 0; h < _hiddenValueCount; h++)
            {
                double v = posterior01[h] * _party2SignalProbabilitiesGivenHidden[h][s2];
                if (v < 0) v = 0;
                posterior[h] = v;
                sum += v;
            }

            if (!(sum > 0.0) || double.IsNaN(sum) || double.IsInfinity(sum))
            {
                double uniform = 1.0 / _hiddenValueCount;
                for (int h = 0; h < _hiddenValueCount; h++)
                    posterior[h] = uniform;
                return posterior;
            }

            double inv = 1.0 / sum;
            for (int h = 0; h < _hiddenValueCount; h++)
                posterior[h] *= inv;

            return posterior;
        }

        private void Precompute()
        {
            for (int s0 = 0; s0 < _party0SignalCount; s0++)
            {
                double unconditional = 0.0;
                for (int h = 0; h < _hiddenValueCount; h++)
                    unconditional += _priorHiddenValues[h] * _party0SignalProbabilitiesGivenHidden[h][s0];
                _party0SignalProbabilitiesUnconditional[s0] = Math.Max(0.0, unconditional);
            }
            NormalizeInPlaceOrUniform(_party0SignalProbabilitiesUnconditional);

            for (int s0 = 0; s0 < _party0SignalCount; s0++)
            {
                double sum = 0.0;
                for (int h = 0; h < _hiddenValueCount; h++)
                {
                    double v = _priorHiddenValues[h] * _party0SignalProbabilitiesGivenHidden[h][s0];
                    if (v < 0) v = 0;
                    _posteriorHiddenGivenParty0Signal[s0][h] = v;
                    sum += v;
                }
                if (!(sum > 0.0) || double.IsNaN(sum) || double.IsInfinity(sum))
                {
                    double uniform = 1.0 / _hiddenValueCount;
                    for (int h = 0; h < _hiddenValueCount; h++)
                        _posteriorHiddenGivenParty0Signal[s0][h] = uniform;
                }
                else
                {
                    double inv = 1.0 / sum;
                    for (int h = 0; h < _hiddenValueCount; h++)
                        _posteriorHiddenGivenParty0Signal[s0][h] *= inv;
                }
            }

            for (int s0 = 0; s0 < _party0SignalCount; s0++)
            {
                double[] post0 = _posteriorHiddenGivenParty0Signal[s0];

                for (int s1 = 0; s1 < _party1SignalCount; s1++)
                {
                    double sum01 = 0.0;
                    for (int h = 0; h < _hiddenValueCount; h++)
                    {
                        double v01 = _priorHiddenValues[h] * _party0SignalProbabilitiesGivenHidden[h][s0] * _party1SignalProbabilitiesGivenHidden[h][s1];
                        if (v01 < 0) v01 = 0;
                        _posteriorHiddenGivenParty0AndParty1Signals[s0][s1][h] = v01;
                        sum01 += v01;
                    }

                    if (!(sum01 > 0.0) || double.IsNaN(sum01) || double.IsInfinity(sum01))
                    {
                        double uniform = 1.0 / _hiddenValueCount;
                        for (int h = 0; h < _hiddenValueCount; h++)
                            _posteriorHiddenGivenParty0AndParty1Signals[s0][s1][h] = uniform;
                    }
                    else
                    {
                        double inv01 = 1.0 / sum01;
                        for (int h = 0; h < _hiddenValueCount; h++)
                            _posteriorHiddenGivenParty0AndParty1Signals[s0][s1][h] *= inv01;
                    }
                }

                for (int s1 = 0; s1 < _party1SignalCount; s1++)
                {
                    double[] post01 = _posteriorHiddenGivenParty0AndParty1Signals[s0][s1];

                    double sumS1GivenS0 = 0.0;
                    double[] s1GivenS0 = _party1SignalProbabilitiesGivenParty0Signal[s0];

                    for (int s1Alt = 0; s1Alt < _party1SignalCount; s1Alt++)
                    {
                        double v = 0.0;
                        for (int h = 0; h < _hiddenValueCount; h++)
                            v += post0[h] * _party1SignalProbabilitiesGivenHidden[h][s1Alt];
                        v = Math.Max(0.0, v);
                        s1GivenS0[s1Alt] = v;
                        sumS1GivenS0 += v;
                    }
                    if (!(sumS1GivenS0 > 0.0) || double.IsNaN(sumS1GivenS0) || double.IsInfinity(sumS1GivenS0))
                    {
                        double uniform = 1.0 / _party1SignalCount;
                        for (int s1Alt = 0; s1Alt < _party1SignalCount; s1Alt++)
                            s1GivenS0[s1Alt] = uniform;
                    }
                    else
                    {
                        double inv = 1.0 / sumS1GivenS0;
                        for (int s1Alt = 0; s1Alt < _party1SignalCount; s1Alt++)
                            s1GivenS0[s1Alt] *= inv;
                    }

                    double[] s2GivenS0S1 = _party2SignalProbabilitiesGivenParty0AndParty1Signals[s0][s1];
                    double sumS2 = 0.0;
                    for (int s2 = 0; s2 < _party2SignalCount; s2++)
                    {
                        double v = 0.0;
                        for (int h = 0; h < _hiddenValueCount; h++)
                            v += post01[h] * _party2SignalProbabilitiesGivenHidden[h][s2];
                        v = Math.Max(0.0, v);
                        s2GivenS0S1[s2] = v;
                        sumS2 += v;
                    }
                    if (!(sumS2 > 0.0) || double.IsNaN(sumS2) || double.IsInfinity(sumS2))
                    {
                        double uniform = 1.0 / _party2SignalCount;
                        for (int s2 = 0; s2 < _party2SignalCount; s2++)
                            s2GivenS0S1[s2] = uniform;
                    }
                    else
                    {
                        double inv = 1.0 / sumS2;
                        for (int s2 = 0; s2 < _party2SignalCount; s2++)
                            s2GivenS0S1[s2] *= inv;
                    }
                }
            }
        }

        private static int RequireConsistentSignalCount(double[][] table, string paramName)
        {
            if (table.Length == 0) throw new ArgumentException("Empty table.", paramName);
            int signalCount = table[0]?.Length ?? 0;
            if (signalCount <= 0) throw new ArgumentException("Signal dimension must be positive.", paramName);

            for (int i = 0; i < table.Length; i++)
            {
                if (table[i] == null) throw new ArgumentException("Null row.", paramName);
                if (table[i].Length != signalCount) throw new ArgumentException("Inconsistent signal dimension.", paramName);
            }

            return signalCount;
        }

        private static double[][] DeepCloneAndNormalizeConditionals(double[][] table)
        {
            int hiddenCount = table.Length;
            int signalCount = table[0].Length;
            var result = new double[hiddenCount][];

            for (int h = 0; h < hiddenCount; h++)
            {
                double[] row = (double[])table[h].Clone();
                for (int s = 0; s < signalCount; s++)
                {
                    double v = row[s];
                    if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;
                    if (v < 0.0) v = 0.0;
                    row[s] = v;
                }
                NormalizeInPlaceOrUniform(row);
                result[h] = row;
            }

            return result;
        }

        private static void NormalizeInPlaceOrUniform(double[] values)
        {
            double sum = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                double v = values[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;
                if (v < 0.0) v = 0.0;
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
                table[h - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h, parameters);

            return table;
        }

    }
}
