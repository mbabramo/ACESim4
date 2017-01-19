using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class AggressivenessModuleInputs : GameModuleInputs
    {
        public bool MisestimateOpponentsAggressivenessWhenOptimizing;
        public double AssumedAggressivenessOfOpponent;
        public double WeightOnAggressivenessAssumption;
    }
}
