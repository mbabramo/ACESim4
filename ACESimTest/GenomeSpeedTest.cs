using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESim;
using System.Diagnostics;

namespace ACESimTest
{
    [TestClass]
    public class GenomeSpeedTest
    {
        [TestMethod()]
        public void CalculateSpeedComparison_BestCaseTest()
        {
            Assert.Inconclusive("Skipping speed test because it takes so long.  Remove this assertion to test.");

            int iterationCount = 500000;

            Tuple<List<double>, List<PolynomialGenomeTerm>, double[]> randoms = 
                GenomeTest.MakeRandomCoefficientsTermsAndInputs();
            List<double> coefficients = randoms.Item1;
            List<PolynomialGenomeTerm> terms = randoms.Item2;
            double[] inputs = randoms.Item3;

            PolynomialGenome genome = new PolynomialGenome(coefficients, terms);

            DateTime directStartTime = DateTime.Now;
            for (int i = 0; i < iterationCount; i++)
            {
                genome.CalculateDirectly(inputs);
            }
            TimeSpan directTimeSpan = DateTime.Now - directStartTime;

            DateTime compiledStartTime = DateTime.Now;
            for (int i = 0; i < iterationCount; i++)
            {
                genome.CalculateUsingCompiledExpression(inputs);
            }
            TimeSpan compiledTimeSpan = DateTime.Now - compiledStartTime;

            Trace.WriteLine(String.Format(
                "{3}\r\nIterations: {4}\r\nDirect: {0}\r\nCompiled: {1}\r\nratio: {2}",
                new object[] { 
                    directTimeSpan, 
                    compiledTimeSpan, 
                    (double)directTimeSpan.Ticks / compiledTimeSpan.Ticks,
                    genome.ToStringFull(),
                    iterationCount
                    }
                ));
        }


        /// <summary>
        ///A test for Calculate
        ///</summary>
        [TestMethod()]
        public void CalculateSpeedComparison_WorstCaseTest()
        {
            Assert.Inconclusive("Skipping speed test because it takes so long.  Remove this assertion to test.");

            int iterationCount = 10000;

            List<PolynomialGenome> genomes = new List<PolynomialGenome>();
            List<double[]> inputsList = new List<double[]>();
            for (int i = 0; i < iterationCount; i++)
            {
                Tuple<List<double>, List<PolynomialGenomeTerm>, double[]> randoms = 
                    GenomeTest.MakeRandomCoefficientsTermsAndInputs(minTermCount:10, minInputCount:10);
                List<double> coefficients = randoms.Item1;
                List<PolynomialGenomeTerm> terms = randoms.Item2;
                double[] inputs = randoms.Item3;

                genomes.Add(new PolynomialGenome(coefficients, terms));
                inputsList.Add(inputs);
            }

            DateTime directStartTime = DateTime.Now;
            for (int i = 0; i < iterationCount; i++)
            {
                genomes[i].CalculateDirectly(inputsList[i]);
            }
            TimeSpan directTimeSpan = DateTime.Now - directStartTime;

            DateTime compiledStartTime = DateTime.Now;
            for (int i = 0; i < iterationCount; i++)
            {
                genomes[i].CalculateUsingCompiledExpression(inputsList[i]);
            }
            TimeSpan compiledTimeSpan = DateTime.Now - compiledStartTime;

            Trace.WriteLine(String.Format(
                "Iterations: {3}\r\nDirect: {0}\r\nCompiled: {1}\r\nRatio: {2}",
                new object[] { 
                    directTimeSpan, 
                    compiledTimeSpan, 
                    (double)directTimeSpan.Ticks / compiledTimeSpan.Ticks,
                    iterationCount
                    }
                ));
        }
    }
}
