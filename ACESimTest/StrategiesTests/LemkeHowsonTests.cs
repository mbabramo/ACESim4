using System;
using System.Diagnostics;
using ACESimBase.Util.Mathematics;
using ACESimBase.Util.Randomization;
using ACESimBase.Util.Statistical;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest.StrategiesTests
{
    [TestClass]
    public class LemkeHowsonTests
    {
        [TestMethod]
        public void LemkeHowsonWorks1()
        {
            double[,] rowPlayer = new double[,]
            {
                {3, 1 },
                {1, 3 }
            };
            double[,] colPlayer = new double[,]
            {
                {1, 3 },
                {3, 1 }
            };
            double[] rowPlayerExpected = new double[] { 0.5, 0.5 };
            double[] colPlayerExpected = new double[] { 0.5, 0.5 };
            LemkeHowsonCheck(rowPlayer, colPlayer, rowPlayerExpected, colPlayerExpected);
        }

        [TestMethod]
        public void LemkeHowsonWorks2()
        {
            double[,] rowPlayer = new double[,]
            {
                {1, -1 },
                {-1, 1 }
            };
            double[,] colPlayer = new double[,]
            {
                {-1, 1 },
                {1, -1 }
            };
            double[] rowPlayerExpected = new double[] { 0.5, 0.5 };
            double[] colPlayerExpected = new double[] { 0.5, 0.5 };
            LemkeHowsonCheck(rowPlayer, colPlayer, rowPlayerExpected, colPlayerExpected);
        }

        [TestMethod]
        public void LemkeHowsonWorks3()
        {
            double[,] rowPlayer = new double[,]
            {
                { 3, 2 },
                {4, 1 }
            };
            double[,] colPlayer = new double[,]
            {
                {1, 3 },
                {2, 4 }
            };
            double[] rowPlayerExpected = new double[] { 1.0, 0 };
            double[] colPlayerExpected = new double[] { 0, 1};
            LemkeHowsonCheck(rowPlayer, colPlayer, rowPlayerExpected, colPlayerExpected);
        }

        [TestMethod]
        public void LemkeHowsonWorks3b()
        {
            double[,] rowPlayer = new double[,]
            {
                { 3, 1 },
                {1, 3 }
            };
            double[,] colPlayer = new double[,]
            {
                {1, 3 },
                {2, 1 }
            };
            double[] rowPlayerExpected = new double[] { 1.0 / 3.0, 2.0 / 3.0};
            double[] colPlayerExpected = new double[] { 0.5, 0.5 };
            LemkeHowsonCheck(rowPlayer, colPlayer, rowPlayerExpected, colPlayerExpected);
        }

        [TestMethod]
        public void LemkeHowsonWorks4()
        {
            // This uses an example from "Game Theory and Algorithms, Lecture 6: The Lemke-Howson Algorithm," by David Pritchard. It has a unique Nash equilibrium in mixed strategies.
            double[,] rowPlayer = new double[,]
            {
                {1, 3, 0},
                {0, 0, 2},
                {2, 1, 1 }
            };
            double[,] colPlayer = new double[,]
            {
                {2, 1, 0},
                {1, 3, 1},
                {0, 0, 3 }
            };
            double[] rowPlayerExpected = new double[] { 6.0 / 13.0, 3.0 / 13.0, 4.0 / 13.0 };
            double[] colPlayerExpected = new double[] { 1.0 / 9.0, 3.0 / 9.0, 5.0 / 9.0 };
            LemkeHowsonCheck(rowPlayer, colPlayer, rowPlayerExpected, colPlayerExpected);
        }

        [TestMethod]
        public void LemkeHowsonWorks_Random()
        {
            ConsistentRandomSequenceProducer ran = new ConsistentRandomSequenceProducer(0);
            int numRepetitions = 100; // NOTE: We do seem to have problems with numeric instability. If we set the number high enough, eventually we will get this. The ECTA algorithm avoids this because it uses exact arithmetic.
            int maxNumStrategies = 10;
            for (int repetition = 0; repetition < numRepetitions; repetition++)
            {
                int numRowStrategies = 2 + ran.NextInt(maxNumStrategies - 1), numColStrategies = 2 + ran.NextInt(maxNumStrategies - 1);
                double[,] rowPlayer = new double[numRowStrategies, numColStrategies];
                double[,] colPlayer = new double[numRowStrategies, numColStrategies];
                for (int r = 0; r < numRowStrategies; r++)
                {
                    for (int c = 0; c < numColStrategies; c++)
                    {
                        rowPlayer[r, c] = Math.Round(-10 + 20.0 * ran.NextDouble(), 1); // do an irrelevant scaling
                        colPlayer[r, c] = Math.Round(ran.NextDouble(), 1);
                    }
                }
                LemkeHowson tableaux = null;
                tableaux = new LemkeHowson(rowPlayer, colPlayer);

                if (repetition == 2848)
                {
                    // NOTE: This is where we have a problem. But the problem also occurs in the code that we translated from Python.
                    // I have created a GitHub issue to track that: https://github.com/drvinceknight/Nashpy/issues/83
                    // As noted there, I don't believe the problem is that the game is degenerate. 
                    // This problem occurs much more frequently with large matrices.
                    // If we can't fix it, one possibility is to implement the integer_pivoting_lex approach that Nashpy implements.
                    // Much of the code will be similar to what we have, but the integer pivoting code will take some work, because
                    // some of the Python functions used by that code are not readily available in C# (even with NumSharp).
                    Debug.WriteLine(rowPlayer.ToCodeStringPython());
                    Debug.WriteLine("");
                    Debug.WriteLine(colPlayer.ToCodeStringPython());
                    Debug.WriteLine("");
                    Debug.WriteLine(rowPlayer.ToCodeString());
                    Debug.WriteLine("");
                    Debug.WriteLine(colPlayer.ToCodeString());
                    Debug.WriteLine("");
                    Debug.WriteLine(rowPlayer.ToCodeStringSpaces());
                    Debug.WriteLine("");
                    Debug.WriteLine(colPlayer.ToCodeStringSpaces());
                    Debug.WriteLine("");
                }
                double[][] result_allPossibilities = null;
                try
                {
                    result_allPossibilities = tableaux.DoLemkeHowsonStartingAtAllPossibilities(10, 100_000, true);
                }
                catch (Exception ex)
                {
                    if (result_allPossibilities == null)
                        throw new Exception($"No eq found on repetition {repetition} with {numRowStrategies}, {numColStrategies} strategies ({ex.Message})");
                }
                ConfirmNash(rowPlayer, colPlayer, result_allPossibilities);
            }
        }

        private static void LemkeHowsonCheck(double[,] rowPlayer, double[,] colPlayer, double[] rowPlayerExpected, double[] colPlayerExpected)
        {
            int numStrategies = rowPlayer.GetLength(0) + colPlayer.GetLength(0);
            for (int i = 0; i <= numStrategies; i++)
            {
                Debug.WriteLine($"=======================================");
                Debug.WriteLine($"Check {i}");
                var tableaux = new LemkeHowson(rowPlayer, colPlayer);
                var result = i == numStrategies ? tableaux.DoLemkeHowsonStartingAtAllPossibilities(int.MaxValue, int.MaxValue) : tableaux.DoLemkeHowsonStartingAtLabel(i);
                ConfirmNash(rowPlayer, colPlayer, result);
                result[0].Should().BeEquivalentTo(rowPlayerExpected);
                result[1].Should().BeEquivalentTo(colPlayerExpected);
            }
        }

        private static void ConfirmNash(double[,] rowPlayer, double[,] colPlayer, double[][] result)
        {
            if (result != null)
            {
                bool isNash = rowPlayer.ConfirmNash(colPlayer, result[0], result[1]);
                if (!isNash)
                    throw new Exception("Not nash");
            }
        }

    }
}
