using System.Collections.Generic;

namespace ACESimBase.GameSolvingSupport
{
    public readonly struct DeepCFRPlaybackHelper
    {
        public readonly Dictionary<byte, IRegressionMachine> regressionMachines;
        public readonly DeepCFRProbabilitiesCache probabilitiesCache;

        public DeepCFRPlaybackHelper(Dictionary<byte, IRegressionMachine> regressionMachines, DeepCFRProbabilitiesCache probabilitiesCache)
        {
            this.regressionMachines = regressionMachines;
            this.probabilitiesCache = probabilitiesCache;
        }
    }
}
