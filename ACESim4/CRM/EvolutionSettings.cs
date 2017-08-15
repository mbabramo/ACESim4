using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class EvolutionSettings
    {
        public bool ParallelOptimization = false;
        public int MaxParallelDepth = 1;
        public CRMAlgorithm Algorithm = CRMAlgorithm.Probing;
        public int TotalAvgStrategySamplingCFRIterations = 100000;
        public int TotalProbingCFRIterations = 100000;
        public int TotalVanillaCFRIterations = 100000;
        public int? ReportEveryNIterations = 10000;
        public const int EffectivelyNever = 999999999;
        public int? BestResponseEveryMIterations = EffectivelyNever; // For now, don't do it. This takes most of the time when dealing with partial recall games.

        public EvolutionSettings DeepCopy()
        {
            return new EvolutionSettings() {
                ParallelOptimization = ParallelOptimization,
                MaxParallelDepth = MaxParallelDepth,
                Algorithm = Algorithm,
                TotalAvgStrategySamplingCFRIterations = TotalAvgStrategySamplingCFRIterations,
                TotalProbingCFRIterations = TotalProbingCFRIterations,
                TotalVanillaCFRIterations = TotalVanillaCFRIterations,
                ReportEveryNIterations = ReportEveryNIterations,
                BestResponseEveryMIterations = BestResponseEveryMIterations
            };
        }
    }
}
