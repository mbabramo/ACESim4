using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase
{
    public interface IRegression
    {
        Task Regress((float[] X, float[] Y, float W)[] data);
        string GetTrainingResultString();
        public IRegressionMachine GetRegressionMachine() => null;
        public void ReturnRegressionMachine(IRegressionMachine regressionMachine) { }
        float[] GetResults(float[] x, IRegressionMachine regressionMachine);
    }

    /// <summary>
    /// The regression machine is code that is capable of running the regression calculated by an IRegression. This code need not be thread-safe. Thus, if an IRegression has an expensive process for creating an object that can only be used on a single thread at once, that object can be stored in an IRegressionMachine. The consumer of the IRegression can then create the regression machine and take care to use it only on a single thread at once.
    /// </summary>
    public interface IRegressionMachine
    {
        float[] GetResults(float[] x);
    }
}
