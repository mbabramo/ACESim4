using ACESimBase.Util.Statistical;
using System.Collections.Generic;
using System.Diagnostics;

namespace ACESimBase.GameSolvingSupport
{
    public readonly struct DeepCFRPlaybackHelper
    {

        public readonly DeepCFRMultiModel MultiModel;
        public readonly Dictionary<byte, IRegressionMachine> RegressionMachines;
        public readonly DeepCFRProbabilitiesCache ProbabilitiesCache;

        public DeepCFRPlaybackHelper(DeepCFRMultiModel multiModel, Dictionary<byte, IRegressionMachine> regressionMachines, DeepCFRProbabilitiesCache probabilitiesCache)
        {
            MultiModel = multiModel;
            RegressionMachines = regressionMachines;
            ProbabilitiesCache = probabilitiesCache;
        }

        public IRegressionMachine GetRegressionMachineIfExists(byte decisionIndex) => RegressionMachines?.GetValueOrDefault(decisionIndex);
    }
}
