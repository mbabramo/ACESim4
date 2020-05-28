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
        public List<double>[] PerPlayerWeightOnSupplementalMachines; Debug; // separate for each player?

        public CompoundRegressionMachine(IRegressionMachine baselineMachine, List<IRegressionMachine> supplementalMachines, byte numberOfPlayers)
        {
            BaselineMachine = baselineMachine;
            SupplementalMachines = supplementalMachines;
            PerPlayerWeightOnSupplementalMachines = new List<double>[numberOfPlayers];
            for (byte p = 0; p < numberOfPlayers; p++)
            {
                List<double> weightOnMachines = supplementalMachines.Select(x => (double)0).ToList();
                PerPlayerWeightOnSupplementalMachines[p] = weightOnMachines;
            }
        }

        public void SpecifyWeightOnSupplementalMachines(byte playerIndex, List<double> weightOnSupplementalMachines)
        {
            PerPlayerWeightOnSupplementalMachines[playerIndex] = weightOnSupplementalMachines;
        }

        public float[] GetResults(float[] x, object supplementalInfo)
        {
            byte playerIndex = (byte)(byte?)supplementalInfo;
            float[] baselineResults = BaselineMachine.GetResults(x);
            float[] outcome = baselineResults.ToArray();
            for (int i = 0; i < SupplementalMachines.Count(); i++)
            {
                double weight = PerPlayerWeightOnSupplementalMachines[playerIndex][i];
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

        public float[] GetResults(float[] x) => throw new NotImplementedException("Must know player number to GetResults on CompoundRegressionMachine");
    }
}
