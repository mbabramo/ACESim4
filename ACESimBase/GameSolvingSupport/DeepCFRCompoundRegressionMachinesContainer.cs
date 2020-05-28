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

        public DeepCFRCompoundRegressionMachinesContainer(List<DeepCFRMultiModel> baselineAndAdditiveModels, byte numberOfPlayers)
        {
            BaselineAndAdditiveModels = baselineAndAdditiveModels;
            DeepCFRMultiModel baselineModel = baselineAndAdditiveModels.First();
            List<DeepCFRMultiModel> additionalModels = baselineAndAdditiveModels.Skip(1).ToList();
            CompoundRegressionMachines = new Dictionary<byte, IRegressionMachine>();
            foreach (var entry in baselineModel.GetRegressionMachinesForLocalUse())
            {
                List<IRegressionMachine> regressionMachinesToCompoundForDecision = new List<IRegressionMachine>() { };
                byte decisionIndex = entry.Key;
                foreach (DeepCFRMultiModel additionalModel in additionalModels)
                {
                    regressionMachinesToCompoundForDecision.Add(additionalModel.GetRegressionMachineForDecision(decisionIndex));
                }
                CompoundRegressionMachine compoundRegressionMachine = new CompoundRegressionMachine(entry.Value, regressionMachinesToCompoundForDecision, numberOfPlayers);
                CompoundRegressionMachines[decisionIndex] = compoundRegressionMachine;
            }
        }

        public void SpecifyWeightOnSupplementalMachines(byte playerIndex, List<double> weightOnSupplementalMachines)
        {
            foreach (var entry in CompoundRegressionMachines)
            {
                CompoundRegressionMachine machine = (CompoundRegressionMachine)entry.Value;
                Debug;
            }
        }

        public Dictionary<byte, IRegressionMachine> GetRegressionMachinesForLocalUse() => CompoundRegressionMachines;

        public void ReturnRegressionMachines()
        {
            var entries = CompoundRegressionMachines.ToList();
            for (int i = 0; i < entries.Count(); i++)
            {
                byte decisionIndex = entries[i].Key;
                CompoundRegressionMachine compoundRegressionMachine = (CompoundRegressionMachine) entries[i].Value;
                for (int j = 0; j < BaselineAndAdditiveModels.Count(); j++)
                {
                    IRegressionMachine regressionMachine;
                    if (j == 0)
                        regressionMachine = compoundRegressionMachine.BaselineMachine;
                    else
                        regressionMachine = compoundRegressionMachine.SupplementalMachines[j - 1];
                    var model = BaselineAndAdditiveModels[j];
                    model.ReturnRegressionMachineForDecision(decisionIndex, regressionMachine);
                }
            }
        }
    }
}
