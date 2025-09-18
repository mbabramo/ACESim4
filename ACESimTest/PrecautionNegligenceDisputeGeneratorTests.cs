using ACESim;
using ACESimBase.Games.LitigGame;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Statistical;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using static Microsoft.CodeAnalysis.CSharp.SyntaxTokenParser;

namespace ACESimTest
{
    [TestClass]
    public class PrecautionNegligenceDisputeGeneratorTests : StrategiesDeveloperTestsBase
    {

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task CollapsingDecisionsGivesEquivalentUtilities(bool randomInformationSets, bool largerTree)
        {

            EvolutionSettings evolutionSettings = new EvolutionSettings();
            evolutionSettings.TotalIterations = 1;
            evolutionSettings.UnrollRepeatIdenticalRanges = false;

            byte branching = largerTree ? (byte)5 : (byte)2; // signals, precaution powers, and precaution levels
            var regular = LitigGameOptionsGenerator.PrecautionNegligenceGame(false, false, branching, 1, branching, branching);
            double[] regularUtilities = await GetUtilities(regular, "Regular", randomInformationSets, evolutionSettings);

            var collapsed = LitigGameOptionsGenerator.PrecautionNegligenceGame(true, false, branching, 1, branching, branching);
            double[] collapsedUtilities = await GetUtilities(collapsed, "Collapse", randomInformationSets, evolutionSettings);

            regularUtilities.Should().Equal(
                collapsedUtilities,
                (actualValue, expectedValue) =>
                    Math.Abs(actualValue - expectedValue) <= tolerance
            );
            // Note: Each execution should produce different runs (because string.GetHashCode()) is not consistent across runs, but they should match regardless.
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task CollapsingDecisionsAggregatesProperly(bool randomInformationSets, bool largerTree)
        {
            byte branching = largerTree ? (byte) 3 : (byte) 2; // signals, precaution powers, and precaution levels

            EvolutionSettings evolutionSettings = new EvolutionSettings();
            evolutionSettings.TotalIterations = 1;
            evolutionSettings.UnrollRepeatIdenticalRanges = false;

            var regular = LitigGameOptionsGenerator.PrecautionNegligenceGame(false, false, branching, 1, branching, branching);
            List<(double probability, PrecautionNegligenceProgress progress)> regularResults = await GetConsistentProgressForEveryGamePathAsync(regular, randomInformationSets, evolutionSettings);

            var collapsed = LitigGameOptionsGenerator.PrecautionNegligenceGame(true, false, branching, 1, branching, branching);
            List<(double probability, PrecautionNegligenceProgress progress)> collapsedResults = await GetConsistentProgressForEveryGamePathAsync(collapsed, randomInformationSets, evolutionSettings);

            regularResults.Sum(x => x.probability).Should().BeApproximately(1.0, tolerance);
            collapsedResults.Sum(x => x.probability).Should().BeApproximately(1.0, tolerance);

            // first, confirm equivalence for all signals taken together (for all as a whole, plus some subsets)
            ConfirmEquivalence(x => true);
            ConfirmEquivalence(x => x.EngagesInActivity);
            ConfirmEquivalence(x => x.AccidentOccurs);
            ConfirmEquivalence(x => x.AccidentWronglyCausallyAttributedToDefendant);
            ConfirmEquivalence(x => x.TrialOccurs);
            ConfirmEquivalence(x => x.PWinsAtTrial);

            // second, confirm equivalence for subsets defined by the combination of signals
            var signalValues = GetDistinctValues(regularResults, x => x.PLiabilitySignalDiscrete).OrderBy(x => x).ToList();
            foreach (var pSignalValue in signalValues)
            {
                foreach (var dSignalValue in signalValues)
                {
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue);
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue && x.EngagesInActivity);
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue && x.AccidentOccurs);
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue && x.AccidentWronglyCausallyAttributedToDefendant);
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue && x.TrialOccurs);
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue && x.PWinsAtTrial);
                }
            }

            // third, confirm equivalence by hidden state and the wrongful/true split when an accident occurs
            var hiddenValues = GetDistinctValues(regularResults, x => x.LiabilityStrengthDiscrete).OrderBy(x => x).ToList();
            foreach (var h in hiddenValues)
            {
                // Hidden-state marginal
                ConfirmEquivalence(x => x.LiabilityStrengthDiscrete == h);

                // Accident by hidden state
                ConfirmEquivalence(x => x.LiabilityStrengthDiscrete == h && x.AccidentOccurs);

                // Wrongful vs. true attribution by hidden state (only meaningful when an accident occurs)
                ConfirmEquivalence(x => x.LiabilityStrengthDiscrete == h && x.AccidentOccurs && x.AccidentWronglyCausallyAttributedToDefendant);
                ConfirmEquivalence(x => x.LiabilityStrengthDiscrete == h && x.AccidentOccurs && !x.AccidentWronglyCausallyAttributedToDefendant);
            }

            // local helper functions
            HashSet<T> GetDistinctValues<T>(List<(double probability, PrecautionNegligenceProgress progress)> results, Func<PrecautionNegligenceProgress, T> selector)
                => new HashSet<T>(results.Select(x => selector(x.progress)));

            List<(double probability, PrecautionNegligenceProgress progress)> Filter(
                List<(double probability, PrecautionNegligenceProgress progress)> results,
                Func<PrecautionNegligenceProgress, bool> filterFunc)
                => results.Where(x => filterFunc(x.progress)).ToList();

            (List<(double probability, PrecautionNegligenceProgress progress)> regular, List<(double probability, PrecautionNegligenceProgress progress)> collapsed)
                FilterBoth(Func<PrecautionNegligenceProgress, bool> filterFunc)
                => (Filter(regularResults, filterFunc), Filter(collapsedResults, filterFunc));

            void ConfirmEquivalence(Func<PrecautionNegligenceProgress, bool> filterFunc)
            {
                ConfirmEquivalentProbabilities(filterFunc);
                int funcIndex = 0; // to help identify func in event of test failure
                foreach (Func<PrecautionNegligenceProgress, double?> func in new Func<PrecautionNegligenceProgress, double?>[]
                {
                    x => x.HarmCost,
                    x => x.OpportunityCost,
                    x => x.BenefitCostRatio,
                    x => x.DamagesAwarded,
                    x => x.TotalExpensesIncurred,
                    x => x.PWelfare,
                    x => x.DWelfare,
                    x => x.PLiabilitySignalDiscrete,
                    x => x.DLiabilitySignalDiscrete,
                    x => x.PLiabilitySignalDiscrete - x.DLiabilitySignalDiscrete,
                    x => x.LiabilityStrengthDiscrete,
                    x => x.RelativePrecautionLevel,
                })
                {
                    ConfirmEquivalentValues(filterFunc, func);
                    funcIndex++;
                }
            }

            void ConfirmEquivalentProbabilities(Func<PrecautionNegligenceProgress, bool> filterFunc)
            {
                var filtered = FilterBoth(filterFunc);
                double regularSum = filtered.regular.Sum(x => x.probability);
                double collapsedSum = filtered.collapsed.Sum(x => x.probability);
                regularSum.Should().BeApproximately(collapsedSum, tolerance);
            }

            void ConfirmEquivalentValues(Func<PrecautionNegligenceProgress, bool> filterFunc, Func<PrecautionNegligenceProgress, double?> valueFunc)
            {
                var filtered = FilterBoth(filterFunc);
                double? regularResult = filtered.regular.WeightedAverage(x => valueFunc(x.progress), x => x.probability);
                double? collapsedResult = filtered.collapsed.WeightedAverage(x => valueFunc(x.progress), x => x.probability);
                regularResult.Should().BeApproximately(collapsedResult, tolerance);
            }
        }

    }
}
