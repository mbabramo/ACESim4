using ACESim;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ACESimTest
{
    /// <summary>
    ///This is a test class for GenomeTest and is intended
    ///to contain all GenomeTest Unit Tests
    ///</summary>
    [TestClass()]
    public class GenomeTest
    {
        TestContext testContextInstance;
        List<PolynomialGenomeTerm> testPolynomialGenomeTermsPositiveExponents;
        List<PolynomialGenomeTerm> testPolynomialGenomeTermsNegativeExponents;
        PolynomialGenomeTerm_Accessor[] testGenomeAccessorTerms;
        List<double> testCoefficientsPositive;
        List<double> testCoefficientsNegative;
        List<double> testInputsPositive;
        List<double> testInputsNegative;
        
        static Random random;

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }


        [ClassInitialize()]
        public static void GenomeTestClassInitialize(TestContext testContext)
        {
            random = new Random();
        }
        
        [ClassCleanup()]
        public static void GenomeTestClassCleanup()
        {
            random = null;
        }


        // Use TestInitialize to run code before running each test
        [TestInitialize()]
        public void GenomeTestInitialize()
        {
            testPolynomialGenomeTermsPositiveExponents = new List<PolynomialGenomeTerm>();
            int[][] exponentsArrayPositive = 
                new int[][] {
                    new int[] { 1, 2, 3 },
                    new int[] { 4, 5, 6 },
                    new int[] { 7, 8, 9 }
                    };
            foreach (int[] exponents in exponentsArrayPositive)
            {
                testPolynomialGenomeTermsPositiveExponents.Add(new PolynomialGenomeTerm(exponents, null));
            }

            testPolynomialGenomeTermsNegativeExponents = new List<PolynomialGenomeTerm>();
            int[][] exponentsArrayNegative =
                new int[][] {
                    new int[] { -1,  2, -3 },
                    new int[] {  4, -5,  6 },
                    new int[] { -7,  8, -9 }
                    };
            foreach (int[] exponents in exponentsArrayNegative)
            {
                testPolynomialGenomeTermsNegativeExponents.Add(new PolynomialGenomeTerm(exponents, null));
            }

            testGenomeAccessorTerms = new PolynomialGenomeTerm_Accessor[testPolynomialGenomeTermsPositiveExponents.Count];
            for (int i = 0; i < testGenomeAccessorTerms.Length; i++)
            {
                int[] exponents = exponentsArrayPositive[i];
                testGenomeAccessorTerms[i] = new PolynomialGenomeTerm_Accessor(exponents, null);
            }

            testCoefficientsPositive = new List<double>(new double[] {  1.0, 2.0,  3.0 });
            testCoefficientsNegative = new List<double>(new double[] { -1.0, 2.0, -3.0 });

            testInputsPositive = new List<double>(new double[] {  1.0, 2.0,  3.0 });
            testInputsNegative = new List<double>(new double[] { -1.0, 2.0, -3.0 });
        }
        
        // Use TestCleanup to run code after each test has run
        [TestCleanup()]
        public void GenomeTestCleanup()
        {
            testPolynomialGenomeTermsPositiveExponents = null;
            testPolynomialGenomeTermsNegativeExponents = null;
            testCoefficientsPositive = null;
            testCoefficientsNegative = null;
            testInputsPositive = null;
            testInputsNegative = null;
        }

        #region Additional test attributes
        // 
        //You can use the following additional attributes as you write your tests:
        //
        //Use ClassInitialize to run code before running the first test in the class
        //[ClassInitialize()]
        //public static void MyClassInitialize(TestContext testContext)
        //{
        //}
        //
        //Use ClassCleanup to run code after all tests in a class have run
        //[ClassCleanup()]
        //public static void MyClassCleanup()
        //{
        //}
        //
        //Use TestInitialize to run code before running each test
        //[TestInitialize()]
        //public void MyTestInitialize()
        //{
        //}
        //
        //Use TestCleanup to run code after each test has run
        //[TestCleanup()]
        //public void MyTestCleanup()
        //{
        //}
        //
        #endregion


        /// <summary>
        ///A test for Calculate
        ///</summary>
        [TestMethod()]
        public void CalculateDirectly_PositiveTest()
        {
            PolynomialGenome target = new PolynomialGenome(testCoefficientsPositive, testPolynomialGenomeTermsPositiveExponents);
            // 1 * (1^1 * 2^2 * 3^3) + 2 * (1^4 * 2^5 * 3^6) + 3 * (1^7 * 2^8 * 3^9)
            // = (1 * 4 * 27) + 2 * (1 * 32 * 729) + 3 * (1 * 256 * 19683)
            // = 108 + 2 * 23328 + 3 * 5038848
            // = 108 + 46656 + 15116544
            // = 15163308
            double expected = 15163308.0;

            double actual = target.CalculateDirectly(testInputsPositive.ToArray());
            
            Assert.AreEqual(expected, actual, "CalculateDirectly did not calculate correctly.");
        }

        /// <summary>
        ///A test for Calculate
        ///</summary>
        [TestMethod()]
        public void CalculateDirectly_NegativeTest()
        {
            PolynomialGenome target = new PolynomialGenome(testCoefficientsNegative, testPolynomialGenomeTermsNegativeExponents);
            // -1 * (-1^-1 * 2^2 * -3^-3) + 2 * (-1^4 * 2^-5 * -3^6) + -3 * (-1^-7 * 2^8 * -3^-9)
            // -1 * (-1 * 4 * -0.037037037037037) + 2 * (1 * 0.03125 * 729) + -3 * (-1 * 256 * -0.000050805263425)
            // -0.148148148148148 + 45.5625 + -0.039018442310623
            // 45.375333409541229
            double expected = 45.37533;
            double error = 0.00001f;

            double actual = target.CalculateDirectly(testInputsNegative.ToArray());

            Assert.AreEqual(expected, actual, error, "CalculateDirectly did not calculate correctly.");
        }

        /// <summary>
        ///A test for Calculate
        ///</summary>
        [TestMethod()]
        public void CalculateDirectly_DivideByZeroTest()
        {
            List<double> coefficients = new List<double>(new double[] { 1 });
            List<int> exponents = new List<int>(new int[] { -1 });
            List<PolynomialGenomeTerm> terms = new List<PolynomialGenomeTerm>(new PolynomialGenomeTerm[] { new PolynomialGenomeTerm(exponents, null) });
            List<double> inputs = new List<double>(new double[] { 0.0 });
            PolynomialGenome genome = new PolynomialGenome(coefficients, terms);

            double actual = genome.CalculateDirectly(inputs.ToArray());

            Assert.IsTrue(Double.IsPositiveInfinity(actual));
        }

        /// <summary>
        ///A test for Calculate
        ///</summary>
        [TestMethod()]
        public void CalculateUsingCompiledExpression_DivideByZeroTest()
        {
            List<double> coefficients = new List<double>(new double[] { 1 });
            List<int> exponents = new List<int>(new int[] { -1 });
            List<PolynomialGenomeTerm> terms = new List<PolynomialGenomeTerm>(new PolynomialGenomeTerm[] { new PolynomialGenomeTerm(exponents, null) });
            List<double> inputs = new List<double>(new double[] { 0.0 });
            PolynomialGenome genome = new PolynomialGenome(coefficients, terms);

            double actual = genome.CalculateUsingCompiledExpression(inputs.ToArray());

            Assert.IsTrue(Double.IsPositiveInfinity(actual));
        }


        /// <summary>
        ///A test for Calculate
        ///</summary>
        [TestMethod()]
        public void CalculateUsingCompiledExpression_PositiveTest()
        {
            PolynomialGenome_Accessor target = new PolynomialGenome_Accessor(testCoefficientsPositive, testPolynomialGenomeTermsPositiveExponents);
            // 1 * (1^1 * 2^2 * 3^3) + 2 * (1^4 * 2^5 * 3^6) + 3 * (1^7 * 2^8 * 3^9)
            // = (1 * 4 * 27) + 2 * (1 * 32 * 729) + 3 * (1 * 256 * 19683)
            // = 108 + 2 * 23328 + 3 * 5038848
            // = 108 + 46656 + 15116544
            // = 15163308
            double expected = 15163308.0;

            double actual = target.CalculateUsingCompiledExpression(testInputsPositive.ToArray());
            //Func<List<double>, double> compiled = target.compiledCalculation;

            Assert.AreEqual(expected, actual, "CalculateUsingCompiledExpression did not calculate correctly.");
        }

        /// <summary>
        ///A test for Calculate
        ///</summary>
        [TestMethod()]
        public void CalculateUsingCompiledExpression_NegativeExponentsTest()
        {

            PolynomialGenome target = new PolynomialGenome(testCoefficientsPositive, testPolynomialGenomeTermsNegativeExponents);
            // 1 * (1^-1 * 2^2 * 3^-3) + 2 * (1^4 * 2^-5 * 3^6) + 3 * (1^-7 * 2^8 * 3^-9)
            // 1 * (1 * 4 * 0.111111111111111) + 2 * (1 * 0.03125 * 729) + 3 * (1 * 256 * 0.000050805263425)
            // 0.444444444444444 + 45.5625 + 0.039018442310623
            // 46.045962886755067
            double expected = 46.045;
            double error = expected * 1E-2; // Why is the error so high?

            Assert.AreEqual(expected, target.CalculateDirectly(testInputsPositive.ToArray()), error, "You might have done your math incorrectly.");

            double actual = target.CalculateUsingCompiledExpression(testInputsPositive.ToArray());
            //Func<List<double>, double> compiled = target.compiledCalculation;

            Assert.AreEqual(expected, actual, error, "CalculateUsingCompiledExpression did not calculate correctly.");
        }

        /// <summary>
        ///A test for Calculate
        ///</summary>
        [TestMethod()]
        public void CalculateUsingCompiledExpression_NegativeTest()
        {
            PolynomialGenome target = new PolynomialGenome(testCoefficientsNegative, testPolynomialGenomeTermsNegativeExponents);
            // -1 * (-1^-1 * 2^2 * -3^-3) + 2 * (-1^4 * 2^-5 * -3^6) + -3 * (1^-7 * 2^8 * -3^-9)
            // -1 * (-1 * 4 * -0.111111111111111) + 2 * (1 * 0.03125 * 729) + -3 * (1 * 256 * -0.000050805263425)
            // -0.444444444444444 + 45.5625 + 0.039018442310623
            // 45.157073997866179
            double expected = 45.1570;
            double error = expected * 1E-2; // Why is the error so high?

            Assert.AreEqual(expected, target.CalculateDirectly(testInputsNegative.ToArray()), error, "You might have done your math incorrectly.");

            double actual = target.CalculateUsingCompiledExpression(testInputsNegative.ToArray());
            //Func<List<double>, double> compiled = target.compiledCalculation;

            Assert.AreEqual(expected, actual, error, "CalculateUsingCompiledExpression did not calculate correctly.");
        }

        public static Tuple<List<double>, List<PolynomialGenomeTerm>, double[]> MakeRandomCoefficientsTermsAndInputs(
            int maxTermCount = 10, 
            double maxCoefficientMagnitude = 10.0,
            int maxExponentMagnitude = 5, 
            int maxInputCount = 10, 
            double maxInputMagnitude = 10.0,
            int minTermCount = 1, 
            int minInputCount = 1
            )
        {
            if (random == null)
                random = new Random();

            int termCount = random.Next(minTermCount, maxTermCount);
            int inputCount = random.Next(minInputCount, maxInputCount);

            List<double> coefficients = new List<double>();
            List<PolynomialGenomeTerm> terms = new List<PolynomialGenomeTerm>();
            for (int termIndex = 0; termIndex < termCount; termIndex++)
            {
                double coefficient = (double)((random.NextDouble() - 0.5) * 2.0 * maxCoefficientMagnitude);
                coefficients.Add(coefficient);

                List<int> exponents = new List<int>();
                for (int inputIndex = 0; inputIndex < inputCount; inputIndex++)
                {
                    int exponent = random.Next(-1 * maxExponentMagnitude, maxExponentMagnitude);
                    exponents.Add(exponent);
                }
                terms.Add(new PolynomialGenomeTerm(exponents, null));
            }
            PolynomialGenome target = new PolynomialGenome(coefficients, terms);

            double[] inputs = new double[inputCount];
            for (int inputIndex = 0; inputIndex < inputCount; inputIndex++)
            {
                double input = (double)((random.NextDouble() - 0.5) * 2.0 * maxInputMagnitude);
                inputs[inputIndex] = input;
            }

            return new Tuple<List<double>, List<PolynomialGenomeTerm>, double[]>(coefficients, terms, inputs);
        }

        /// <summary>
        ///A test for Calculate
        ///</summary>
        [TestMethod()]
        public void CalculateDirectlyAndCalculateUsingCompiledExpressionAreEqual_Test()
        {
            Tuple<List<double>, List<PolynomialGenomeTerm>, double[]> randoms = MakeRandomCoefficientsTermsAndInputs();
            List<double> coefficients = randoms.Item1;
            List<PolynomialGenomeTerm> terms = randoms.Item2;
            double[] inputs = randoms.Item3;
            PolynomialGenome target = new PolynomialGenome(coefficients, terms);

            double directCalculationResult = target.CalculateDirectly(inputs);
            double compiledCalculationResult = target.CalculateUsingCompiledExpression(inputs);

            double error = Math.Abs(directCalculationResult * 1E-5);

            Assert.AreEqual(directCalculationResult, compiledCalculationResult, error, "Direct Calculation and Compiled Calculation did not match.");
        }

        /// <summary>
        ///A test for DeepCopy
        ///</summary>
        [TestMethod()]
        public void DeepCopy_Test()
        {
            PolynomialGenome target = new PolynomialGenome(testCoefficientsPositive, testPolynomialGenomeTermsPositiveExponents);

            PolynomialGenome actual = target.DeepCopy() as PolynomialGenome;

            Assert.AreEqual(target, actual, "DeepCopy created an unequal copy.");
            for (int i = 0; i < target.Coefficients.Count; i++)
            {
                Assert.AreEqual(target.Coefficients[i], actual.Coefficients[i], "DeepCopy copy has unequal Coefficients.");
            }
            for (int i = 0; i < target.Terms.Count; i++)
            {
                PolynomialGenomeTerm targetTerm = target.Terms[i];
                PolynomialGenomeTerm actualTerm = actual.Terms[i];
                Assert.AreEqual(targetTerm, actualTerm, "DeepCopy put unequal PolynomialGenomeTerm in copy's Terms.");
            }

            Assert.AreNotSame(target, actual, "DeepCopy returned identical object, not a copy.");
            Assert.AreNotSame(target.Coefficients, actual.Coefficients, "DeepCopy returned identical Coefficients, and did not copy them.");
            for (int i = 0; i < target.Terms.Count; i++)
            {
                PolynomialGenomeTerm targetTerm = target.Terms[i];
                PolynomialGenomeTerm actualTerm = actual.Terms[i];
                Assert.AreNotSame(targetTerm, actualTerm, "DeepCopy put identical PolynomialGenomeTerm in copies Terms.");
            }
        }

        /// <summary>
        ///A test for Equals
        ///</summary>
        [TestMethod()]
        public void Equals_Test()
        {
            PolynomialGenome target = new PolynomialGenome(testCoefficientsPositive, testPolynomialGenomeTermsPositiveExponents);

            List<PolynomialGenomeTerm> otherPolynomialGenomeTerms = new List<PolynomialGenomeTerm>();
            foreach (PolynomialGenomeTerm term in testPolynomialGenomeTermsPositiveExponents)
            {
                int[] exponentsArray = term.Exponents.ToArray();
                otherPolynomialGenomeTerms.Add(new PolynomialGenomeTerm(exponentsArray, null));
            }
            List<double> otherCoefficients = new List<double>(testCoefficientsPositive.ToArray());
            PolynomialGenome other = new PolynomialGenome(otherCoefficients, otherPolynomialGenomeTerms);

            Assert.IsTrue(target.Equals(other), "Genome was not equal to Genome made with equal values.");
        }

        /// <summary>
        ///A test for Equals
        ///</summary>
        [TestMethod()]
        public void Equals_OutOfOrderReverseTest()
        {
            PolynomialGenome target = new PolynomialGenome(testCoefficientsPositive, testPolynomialGenomeTermsPositiveExponents);

            List<PolynomialGenomeTerm> otherPolynomialGenomeTerms = new List<PolynomialGenomeTerm>();
            foreach (PolynomialGenomeTerm term in testPolynomialGenomeTermsPositiveExponents)
            {
                int[] exponentsArray = term.Exponents.ToArray();
                otherPolynomialGenomeTerms.Add(new PolynomialGenomeTerm(exponentsArray, null));
            }
            List<double> otherCoefficients = new List<double>(testCoefficientsPositive.ToArray());
            otherPolynomialGenomeTerms.Reverse();
            otherCoefficients.Reverse();
            PolynomialGenome other = new PolynomialGenome(otherCoefficients, otherPolynomialGenomeTerms);

            Assert.IsTrue(target.Equals(other), "Genome was not equal to Genome made with equal values.");
        }

        /// <summary>
        ///A test for Equals
        ///</summary>
        [TestMethod()]
        public void Equals_OutOfOrderRandomTest()
        {
            PolynomialGenome target = new PolynomialGenome(testCoefficientsPositive, testPolynomialGenomeTermsPositiveExponents);

            List<PolynomialGenomeTerm> otherPolynomialGenomeTerms = new List<PolynomialGenomeTerm>();
            foreach (PolynomialGenomeTerm term in testPolynomialGenomeTermsPositiveExponents)
            {
                int[] exponentsArray = term.Exponents.ToArray();
                otherPolynomialGenomeTerms.Add(new PolynomialGenomeTerm(exponentsArray, null));
            }
            List<double> otherCoefficients = new List<double>(testCoefficientsPositive.ToArray());
            for (int shuffleFromIndex = otherPolynomialGenomeTerms.Count - 1; shuffleFromIndex > 0; shuffleFromIndex--)
            {
                int shuffleToIndex = random.Next(shuffleFromIndex - 1);
                
                PolynomialGenomeTerm shuffleTerm = otherPolynomialGenomeTerms[shuffleFromIndex];
                otherPolynomialGenomeTerms[shuffleFromIndex] = otherPolynomialGenomeTerms[shuffleToIndex];
                otherPolynomialGenomeTerms[shuffleToIndex] = shuffleTerm;

                double shuffleCoefficient = otherCoefficients[shuffleFromIndex];
                otherCoefficients[shuffleFromIndex] = otherCoefficients[shuffleToIndex];
                otherCoefficients[shuffleToIndex] = shuffleCoefficient;
            }
            PolynomialGenome other = new PolynomialGenome(otherCoefficients, otherPolynomialGenomeTerms);
            //Debug.WriteLine(other.ToString());

            Assert.IsTrue(target.Equals(other), "Genome was not equal to Genome made with equal values.");
        }

        /// <summary>
        ///A test for Equals
        ///</summary>
        [TestMethod()]
        public void Equals_ObjectTest()
        {
            PolynomialGenome target = new PolynomialGenome(testCoefficientsPositive, testPolynomialGenomeTermsPositiveExponents);

            List<PolynomialGenomeTerm> otherPolynomialGenomeTerms = new List<PolynomialGenomeTerm>();
            foreach (PolynomialGenomeTerm term in testPolynomialGenomeTermsPositiveExponents)
            {
                int[] exponentsArray = term.Exponents.ToArray();
                otherPolynomialGenomeTerms.Add(new PolynomialGenomeTerm(exponentsArray, null));
            }
            List<double> otherCoefficients = new List<double>(testCoefficientsPositive.ToArray());
            object otherAsObject = new PolynomialGenome(otherCoefficients, otherPolynomialGenomeTerms);

            Assert.IsTrue(target.Equals(otherAsObject), "Genome was not equal to Genome (as object) made with equal values.");
        }

        /// <summary>
        ///A test for PerturbCoefficient
        ///</summary>
        [TestMethod()]
        public void PerturbCoefficient_Test()
        {
            PolynomialGenome target = new PolynomialGenome(testCoefficientsPositive, testPolynomialGenomeTermsPositiveExponents);
            List<double> savedCoefficients = new List<double>(target.Coefficients);
            List<PolynomialGenomeTerm> savedTerms = new List<PolynomialGenomeTerm>();
            foreach (PolynomialGenomeTerm term in target.Terms)
            {
                savedTerms.Add(term.DeepCopy());
            }
            double error = 0.0000001;
            double maxPerturbSize = 0.5; // target.MaxPerturbSize;
            Random random = new Random();
            int perturbedIndex = random.Next(target.Terms.Count);

            target.MutateValue(maxPerturbSize, perturbedIndex, false, null);

            // The perturbed coefficient must be between the original value minus the maximum perterbation and plus the maximum perterbation
            Assert.IsTrue(target.Coefficients[perturbedIndex] <= savedCoefficients[perturbedIndex] * (1.0 + maxPerturbSize), "perturbed coefficient is too large.");
            Assert.IsTrue(target.Coefficients[perturbedIndex] >= savedCoefficients[perturbedIndex] * (1.0 - maxPerturbSize), "perturbed coefficient is too small.");
            for (int i = 0; i < target.Terms.Count; i++)
            {
                Assert.AreEqual(target.Terms[perturbedIndex], savedTerms[perturbedIndex], "perturb coefficient changed a term.  Terms shouldn't change.");
                if (i != perturbedIndex)
                {
                    // No other coefficients or terms should have changed
                    Assert.AreEqual(target.Coefficients[i], savedCoefficients[i], error, "coefficient at non-perturbed index changed.");
                }
            }
        }
    }
}
