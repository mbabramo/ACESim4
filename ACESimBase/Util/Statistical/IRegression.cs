using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Statistical
{
    public interface IRegression
    {
        Task Regress((float[] X, float[] Y, float W)[] data);
        string GetTrainingResultString();
        public IRegressionMachine GetRegressionMachine() => null;
        public void ReturnRegressionMachine(IRegressionMachine regressionMachine) { }
        float[] GetResults(float[] x, IRegressionMachine regressionMachine);
    }
}
