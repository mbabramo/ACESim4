using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class EvolutionSettings
    {
        public bool ParallelOptimization;
        public int StopParallelOptimizationAfterOptimizingNDecisions;
        public bool AverageInPreviousVersionsOfStrategy = false;
        public bool UseAlternatingAveraging = false;
        public int StartAveragingInPreviousVersionsOfStrategyOnStepN = 3;
        public bool DoNotEvolveByDefault;
        public int RepetitionsOfEntireSmoothingProcess; // this is useful if we are optimizing more than one decision, and at least one decision depends on the optimization of another
        public int NumberPointsInCumulativeDistribution;

        public SmoothingPointsMainSetQuantification SmoothingPointsMainSet;
        public SmoothingPointsValidationSetQuantification SmoothingPointsValidationSet;

        public SmoothingOptions SmoothingOptions;
        [OptionalSetting]
        public SmoothingOptions DecisionRepresentsCorrectAnswerSmoothingOptions;

        public EvolutionSettings DeepCopy()
        {
            return new EvolutionSettings() {
                RepetitionsOfEntireSmoothingProcess = RepetitionsOfEntireSmoothingProcess,
                ParallelOptimization = ParallelOptimization,
                DoNotEvolveByDefault = DoNotEvolveByDefault,
                NumberPointsInCumulativeDistribution = NumberPointsInCumulativeDistribution
            };
        }

        public void CopyFrom(EvolutionSettings copy)
        {
            this.RepetitionsOfEntireSmoothingProcess = copy.RepetitionsOfEntireSmoothingProcess;
            this.ParallelOptimization = copy.ParallelOptimization;
            this.DoNotEvolveByDefault = copy.DoNotEvolveByDefault;
            this.NumberPointsInCumulativeDistribution = NumberPointsInCumulativeDistribution;
        }
    }
}
