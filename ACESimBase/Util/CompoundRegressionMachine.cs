using System.Collections.Generic;
using System.Linq;

namespace ACESimBase
{
    public class CompoundRegressionMachine : IRegressionMachine
    {
        public IRegressionMachine BaselineMachine;
        public List<IRegressionMachine> SupplementalMachines;
        public List<double> WeightOnSupplementalMachines;

        public CompoundRegressionMachine(IRegressionMachine baselineMachine, List<IRegressionMachine> supplementalMachines, List<double> weightOnSupplementalMachines)
        {
            BaselineMachine = baselineMachine;
            SupplementalMachines = supplementalMachines;
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
                    float[] weighted = unweighted.Select(x => x * (float) weight).ToArray();
                    for (int j = 0; j < outcome.Length; j++)
                        outcome[j] += weighted[j];
                }
            }
            return outcome;
        }
    }
}
