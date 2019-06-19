using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim.Util
{
    [Serializable]
    public struct DiscreteValueLiabilitySignalParameters
    {
        public int NumPointsInSourceUniformDistribution;
        public double StdevOfNormalDistribution;
        public int NumLiabilitySignals;
        public bool UseEndpoints;

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 13;
                hash = (hash * 7) + NumPointsInSourceUniformDistribution.GetHashCode();
                hash = (hash * 7) + StdevOfNormalDistribution.GetHashCode();
                hash = (hash * 7) + NumLiabilitySignals.GetHashCode();
                hash = (hash * 7) + UseEndpoints.GetHashCode();
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            DiscreteValueLiabilitySignalParameters other = (DiscreteValueLiabilitySignalParameters)obj;
            return NumPointsInSourceUniformDistribution == other.NumPointsInSourceUniformDistribution && StdevOfNormalDistribution == other.StdevOfNormalDistribution && NumLiabilitySignals == other.NumLiabilitySignals && UseEndpoints == other.UseEndpoints;
        }
    }
}
