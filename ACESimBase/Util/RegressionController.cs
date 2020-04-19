using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util
{
    public class RegressionController
    {
        IRegression Regression;
        bool Normalize = true;
        (float MinX, float MaxX)[] Ranges;
        float?[] IndependentVariableConstant;
        int NumConstantIndependentVariables;
        float MinY, MaxY;

        public RegressionController()
        {

        }

        public RegressionController(Func<IRegression> regressionFactory)
        {
            Regression = regressionFactory();
        }

        /// <summary>
        /// Returns a regression controller without the accompanying regression. This can be useful as a lightweight object to get results, 
        /// if the regression machine is passed in separately.
        /// </summary>
        /// <returns></returns>
        public RegressionController DeepCopyExceptRegressionItself()
        {
            return new RegressionController()
            {
                Regression = null,
                Normalize = Normalize,
                Ranges = Ranges.ToArray(),
                IndependentVariableConstant = IndependentVariableConstant?.ToArray(),
                NumConstantIndependentVariables = NumConstantIndependentVariables,
                MinY = MinY,
                MaxY = MaxY
            };
        }

        public async Task Regress((float[] X, float Y)[] data)
        {
            (float[] X, float[] Y)[] data2 = data.Select(d => (d.X, new float[] { d.Y })).ToArray();
            if (Normalize)
            {
                int numItems = data2.Count();
                int lengthX = data2.First().Item1.Length;
                Ranges = new (float MinX, float MaxX)[lengthX];
                IndependentVariableConstant = new float?[lengthX];
                for (int xIndex = 0; xIndex < lengthX; xIndex++)
                {
                    Ranges[xIndex].MinX = data2.Min(d => d.X[xIndex]);
                    Ranges[xIndex].MaxX = data2.Max(d => d.X[xIndex]);
                    if (Ranges[xIndex].MinX == Ranges[xIndex].MaxX)
                        IndependentVariableConstant[xIndex] = Ranges[xIndex].MinX;
                }
                NumConstantIndependentVariables = IndependentVariableConstant.Where(x => x != null).Count();
                MinY = data2.Min(d => d.Y[0]);
                MaxY = data2.Max(d => d.Y[0]);
                for (int i = 0; i < numItems; i++)
                {
                    data2[i].X = NormalizeIndependentVars(data2[i].X);
                    float yUnnormalized = data2[i].Y[0];
                    data2[i].Y[0] = NormalizeDependentVar(yUnnormalized);
                }
                bool createString = false;
                if (createString)
                {
                    StringBuilder s = new StringBuilder();
                    for (int i = 0; i < numItems; i++)
                    {
                        s.AppendLine(data2[i].Y[0] + "," + String.Join(",", data2[i].X));
                    }
                }
            }
            string dataString = GetDataString(data2);
            await Regression.Regress(data2);
        }

        private string GetDataString((float[] X, float[] Y)[] data)
        {
            StringBuilder s = new StringBuilder();
            s.AppendLine(String.Join(",", Enumerable.Range(1, data.First().Y.Length).Select(a => $"Y{a}")) + "," + String.Join(",", Enumerable.Range(1, data.First().X.Length).Select(a => $"X{a}")));
            foreach (var datum in data)
            {
                string toAdd = String.Join(",", datum.Y) + "," + String.Join(",", datum.X);
                s.AppendLine(toAdd);
            }
            return s.ToString();
        }

        public float[] NormalizeIndependentVars(float[] x)
        {
            float[] result = new float[x.Length - NumConstantIndependentVariables];
            int constantsSoFar = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (IndependentVariableConstant[i] != null)
                    constantsSoFar++;
                else
                {
                    float item = x[i];
                    // TODO: Allow for extrapolation by allowing to exceed min-max range
                    if (item < Ranges[i].MinX)
                        item = Ranges[i].MinX;
                    if (item > Ranges[i].MaxX)
                        item = Ranges[i].MaxX;
                    result[i - constantsSoFar] = (item - Ranges[i].MinX) / (Ranges[i].MaxX - Ranges[i].MinX);
                }
            }
            return result;
        }
        public float NormalizeDependentVar(float y) => (y - MinY) / (MaxY - MinY);
        public float[] DenormalizeIndependentVars(float[] x)
        {
            float[] result = new float[x.Length + NumConstantIndependentVariables];
            int constantsSoFar = 0;
            for (int i = 0; i < result.Length; i++)
            {
                if (IndependentVariableConstant[i] is float constantValue)
                {
                    constantsSoFar++;
                    result[i] = constantValue;
                }
                else
                {
                    float item = x[i - constantsSoFar];
                    result[i] = Ranges[i].MinX + item * (Ranges[i].MaxX - Ranges[i].MinX);
                }
            }
            return result;
        }
        public float DenormalizeDependentVar(float y) => MinY + y * (MaxY - MinY);

        public string GetTrainingResultString() => Regression.GetTrainingResultString();

        public IRegressionMachine GetRegressionMachine() => Regression.GetRegressionMachine();
        public void ReturnRegressionMachine(IRegressionMachine regressionMachine) => Regression.ReturnRegressionMachine(regressionMachine);

        public float GetResult(float[] x, IRegressionMachine regressionMachine)
        {
            if (Normalize)
                x = NormalizeIndependentVars(x);
            float result = regressionMachine == null ? Regression.GetResults(x, null)[0] : regressionMachine.GetResults(x)[0];
            if (Normalize)
                result = DenormalizeDependentVar(result);
            return result;
        }
    }
}
