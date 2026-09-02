using ACESim;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.GameSolvingSupport.Settings;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;
using System.Linq;

namespace ACESimTest.GameTests
{
    [TestClass]
    public class LitigGameCorrelatedSignalsArticleLauncherTests
    {
        [TestMethod]
        public void ProductionMatrix_IsCompleteLeanUniqueAndCalibrated()
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher();
            var optionSets = launcher.GetOptionsSets();
            var audit = launcher.ValidateProductionMatrix(optionSets);

            audit.OptionSetCount.Should().Be(200);
            audit.CoreCombinationCount.Should().Be(50);
            audit.PairedComparisonCount.Should().Be(100);
            audit.CountsByInformationAndRisk.Should().BeEquivalentTo(new System.Collections.Generic.Dictionary<string, int>
            {
                ["0.5x|Risk Neutral"] = 50,
                ["1x|Risk Neutral"] = 50,
                ["2x|Risk Neutral"] = 50,
                ["1x|Moderately Risk Averse"] = 50,
            });

            optionSets.Select(x => x.Name).Should().OnlyHaveUniqueItems();
            optionSets.Select(x => x.Name).Should().OnlyContain(name =>
                name.Contains("Structure-") &&
                !name.Contains("old", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("new", StringComparison.OrdinalIgnoreCase));

            foreach (LitigGameOptions options in optionSets.Cast<LitigGameOptions>())
            {
                string structure = Setting(options, "Signal Structure");
                string informationLevel = Setting(options, "Information Level");
                var structureEnum = structure == LitigGameCorrelatedSignalsArticleLauncher.CaseQualityLabel
                    ? LitigGameCorrelatedSignalsArticleLauncher.ArticleSignalStructure.CaseQuality
                    : LitigGameCorrelatedSignalsArticleLauncher.ArticleSignalStructure.BinaryTruth;

                options.PLiabilityNoiseStdev.Should().BeApproximately(
                    LitigGameCorrelatedSignalsArticleLauncher.GetPartySigma(structureEnum, informationLevel),
                    1E-12);
                options.DLiabilityNoiseStdev.Should().Be(options.PLiabilityNoiseStdev);
                options.CourtLiabilityNoiseStdev.Should().BeApproximately(
                    LitigGameCorrelatedSignalsArticleLauncher.GetCourtSigma(structureEnum, informationLevel),
                    1E-12);
                options.NumOffers.Should().Be(10);
                options.NumLiabilitySignals.Should().Be(10);
                options.LiabilitySignalShapeParameters.Mode.Should().Be(SignalShapeMode.Identity);
                options.DamagesSignalShapeParameters.Mode.Should().Be(SignalShapeMode.Identity);

                if (structureEnum == LitigGameCorrelatedSignalsArticleLauncher.ArticleSignalStructure.CaseQuality)
                {
                    options.LitigGameDisputeGenerator.Should().BeOfType<LitigGameExogenousDisputeGenerator>();
                    options.NumLiabilityStrengthPoints.Should().Be(10);
                }
                else
                {
                    options.LitigGameDisputeGenerator.Should().BeOfType<LitigGameExogenousDirectSignalDisputeGenerator>();
                    options.NumLiabilityStrengthPoints.Should().Be(2);
                }
            }
        }

        [TestMethod]
        public void ProductionTaskPlan_ContainsOneUniqueOptimizeTaskPerOptionSet()
        {
            var firstLauncher = new LitigGameCorrelatedSignalsArticleLauncher();
            var secondLauncher = new LitigGameCorrelatedSignalsArticleLauncher();
            var coordinator = firstLauncher.GetUninitializedTaskList();

            coordinator.NumIndividualTasks.Should().Be(200);
            coordinator.Tasks.Should().OnlyContain(task =>
                task.TaskType == "Optimize" &&
                task.Repetition == 0 &&
                task.RestrictToScenarioIndex == null);
            coordinator.Tasks.Select(task => task.Identity).Should().OnlyHaveUniqueItems();
            coordinator.PlanFingerprint.Should().Be(secondLauncher.GetUninitializedTaskList().PlanFingerprint);

            var paths = firstLauncher.GetExpectedPrimaryResultPaths();
            paths.Should().HaveCount(200);
            paths.Select(path => path.ToUpperInvariant()).Should().OnlyHaveUniqueItems();
            paths.Should().OnlyContain(path => path.Length < 260);
        }

        [TestMethod]
        public void RetainedReportIdentifiers_EachSelectExactlyOneBaselineCoreOptionSet()
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher();
            var optionSets = launcher.GetOptionsSets();

            foreach (var set in launcher.GetSimulationSetsIdentifiers())
            {
                foreach (var identifier in set.simulationIdentifiers)
                {
                    var matches = optionSets.Where(option => identifier.columnMatches.All(match =>
                        option.VariableSettings.TryGetValue(match.columnName, out object actual) &&
                        string.Equals(
                            Convert.ToString(actual, CultureInfo.InvariantCulture),
                            match.expectedValue,
                            StringComparison.Ordinal))).ToList();
                    matches.Should().ContainSingle($"identifier {set.nameOfSet} / {identifier.nameForSimulation}");
                }
            }
        }

        [TestMethod]
        public void BothSignalStructures_SetUpAtEveryInformationLevelWithoutSolving()
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher();
            var representativeOptions = launcher.GetOptionsSets()
                .Cast<LitigGameOptions>()
                .Where(options =>
                    Setting(options, "Costs Multiplier") == "1" &&
                    Setting(options, "Fee Shifting Multiplier") == "0" &&
                    Setting(options, "Risk Aversion") == "Risk Neutral")
                .ToList();

            representativeOptions.Should().HaveCount(6);
            foreach (LitigGameOptions options in representativeOptions)
            {
                var definition = new LitigGameDefinition();
                Action setup = () => definition.Setup(options);
                setup.Should().NotThrow();
            }
        }

        private static string Setting(GameOptions options, string name) =>
            Convert.ToString(options.VariableSettings[name], CultureInfo.InvariantCulture);
    }
}
