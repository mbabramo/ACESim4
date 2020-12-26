using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim.Util
{
    [Serializable]
    public struct DiscreteValueSignalParameters
    {
        public int NumPointsInSourceUniformDistribution;
        public double StdevOfNormalDistribution;
        public double? StdevOfNormalDistributionForCutoffPoints; // DEBUG -- eliminate when eliminating DiscreteValueSignalOld
        public int NumSignals;
        public bool SourcePointsIncludeExtremes;

        public double MapSourceTo0To1(int sourceValue)
        {
            return EquallySpaced.GetLocationOfEquallySpacedPoint(sourceValue - 1, NumPointsInSourceUniformDistribution, SourcePointsIncludeExtremes);
        }

        public (double bottomOfRange, double topOfRange) MapSignalToRangeIn0To1(int signalValue)
        {
            double middleOfRange = EquallySpaced.GetLocationOfMidpoint(signalValue - 1, NumSignals);
            double widthOfRange = 1.0 / (double)NumSignals;
            double halfWidth = widthOfRange / 2.0;
            return (middleOfRange - halfWidth, middleOfRange + halfWidth);

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
                if (StdevOfNormalDistributionForCutoffPoints != null)
                    hash = (hash * 29) + StdevOfNormalDistributionForCutoffPoints.GetHashCode();
                hash = (hash * 17) + NumSignals.GetHashCode();
                hash = (hash * 23) + SourcePointsIncludeExtremes.GetHashCode();
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            DiscreteValueSignalParameters other = (DiscreteValueSignalParameters)obj;
            return NumPointsInSourceUniformDistribution == other.NumPointsInSourceUniformDistribution && StdevOfNormalDistribution == other.StdevOfNormalDistribution && NumSignals == other.NumSignals && SourcePointsIncludeExtremes == other.SourcePointsIncludeExtremes && StdevOfNormalDistributionForCutoffPoints == other.StdevOfNormalDistributionForCutoffPoints;
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
