using ACESim.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;

namespace ACESimTest
{
    /// <summary>
    ///This is a test class for RangeFinderTest and is intended
    ///to contain all RangeFinderTest Unit Tests
    ///</summary>
    [TestClass()]
    public class RangeFinderTest
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


        //[TestMethod()]
        //public void InwardSearchForFirstValueWithProportionInBounds_PositiveTest()
        //{
        //    double initialGuess = 1.0;
        //    double lastGuess = 0.0;
        //    double exponentialFactor = 2.0;

        //    double boundary = 0.5;
        //    RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess) { return Math.Abs(guess) <= boundary; };

        //    double result = RangeFinder.InwardSearchForFirstValueWithProportionInBounds(initialGuess, lastGuess, 
        //        exponentialFactor, callback);

        //    Debug.WriteLine(String.Format("result: {0}", result));
        //    Assert.IsTrue(
        //        Math.Abs(result) > lastGuess, 
        //        String.Format("`result` ({0}) should have satisfied callback before reaching `around` ({1})", result, lastGuess));
        //    Assert.IsTrue(
        //        Math.Abs(result) <= boundary, 
        //        String.Format("`result` ({0}) was not within the `boundary` ({1})", result, boundary));
        //}


        //[TestMethod()]
        //public void InwardSearchForFirstValueWithProportionInBounds_NegativeTest()
        //{
        //    double insideOf = -1.0;
        //    double around = 0.0;
        //    double exponentialFactor = 2.0;
        //    double boundary = 0.5;
        //    RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess) { return Math.Abs(guess) <= boundary; };

        //    double result = RangeFinder.InwardSearchForFirstValueWithProportionInBounds(insideOf, around, exponentialFactor, callback);

        //    Debug.WriteLine(String.Format("result: {0}", result));
        //    Assert.IsTrue(
        //        Math.Abs(result) > around, 
        //        String.Format("`result` ({0}) should have satisfied callback before reaching `around` ({1})", result, around));
        //    Assert.IsTrue(
        //        Math.Abs(result) <= boundary, 
        //        String.Format("`result` ({0}) was not within the `boundary` ({1})", result, boundary));
        //}

        ///// <summary>
        ///// A test for FirstValue
        ///// </summary>
        //[TestMethod()]
        //public void FirstSatisfactoryValueByExponentialSearch_PositiveTest()
        //{
        //    double init = 1.0;
        //    double exponentialFactor = 2.0;
        //    Tuple<double, double> bounds = new Tuple<double, double>(20.8, 20.9);
        //    RangeFinder.RelationToBounds boundsRelationCallback = delegate(double guess) {
        //            if (guess < bounds.Item1)
        //                return RangeFinder.BoundsRelation.Below;
        //            else if (guess > bounds.Item2)
        //                return RangeFinder.BoundsRelation.Above;
        //            else
        //                return RangeFinder.BoundsRelation.Within;
        //        };
        //    RangeFinder.ProportionOfProportionInBounds proportionOfProportionInBoundsCallback = delegate(double guess) {
        //            return true; //Just to make life simple, always return true
        //        };

        //    double result = RangeFinder.FirstValueWithProportionInBounds(init, exponentialFactor,
        //        boundsRelationCallback, proportionOfProportionInBoundsCallback);

        //    Debug.WriteLine(String.Format("result: {0}", result));
        //    Assert.IsTrue(bounds.Item1 <= result && result <= bounds.Item2, String.Format("result ({0}) was not within the boundary ({1})", result, bounds));
        //}

        //[TestMethod()]
        //public void FirstSatisfactoryValueByExponentialSearch_NegativeTest()
        //{
        //    double initialGuess = -1.0;
        //    double exponentialFactor = 2.0;
        //    Tuple<double, double> bounds = new Tuple<double, double>(-20.9, -20.8);
        //    RangeFinder.RelationToBounds boundsRelationCallback = delegate(double guess)
        //    {
        //        if (guess < bounds.Item1)
        //            return RangeFinder.BoundsRelation.Below;
        //        else if (guess > bounds.Item2)
        //            return RangeFinder.BoundsRelation.Above;
        //        else
        //            return RangeFinder.BoundsRelation.Within;
        //    };
        //    RangeFinder.ProportionOfProportionInBounds proportionOfProportionInBoundsCallback = delegate(double guess)
        //    {
        //        return true; //Just to make life simple, always return true
        //    };

