using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase
{
    public interface IRegression
    {
        Task Regress((float[] X, float []Y)[] data);
        string GetTrainingResultString();
        float[] GetResults(float[] x);
    }
}
