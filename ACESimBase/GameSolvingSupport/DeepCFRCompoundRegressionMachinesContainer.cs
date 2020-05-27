using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{

    public class DeepCFRCompoundRegressionMachinesContainer
    {
        Dictionary<byte, IRegressionMachine> CompoundRegressionMachines = null;
        List<DeepCFRMultiModel> BaselineAndAdditiveModels;
        List<double> Weights;

        public DeepCFRCompoundRegressionMachinesContainer(List<DeepCFRMultiModel> baselineAndAdditiveModels, List<double> weights)
        {
            BaselineAndAdditiveModels = baselineAndAdditiveModels;
            Weights = weights;
            DeepCFRMultiModel baselineModel = baselineAndAdditiveModels.First();
            List<DeepCFRMultiModel> additionalModels = baselineAndAdditiveModels.Skip(1).ToList();
            CompoundRegressionMachines = new Dictionary<byte, IRegressionMachine>();
            foreach (var entry in baselineModel.GetRegressionMachinesForLocalUse())
            {
                List<IRegressionMachine> regressionMachinesToCompoundForDecision = new List<IRegressionMachine>() { };
                foreach (DeepCFRMultiModel additionalModel in additionalModels)
                    regressionMachinesToCompoundForDecision.Add(additionalModel.GetParticularRegressionMachineForLocalUse(entry.Key));
                CompoundRegressionMachine compoundRegressionMachine = new CompoundRegressionMachine(entry.Value, regressionMachinesToCompoundForDecision, weights);
                CompoundRegressionMachines[entry.Key] = compoundRegressionMachine;
            }
        }

        public void ReturnRegressionMachines()
        {
            int i = 0;
            foreach (var entry in CompoundRegressionMachines)
            {
                Debug;
                var compoundRegressionMachine = (CompoundRegressionMachine)entry.Value;
            }
        }
    }
}