        //    double result = RangeFinder.FirstValueWithProportionInBounds(initialGuess, exponentialFactor,
        //        boundsRelationCallback, proportionOfProportionInBoundsCallback);

        //    Debug.WriteLine(String.Format("result: {0}", result));
        //    Assert.IsTrue(
        //        bounds.Item1 <= result && result <= bounds.Item2, 
        //        String.Format("result ({0}) was not within the boundary ({1})", result, bounds));
        //}

        ///// <summary>
        ///// The initial guess cannot be zero.
        ///// </summary>
        //[TestMethod()]
        //[ExpectedException(typeof(ArgumentOutOfRangeException))]
        //public void FirstSatisfactoryValueByExponentialSearch_InitialGuessIsZeroExceptionTest()
        //{
        //    double init = 0.0;
        //    double exponentialFactor = 2.0;
        //    Tuple<double, double> bounds = new Tuple<double, double>(20.8, 20.9);
        //    RangeFinder.RelationToBounds boundsRelationCallback = delegate(double guess)
        //    {
        //        return RangeFinder.BoundsRelation.Above;
        //    };
        //    RangeFinder.ProportionOfProportionInBounds proportionOfProportionInBoundsCallback = delegate(double guess)
        //    {
        //        return true; //Just to make life simple, always return true
        //    };

        //    double result = RangeFinder.FirstValueWithProportionInBounds(init, exponentialFactor,
        //        boundsRelationCallback, proportionOfProportionInBoundsCallback);
        //}

        ///// <summary>
        ///// If callback always returns Above (for a positive guess) then eventually the guess will reach zero, and should trigger an exception.
        ///// </summary>
        //[TestMethod()]
        //[ExpectedException(typeof(Exception))]
        //public void FirstValueInRangeByExponentialSearch_GuessReachedZeroExceptionTest()
        //{
        //    double init = 1.0;
        //    double exponentialFactor = 2.0;
        //    Tuple<double, double> bounds = new Tuple<double, double>(20.8, 20.9);
        //    RangeFinder.RelationToBounds boundsRelationCallback = delegate(double guess)
        //    {
        //        return RangeFinder.BoundsRelation.Above;
        //    };
        //    RangeFinder.ProportionOfProportionInBounds proportionOfProportionInBoundsCallback = delegate(double guess)
        //    {
        //        return true; //Just to make life simple, always return true
        //    };

        //    double result = RangeFinder.FirstValueWithProportionInBounds(init, exponentialFactor,
        //        boundsRelationCallback, proportionOfProportionInBoundsCallback);
        //}

        [TestMethod()]
        public void UncertaintySearch_PositiveOutwardsTest()
        {
            double initialGuess = 41.0;
            double center = 40.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(20.8, 120.9);
            RangeFinder.ProportionOfProportionInBounds proportionOfProportionInBounds = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, 
                RangeFinder.MaximumPrecision, proportionOfProportionInBounds);

