using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class DebiasUsingRegression
    {

        public static double CalculateMagnitudeOfBias(double[] biasedData, double[] proxyForBias, double proportionOfItemsToUseToCalculateDegree = 0.10)
        {
            double[] debiasedData = Debias(biasedData, proxyForBias);
            int numToConsider = (int) Math.Floor(debiasedData.Length * proportionOfItemsToUseToCalculateDegree);
            double total = 0;
            for (int i = 0; i < numToConsider; i++)
                total += Math.Abs(debiasedData[i] - biasedData[i]);
            total /= (double)numToConsider;
            return total;
        }

        public static double[] Debias(double[] biasedData, double[] proxyForBias)
        {
            List<LinearRegressionObservation> obs = new List<LinearRegressionObservation>();
            int length = biasedData.Length;
            bool includeSquareTerm = false; // length >= 1000;
            bool includeCubicTerm = false; // length >= 2000;
            VariableTransformationOptions transform = new VariableTransformationOptions() { IncludeLinearTerm = true, IncludeSquareTerm = includeSquareTerm, IncludeCubicTerm = includeCubicTerm, IncludeNaturalLogarithmTerm = false, IsTermToMultiplyAllOtherTermsBy = false, IsTermToMultiplyPreviousTermOnlyBy = false };
            List<VariableTransformationOptions> transformList = new List<VariableTransformationOptions>() { transform };
            for (int i = 0; i < length; i++)
            {
                obs.Add(new LinearRegressionObservation() { DependentVariable = biasedData[i], IndependentVariables = VariablesTransformer.ExpandIndependentVariables(new List<double> { proxyForBias[i] }, transformList), Weight = 1.0 });
            }
            double[] coefficients;
            bool success;
            LinearRegressionSimple.DoLinearRegression(obs, out coefficients, out success, true);
            if (!success)
                throw new Exception("Linear regression failed.");
            double[] returnVals = new double[length];
            for (int i = 0; i < length; i++)
            {
                returnVals[i] = biasedData[i] - coefficients[0] * proxyForBias[i];
                if (includeSquareTerm)
                    returnVals[i] -= coefficients[1] * proxyForBias[i] * proxyForBias[i];
                if (includeCubicTerm)
                    returnVals[i] -= coefficients[2] * proxyForBias[i] * proxyForBias[i] * proxyForBias[i];
            }
            return returnVals;
        }
    }

    //public static class LinearRegressionWithAlgLib
    //{
    //    public static double[] GetRegressionCoefficients(ref double[,] regressionData, int numVariablesIncludingConstantTerm, int numObservations)
    //    {
    //        alglib.linreg myRegression2 = new alglib.linreg();
    //        alglib.linreg.linearmodel lm = new alglib.linreg.linearmodel();
    //        alglib.linreg.lrreport ar = new alglib.linreg.lrreport();
    //        int info = 0;
    //        alglib.linreg.lrbuild(ref regressionData, numObservations, numVariablesIncludingConstantTerm, ref info, ref lm, ref ar);
    //        double[] coefficients = lm.w.Skip(4).Take(numVariablesIncludingConstantTerm).ToArray();
    //        return coefficients;
    //    }
    //}

    [Serializable]
    public class LinearRegressionObservation
    {
        public double DependentVariable;
        public List<double> IndependentVariables;
        public double Weight = 1;
    }

    [Serializable]
    public class VariableTransformationOptions
    {
        public bool IncludeLinearTerm = true;
        public bool IncludeSquareTerm = false;
        public bool IncludeCubicTerm = false;
        public bool IncludeNaturalLogarithmTerm = false;
        public bool IsTermToMultiplyAllOtherTermsBy = false;
        public bool IsTermToMultiplyPreviousTermOnlyBy = false;
    }

    public static class VariablesTransformer
    {
        public static double ApplyCoefficients(List<double> indVars, double[] coefficients, List<VariableTransformationOptions> trans)
        {
            List<double> indVarsExpanded = ExpandIndependentVariables(indVars, trans);
            double total = 0;
            for (int i = 0; i < indVarsExpanded.Count; i++)
                total += indVarsExpanded[i] * coefficients[i];
            total += coefficients[indVarsExpanded.Count]; // constant term
            return total;
        }

        public static List<double> ExpandIndependentVariables(List<double> indVars, List<VariableTransformationOptions> trans)
        {
            if (indVars == null)
                return null;
            if (indVars.Count != trans.Count)
                throw new Exception("Exception: One VariableTransformationOptions required for each independent variable.");
            List<double> list = new List<double>();
            List<int> termNumbers = new List<int>();
            for (int i = 0; i < indVars.Count; i++)
            {
                if (trans[i].IncludeLinearTerm)
                {
                    list.Add(indVars[i]);
                    termNumbers.Add(i);
                }
                if (trans[i].IncludeSquareTerm)
                {
                    list.Add(indVars[i] * indVars[i]);
                    termNumbers.Add(i);
                }
                if (trans[i].IncludeCubicTerm)
                {
                    list.Add(indVars[i] * indVars[i] * indVars[i]);
                    termNumbers.Add(i);
                }
                if (trans[i].IncludeNaturalLogarithmTerm)
                {
                    list.Add(Math.Log(indVars[i]));
                    termNumbers.Add(i);
                }
            }
            int listLength = list.Count;
            for (int m = 0; m < trans.Count; m++)
            {
                if (trans[m].IsTermToMultiplyAllOtherTermsBy)
                {
                    for (int i = 0; i < listLength; i++)
                    {
                        list.Add(list[i] * indVars[m]);
                    }
                }
                else if (trans[m].IsTermToMultiplyPreviousTermOnlyBy)
                {
                    for (int i = 0; i < listLength; i++)
                    {
                        if (termNumbers[i] == m - 1)
                            list.Add(list[i] * indVars[m]);
                    }
                }
            }
            return list;
        }

    }

    // this allows easier access to the linear regression methods below.
    public static class LinearRegressionSimple
    {
        
        public static void DoLinearRegressionWithVariableTransformations(List<LinearRegressionObservation> observations, List<VariableTransformationOptions> transformationOptions, int? yhatForObservation, out double requestedYhat, List<double> notIncludedIndependentVariables, out double calculatedDependentVariable, out bool success, bool includeConstantTerm = true)
        {
            List<LinearRegressionObservation> transformedObservations = new List<LinearRegressionObservation>();
            foreach (var obs in observations)
                transformedObservations.Add(new LinearRegressionObservation() { DependentVariable = obs.DependentVariable, IndependentVariables = VariablesTransformer.ExpandIndependentVariables(obs.IndependentVariables, transformationOptions), Weight = obs.Weight });
            double[] coefficients;
            double[] allYhats;
            DoLinearRegression(transformedObservations, yhatForObservation, out requestedYhat, out allYhats, VariablesTransformer.ExpandIndependentVariables(notIncludedIndependentVariables, transformationOptions), out calculatedDependentVariable, out coefficients, out success, includeConstantTerm);
        }

        public static List<double> OptimizeByLinearRegression(List<LinearRegressionObservation> observations, bool maximize, out bool success, int numControlVariables = 0)
        {
            List<VariableTransformationOptions> transformationOptions = new List<VariableTransformationOptions>();
            int numIndVars = observations.First().IndependentVariables.Count;
            List<Tuple<double,double>> minAndMax = new List<Tuple<double,double>>();
            for (int i = 0; i < numIndVars; i++)
            {
                if (i < numIndVars - numControlVariables)
                {
                    double min, max;
                    min = observations.Select(x => x.IndependentVariables[i]).Min();
                    max = observations.Select(x => x.IndependentVariables[i]).Max();
                    minAndMax.Add(new Tuple<double, double>(min, max));
                    transformationOptions.Add(new VariableTransformationOptions() { IncludeLinearTerm = true, IncludeSquareTerm = true, IncludeCubicTerm = false });
                }
                else
                    transformationOptions.Add(new VariableTransformationOptions() { IncludeLinearTerm = true, IncludeSquareTerm = false, IncludeCubicTerm = false });
            }
            List<LinearRegressionObservation> transformedObservations = new List<LinearRegressionObservation>();
            foreach (var obs in observations)
                transformedObservations.Add(new LinearRegressionObservation() { DependentVariable = obs.DependentVariable, IndependentVariables = VariablesTransformer.ExpandIndependentVariables(obs.IndependentVariables, transformationOptions), Weight = obs.Weight });
            double[] coefficients;
            DoLinearRegression(transformedObservations, out coefficients, out success);
            if (!success)
                return null;
            List<double> optimizedValues = new List<double>();
            for (int i = 0; i < numIndVars - numControlVariables; i++)
            {
                double linearTermCoefficient = coefficients[i * 2];
                double squareTermCoefficient = coefficients[i * 2 + 1];
                double optimizedValue = GetOptimizedValue(minAndMax[i].Item1, minAndMax[i].Item2, linearTermCoefficient, squareTermCoefficient, maximize);
                optimizedValues.Add(optimizedValue);
            }
            return optimizedValues;
        }

        internal static double GetOptimizedValue(double min, double max, double linearTermCoefficient, double squareTermCoefficient, bool maximize)
        {
            // 2 * squareTermCoefficient * possibleMidRangeValue + linearTermCoefficient = 0;
            double possibleMidRangeValue = (0 - linearTermCoefficient) / (2 * squareTermCoefficient);
            double minTransformed = linearTermCoefficient * min + squareTermCoefficient * min * min;
            double maxTransformed = linearTermCoefficient * max + squareTermCoefficient * max * max;
            double midTransformed = linearTermCoefficient * possibleMidRangeValue + squareTermCoefficient * possibleMidRangeValue * possibleMidRangeValue;
            if (possibleMidRangeValue > min && possibleMidRangeValue < max &&  (!(minTransformed < midTransformed && midTransformed < maxTransformed)))
            { // the midValue is an extreme value, so let's see if it is better than the other two values
                if ((maximize && midTransformed > minTransformed) || (!maximize && midTransformed < minTransformed))
                    return possibleMidRangeValue;
            }
            // the possibleMidRangeValue is not the optimal value, so optimal value must be at the ends.
            if (maximize)
            {
                if (minTransformed < maxTransformed)
                    return max;
                else
                    return min;
            }
            else
            {
                if (minTransformed < maxTransformed)
                    return min;
                else
                    return max;
            }
        }

        public static void DoLinearRegressionToEstimateDerivatives(List<LinearRegressionObservation> observations, List<double> pointAtWhichToCalculateDerivative, out List<double> derivatives, out bool success)
        {
            List<LinearRegressionObservation> transformedObservations = new List<LinearRegressionObservation>();
            List<VariableTransformationOptions> transformationOptions = new List<VariableTransformationOptions>();
            for (int d = 0; d < pointAtWhichToCalculateDerivative.Count; d++)
                transformationOptions.Add(new VariableTransformationOptions() { IncludeLinearTerm = true, IncludeSquareTerm = true, IncludeCubicTerm = false, IncludeNaturalLogarithmTerm = false });
            foreach (var obs in observations)
                transformedObservations.Add(new LinearRegressionObservation() { DependentVariable = obs.DependentVariable, IndependentVariables = VariablesTransformer.ExpandIndependentVariables(obs.IndependentVariables, transformationOptions), Weight = obs.Weight });
            double[] coefficients;
            int? yhatForObservation = null;
            double requestedYhat, calculatedDependentVariable;
            double[] allYhats;
            DoLinearRegression(transformedObservations, yhatForObservation, out requestedYhat, out allYhats, VariablesTransformer.ExpandIndependentVariables(pointAtWhichToCalculateDerivative, transformationOptions), out calculatedDependentVariable, out coefficients, out success);
            derivatives = new List<double>();
            for (int d = 0; d < pointAtWhichToCalculateDerivative.Count; d++)
            {
                derivatives.Add(coefficients[d * 2] + 2 * coefficients[d * 2 + 1]);
            }
        }


        public static void DoLinearRegression(List<LinearRegressionObservation> observations, out double[] coefficients, out bool success, bool includeConstantTerm = true)
        {
            int? yhatForObservation = null; 
            double requestedYhat; 
            double[] allYhats;
            List<double> notIncludedIndependentVariables = null;
            double calculatedDependentVariable;
            DoLinearRegression(observations, yhatForObservation, out requestedYhat, out allYhats, notIncludedIndependentVariables, out calculatedDependentVariable, out coefficients, out success, includeConstantTerm);
        }

        public static void DoLinearRegression(List<LinearRegressionObservation> observations, int? yhatForObservation, out double requestedYhat, out double[] allYhats, List<double> notIncludedIndependentVariables, out double calculatedDependentVariable, out double[] coefficients, out bool success, bool includeConstantTerm = true)
        {
            if (!observations.Any() || !observations.First().IndependentVariables.Any())
                throw new Exception("Invalid regression data.");
            int numObservations = observations.Count;
            int numIndependentVariables = observations.First().IndependentVariables.Count;
            double[] Y = new double[numObservations];
            double[,] X = new double[includeConstantTerm ? numIndependentVariables + 1 : numIndependentVariables, numObservations]; 
            double[] W = new double[numObservations];
            int o = 0;
            foreach (LinearRegressionObservation obs in observations)
            {
                Y[o] = obs.DependentVariable;
                for (int iv = 0; iv < numIndependentVariables; iv++)
                    X[iv, o] = obs.IndependentVariables[iv];
                if (includeConstantTerm)
                    X[numIndependentVariables, o] = 1; // constant term
                W[o] = obs.Weight;
                o++;
            }

            LinearRegression reg = new LinearRegression();
            
            requestedYhat = 0;
            calculatedDependentVariable = 0;

            success = reg.Regress(Y, X, W);

            coefficients = reg.Coefficients;
            allYhats = reg.CalculatedValues;
            if (!success)
                return;
            if (yhatForObservation != null)
                requestedYhat = reg.CalculatedValues[(int)yhatForObservation];
            if (notIncludedIndependentVariables != null)
            {
                for (int iv = 0; iv < numIndependentVariables + 1; iv++)
                {
                    if (iv == numIndependentVariables)
                        calculatedDependentVariable += reg.Coefficients[iv]; // constant term
                    else
                        calculatedDependentVariable += notIncludedIndependentVariables[iv] * reg.Coefficients[iv];
                }
            }
        }
    }

    // source from http://www.codeproject.com/KB/recipes/LinReg.aspx

    public class LinearRegression
    {
        double[,] V;            // Least squares and var/covar matrix
        public double[] C;      // Coefficients
        public double[] SEC;    // Std Error of coefficients
        double RYSQ;            // Multiple correlation coefficient
        double SDV;             // Standard deviation of errors
        double FReg;            // Fisher F statistic for regression
        double[] Ycalc;         // Calculated values of Y
        double[] DY;            // Residual values of Y

        public double FisherF
        {
            get { return FReg; }
        }

        public double CorrelationCoefficient
        {
            get { return RYSQ; }
        }

        public double StandardDeviation
        {
            get { return SDV; }
        }

        public double[] CalculatedValues
        {
            get { return Ycalc; }
        }

        public double[] Residuals
        {
            get { return DY; }
        }

        public double[] Coefficients
        {
            get { return C; }
        }

        public double[] CoefficientsStandardError
        {
            get { return SEC; }
        }

        public double[,] VarianceMatrix
        {
            get { return V; }
        }

        public bool Regress(double[] Y, double[,] X, double[] W)
        {
            int M = Y.Length;             // M = Number of data points
            int N = X.Length / M;         // N = Number of linear terms
            int NDF = M - N;              // Degrees of freedom
            Ycalc = new double[M];
            DY = new double[M];
            // If not enough data, don't attempt regression
            if (NDF < 1)
            {
                return false;
            }
            V = new double[N, N];
            C = new double[N];
            SEC = new double[N];
            double[] B = new double[N];   // Vector for LSQ

            // Clear the matrices to start out
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                    V[i, j] = 0;

            // Form Least Squares Matrix
            for (int i = 0; i < N; i++)
            {
                for (int j = 0; j < N; j++)
                {
                    V[i, j] = 0;
                    for (int k = 0; k < M; k++)
                        V[i, j] = V[i, j] + W[k] * X[i, k] * X[j, k];
                }
                B[i] = 0;
                for (int k = 0; k < M; k++)
                    B[i] = B[i] + W[k] * X[i, k] * Y[k];
            }
            // V now contains the raw least squares matrix
            if (!SymmetricMatrixInvert(V))
            {
                return false;
            }
            // V now contains the inverted least square matrix
            // Matrix multpily to get coefficients C = VB
            for (int i = 0; i < N; i++)
            {
                C[i] = 0;
                for (int j = 0; j < N; j++)
                    C[i] = C[i] + V[i, j] * B[j];
            }

            // Calculate statistics
            double TSS = 0;
            double RSS = 0;
            double YBAR = 0;
            double WSUM = 0;
            for (int k = 0; k < M; k++)
            {
                YBAR = YBAR + W[k] * Y[k];
                WSUM = WSUM + W[k];
            }
            YBAR = YBAR / WSUM;
            for (int k = 0; k < M; k++)
            {
                Ycalc[k] = 0;
                for (int i = 0; i < N; i++)
                    Ycalc[k] = Ycalc[k] + C[i] * X[i, k];
                DY[k] = Ycalc[k] - Y[k];
                TSS = TSS + W[k] * (Y[k] - YBAR) * (Y[k] - YBAR);
                RSS = RSS + W[k] * DY[k] * DY[k];
            }
            double SSQ = RSS / NDF;
            RYSQ = 1 - RSS / TSS;
            FReg = 9999999;
            if (RYSQ < 0.9999999)
                FReg = RYSQ / (1 - RYSQ) * NDF / (N - 1);
            SDV = Math.Sqrt(SSQ);

            // Calculate var-covar matrix and std error of coefficients
            for (int i = 0; i < N; i++)
            {
                for (int j = 0; j < N; j++)
                    V[i, j] = V[i, j] * SSQ;
                SEC[i] = Math.Sqrt(V[i, i]);
            }
            return true;
        }


        public bool SymmetricMatrixInvert(double[,] V)
        {
            int N = (int)Math.Sqrt(V.Length);
            double[] t = new double[N];
            double[] Q = new double[N];
            double[] R = new double[N];
            double AB;
            int K, L, M;

            // Invert a symetric matrix in V
            for (M = 0; M < N; M++)
                R[M] = 1;
            K = 0;
            for (M = 0; M < N; M++)
            {
                double Big = 0;
                for (L = 0; L < N; L++)
                {
                    AB = Math.Abs(V[L, L]);
                    if ((AB > Big) && (R[L] != 0))
                    {
                        Big = AB;
                        K = L;
                    }
                }
                if (Big == 0)
                {
                    return false;
                }
                R[K] = 0;
                Q[K] = 1 / V[K, K];
                t[K] = 1;
                V[K, K] = 0;
                if (K != 0)
                {
                    for (L = 0; L < K; L++)
                    {
                        t[L] = V[L, K];
                        if (R[L] == 0)
                            Q[L] = V[L, K] * Q[K];
                        else
                            Q[L] = -V[L, K] * Q[K];
                        V[L, K] = 0;
                    }
                }
                if ((K + 1) < N)
                {
                    for (L = K + 1; L < N; L++)
                    {
                        if (R[L] != 0)
                            t[L] = V[K, L];
                        else
                            t[L] = -V[K, L];
                        Q[L] = -V[K, L] * Q[K];
                        V[K, L] = 0;
                    }
                }
                for (L = 0; L < N; L++)
                    for (K = L; K < N; K++)
                        V[L, K] = V[L, K] + t[L] * Q[K];
            }
            M = N;
            L = N - 1;
            for (K = 1; K < N; K++)
            {
                M = M - 1;
                L = L - 1;
                for (int J = 0; J <= L; J++)
                    V[M, J] = V[J, M];
            }
            return true;
        }

        public double RunTest(double[] X)
        {
            int NRuns = 1;
            int N1 = 0;
            int N2 = 0;
            if (X[0] > 0)
                N1 = 1;
            else
                N2 = 1;

            for (int k = 1; k < X.Length; k++)
            {
                if (X[k] > 0)
                    N1++;
                else
                    N2++;
                if (X[k] * X[k - 1] < 0)
                    NRuns++;
            }
            return 1;
        }

    }
}

