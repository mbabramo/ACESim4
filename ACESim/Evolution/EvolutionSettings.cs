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
        public int NumIterationsPerPhase;
        public int NumPhases;

        public EvolutionSettings DeepCopy()
        {
            return new EvolutionSettings() {
                ParallelOptimization = ParallelOptimization,
                NumIterationsPerPhase = NumIterationsPerPhase,
                NumPhases = NumPhases
            };
        }

        public void CopyFrom(EvolutionSettings copy)
        {
            this.ParallelOptimization = copy.ParallelOptimization;
        }
    }
}