            Assert.IsTrue(Math.Abs((result - bounds.Item2) / result) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item2,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_PositiveInwardsTest()
        {
            double initialGuess = 39.0;
            double center = 40.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(20.8, 120.9);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item1) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item1,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_NegativeOutwardsTest()
        {
            double init = -41.0;
            double around = -40.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(-120.9, -20.8);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(init, around, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item1) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item2,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_NegativeInwardsTest()
        {
            double initialGuess = -39.0;
            double center = -40.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(-120.9, -20.8);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item2) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item1,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_NegativeAcrossZeroTest()
        {
            double initialGuess = -39.0;
            double center = -40.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(-120.9, 20.8);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item2) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item1,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_PositiveAcrossZeroTest()
        {
            double initialGuess = 9.0;
            double center = 10.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(-120.9, 20.8);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item1) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item1,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_PositiveUpperEdgeTest()
        {
            double initialGuess = 20.0;
            double center = 10.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(10.0, 20.0);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item2) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item1,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_PositiveLowerEdgeTest()
        {
            double initialGuess = 10.0;
            double center = 20.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(10.0, 20.0);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item1) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item1,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_NegativeUpperEdgeTest()
        {
            double initialGuess = -10.0;
            double center = -20.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(-20.0, -10.0);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item2) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item1,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_NegativeLowerEdgeTest()
        {
            double initialGuess = -20.0;
            double center = -10.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(-20.0, -10.0);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item1) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item1,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_PositiveOvershotUpperTest()
        {
            double initialGuess = 200.0;
            double center = 40.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(20.8, 120.9);
            RangeFinder.ProportionOfProportionInBounds proportionOfProportionInBounds = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor,
                RangeFinder.MaximumPrecision, proportionOfProportionInBounds);

            Assert.IsTrue(Math.Abs((result - bounds.Item2) / result) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item2,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_PositiveOvershotLowerTest()
        {
            double initialGuess = 10.0;
            double center = 40.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(20.8, 120.9);
            RangeFinder.ProportionOfProportionInBounds proportionOfProportionInBounds = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor,
                RangeFinder.MaximumPrecision, proportionOfProportionInBounds);

            Assert.IsTrue(Math.Abs((result - bounds.Item1) / result) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item2,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_NegativeOvershotUpperTest()
        {
            double initialGuess = -10.0;
            double center = -40.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(-120.8, -20.9);
            RangeFinder.ProportionOfProportionInBounds proportionOfProportionInBounds = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor,
                RangeFinder.MaximumPrecision, proportionOfProportionInBounds);

            Assert.IsTrue(Math.Abs((result - bounds.Item2) / result) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item2,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_NegativeOvershotLowerTest()
        {
            double initialGuess = -200.0;
            double center = -40.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(-120.8, -20.9);
            RangeFinder.ProportionOfProportionInBounds proportionOfProportionInBounds = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor,
                RangeFinder.MaximumPrecision, proportionOfProportionInBounds);

            Assert.IsTrue(Math.Abs((result - bounds.Item1) / result) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item2,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_TinyRange()
        {
            double initialGuess = 1.0 + RangeFinder.MaximumPrecision;
            double center = 1.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(1.0, 1.0 + 2 * RangeFinder.MaximumPrecision);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item2) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item1,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_TooTinyRange()
        {
            double initialGuess = 1.0 + RangeFinder.MaximumPrecision / (2 * RangeFinder.MaximumPrecision);
            double center = 1.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(1.0, 1.0 + RangeFinder.MaximumPrecision / RangeFinder.MaximumPrecision);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item2) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item1,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }

        [TestMethod()]
        public void UncertaintySearch_DegenerateRangeTest()
        {
            double initialGuess = 1.1;
            double center = 1.0;
            double exponentialFactor = 2.0;
            Tuple<double, double> bounds = new Tuple<double, double>(1.1, 1.1);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(initialGuess, center, exponentialFactor, RangeFinder.MaximumPrecision, callback);

            Assert.IsTrue(RangeFinder.CalculatePrecision(result, bounds.Item2) <= RangeFinder.MaximumPrecision, String.Format(
                "result ({0}) was not equal to bound ({1}) within uncertainty ({2}) ({3}±{4})",
                result,
                bounds.Item1,
                RangeFinder.MaximumPrecision,
                result,
                result * RangeFinder.MaximumPrecision
                ));
        }


        [TestMethod()]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void UncertaintySearch_UncertaintyTooLowExceptionTest()
        {
            double init = -39.0;
            double around = -40.0;
            double exponentialFactor = 2.0;
            double requiredUncertainty = RangeFinder.MaximumPrecision / 10.0; // Uncertainty must be >= RangeFinder.MaximumPrecision
            Tuple<double, double> bounds = new Tuple<double, double>(-20.8, -120.9);
            RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
            {
                if (bounds.Item1 <= guess && guess <= bounds.Item2)
                    return 1.0;
                else
                    return 0.0;
            };

            double result = RangeFinder.UncertaintySearch(init, around, exponentialFactor, requiredUncertainty, callback);
        }

        //[TestMethod()]
        //[ExpectedException(typeof(ArgumentOutOfRangeException))]
        //public void UncertaintySearch_InitialGuessFailsCallbackTest()
        //{
        //    double initialGuess = -10.0;
        //    double center = -40.0;
        //    double exponentialFactor = 2.0;
        //    double requiredUncertainty = RangeFinder.MaximumPrecision;
        //    Tuple<double, double> bounds = new Tuple<double, double>(-20.8, -120.9);
        //    RangeFinder.ProportionOfProportionInBounds callback = delegate(double guess)
        //    {
        //        if (bounds.Item1 <= guess && guess <= bounds.Item2)
        //            return 1.0;
        //        else
        //            return 0.0;
        //    };

        //    double result = RangeFinder.UncertaintytSearch(initialGuess, center, exponentialFactor, requiredUncertainty, callback);
        //}
    }
}
