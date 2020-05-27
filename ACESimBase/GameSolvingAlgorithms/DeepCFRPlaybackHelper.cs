using System.Collections.Generic;
using System.Diagnostics;

namespace ACESimBase.GameSolvingSupport
{
    public readonly struct DeepCFRPlaybackHelper
    {

        Debug; // 1. Use an interface for MultiModel, implementing ChooseAction, GetRegretMatchingProbabilities, and GetExpectedRegretsForAllActions. Then, create an implementation with a baseline model and several additional models. Or maybe just extend DeepCFRMultiModel so that it can support having a list of principal component models. But we also then need to figure out how to make it work with IRegressionMachines, perhaps by having a compound machine. This implementation will have a property specifying the principal component scores. Then, GetExpectedRegretsForAllActions will work accordingly. 

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
