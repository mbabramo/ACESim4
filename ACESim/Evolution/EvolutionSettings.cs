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
        public int MaxParallelDepth;
        public int NumIterationsPerPhase;
        public int NumPhases;

        public EvolutionSettings DeepCopy()
        {
            return new EvolutionSettings() {
                ParallelOptimization = ParallelOptimization,
                MaxParallelDepth = MaxParallelDepth,
                NumIterationsPerPhase = NumIterationsPerPhase,
                NumPhases = NumPhases
            };
        }

        public void CopyFrom(EvolutionSettings copy)
        {
            this.ParallelOptimization = copy.ParallelOptimization;
            this.MaxParallelDepth = copy.MaxParallelDepth;
            this.NumIterationsPerPhase = copy.NumIterationsPerPhase;
            this.NumPhases = copy.NumPhases;
        }
    }
}
