using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ACESim;
using ACESim.Util;
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
            MyGameRunningSideBets sideBets = new MyGameRunningSideBets()
            {
                TrialCostsMultiplierAsymptote = 3.0,
                TrialCostsMultiplierWithDoubleStakes = 1.3,
                ValueOfChip = 50_000
            };
            MyGameDefinition gameDefinition = new MyGameDefinition()
            {
                Options = new MyGameOptions()
                {
                    DamagesMin = 100_000,
                    DamagesMax = 100_000,
                    MyGameRunningSideBets = sideBets
                }
            };
            sideBets.Setup(gameDefinition);

            sideBets.GetTrialCostsMultiplier(gameDefinition, 0).Should().Be(1.0);
            sideBets.GetTrialCostsMultiplier(gameDefinition, 1).Should().BeGreaterThan(1.15);
            sideBets.GetTrialCostsMultiplier(gameDefinition, 2).Should().BeApproximately(1.3, 0.001);
            sideBets.GetTrialCostsMultiplier(gameDefinition, 999).Should().BeApproximately(3.0, 0.1);

            sideBets = new MyGameRunningSideBets()
            {
                TrialCostsMultiplierAsymptote = 1.0,
                TrialCostsMultiplierWithDoubleStakes = 1.0,
                ValueOfChip = 50_000
            };
            gameDefinition = new MyGameDefinition()
            {
                Options = new MyGameOptions()
                {
                    DamagesMin = 100_000,
                    DamagesMax = 100_000,
                    MyGameRunningSideBets = sideBets
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
    }
}
