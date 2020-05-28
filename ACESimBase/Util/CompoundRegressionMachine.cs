using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ACESimBase
{

    public class CompoundRegressionMachine : IRegressionMachine
    {
        public IRegressionMachine BaselineMachine;
        public List<IRegressionMachine> SupplementalMachines;
        public List<double> WeightOnSupplementalMachines;

        public CompoundRegressionMachine(IRegressionMachine baselineMachine, List<IRegressionMachine> supplementalMachines)
        {
            BaselineMachine = baselineMachine;
            SupplementalMachines = supplementalMachines;
            WeightOnSupplementalMachines = supplementalMachines.Select(x => (double)0).ToList();
        }

        public void SpecifyWeightOnSupplementalMachines(List<double> weightOnSupplementalMachines)
        {
            WeightOnSupplementalMachines = weightOnSupplementalMachines;
        }

        public float[] GetResults(float[] x)
        {
            float[] baselineResults = BaselineMachine.GetResults(x);
            float[] outcome = baselineResults.ToArray();
            for (int i = 0; i < SupplementalMachines.Count(); i++)
            {
                double weight = WeightOnSupplementalMachines[i];
                if (weight != 0)
                {
                    float[] unweighted = SupplementalMachines[i].GetResults(x);
                    float[] weighted = unweighted.Select(x => x * (float)weight).ToArray();
                    for (int j = 0; j < outcome.Length; j++)
                        outcome[j] += weighted[j];
                }
            }
            return outcome;
        }
    }
}
