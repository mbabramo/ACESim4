using System;
using System.Collections.Generic;
using System.Linq;
using ACESim;
using ACESim.Util;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public class MiscTests
    {
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
