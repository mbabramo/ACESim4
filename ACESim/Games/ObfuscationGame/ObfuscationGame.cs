using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class ObfuscationGame : Game
    {
        ObfuscationGameInputs ObfuscationGameInputs;
        ObfuscationGameProgressInfo ObfuscationGameProgress;
        double numberWithObfuscation;

        public override void PrepareForCurrentDecision()
        {
            if (CurrentDecisionIndex == 0)
            {
                ObfuscationGameProgress = (ObfuscationGameProgressInfo)Progress;
                ObfuscationGameInputs = (ObfuscationGameInputs)GameInputs;
                numberWithObfuscation = ObfuscationGameInputs.numberWithObfuscation;
                ObfuscationGameProgress.inputsToUse = GetDecisionInputs();

                RecordInputsIfNecessary(ObfuscationGameProgress.inputsToUse);
            }
            else
                throw new NotImplementedException();
        }

        /// <summary>
        /// If game play is completed, then gameSettings.gameComplete should be set to true. 
        /// </summary>
        public override void MakeCurrentDecision()
        {
            if (Progress.GameComplete)
                return;

            ObfuscationGameInputs = (ObfuscationGameInputs)GameInputs;
            ObfuscationGameProgress = (ObfuscationGameProgressInfo)Progress;

            numberWithObfuscation = ObfuscationGameInputs.numberWithObfuscation;

            // Although technicially we should be iterating over GameDefinition.Decisions.Count,
            // the number of Strategies should equal the number of Decisions
            
            switch (CurrentDecisionIndex)
            {
                case 0: // estimation of the obfuscated number
                    double calculation = 0.0;
                    double scoreForOverallEstimation = Forecast.Score(Strategies[0], ObfuscationGameProgress.inputsToUse, ObfuscationGameInputs.actualNumber, out calculation);
                    
                    if (CurrentlyEvolving)
                        Score(0, scoreForOverallEstimation);
                    if (RecordReportInfo)
                    {
                        ObfuscationGameProgress.obfuscated = numberWithObfuscation;
                        ObfuscationGameProgress.strategyCalc = calculation;
                        double bruteForceCalc = 0; // Commented out to make it run faster ObfuscationBruteForceCalculation.GetValue(numberWithObfuscation, ObfuscationGameInputs.standardDeviationOfObfuscation);
                        ObfuscationGameProgress.bruteForceCalc = bruteForceCalc;
                        ObfuscationGameProgress.stdev = ObfuscationGameInputs.standardDeviationOfObfuscation;
                    }
                    break;

                case 1: // estimation of the absolute error
                    break;
            }
            Progress.GameComplete = true;
        }

        

        public static class ObfuscationCorrectAnswer
        {
            public static Tuple<string, string> GenerateStrings(double obfMin, double obfMax, double step, double stdev)
            {
                List<double> obf = new List<double>(), result = new List<double>();
                string obfString = "", resultString = "";
                for (double obfStep = obfMin; obfStep <= obfMax; obfStep += step)
                {
                    obf.Add(obfStep);
                    result.Add(Calculate(stdev, obfStep));
                }
                obfString = String.Join(",", obf.Select(x => x.ToSignificantFigures()).ToArray());
                resultString = String.Join(",", result.Select(x => x.ToSignificantFigures()).ToArray());
                Debug.WriteLine(obfString);
                Debug.WriteLine(resultString);
                return new Tuple<string, string>(obfString, resultString);
            }

            public static double erf(double x)
            {
                return alglib.normaldistr.errorfunction(x);
            }

            public static double norm_pdf(double x, double standard_deviation = 1.0)
            {
                double ssquaredtimes2 = 2 * standard_deviation * standard_deviation;
                double rt2p = Math.Sqrt(2 * Math.PI);
                return Math.Exp(-x * x / ssquaredtimes2) / (rt2p);
            }

            public static double Calculate_Alt(double sigma, double z)
            {
                double sigma_rt2 = Math.Sqrt(2) * sigma; // (square route of 2) times s
                double rt2p = Math.Sqrt(2 * Math.PI);
                double ssquaredtimes2 = 2 * sigma * sigma;
                double numeratorterm1 = -0.5 * z * erf((z - 1) / sigma_rt2);
                double numeratorterm2 = 0.5 * z * erf(z / sigma_rt2);
                double embeddedinterm3a = Math.Exp(-z*z/ssquaredtimes2);
                double embeddedinterm3b = Math.Exp(-(z-1)*(z-1)/ssquaredtimes2);
                double numeratorterm3 = sigma*(embeddedinterm3a - embeddedinterm3b)/rt2p;
                double testcalc = sigma * (norm_pdf(z, sigma) - norm_pdf(z - 1, sigma));
                if (Math.Abs(numeratorterm3 - testcalc) > 0.00001)
                    throw new Exception();
                double numerator = numeratorterm1 + numeratorterm2 + numeratorterm3;
                double denominatorterm1 = erf(z / sigma_rt2);
                double denominatorterm2 = -erf((z - 1) / sigma_rt2);
                double denominator = 0.5*(denominatorterm1 + denominatorterm2);
                return numerator / denominator;
            }

            public static double Calculate(double standardDeviation, double obfuscatedNumber)
            {
                if (standardDeviation == 0)
                {
                    // avoid division by zero
                    if (obfuscatedNumber > 0 && obfuscatedNumber < 1)
                        return obfuscatedNumber;
                    return double.NaN;
                }
                double sigma_rt2 = Math.Sqrt(2) * standardDeviation; // (square route of 2) times s
                // [erf(z / rt2 * s)] z-1 to z
                double erfTerm = erf (obfuscatedNumber / sigma_rt2) - erf ((obfuscatedNumber - 1) / sigma_rt2);
                double phiTerm = 2 * standardDeviation * (norm_pdf(obfuscatedNumber,standardDeviation) - norm_pdf((obfuscatedNumber - 1), standardDeviation));
                double result = obfuscatedNumber + phiTerm / erfTerm;
                return result;
            }

            public static double CalculateDerivativeAtPoint(double standardDeviation, double obfuscatedNumber, bool withRespectToStandardDeviation)
            {
                double originalCalculation = Calculate(standardDeviation, obfuscatedNumber);
                double derivDistance = 0.0001;
                if (withRespectToStandardDeviation)
                    standardDeviation += derivDistance;
                else
                    obfuscatedNumber += 0.0001;
                double nearbyCalculation = Calculate(standardDeviation, obfuscatedNumber);
                double derivative = (nearbyCalculation - originalCalculation) / derivDistance;
                return derivative;
            }

            public static string Calculate_Group(IEnumerable<double> sd, IEnumerable<double> obf)
            {
                string theString = "";
                foreach (var s in sd)
                    theString += String.Join(",", obf.Select(x => Calculate(s, x).ToString())) + "\n";
                return theString;
            }

        }



        public static class ObfuscationBruteForceCalculationHighPrecision
        {
            public static double[] Calculate(double stdev, double[] numbersWithObfuscation, double threshhold, int requiredMatches)
            {
                double[] total = new double[numbersWithObfuscation.Length];
                int[] numMatches = new int[numbersWithObfuscation.Length];
                while (numMatches.Min() < requiredMatches)
                {
                    double actualNumber = RandomGenerator.NextDouble();
                    double obfuscation = stdev * (double)alglib.normaldistr.invnormaldistribution(RandomGenerator.NextDouble());
                    double obfuscated = actualNumber + obfuscation;
                    for (int i = 0; i < numbersWithObfuscation.Length; i++)
                    {
                        if (obfuscated > numbersWithObfuscation[i] - threshhold && obfuscated < numbersWithObfuscation[i] + threshhold)
                        {
                            total[i] += actualNumber;
                            numMatches[i]++;
                        }
                    }
                }
                return total.Zip(numMatches, (x, y) => x / (double)y).ToArray();
            }

            public static long NumIterationsTo(double maxErrOfEstimate, double stdev, double obfNum, double squaredNormaliedDistance)
            {
                double correctValue = ObfuscationCorrectAnswer.Calculate(stdev, obfNum);
                double obfNormalized = (obfNum - 0.5) / .64111;
                double stdevNormalized = (stdev - 0.5) / .28855;
                StatCollector stat = new StatCollector();
                long numTries = 0;
                int consecutiveSuccesses = 0;
                while (consecutiveSuccesses < 50)
                {
                    bool found;
                    do
                    {
                        double actualNumber = RandomGenerator.NextDouble();
                        double standardDeviationOfObfuscation = RandomGenerator.NextDouble();
                        double obfuscation = standardDeviationOfObfuscation * alglib.normaldistr.invnormaldistribution(RandomGenerator.NextDouble());
                        double obfuscatedNumber = actualNumber + obfuscation;
                        double stdDevNormalized2 = (standardDeviationOfObfuscation - 0.5) / .28855;
                        double obfNormalized2 = (obfuscatedNumber - 0.5) / .64111;
                        double stdDevDistance = stdevNormalized - stdDevNormalized2;
                        double obfDistance = obfNormalized - obfNormalized2;
                        double squaredDistance = stdDevDistance * stdDevDistance + obfDistance * obfDistance;
                        found = squaredDistance < squaredNormaliedDistance;
                        if (found)
                            stat.Add(actualNumber);
                        numTries++;
                    } while (!found);
                    if (Math.Abs(stat.Average() - correctValue) < maxErrOfEstimate)
                        consecutiveSuccesses++;
                    else
                        consecutiveSuccesses = 0;
                }
                Debug.WriteLine("Total iterations " + numTries + " within distance " + stat.Num());
                return numTries;
            }
        }
        
        public static class ObfuscationBruteForceCalculation
        {
            static object lockObj = new object();
            static double min = 0.0, max = 1.0, step = 0.01;
            static ObfuscationBruteForceCalculationForStdDev[] list = null;
            static bool initialized = false;

            public static void Initialize()
            {
                lock (lockObj)
                {
                    if (!initialized)
                    {
                        int numToInclude = (int) (Math.Round((max - min) / step)) + 1;
                        list = new ObfuscationBruteForceCalculationForStdDev[numToInclude];
                        for (double current = min; current <= max + step; current += step)
                        {
                            int index = (int) Math.Round(current / step);
                            list[index] = new ObfuscationBruteForceCalculationForStdDev(current);                            
                        }
                    }
                    initialized = true;
                }
            }
            
            public static double GetValue(double obfuscated, double stdev)
            {
                if (!initialized)
                    Initialize();
                int index = (int)Math.Round(stdev / step);
                return list[index].GetValue(obfuscated);
            }
        }

        protected override List<double> GetDecisionInputs()
        {
            int decisionNumber = (int)CurrentDecisionIndex;
            switch (decisionNumber)
            {
                case 0:
                    return new List<double>() { numberWithObfuscation, ObfuscationGameInputs.standardDeviationOfObfuscation };

                case 1:
                    return new List<double>() { numberWithObfuscation, ObfuscationGameInputs.standardDeviationOfObfuscation };

                default:
                    throw new Exception();
            }
        }
    }
}
