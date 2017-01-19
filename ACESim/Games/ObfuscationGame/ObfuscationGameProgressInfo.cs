using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ObfuscationGameProgressInfo : GameProgress
    {
        public double obfuscated = -1;
        public double strategyCalc;
        public double bruteForceCalc;
        public double stdev;
        public List<double> inputsToUse;

        public override GameProgress DeepCopy()
        {
            ObfuscationGameProgressInfo copy = new ObfuscationGameProgressInfo();

            copy.obfuscated = obfuscated;
            copy.strategyCalc = strategyCalc;
            copy.bruteForceCalc = bruteForceCalc;
            copy.stdev = stdev;
            copy.GameComplete = this.GameComplete;
            copy.inputsToUse = inputsToUse == null ? null : inputsToUse.ToList();
            base.CopyFieldInfo(copy);

            return copy;
        }
    }

    

        
}
