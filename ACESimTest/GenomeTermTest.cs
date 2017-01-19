using ACESim;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace ACESimTest
{
    
    
    /// <summary>
    ///This is a test class for GenomeTermTest and is intended
    ///to contain all GenomeTermTest Unit Tests
    ///</summary>
    [TestClass()]
    public class GenomeTermTest
    {


        private TestContext testContextInstance;

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
        ///A test for DeepCopy
        ///</summary>
        [TestMethod()]
        public void DeepCopy_Test()
        {
            PolynomialGenomeTerm target = new PolynomialGenomeTerm(new int[] { 1, 2, 3 }, null);

            PolynomialGenomeTerm actual = target.DeepCopy();

            Assert.AreEqual(target, actual, "DeepCopy created an unequal copy.");
            for (int i = 0; i < target.Exponents.Count; i++)
            {
                Assert.AreEqual(target.Exponents[i], actual.Exponents[i], "DeepCopy copy has unequal exponents.");
            }
            Assert.AreNotSame(target, actual, "DeepCopy returned identical object, not a copy.");
            Assert.AreNotSame(target.Exponents, actual.Exponents, "DeepCopy returned identical Exponents, and did not copy them.");
        }

        /// <summary>
        ///A test for Calculate
        ///</summary>
        [TestMethod()]
        public void Calculate_PositiveTest()
        {
            PolynomialGenomeTerm target = new PolynomialGenomeTerm(new int[] { 1, 2, 3 }, null);
            List<double> inputs = new List<double>(new double[] { 2.0, 3.0, 4.0 });
            double expected = 1152.0; // 2^1 * 3^2 * 4^3 = 2 * 9 * 64 = 75
            double error = 0.00001;

            double actual = target.Calculate(inputs.ToArray());

            Assert.AreEqual(expected, actual, error, "Calculation did not return expected value.");
        }

        /// <summary>
        ///A test for Calculate using negative exponents and inputs
        ///</summary>
        [TestMethod()]
        public void Calculate_NegativesTest()
        {
            PolynomialGenomeTerm target = new PolynomialGenomeTerm(new int[] { 1, -2, -3 }, null);
            List<double> inputs = new List<double>(new double[] { -2.0, 3.0, -4.0 });
            double expected = 0.003472222222222; // -2^1 * 3^-2 * -4^-3 = -2 * 1/9 * -1/64 = 12
            double error = 0.000001;

            double actual = target.Calculate(inputs.ToArray());

            Assert.AreEqual(expected, actual, error, "Calculation did not return expected value.");
        }

        /// <summary>
        ///Test that Calculate raises an exception when a number of inputs that doesn't match the number of exponents is given.
        ///</summary>
        [TestMethod()]
        [ExpectedException(typeof(RankException), "Passing a number of inputs not equal to the number of exponents did NOT result in ArgumentException!")]
        public void Calculate_IncorrectInputsCountTest()
        {
            PolynomialGenomeTerm target = new PolynomialGenomeTerm(new int[] { 1, 2, 3 }, null);
            List<double> inputs = new List<double>(new double[] { 1.0, 2.0, 3.0, 4.0 });

            double actual = target.Calculate(inputs.ToArray());
        }

        /// <summary>
        ///A test for Equals
        ///</summary>
        [TestMethod()]
        public void Equals_ObjectTest()
        {
            PolynomialGenomeTerm target = new PolynomialGenomeTerm(new int[] { 1, 2, 3 }, null);
            object otherAsObject = new PolynomialGenomeTerm(new int[] { 1, 2, 3 }, null);

            Assert.IsTrue(target.Equals(otherAsObject), "GenomeTerm was not equal to GenomeTerm (as object) made with equal values.");
        }

        /// <summary>
        ///A test for Equals
        ///</summary>
        [TestMethod()]
        public void Equals_GenomeTest()
        {
            PolynomialGenomeTerm target = new PolynomialGenomeTerm(new int[] { 1, 2, 3 }, null);
            PolynomialGenomeTerm other = new PolynomialGenomeTerm(new int[] { 1, 2, 3 }, null);

            Assert.IsTrue(target.Equals(other), "GenomeTerm was not equal to equivalent GenomeTerm.");
        }
    }
}
