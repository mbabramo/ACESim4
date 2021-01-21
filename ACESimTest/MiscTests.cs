using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ACESim;
using ACESim.Util;
using ACESimBase.GameSolvingSupport;
using ACESimBase.Util;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public class MiscTests
    {
        [TestMethod]
        public void TrialCostsMultiplierWorks()
        {
            LitigGameRunningSideBets sideBets = new LitigGameRunningSideBets()
            {
                TrialCostsMultiplierAsymptote = 3.0,
                TrialCostsMultiplierWithDoubleStakes = 1.3,
                ValueOfChip = 50_000
            };
            LitigGameDefinition gameDefinition = new LitigGameDefinition()
            {
                GameOptions = new LitigGameOptions()
                {
                    DamagesMin = 100_000,
                    DamagesMax = 100_000,
                    LitigGameRunningSideBets = sideBets
                }
            };
            sideBets.Setup(gameDefinition);

            sideBets.GetTrialCostsMultiplier(gameDefinition, 0).Should().Be(1.0);
            sideBets.GetTrialCostsMultiplier(gameDefinition, 1).Should().BeGreaterThan(1.15);
            sideBets.GetTrialCostsMultiplier(gameDefinition, 2).Should().BeApproximately(1.3, 0.001);
            sideBets.GetTrialCostsMultiplier(gameDefinition, 999).Should().BeApproximately(3.0, 0.1);

            sideBets = new LitigGameRunningSideBets()
            {
                TrialCostsMultiplierAsymptote = 1.0,
                TrialCostsMultiplierWithDoubleStakes = 1.0,
                ValueOfChip = 50_000
            };
            gameDefinition = new LitigGameDefinition()
            {
                GameOptions = new LitigGameOptions()
                {
                    DamagesMin = 100_000,
                    DamagesMax = 100_000,
                    LitigGameRunningSideBets = sideBets
                }
            };
            sideBets.Setup(gameDefinition);

            sideBets.GetTrialCostsMultiplier(gameDefinition, 0).Should().Be(1.0);
            sideBets.GetTrialCostsMultiplier(gameDefinition, 1).Should().Be(1.0);
            sideBets.GetTrialCostsMultiplier(gameDefinition, 2).Should().Be(1.0);
        }

        [TestMethod]
        public void SubdivisionDisaggregationWorks()
        {
            // Our subdivision calculations disaggregate one-based values into one-based actions.
            // Example with base 2 and two levels (listing the first, most significant level first):
            // Value 1 => 1,1
            // Value 2 => 1,2
            // Value 3 => 2,1
            // Value 4 => 2,2

            SubdivisionCalculations.GetOneBasedDisaggregatedAction(oneBasedAggregateValue: 1, oneBasedDisaggregatedLevel: 1, numLevels: 2, numOptionsPerLevel: 2).Should().Be(1);
            SubdivisionCalculations.GetOneBasedDisaggregatedAction(oneBasedAggregateValue: 1, oneBasedDisaggregatedLevel: 2, numLevels: 2, numOptionsPerLevel: 2).Should().Be(1);
            SubdivisionCalculations.GetOneBasedDisaggregatedAction(oneBasedAggregateValue: 2, oneBasedDisaggregatedLevel: 1, numLevels: 2, numOptionsPerLevel: 2).Should().Be(1);
            SubdivisionCalculations.GetOneBasedDisaggregatedAction(oneBasedAggregateValue: 2, oneBasedDisaggregatedLevel: 2, numLevels: 2, numOptionsPerLevel: 2).Should().Be(2);
            SubdivisionCalculations.GetOneBasedDisaggregatedAction(oneBasedAggregateValue: 3, oneBasedDisaggregatedLevel: 1, numLevels: 2, numOptionsPerLevel: 2).Should().Be(2);
            SubdivisionCalculations.GetOneBasedDisaggregatedAction(oneBasedAggregateValue: 3, oneBasedDisaggregatedLevel: 2, numLevels: 2, numOptionsPerLevel: 2).Should().Be(1);
            SubdivisionCalculations.GetOneBasedDisaggregatedAction(oneBasedAggregateValue: 4, oneBasedDisaggregatedLevel: 1, numLevels: 2, numOptionsPerLevel: 2).Should().Be(2);
            SubdivisionCalculations.GetOneBasedDisaggregatedAction(oneBasedAggregateValue: 4, oneBasedDisaggregatedLevel: 2, numLevels: 2, numOptionsPerLevel: 2).Should().Be(2);
        }

        [TestMethod]
        public void RiskAversionWorks()
        {
            foreach (var calculator in new (string, UtilityCalculator)[] {("log", new LogRiskAverseUtilityCalculator()), ("CARA1", new CARARiskAverseUtilityCalculator() {Alpha = 1.0 / 1000000.0}), ("CARA5", new CARARiskAverseUtilityCalculator() {Alpha = 5.0 / 1000000.0}), ("CARA10", new CARARiskAverseUtilityCalculator() {Alpha = 10.0 / 1000000.0}) })
            {
                Debug.WriteLine(calculator.Item1);
                double baseline = calculator.Item2.GetSubjectiveUtilityForWealthLevel(1.0 * 1_000_000);
                for (double p = 0.1; p <= 2.0; p += 0.1)
                {
                    Debug.WriteLine($"{p} => {calculator.Item2.GetSubjectiveUtilityForWealthLevel(p * 1_000_000)}");

                }
                double lossOfUtilityTo900_000 = baseline - calculator.Item2.GetSubjectiveUtilityForWealthLevel(1.0 * 900_000);
                double lossOfUtilityTo800_000 = baseline - calculator.Item2.GetSubjectiveUtilityForWealthLevel(1.0 * 800_000);
                double ratio = lossOfUtilityTo800_000 / lossOfUtilityTo900_000;
                Debug.WriteLine($"{ratio}");
                //Debug.WriteLine($"{calculator.Item2.GetSubjectiveUtilityForWealthLevel(1.1 * 1_000_000) / calculator.Item2.GetSubjectiveUtilityForWealthLevel(0.9 * 1_000_000)}");
            }
        }

        [TestMethod]
        public void SpanBitArrayWorks()
        {
            byte[] bytes = new byte[100];
            SpanBitArray.Set(bytes, 799, true);
            SpanBitArray.Get(bytes, 799).Should().BeTrue();
            SpanBitArray.Get(bytes, 798).Should().BeFalse();
            SpanBitArray.Set(bytes, 798, true);
            SpanBitArray.Get(bytes, 799).Should().BeTrue();
            SpanBitArray.Get(bytes, 798).Should().BeTrue();
            SpanBitArray.Set(bytes, 798, false);
            SpanBitArray.Get(bytes, 799).Should().BeTrue();
            SpanBitArray.Get(bytes, 798).Should().BeFalse();
        }

        [TestMethod]
        public void DoubleListWorks()
        {
            // checking whether it can be used as a Hash
            DoubleList d = new DoubleList(new double[] { 0.3, 0.4 });
            DoubleList d2 = new DoubleList(new double[] { 0.3, 0.4 });
            Dictionary<DoubleList, string> dict = new Dictionary<DoubleList, string>();
            dict[d] = "Yes";
            dict[d2].Should().Be("Yes");
            d.GetHashCode().Should().Be(d2.GetHashCode());
        }

        [TestMethod]
        public void CheckConfidenceInterval()
        {
            // this is a minimal check to make sure that our logit transformation works
            List<double> vals = Enumerable.Range(0, 100).Select(x => RandomGenerator.NextDouble() * 0.05).ToList();
            double lowerBound = ConfidenceInterval.GetBoundWithLogitIfNeeded(true, false, true, vals);
            double upperBound = ConfidenceInterval.GetBoundWithLogitIfNeeded(false, false, true, vals);
            double average = vals.Average();
            lowerBound.Should().BeLessThan(average);
            upperBound.Should().BeGreaterThan(average);
        }

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
                bool isNash = Matrix.ConfirmNash(rowPlayer, colPlayer, result[0], result[1]);
                if (!isNash)
                    throw new Exception("Not nash");
            }
        }

        [TestMethod]
        public void MergeCSVWorks()
        {
            string csv1 = @"""V1"",""V2"",""V3""
""A1"",""1.2"",""3.4""
";
            string csv2 = @"""V1"",""V3"",""U4"",""U5""
""A1"",""5.6"",""V4a1"",""V5a1""
""A2"",,""V4a2"",""V5a2""
";
            string expected = @"""V1"",""V2"",""V3"",""U4"",""U5""
""A1"",""1.2"",""5.6"",""V4a1"",""V5a1""
""A2"","""","""",""V4a2"",""V5a2""
";
            string result = DynamicUtilities.MergeCSV(csv1, csv2, new List<string>() { "V1" });
            result.Should().Be(expected);
        }

        [TestMethod]
        public void SubdivisionAggregationReversesDisaggregation()
        {
            const byte maxLevel = 3;
            for (byte numOptionsPerLevel = 2; numOptionsPerLevel <= 5; numOptionsPerLevel++)
            for (byte numLevels = 1; numLevels <= maxLevel; numLevels++)
            {
                byte maxOneBasedValue = (byte) Math.Pow(numOptionsPerLevel, numLevels);
                for (byte oneBasedValueToTest = 1; oneBasedValueToTest <= maxOneBasedValue; oneBasedValueToTest++)
                {
                    List<byte> disaggregated = new List<byte>();
                    byte aggregatedValue = 0; // zero-based until final level
                    for (byte level = 1; level <= numLevels; level++)
                    {
                        byte oneBasedDisaggregatedAction = SubdivisionCalculations.GetOneBasedDisaggregatedAction(oneBasedValueToTest, level, numLevels, numOptionsPerLevel);
                        disaggregated.Add(oneBasedDisaggregatedAction);
                        aggregatedValue = SubdivisionCalculations.GetAggregatedDecision(aggregatedValue, oneBasedDisaggregatedAction, numOptionsPerLevel, level == numLevels);
                    }
                    aggregatedValue.Should().Be(oneBasedValueToTest);
                }
            }

        }

        [TestMethod]
        public void EFGProcess_PlayersReceivingInformation()
        {
            string playerDoesntKnowChance = $@"
EFG 2 R ""Sample"" {{ ""Player 1"" ""Player 2"" }}
c ""ROOT"" 1 ""(0,1)"" {{ ""1G"" 0.500000 ""1B"" 0.500000 }} 0
p """" 1 1 """" {{ ""L"" ""R"" }} 0
t """" 1 ""Outcome 1"" {{ 2.000000 4.000000 }}
t """" 2 ""Outcome 2"" {{ 2.000000 4.000000 }}
p """" 1 1 """" {{ ""R"" ""R"" }} 0
t """" 3 ""Outcome 3"" {{ 4.000000 5.000000 }}
t """" 4 ""Outcome 4"" {{ 4.000000 1.000000 }}
";

            EFGProcess process = new EFGProcess();
            var tree1 = process.GetEFGFileNodesTree(playerDoesntKnowChance);
            tree1.GetInformationSet().PlayersReceivingInfo.Count().Should().Be(0);

            string playerKnowsChance = $@"
EFG 2 R ""Sample"" {{ ""Player 1"" ""Player 2"" }}
c ""ROOT"" 1 ""(0,1)"" {{ ""1G"" 0.500000 ""1B"" 0.500000 }} 0
p """" 1 1 """" {{ ""L"" ""R"" }} 0
t """" 1 ""Outcome 1"" {{ 2.000000 4.000000 }}
t """" 2 ""Outcome 2"" {{ 2.000000 4.000000 }}
p """" 1 2 """" {{ ""R"" ""R"" }} 0
t """" 3 ""Outcome 3"" {{ 4.000000 5.000000 }}
t """" 4 ""Outcome 4"" {{ 4.000000 1.000000 }}
"; // difference is that set player information set has a different number

            process = new EFGProcess();
            var tree2 = process.GetEFGFileNodesTree(playerKnowsChance);
            tree2.GetInformationSet().PlayersReceivingInfo.Count().Should().Be(1);
        }

            [TestMethod]
        public void EFGProcess_Overall()
        {

            string exampleGame = $@"
EFG 2 R ""General Bayes game, one stage"" {{ ""Player 1"" ""Player 2"" }}
c ""ROOT"" 1 ""(0,1)"" {{ ""1G"" 0.500000 ""1B"" 0.500000 }} 0
c """" 2 ""(0,2)"" {{ ""2g"" 0.500000 ""2b"" 0.500000 }} 0
p """" 1 1 ""(1,1)"" {{ ""H"" ""L"" }} 0
p """" 2 1 ""(2,1)"" {{ ""h"" ""l"" }} 0
t """" 1 ""Outcome 1"" {{ 10.000000 2.000000 }}
t """" 2 ""Outcome 2"" {{ 0.000000 10.000000 }}
p """" 2 1 ""(2,1)"" {{ ""h"" ""l"" }} 0
t """" 3 ""Outcome 3"" {{ 2.000000 4.000000 }}
t """" 4 ""Outcome 4"" {{ 4.000000 0.000000 }}
p """" 1 1 ""(1,1)"" {{ ""H"" ""L"" }} 0
p """" 2 2 ""(2,2)"" {{ ""h"" ""l"" }} 0
t """" 5 ""Outcome 5"" {{ 10.000000 2.000000 }}
t """" 6 ""Outcome 6"" {{ 0.000000 10.000000 }}
p """" 2 2 ""(2,2)"" {{ ""h"" ""l"" }} 0
t """" 7 ""Outcome 7"" {{ 2.000000 4.000000 }}
t """" 8 ""Outcome 8"" {{ 4.000000 0.000000 }}
c """" 3 ""(0,3)"" {{ ""2g"" 0.500000 ""2b"" 0.500000 }} 0
p """" 1 2 ""(1,2)"" {{ ""H"" ""L"" }} 0
p """" 2 1 ""(2,1)"" {{ ""h"" ""l"" }} 0
t """" 9 ""Outcome 9"" {{ 4.000000 2.000000 }}
t """" 10 ""Outcome 10"" {{ 2.000000 10.000000 }}
p """" 2 1 ""(2,1)"" {{ ""h"" ""l"" }} 0
t """" 11 ""Outcome 11"" {{ 0.000000 4.000000 }}
t """" 12 ""Outcome 12"" {{ 10.000000 2.000000 }}
p """" 1 2 ""(1,2)"" {{ ""H"" ""L"" }} 0
p """" 2 2 ""(2,2)"" {{ ""h"" ""l"" }} 0
t """" 13 ""Outcome 13"" {{ 4.000000 2.000000 }}
t """" 14 ""Outcome 14"" {{ 2.000000 10.000000 }}
p """" 2 2 ""(2,2)"" {{ ""h"" ""l"" }} 0
t """" 15 ""Outcome 15"" {{ 0.000000 4.000000 }}
t """" 16 ""Outcome 16"" {{ 10.000000 0.000000 }}
";
            EFGProcess process = new EFGProcess();
            var result = process.GetEFGFileNodesTree(exampleGame);
        }
    }
}
