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
        public double? StdevOfNormalDistributionForCutoffPoints;
        public int NumSignals;
        public bool UseEndpoints;

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
                hash = (hash * 23) + UseEndpoints.GetHashCode();
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            DiscreteValueSignalParameters other = (DiscreteValueSignalParameters)obj;
            return NumPointsInSourceUniformDistribution == other.NumPointsInSourceUniformDistribution && StdevOfNormalDistribution == other.StdevOfNormalDistribution && NumSignals == other.NumSignals && UseEndpoints == other.UseEndpoints && StdevOfNormalDistributionForCutoffPoints == other.StdevOfNormalDistributionForCutoffPoints;
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
