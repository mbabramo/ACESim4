using ACESim;
using ACESimBase.Util.Statistical;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    /// <summary>
    /// Stores baseline and additive models, each of which specifies regressions for all decisions in the game. 
    /// It then can create regression machines accessible by a map from decision index, where each regression
    /// machine is a CompoundRegressionMachine of the baseline and additive models for that decision. The weight
    /// on the baseline and additive models can be changed programmatically. 
    /// </summary>
    public class DeepCFRCompoundRegressionMachinesContainer
    {
        List<DeepCFRMultiModel> BaselineAndAdditiveModels;
        GameDefinition GameDefinition;
        byte NumNonChancePlayers;
        List<double>[] WeightOnSupplementalMachines;

        public DeepCFRCompoundRegressionMachinesContainer(List<DeepCFRMultiModel> baselineAndAdditiveModels, GameDefinition gameDefinition, byte numNonChancePlayers)
        {
            BaselineAndAdditiveModels = baselineAndAdditiveModels;
            GameDefinition = gameDefinition;
            NumNonChancePlayers = numNonChancePlayers;
            WeightOnSupplementalMachines = new List<double>[numNonChancePlayers];
            for (byte p = 0; p < NumNonChancePlayers; p++)
                WeightOnSupplementalMachines[p] = BaselineAndAdditiveModels.Skip(1).Select(x => (double)0).ToList();
        }

        public void SpecifyWeightOnSupplementalMachines(List<double>[] weightOnSupplementalMachines, double furtherMultiplier)
        {
            for (byte p = 0; p < weightOnSupplementalMachines.Length; p++)
                SpecifyWeightOnSupplementalMachines(p, weightOnSupplementalMachines[p].Select(x => x * furtherMultiplier).ToList());
        }

        public void SpecifyWeightOnSupplementalMachines(byte playerIndex, List<double> weightOnSupplementalMachines)
        {
            WeightOnSupplementalMachines[playerIndex] = weightOnSupplementalMachines;
        }

        public Dictionary<byte, IRegressionMachine> GetRegressionMachinesForLocalUse()
        {
            DeepCFRMultiModel baselineModel = BaselineAndAdditiveModels.First();
            List<DeepCFRMultiModel> additionalModels = BaselineAndAdditiveModels.Skip(1).ToList();
            Dictionary<byte, IRegressionMachine> CompoundRegressionMachines = new Dictionary<byte, IRegressionMachine>();
            foreach (var entry in baselineModel.GetRegressionMachinesForLocalUse())
            {
                byte decisionIndex = entry.Key;
                byte playerIndexForEntry = GameDefinition.DecisionsExecutionOrder[decisionIndex].PlayerIndex;
                List<IRegressionMachine> regressionMachinesToCompoundForDecision = new List<IRegressionMachine>() { };
                foreach (DeepCFRMultiModel additionalModel in additionalModels)
                {
                    regressionMachinesToCompoundForDecision.Add(additionalModel.GetRegressionMachineForDecision(decisionIndex));
                }
                CompoundRegressionMachine compoundRegressionMachine = new CompoundRegressionMachine(entry.Value, regressionMachinesToCompoundForDecision);
                compoundRegressionMachine.SpecifyWeightOnSupplementalMachines(WeightOnSupplementalMachines[playerIndexForEntry]);
                CompoundRegressionMachines[decisionIndex] = compoundRegressionMachine;
            }
            return CompoundRegressionMachines;
        }

        public void ReturnRegressionMachines(Dictionary<byte, IRegressionMachine> regressionMachines)
        {
            var entries = regressionMachines.ToList();
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
