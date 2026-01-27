using ACESimBase.Util.Statistical;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim.Util.DiscreteProbabilities
{
    public enum DiscreteSignalBoundaryMode
    {
        EqualWidth = 0,
    }

    public static class DiscreteSignalBoundaries
    {
        public static (double bottomOfRange, double topOfRange) MapSignalToRangeIn0To1(
            int signalValue,
            int numSignals,
            DiscreteSignalBoundaryMode mode)
        {
            if (numSignals <= 0) throw new ArgumentOutOfRangeException(nameof(numSignals));
            if (signalValue < 1 || signalValue > numSignals) throw new ArgumentOutOfRangeException(nameof(signalValue));

            switch (mode)
            {
                case DiscreteSignalBoundaryMode.EqualWidth:
                {
                    double bottom = (signalValue - 1) / (double)numSignals;
                    double top = signalValue / (double)numSignals;
                    return (bottom, top);
                }
                default:
                    throw new NotSupportedException($"Unsupported {nameof(DiscreteSignalBoundaryMode)}: {mode}");
            }
        }

        public static int MapLocationIn0To1ToZeroBasedSignalIndex(
            double location,
            int numSignals,
            DiscreteSignalBoundaryMode mode)
        {
            if (numSignals <= 0) throw new ArgumentOutOfRangeException(nameof(numSignals));
            if (double.IsNaN(location) || double.IsInfinity(location)) location = 0.0;
            if (location < 0.0) location = 0.0;
            if (location > 1.0) location = 1.0;

            switch (mode)
            {
                case DiscreteSignalBoundaryMode.EqualWidth:
                {
                    if (location >= 1.0) return numSignals - 1;
                    int index = (int)Math.Floor(location * numSignals);
                    if (index < 0) index = 0;
                    if (index >= numSignals) index = numSignals - 1;
                    return index;
                }
                default:
                    throw new NotSupportedException($"Unsupported {nameof(DiscreteSignalBoundaryMode)}: {mode}");
            }
        }
    }

    [Serializable]
    public struct DiscreteValueSignalParameters
    {
        public int NumPointsInSourceUniformDistribution;
        public double StdevOfNormalDistribution;
        public int NumSignals;
        public bool SourcePointsIncludeExtremes;

        public DiscreteSignalBoundaryMode SignalBoundaryMode;

        public double MapSourceTo0To1(int sourceValue)
        {
            return EquallySpaced.GetLocationOfEquallySpacedPoint(sourceValue - 1, NumPointsInSourceUniformDistribution, SourcePointsIncludeExtremes);
        }

        public (double bottomOfRange, double topOfRange) MapSignalToRangeIn0To1(int signalValue)
        {
            return DiscreteSignalBoundaries.MapSignalToRangeIn0To1(signalValue, NumSignals, SignalBoundaryMode);
        }

        private double MapTo0To1(bool sourceDistribution, int value)
        {
            return EquallySpaced.GetLocationOfEquallySpacedPoint(value, sourceDistribution ? NumPointsInSourceUniformDistribution : NumSignals, sourceDistribution && SourcePointsIncludeExtremes);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 13;
                hash = (hash * 7) + NumPointsInSourceUniformDistribution.GetHashCode();
                hash = (hash * 11) + StdevOfNormalDistribution.GetHashCode();
                hash = (hash * 17) + NumSignals.GetHashCode();
                hash = (hash * 23) + SourcePointsIncludeExtremes.GetHashCode();
                hash = (hash * 29) + SignalBoundaryMode.GetHashCode();
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            if (!(obj is DiscreteValueSignalParameters other))
                return false;

            return NumPointsInSourceUniformDistribution == other.NumPointsInSourceUniformDistribution
                   && StdevOfNormalDistribution == other.StdevOfNormalDistribution
                   && NumSignals == other.NumSignals
                   && SourcePointsIncludeExtremes == other.SourcePointsIncludeExtremes
                   && SignalBoundaryMode == other.SignalBoundaryMode;
        }

        public static bool operator ==(DiscreteValueSignalParameters left, DiscreteValueSignalParameters right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(DiscreteValueSignalParameters left, DiscreteValueSignalParameters right)
        {
            return !(left == right);
        }
    }
}
