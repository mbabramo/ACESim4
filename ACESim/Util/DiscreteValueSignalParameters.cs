using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim.Util
{

    public struct DiscreteValueSignalParameters
    {
        public int NumPointsInSourceUniformDistribution;
        public double StdevOfNormalDistribution;
        public int NumSignals;

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 13;
                hash = (hash * 7) + NumPointsInSourceUniformDistribution.GetHashCode();
                hash = (hash * 7) + StdevOfNormalDistribution.GetHashCode();
                hash = (hash * 7) + NumSignals.GetHashCode();
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            DiscreteValueSignalParameters other = (DiscreteValueSignalParameters)obj;
            return NumPointsInSourceUniformDistribution == other.NumPointsInSourceUniformDistribution && StdevOfNormalDistribution == other.StdevOfNormalDistribution && NumSignals == other.NumSignals;
        }
    }
}
