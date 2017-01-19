using ACESim.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ACESimTest
{
    
    
    /// <summary>
    ///This is a test class for CombinationMakerTest and is intended
    ///to contain all CombinationMakerTest Unit Tests
    ///</summary>
    [TestClass()]
    public class CombinationMakerTest
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
        ///A test for CalculateCombinations
        ///</summary>
        [TestMethod()]
        public void CalculateCombinations_Test()
        {
            int maxComboSum = 2;
            int numPerCombo = 2;

            int[][] expectedArrayArray = new int[][] {
                new int[] {  2,  0 },
                new int[] {  0,  2 },
                new int[] {  1,  1 },
                new int[] {  1, -1 },
                new int[] {  0,  1 },
                new int[] {  1,  0 },
                new int[] {  0,  0 },
                new int[] { -1,  0 },
                new int[] {  0, -1 },
                new int[] { -1,  1 },
                new int[] { -1, -1 },
                new int[] { -2,  0 },
                new int[] {  0, -2 }
                };
            List<List<int>> expected = new List<List<int>>();
            foreach (int[] expectedArray in expectedArrayArray)
            {
                expected.Add(new List<int>(expectedArray));
            }

            List<List<int>> actual = CombinationMaker.CalculateCombinations(maxComboSum, numPerCombo, false);

            //Debug.WriteLine(String.Format(
            //    "Combinations: {0}",
            //    String.Join("\r\n", actual.Select(l => String.Join(",", l.Select(i => i.ToString()).ToArray())))
            //    ));

            Assert.AreEqual(expected.Count, actual.Count, "actual and expected combinations are not the same length.");
            List<List<int>> expectedRemaining = new List<List<int>>(expected);
            for (int actualIndex = 0; actualIndex < actual.Count; actualIndex++)
            {
                bool componentsEqual = true;
                for (int expectedIndex = 0; expectedIndex < expectedRemaining.Count; expectedIndex++)
                {
                    Assert.AreEqual(expectedRemaining[expectedIndex].Count, actual[actualIndex].Count, String.Format(
                        "Combinations are of unequal rank: ({0}) and ({1}).",
                        String.Join(",", expectedRemaining[expectedIndex].Select(t => t.ToString()).ToArray()),
                        String.Join(",", actual[actualIndex].Select(t => t.ToString()).ToArray())
                        ));

                    componentsEqual = true;
                    for (int i = 0; i < actual[actualIndex].Count; i++)
                    {
                        if (actual[actualIndex][i] != expectedRemaining[expectedIndex][i])
                        {
                            componentsEqual = false;
                            break;
                        }
                    }
                    if (componentsEqual)
                    {
                        expectedRemaining.RemoveAt(expectedIndex); // Only let an expected match once.
                        break;
                    }
                }
                Assert.IsTrue(componentsEqual, String.Format(
                    "No match found for ({0})",
                    String.Join(",", actual[actualIndex].Select(t => t.ToString()).ToArray())
                    ));
            }
        }
    }
}
