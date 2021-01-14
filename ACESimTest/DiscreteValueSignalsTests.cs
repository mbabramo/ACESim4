using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ACESim;
using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util;
using ACESimBase.Util.DiscreteProbabilities;
using FluentAssertions;
using JetBrains.Annotations;
using MathNet.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public class DiscreteValueSignalsTests
    {

        [TestMethod]
        public void DiscreteValueSignals_MoreNoiseObscuresTrueValues()
        {
            int numTrueValues = 2;
            int numSignals = 5;
            var minimalNoise = Helper(0.02);
            var mediumNoise = Helper(0.25);
            var highNoise = Helper(1.0);
            var veryHighNoise = Helper(10.0);
            // now, assume the lowest liability strength
            mediumNoise[0].Should().BeGreaterThan(minimalNoise[0]);
            highNoise[0].Should().BeGreaterThan(mediumNoise[0]);
            veryHighNoise[0].Should().BeGreaterThan(0.49);
            veryHighNoise[0].Should().BeLessThan(0.50);

            double[] Helper(double noise)
            {
                DiscreteValueSignalParameters dvsParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = numTrueValues, NumSignals = numSignals, StdevOfNormalDistribution = noise, SourcePointsIncludeExtremes = true };
                double[] probabilitiesSignal_HigherTrueValue = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(2, dvsParams);
                Math.Abs(probabilitiesSignal_HigherTrueValue.Sum() - 1.0).Should().BeLessThan(0.0001);
                double[] probabilitiesSignal_LowerTrueValue = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, dvsParams);
                Math.Abs(probabilitiesSignal_LowerTrueValue.Sum() - 1.0).Should().BeLessThan(0.0001);
                double[] probabilityHigherTrueValue_signalStrength = probabilitiesSignal_HigherTrueValue.Zip(probabilitiesSignal_LowerTrueValue, (tl, tnl) => tl / (tl + tnl)).ToArray();
                return probabilityHigherTrueValue_signalStrength;
            }
        }

        [TestMethod]
        public void DiscreteValueSignals_ManyQualityStrengths()
        {
            int numTrueValues = 20;
            int numSignals = 50;
            var minimalNoise = Helper(0.02);
            var mediumNoise = Helper(0.25);
            var highNoise = Helper(1.0);
            var veryHighNoise = Helper(10.0);
            // now, assume the lowest liability strength
            mediumNoise[0].Should().BeGreaterThan(minimalNoise[0]);
            highNoise[0].Should().BeGreaterThan(mediumNoise[0]);
            veryHighNoise[0].Should().BeGreaterThan(0.49);
            veryHighNoise[0].Should().BeLessThan(0.50);

            double[] Helper(double noise)
            {
                DiscreteValueSignalParameters dvsParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = numTrueValues, NumSignals = numSignals, StdevOfNormalDistribution = noise, SourcePointsIncludeExtremes = false };
                double[] probabilitiesSignal_HigherTrueValue = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(numTrueValues, dvsParams);
                Math.Abs(probabilitiesSignal_HigherTrueValue.Sum() - 1.0).Should().BeLessThan(0.0001);
                double[] probabilitiesSignal_LowerTrueValue = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, dvsParams);
                Math.Abs(probabilitiesSignal_LowerTrueValue.Sum() - 1.0).Should().BeLessThan(0.0001);
                double[] probabilityHigherTrueValue_signalStrength = probabilitiesSignal_HigherTrueValue.Zip(probabilitiesSignal_LowerTrueValue, (tl, tnl) => (tl == 0 && tnl == 0) ? 1 : tl / (tl + tnl)).ToArray();
                return probabilityHigherTrueValue_signalStrength;
            }
        }

        [TestMethod]
        public void DiscreteSignalsProbabilityMap()
        {
            int[] dimensions = new int[] { 2, 5 }; // 2 true values => 5 litigation quality levels 
            double[] prior = new double[] { 0.3, 0.7 };
            List<VariableProductionInstruction> variableProductionInstructions = new List<VariableProductionInstruction>()
            {
                new IndependentVariableProductionInstruction(dimensions, 0, prior),
                new DiscreteValueParametersVariableProductionInstruction(dimensions, 0 /* taking from two initial values */, true /* which map onto extreme values of 0 and 1 */, 0.2 /* adding noise */, 1), // produce 5 litigation quality levels based on 2 initial levels
            };
            double[] probabilities = DiscreteProbabilityDistribution.BuildProbabilityMap(dimensions, variableProductionInstructions);
            var qualityWhenTrueValueIs0 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 1, 0, 0);
            var qualityWhenTrueValueIs1 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 1, 0, 1);
            var trueValueWhenQualityIs2 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 0, 1, 2); // should be essentially equal to prior distribution
            trueValueWhenQualityIs2[0].Should().BeApproximately(prior[0], 0.0001);
            var trueValueWhenQualityIs3 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 0, 1, 3);
            var trueValueWhenQualityIs4 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 0, 1, 4); // should be very high probability of high true quality
            trueValueWhenQualityIs4[1].Should().BeGreaterThan(0.95);
            int[][] crossProducts = DiscreteProbabilityDistribution.GetAllPermutations(dimensions);
            var zipped = crossProducts.Zip(probabilities).ToList();
        }

        [TestMethod]
        public void DiscreteSignalsProbabilityMap_MultipleLevels()
        {
            int[] dimensions = new int[] { 2, 5, 10, 10, 2 }; // 2 true values => 5 litigation quality levels => 10 signal levels for plaintiff and defendant, 2 for judge
            double[] prior = new double[] { 0.3, 0.7 };

            List<VariableProductionInstruction> variableProductionInstructions = new List<VariableProductionInstruction>()
            {
                new IndependentVariableProductionInstruction(dimensions, 0, prior),
                new DiscreteValueParametersVariableProductionInstruction(dimensions, 0 /* taking from two initial values */, true /* which map onto extreme values of 0 and 1 */, 0.25 /* adding noise */, 1), // produce 5 litigation quality levels based on 2 initial levels
                new DiscreteValueParametersVariableProductionInstruction(dimensions, 1 /* values just produced */, false /* from zero to one but excluding extremes */, 0.5 /* adding noise */, 2),  // produce 10 signals based on 5 initial levels
                new DiscreteValueParametersVariableProductionInstruction(dimensions, 1 /* values just produced */, false /* from zero to one but excluding extremes */, 0.1 /* low noise */, 3), // produce 10 signals based on 5 initial levels
                new DiscreteValueParametersVariableProductionInstruction(dimensions, 1 /* values just produced */, false /* from zero to one but excluding extremes */, 0.3 /* medium noise */, 4), // produce 2 signals based on 5 initial levels
            };
            double[] probabilities = DiscreteProbabilityDistribution.BuildProbabilityMap(dimensions, variableProductionInstructions);
            int[][] crossProducts = DiscreteProbabilityDistribution.GetAllPermutations(dimensions);

            // now do things in reverse to make sure we get back to the beginning
            var unconditionalPartySignal = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 2);
            var highTrueValueAsAFunctionOfPartySignal = Enumerable.Range(0, 10).Select(x => DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 0, 2, x)[1]).ToArray();
            double probabilityTrueValue = Enumerable.Range(0, 10).Sum(x => unconditionalPartySignal[x] * highTrueValueAsAFunctionOfPartySignal[x]);
            probabilityTrueValue.Should().BeApproximately(prior[1], 0.0001);

            // figure out the probability distribution of one party's signals given the other party's.
            var secondProbabilitySignalsGivenThirdPartySignals = DiscreteProbabilityDistribution.CalculateConditionalProbabilities(probabilities, dimensions, 2, 3);
            var thirdProbabilitySignalsGivenSecondPartySignals = DiscreteProbabilityDistribution.CalculateConditionalProbabilities(probabilities, dimensions, 3, 2);

            // some other calculations (unverified)
            var qualityWhenTrueValueIs0 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 1, 0, 0);
            var partySignalWhenQualityIs0 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 2, 1, 0); // note that most extreme signal may be less likely because a 0 quality variable corresponds to something > 0
            var partySignalWhenTrueValueIs0 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 2, 0, 0);
            var trueValueWhenPartySignalIs7 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 0, 2, 7);
            var courtDecisionWhenTrueValueIs0 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 4, 0, 0);
            var courtDecisionWhenTrueValueIs1 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 4, 0, 1);

            double[][] courtDecisionGivenPartySignals = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 4, new List<int> { 2, 3 });
            (courtDecisionGivenPartySignals[99][1] > courtDecisionGivenPartySignals[0][1]).Should().BeTrue();

            // Note: The court signal makes it a non-Bayesian decisionmaker. It doesn't take into account the prior probabilities that the true value is 0 or 1. It's just a signal. But we could work back from the signal to figure out the probability of the true value. If the court uses this approach, then the court is a Bayesian decisionmaker (but still ignores the party signals). 
            var trueValueGivenCourtSignalOf0 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 0, 4, 0);
            var trueValueGivenCourtSignalOf1 = DiscreteProbabilityDistribution.CalculateProbabilitiesFromMap(probabilities, dimensions, 0, 4, 1);
        }

        [TestMethod]
        public void DiscreteSignalsProbabilityMap_Calculators()
        {
            double priorTrueValue0 = 0.3;
            int plaintiffsSignal = 6;
            int defendantsSignal = 9;
            int courtSignal = 1;
            int litigationQuality = 4;
            double[] defendantsSignalDistribution;
            double[] courtSignalDistribution;
            double[] litigationQualityDistribution;
            double[] trueValueDistribution;

            BuildCalculators();
            defendantsSignalDistribution[8].Should().BeGreaterThan(defendantsSignalDistribution[1]);
            courtSignalDistribution[1].Should().BeGreaterThan(0.50);
            litigationQualityDistribution[4].Should().BeGreaterThan(litigationQualityDistribution[0]);
            trueValueDistribution[1].Should().BeGreaterThan(0.95); // we've stacked the deck to a high value with high signals by plaintiff, defendant, and court, plus a prior distribution in favor of high true quality.

            priorTrueValue0 = 0.5;
            plaintiffsSignal = 5;
            defendantsSignal = 4;
            BuildCalculators();
            courtSignalDistribution[1].Should().BeApproximately(0.50, 0.0001);

            void BuildCalculators()
            {
                int[] dimensions = new int[] { 2, 5, 10, 10, 2 }; // 2 true values => 5 litigation quality levels => 10 signal levels for plaintiff and defendant, 2 for judge
                double[] prior = new double[] { priorTrueValue0, 1.0 - priorTrueValue0 };
                List<VariableProductionInstruction> variableProductionInstructions = new List<VariableProductionInstruction>()
                {
                    new IndependentVariableProductionInstruction(dimensions, 0, prior),
                    new DiscreteValueParametersVariableProductionInstruction(dimensions, 0 /* taking from two initial values */, true /* which map onto extreme values of 0 and 1 */, 0.25 /* adding noise */, 1), // produce 5 litigation quality levels based on 2 initial levels
                    new DiscreteValueParametersVariableProductionInstruction(dimensions, 1 /* values just produced */, false /* from zero to one but excluding extremes */, 0.1 /* low noise */, 2),  // produce 10 signals based on 5 initial levels
                    new DiscreteValueParametersVariableProductionInstruction(dimensions, 1 /* values just produced */, false /* from zero to one but excluding extremes */, 0.1 /* low noise */, 3), // produce 10 signals based on 5 initial levels
                    new DiscreteValueParametersVariableProductionInstruction(dimensions, 1 /* values just produced */, false /* from zero to one but excluding extremes */, 0.3 /* medium noise */, 4), // produce 2 signals based on 5 initial levels
                };
                int trueValueVariableIndex = 0;
                int litigationQualityVariableIndex = 1;
                int plaintiffSignalVariableIndex = 2;
                int defendantSignalVariableIndex = 3;
                int courtDecisionVariableIndex = 4;
                var calculatorsToProduce = new List<(int distributionVariableIndex, List<int> fixedVariableIndices)>()
                {
                    (defendantSignalVariableIndex, new List<int>() { plaintiffSignalVariableIndex }), // defendant's signal based on plaintiff's signal
                    (courtDecisionVariableIndex, new List<int>() { plaintiffSignalVariableIndex, defendantSignalVariableIndex}), // court liability based on plaintiff's and defendant's signals
                    (litigationQualityVariableIndex, new List<int>() { plaintiffSignalVariableIndex, defendantSignalVariableIndex, courtDecisionVariableIndex }), // litigation quality based on all of above
                    (trueValueVariableIndex, new List<int>() { plaintiffSignalVariableIndex, defendantSignalVariableIndex, courtDecisionVariableIndex, litigationQualityVariableIndex, }) // true value based on all of the above
                };
                double[] probabilities = DiscreteProbabilityDistribution.BuildProbabilityMap(dimensions, variableProductionInstructions);
                var calculators = DiscreteProbabilityDistribution.GetProbabilityMapCalculators(dimensions, variableProductionInstructions, calculatorsToProduce);

                int calculatorIndex = 0;
                defendantsSignalDistribution = calculators[calculatorIndex++](new List<int>() { plaintiffsSignal });
                courtSignalDistribution = calculators[calculatorIndex++](new List<int>() { plaintiffsSignal, defendantsSignal });
                litigationQualityDistribution = calculators[calculatorIndex++](new List<int>() { plaintiffsSignal, defendantsSignal, courtSignal });
                trueValueDistribution = calculators[calculatorIndex++](new List<int>() { plaintiffsSignal, defendantsSignal, courtSignal, litigationQuality });
            }
        }

    }
}