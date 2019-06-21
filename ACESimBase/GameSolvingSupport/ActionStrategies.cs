using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum ActionStrategies
    {
        AverageStrategy,
        CurrentProbability, // whatever we calculated post-iteration; could be something like regret matching, but it doesn't have to be
        BestResponse,
        RegretMatching,
        RegretMatchingWithPruning,
        CorrelatedEquilibrium,
        BestResponseVsCorrelatedEquilibrium,
        CorrelatedEquilibriumVsBestResponse
    }
}
