using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum ActionStrategies
    {
        RegretMatching,
        AverageStrategy,
        BestResponse,
        RegretMatchingWithPruning,
        NormalizedHedge,
        Hedge,
        CorrelatedEquilibrium,
        BestResponseVsCorrelatedEquilibrium,
        CorrelatedEquilibriumVsBestResponse
    }
}
