using ACESim;
using ACESim.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ACESimTest
{
    /// <summary>
    ///This is a test class for GenomeTermCreatorTest and is intended
    ///to contain all GenomeTermCreatorTest Unit Tests
    ///</summary>
    [TestClass()]
    public class GenomeTermCreatorTest
    {
        TestContext testContextInstance;
        PolynomialGenome testInitialGenome;
        PolynomialGenomeTerm testNewTerm;

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

        //Use TestInitialize to run code before running each test
        [TestInitialize()]
        public void GenomeTermCreatorTestInitialize()
        {
            double[] coefficients = new double[] { 1.0, 2.0, 3.0 };
            PolynomialGenomeTerm[] terms = new PolynomialGenomeTerm[] {
                    new PolynomialGenomeTerm(new int[] { 1, 1 }, null),
                    new PolynomialGenomeTerm(new int[] { 2, 2 }, null),
                    new PolynomialGenomeTerm(new int[] { 1, 2 }, null)
                };
            testInitialGenome = new PolynomialGenome(coefficients, terms);

            testNewTerm = new PolynomialGenomeTerm(new int[] { 1, 1 }, null);
        }
        
        //Use TestCleanup to run code after each test has run
        [TestCleanup()]
        public void GenomeTermCreatorTestCleanup()
        {
            testInitialGenome = null;
            testNewTerm = null;
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
        ///Does CalculateWithNewTerm perform correctly on some test input?
        ///</summary>
        [TestMethod()]
        [DeploymentItem("ACESim.exe")]
        public void CalculateWithNewTerm_Test()
        {
            PolynomialGenomeSmallTermGenerator_Accessor target = new PolynomialGenomeSmallTermGenerator_Accessor();
            target.randomTestData = new double[][] {
                    new double[] {  1.0,  1.0 },
                    new double[] { 10.0, 10.0 }
                };
            PolynomialGenome initialGenome = testInitialGenome;
            PolynomialGenomeTerm newTerm = testNewTerm;
            double lowerBound = 1.0;
            double upperBound = 10.0;
            InputBounds[] inputsBounds = new InputBounds[] {
                    new InputBounds(1.0, 10.0),
                    new InputBounds(1.0, 10.0),
                };
            double proportionInBoundsRequired = 0.8;
            GenomeBounds theBounds =
                new GenomeBounds(inputsBounds, new Tuple<double, double>(lowerBound, upperBound), proportionInBoundsRequired);

            double possibleCoefficient = 2.0;
            double proportionInBounds;
            double average;

            // Calculations:
            //      for randomTestData[0]:
            //          initalGenome:
            //              1 * 1^1 * 1^1  +  2 * 1^2 * 1^2  +  3 * 1^1 * 1^2 = 6
            //          newTerm:
            //              2 * 1^1 * 1^1 = 2
            //          CalculateWithNewTerm:
            //              6 + 2 = 8 (in bounds)
            //      for randomTestData[1]:
            //          initalGenome:
            //              1 * 10^1 * 10^1  +  2 * 10^2 * 10^2  +  3 * 10^1 * 10^2 = 23,100
            //          newTerm:
            //              2 * 10^1 * 10^1 = 200
            //          CalculateWithNewTerm:
            //              23,300 (out of bounds)
            double proportionInBoundsExpected = 1 / 2;
            double averageExpected = (8.0 + 23300) / 2.0;
            
            target.CalculateWithNewTerm(initialGenome, newTerm, theBounds, possibleCoefficient, out proportionInBounds, out average);
            
            Assert.AreEqual(proportionInBoundsExpected, proportionInBounds);
            Assert.AreEqual(averageExpected, average, 100);
        }

        /// <summary>
        ///Does CalculateBoundsForTerm perofrm correctly on some test inpu?
        ///</summary>
        [TestMethod()]
        [DeploymentItem("ACESim.exe")]
        public void CalculateBoundsForTerm_Test()
        {
            PolynomialGenomeSmallTermGenerator_Accessor target = new PolynomialGenomeSmallTermGenerator_Accessor();
            target.randomTestData = new double[][] {
                    new double[] { 1.0, 1.0 },
                    new double[] { 1.0, 2.0 }
                };
            PolynomialGenome initialGenome = testInitialGenome;
            PolynomialGenomeTerm newTerm = testNewTerm;
            double lowerBound = 1.0;
            double upperBound = 25.0;
            InputBounds[] inputsBounds = new InputBounds[] {
                    new InputBounds(1.0, 10.0),
                    new InputBounds(1.0, 10.0),
                };
            double proportionInBoundsRequired = 0.8;  // With just two random test data, this effectively requires 100% in bounds.
            GenomeBounds theBounds = new GenomeBounds(inputsBounds, new Tuple<double, double>(lowerBound, upperBound), proportionInBoundsRequired);

            // Calculations:
            //      for randomTestData[0]:
            //          initalGenome:
            //              1 * 1^1 * 1^1  +  2 * 1^2 * 1^2  +  3 * 1^1 * 1^2 = 6
            //          newTerm:
            //              x * 1^1 * 1^1 = x
            //          CalculateWithNewTerm:
            //              6 + x
            //      for randomTestData[1]:
            //          initalGenome:
            //              1 * 1^1 * 2^1  +  2 * 1^2 * 2^2  +  3 * 1^1 * 2^2 = 22
            //          newTerm:
            //              x * 1^1 * 2^1 = 2x
            //          CalculateWithNewTerm:
            //              22 + 2x
            // Linear System:
            //
            //  1 < 6 + x  < 25
            //  1 < 22 + 2x < 25
            //
            //   -5 < x < 19
            //  -21 < 2x <  3
            //
            //     -5 < x <  19
            //  -10.5 < x < 1.5
            //
            //   -5 < x < 1.5
            Tuple<double, double> expected = new Tuple<double, double>(-5.0, 1.5);

            Tuple<double, double> actual = target.CalculateBoundsForTerm(initialGenome, newTerm, theBounds);

            Assert.IsTrue(Math.Abs((expected.Item1 - actual.Item1) / expected.Item1) <= RangeFinder.MaximumPrecision);
            Assert.IsTrue(Math.Abs((expected.Item2 - actual.Item2) / expected.Item2) <= RangeFinder.MaximumPrecision);
        }

        /// <summary>
        ///Does CalculateBoundsForTerm throw an InvalidGenomeException if the output bounds are too tight for 
        ///the input bounds and the initialGenome?  (Can no coefficient make the initialGenome + newTerm satisfy
        ///the bounds?)
        ///</summary>
        //[TestMethod()]
        //[DeploymentItem("ACESim.exe")]
        //[ExpectedException(typeof(InvalidGenomeException))]
        //public void CalculateBoundsForTerm_InvalidGenomeExceptionTest()
        //{
        //    GenomeTermCreator_Accessor target = new GenomeTermCreator_Accessor();
        //    target.randomTestData = new double[][] {
        //            new double[] { 1.0, 1.0 },
        //            new double[] { 1.0, 2.0 }
        //        };
        //    Genome initialGenome = testInitialGenome;
        //    GenomeTerm newTerm = testNewTerm;
        //    double lowerBound = 1.0;
        //    double upperBound = 20.0;
        //    InputBounds[] inputsBounds = new InputBounds[] {
        //            new InputBounds(1.0, 10.0),
        //            new InputBounds(1.0, 10.0),
        //        };
        //    double proportionInBoundsRequired = 0.8;  // With just two random test data, this effectively requires 100% in bounds.
        //    GenomeBounds theBounds = new GenomeBounds(inputsBounds, new Tuple<double, double>(lowerBound, upperBound), proportionInBoundsRequired);

        //    // Calculations:
        //    //      for randomTestData[0]:
        //    //          initalGenome:
        //    //              1 * 1^1 * 1^1  +  2 * 1^2 * 1^2  +  3 * 1^1 * 1^2 = 6
        //    //          newTerm:
        //    //              x * 1^1 * 1^1 = 2x
        //    //          CalculateWithNewTerm:
        //    //              6 + 2x
        //    //      for randomTestData[1]:
        //    //          initalGenome:
        //    //              1 * 1^1 * 2^1  +  2 * 1^2 * 2^2  +  3 * 1^1 * 2^2 = 22
        //    //          newTerm:
        //    //              x * 1^1 * 2^1 = 2x
        //    //          CalculateWithNewTerm:
        //    //              22 + 2x
        //    // Linear System:
        //    //
        //    //  1 < 6 + 2x  < 20
        //    //  1 < 22 + 2x < 20
        //    //
        //    //   -5 < 2x < 14
        //    //  -21 < 2x < -2
        //    //
        //    //   -2.5 < x < 7
        //    //  -10.5 < x < -1
        //    //
        //    //   -2.5 < x < -1
        //    Tuple<double, double> expected = new Tuple<double, double>(-2.5, -1);
            
        //    Tuple<double, double> actual = target.CalculateBoundsForTerm(initialGenome, newTerm, theBounds);
            
        //    Assert.AreEqual(expected, actual);
        //}

        /// <summary>
        ///Does CalculateBoundsForTerm work correctly when the initialGenome is empty?
        ///</summary>
        [TestMethod()]
        [DeploymentItem("ACESim.exe")]
        public void CalculateBoundsForTerm_EmptyGenomePositiveTest()
        {
            PolynomialGenomeSmallTermGenerator_Accessor target = new PolynomialGenomeSmallTermGenerator_Accessor();
            target.randomTestData = new double[][] {
                    new double[] { 1.0, 1.0 },
                    new double[] { 1.0, 2.0 }
                };

            PolynomialGenome initialGenome = new PolynomialGenome();

            PolynomialGenomeTerm newTerm = testNewTerm;

            double lowerBound = 1.0;
            double upperBound = 25.0;
            InputBounds[] inputsBounds = new InputBounds[] {
                    new InputBounds(1.0, 10.0),
                    new InputBounds(1.0, 10.0),
                };
            double proportionInBoundsRequired = 0.8;  // With just two random test data, this effectively requires 100% in bounds.
            GenomeBounds theBounds = new GenomeBounds(inputsBounds, new Tuple<double, double>(lowerBound, upperBound), 
                proportionInBoundsRequired);

            // Calculations:
            //      for randomTestData[0]:
            //          initalGenome:
            //              0
            //          newTerm:
            //              x * 1^1 * 1^1 = x
            //          CalculateWithNewTerm:
            //              x
            //      for randomTestData[1]:
            //          initalGenome:
            //              0
            //          newTerm:
            //              x * 1^1 * 2^1 = 2x
            //          CalculateWithNewTerm:
            //              2x
            // Linear System:
            //
            //  1 <  x < 25
            //  1 < 2x < 25
            //
            //    1 < x <   25
            //  0.5 < x < 12.5
            //
            //  1 < x < 12.5
            Tuple<double, double> expected = new Tuple<double, double>(1.0, 12.5);

            Tuple<double, double> actual = target.CalculateBoundsForTerm(initialGenome, newTerm, theBounds);

            Assert.IsTrue(RangeFinder.CalculatePrecision(expected.Item1, actual.Item1) <= RangeFinder.MaximumPrecision,
                String.Format("Lower bounds of actual bounds ({0}) and expected bounds ({1}) were not equal.", actual, expected));
            Assert.IsTrue(RangeFinder.CalculatePrecision(expected.Item2, actual.Item2) <= RangeFinder.MaximumPrecision,
                String.Format("Upper bounds of actual bounds ({0}) and expected bounds ({1}) were not equal.", actual, expected));
        }
    }
}
