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
        public double[] NonUniformWeightingOfPoints; // can be null if it's really a uniform distribution, but if it's non-uniform then we need to know what proportion for each
        public double StdevOfNormalDistribution;
        public int NumSignals;
        public bool UseEndpoints;

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 13;
                hash = (hash * 7) + NumPointsInSourceUniformDistribution.GetHashCode();
                hash = (hash * 11) + StdevOfNormalDistribution.GetHashCode();
                hash = (hash * 17) + NumSignals.GetHashCode();
                hash = (hash * 23) + UseEndpoints.GetHashCode();
                if (NonUniformWeightingOfPoints != null)
                {
                    hash = (hash * 29) + String.Join(",", NonUniformWeightingOfPoints).GetHashCode();
                }
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            DiscreteValueSignalParameters other = (DiscreteValueSignalParameters)obj;
            return NumPointsInSourceUniformDistribution == other.NumPointsInSourceUniformDistribution && StdevOfNormalDistribution == other.StdevOfNormalDistribution && NumSignals == other.NumSignals && UseEndpoints == other.UseEndpoints && ((NonUniformWeightingOfPoints == null && other.NonUniformWeightingOfPoints == null) || NonUniformWeightingOfPoints.SequenceEqual(other.NonUniformWeightingOfPoints));
        }
    }
}
