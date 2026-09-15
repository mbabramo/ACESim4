using ACESim;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.Util.Mathematics;
using FluentAssertions;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ACESimTest.GameTests
{
    [TestClass]
    public class LitigGameCorrelatedSignalsArticleLauncherTests
    {
        [TestMethod]
        public void DistributedCsvSuffixes_AreIndependentNormalizedAndCollisionSafe()
        {
            var reports = new ReportCollection(string.Empty, new System.Collections.Generic.List<string>
            {
                "first",
                "second",
                "third",
                "fourth",
            });
            reports.AddReportSuffix("-Eq1");
            reports.AddReportSuffix("-Eq2");
            reports.AddReportSuffix("Eq1-InformationSetActions");

            Launcher.GetCsvReportSuffix(reports, 0, "Option", string.Empty).Should().Be("-Eq1");
            Launcher.GetCsvReportSuffix(reports, 1, "Option", string.Empty).Should().Be("-Eq2");
            Launcher.GetCsvReportSuffix(reports, 2, "Option", string.Empty)
                .Should().Be("-Eq1-InformationSetActions");
            Launcher.GetCsvReportSuffix(reports, 3, "Option", "-rep1-scenarioall")
                .Should().Be("-Report4-rep1-scenarioall");

            var singleRecoveredEquilibrium = new ReportCollection(string.Empty, "first");
            singleRecoveredEquilibrium.AddReportSuffix(string.Empty);
            Launcher.GetCsvReportSuffix(
                    singleRecoveredEquilibrium,
                    0,
                    "Option",
                    string.Empty,
                    labelFirstReportAsFirstEquilibrium: true)
                .Should().Be("-Eq1");
        }

        [TestMethod]
        public void EquilibriumRecoveryReport_DistinguishesAttemptsRecoveriesAndDistinctProfiles()
        {
            string csv = SequenceForm.BuildEquilibriumRecoveryCsv(
                "Multiple Starts",
                new List<(double[] equilibrium, int frequency)>
                {
                    (new[] { 0.25, 0.75 }, 30),
                    (new[] { 0.50, 0.50 }, 20),
                },
                requestedPriors: 50,
                exactSolverAttempts: 7,
                inexactSolverAttempts: 49);

            string[] rows = csv.Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries);
            rows.Should().HaveCount(3);
            rows[0].Should().Contain("Requested Priors").And.Contain("Attempted Solves")
                .And.Contain("Verified Recoveries").And.Contain("Recovery Count");
            rows[1].Should().Contain(",50,56,49,7,50,2,1,30,0.59999999999999998,");
            rows[2].Should().Contain(",50,56,49,7,50,2,2,20,0.40000000000000002,");
        }

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

        [TestMethod]
        public void SupplementalPlan_ContainsOnlyTheTwentyFiveUniformBaselineRuns()
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.UniformBaselineSupplement);
            var options = launcher.GetOptionsSets().Cast<LitigGameOptions>().ToList();
            var audit = launcher.ValidateProductionMatrix(options.Cast<GameOptions>().ToList());

            launcher.MasterReportNameForDistributedProcessing.Should().Be("CS002U");
            audit.OptionSetCount.Should().Be(25);
            audit.CoreCombinationCount.Should().Be(25);
            options.Should().OnlyContain(option =>
                Setting(option, "Signal Structure") == LitigGameCorrelatedSignalsArticleLauncher.UniformQualityLabel &&
                Setting(option, "Information Level") == "1x" &&
                Setting(option, "Risk Aversion") == "Risk Neutral" &&
                option.LitigGameDisputeGenerator is LitigGameUniformQualityDisputeGenerator);
            options.Select(option => Setting(option, "Costs Multiplier")).Distinct().Should().HaveCount(5);
            options.Select(option => Setting(option, "Fee Shifting Multiplier")).Distinct().Should().HaveCount(5);
            EveryReportIdentifierShouldSelectOneOption(launcher);
        }

        [TestMethod]
        public void UnifiedPlan_ContainsAllThreeStructuresWithFullRobustnessParity()
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.UnifiedThreeStructure);
            var options = launcher.GetOptionsSets().Cast<LitigGameOptions>().ToList();
            var audit = launcher.ValidateProductionMatrix(options.Cast<GameOptions>().ToList());

            launcher.MasterReportNameForDistributedProcessing.Should().Be("CS002");
            audit.OptionSetCount.Should().Be(300);
            audit.CoreCombinationCount.Should().Be(75);
            audit.PairedComparisonCount.Should().Be(100);
            options.GroupBy(option => Setting(option, "Signal Structure"))
                .Should().HaveCount(3).And.OnlyContain(group => group.Count() == 100);
            options.Count(option => option.LitigGameDisputeGenerator is LitigGameUniformQualityDisputeGenerator)
                .Should().Be(100);
            options.Should().OnlyContain(option => option.VariableSettings.ContainsKey("Integration Method"));
            EveryReportIdentifierShouldSelectOneOption(launcher);
        }

        [TestMethod]
        public void FocusedPlan_CombinesCoreMatrixWithFourFinerOfferCases()
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits);
            var options = launcher.GetOptionsSets().Cast<LitigGameOptions>().ToList();
            var audit = launcher.ValidateProductionMatrix(options.Cast<GameOptions>().ToList());

            launcher.MasterReportNameForDistributedProcessing.Should().Be("CS004");
            audit.OptionSetCount.Should().Be(114);
            audit.CoreCombinationCount.Should().Be(10);
            audit.PairedComparisonCount.Should().Be(102);
            audit.FeeRegimeComparisonCount.Should().Be(57);
            audit.CountsByInformationAndRisk.Should().HaveCount(11);
            audit.CountsByInformationAndRisk[LitigGameCorrelatedSignalsArticleLauncher.FocusedBaselineLabel]
                .Should().Be(12);
            audit.CountsByInformationAndRisk["Moderate symmetric risk aversion"]
                .Should().Be(12);
            audit.CountsByInformationAndRisk
                .Where(pair =>
                    pair.Key != LitigGameCorrelatedSignalsArticleLauncher.FocusedBaselineLabel &&
                    pair.Key != "Moderate symmetric risk aversion")
                .Should().OnlyContain(pair => pair.Value == 10);
            options.Select(option => Setting(option, "Costs Multiplier"))
                .Distinct().Should().BeEquivalentTo("0.25", "0.5", "1", "2", "4");
            options.Select(option => Setting(option, "Fee Regime"))
                .Distinct().Should().BeEquivalentTo("American", "British");
            options.Where(option => option.NumOffers == 10).GroupBy(option => new
                {
                    Cost = Setting(option, "Costs Multiplier"),
                    Fee = Setting(option, "Fee Regime"),
                })
                .Should().HaveCount(10)
                .And.OnlyContain(group =>
                    group.Count() == 11 &&
                    group.Select(option => Setting(option, "Specification")).Distinct().Count() == 11);
            options.Where(option => option.NumOffers == 15)
                .Should().HaveCount(4)
                .And.OnlyContain(option =>
                    new[]
                    {
                        LitigGameCorrelatedSignalsArticleLauncher.FocusedBaselineLabel,
                        "Moderate symmetric risk aversion",
                    }.Contains(Setting(option, "Specification")) &&
                    Setting(option, "Costs Multiplier") == "1");
            options.Where(option => option.NumOffers == 15)
                .GroupBy(option => Setting(option, "Specification"))
                .Should().HaveCount(2)
                .And.OnlyContain(group =>
                    group.Select(option => Setting(option, "Fee Regime"))
                        .OrderBy(value => value)
                        .SequenceEqual(new[] { "American", "British" }));
            foreach (LitigGameOptions option in options)
            {
                var settings = new EvolutionSettings();
                option.ModifyEvolutionSettings.Should().NotBeNull();
                option.ModifyEvolutionSettings(settings);
                settings.GenerateInformationSetActionReport.Should().BeTrue();
                settings.UseExistingEquilibriaIfAvailable.Should().BeTrue();
            }

            options.Count(option =>
                Setting(option, "Information Level") == "0.5x" &&
                Setting(option, "Risk Aversion") == "Moderately Risk Averse")
                .Should().Be(10, "only the expressly combined specification has both changes");
            options.Where(option => Setting(option, "Risk Aversion") == "Moderately Risk Averse")
                .Select(option => Setting(option, "Specification")).Distinct()
                .Should().BeEquivalentTo(
                    "Moderate symmetric risk aversion",
                    "Low noise plus moderate risk aversion");
            launcher.GetSimulationSetsIdentifiers()
                .SelectMany(set => set.simulationIdentifiers)
                .Should().OnlyContain(identifier => identifier.columnMatches.All(match =>
                    match.columnName != "Fee Regime"),
                    "the fee multiplier already uniquely identifies the focused American/British regime");
            List<PermutationalLauncher.SimulationSetsIdentifier> allRowsVariations =
                launcher.GetSimulationSetsIdentifiers()
                    .Where(variation => FeeShiftingDataProcessing.SupportsAllCostRows(
                        launcher,
                        variation))
                    .ToList();
            allRowsVariations.Should().HaveCount(10);
            allRowsVariations.Should().OnlyContain(variation =>
                !variation.nameOfSet.Contains("offer-grid sensitivity", StringComparison.Ordinal));
            EveryReportIdentifierShouldSelectOneOption(launcher);
        }

        [TestMethod]
        public void FocusedPlan_EverySpecificationSetsUpAndIncludesDetailedOfferActions()
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits);
            var representativeOptions = launcher.GetOptionsSets().Cast<LitigGameOptions>()
                .Where(option =>
                    Setting(option, "Costs Multiplier") == "1" &&
                    Setting(option, "Fee Regime") == "American" &&
                    option.NumOffers == 10)
                .ToList();

            representativeOptions.Should().HaveCount(11);
            foreach (LitigGameOptions options in representativeOptions)
            {
                var definition = new LitigGameDefinition();
                Action setup = () => definition.Setup(options);
                setup.Should().NotThrow(Setting(options, "Specification"));
                var columns = definition.GetSimpleReportDefinitions().Single().ColumnItems
                    .Select(column => column.Name)
                    .ToList();
                columns.Should().Contain("POffer1Action1").And.Contain("DOffer1Action10");
                definition.DecisionsExecutionOrder.Count(decision =>
                    decision.Name.Contains("Offer", StringComparison.OrdinalIgnoreCase)).Should().Be(2);
            }
        }

        [TestMethod]
        public void MultipleEquilibriaPlan_UsesFiftyVerifiedStartsForAllSixCoreCases()
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.MultipleEquilibriaRobustness);
            var options = launcher.GetOptionsSets().Cast<LitigGameOptions>().ToList();
            var audit = launcher.ValidateProductionMatrix(options.Cast<GameOptions>().ToList());

            launcher.MasterReportNameForDistributedProcessing.Should().Be("CS004ME");
            audit.OptionSetCount.Should().Be(6);
            audit.FeeRegimeComparisonCount.Should().Be(2);
            foreach (var risk in options.GroupBy(option => Setting(option, "CARA Alpha")))
                risk.Select(LitigGameCorrelatedSignalsArticleLauncher.FeeRuleLabel)
                    .Should().BeEquivalentTo("American", "Trial Fee-Shifting", "Complete Fee-Shifting");
            options.Count(option => option.LoserPaysAfterAbandonment && option.LoserPaysAfterNonAnswer).Should().Be(2);
            options.Should().OnlyContain(option =>
                Setting(option, "Costs Multiplier") == "1" &&
                option.NumLiabilitySignals == 10 &&
                option.NumCourtLiabilitySignals == 2 &&
                option.NumOffers == 10);
            foreach (LitigGameOptions option in options)
            {
                var settings = new EvolutionSettings();
                option.ModifyEvolutionSettings.Should().NotBeNull();
                option.ModifyEvolutionSettings(settings);
                settings.SequenceFormNumPriorsToUseToGenerateEquilibria.Should().Be(50);
                settings.TryInexactArithmeticForAdditionalEquilibria.Should().BeTrue();
                settings.ThrowIfNotPerfectEquilibrium.Should().BeFalse();
                settings.GenerateInformationSetActionReport.Should().BeTrue();
            }
            launcher.GetExpectedPrimaryResultPaths().Should().OnlyContain(path =>
                path.EndsWith("-Eq1.csv", StringComparison.Ordinal));
            EveryReportIdentifierShouldSelectOneOption(launcher);
        }

        [TestMethod]
        public void RequiredArticleProductionPlans_KeepMultipleStartsSeparate()
        {
            LitigGameCorrelatedSignalsArticleLauncher.RequiredArticleProductionPlans.Should()
                .Equal(
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits,
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.ExitFeeShifting);
        }

        [TestMethod]
        public void IncreasedOfferGridPlan_IsTheFourIntegratedCasesAsAConvenientRerunSubset()
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.IncreasedOfferGridRobustness);
            var options = launcher.GetOptionsSets().Cast<LitigGameOptions>().ToList();
            var audit = launcher.ValidateProductionMatrix(options.Cast<GameOptions>().ToList());

            launcher.MasterReportNameForDistributedProcessing.Should().Be("CS005O15");
            audit.OptionSetCount.Should().Be(4);
            audit.FeeRegimeComparisonCount.Should().Be(2);
            options.Select(option => Setting(option, "Specification")).Distinct()
                .Should().BeEquivalentTo(
                    LitigGameCorrelatedSignalsArticleLauncher.FocusedBaselineLabel,
                    "Moderate symmetric risk aversion");
            options.Should().OnlyContain(option =>
                Setting(option, "Costs Multiplier") == "1" &&
                Setting(option, "Number of Signals") == "10" &&
                Setting(option, "Number of Court Signals") == "2" &&
                Setting(option, "Number of Offers") == "15" &&
                option.NumLiabilitySignals == 10 &&
                option.NumCourtLiabilitySignals == 2 &&
                option.NumOffers == 15);
            options.GroupBy(option => Setting(option, "Specification"))
                .Should().HaveCount(2)
                .And.OnlyContain(group =>
                    group.Select(option => Setting(option, "Fee Regime"))
                        .OrderBy(value => value)
                        .SequenceEqual(new[] { "American", "British" }));
            EveryReportIdentifierShouldSelectOneOption(launcher);
        }

        [TestMethod]
        public void CorrelatedSignalsArticle_RestoresPublishedCostsWithoutChangingEndogenousArticleCosts()
        {
            foreach (LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan plan in
                Enum.GetValues(typeof(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan)))
            {
                var articleOptions = (LitigGameOptions)new LitigGameCorrelatedSignalsArticleLauncher(plan)
                    .GetDefaultSingleGameOptions();
                articleOptions.PFilingCost.Should().BeApproximately(0.15, 1E-12);
                articleOptions.DAnswerCost.Should().BeApproximately(0.15, 1E-12);
                articleOptions.PTrialCosts.Should().BeApproximately(0.15, 1E-12);
                articleOptions.DTrialCosts.Should().BeApproximately(0.15, 1E-12);
                articleOptions.PerPartyCostsLeadingUpToBargainingRound.Should().BeApproximately(0, 1E-12);
                articleOptions.RoundSpecificBargainingCosts.Should().BeNull();
            }

            var endogenousArticleOptions = LitigGameOptionsGenerator.PrecautionNegligenceGame();
            endogenousArticleOptions.PFilingCost.Should().BeApproximately(0.10, 1E-12);
            endogenousArticleOptions.DAnswerCost.Should().BeApproximately(0.10, 1E-12);
            endogenousArticleOptions.PTrialCosts.Should().BeApproximately(0.10, 1E-12);
            endogenousArticleOptions.DTrialCosts.Should().BeApproximately(0.10, 1E-12);
            endogenousArticleOptions.PerPartyCostsLeadingUpToBargainingRound.Should().BeApproximately(0.10, 1E-12);
            endogenousArticleOptions.RoundSpecificBargainingCosts.Should().BeNull();
        }

        [TestMethod]
        public void FocusedPlan_CostTimingRedistributesThePublishedTotalAndNeverAddsBargainingCosts()
        {
            var options = new LitigGameCorrelatedSignalsArticleLauncher(
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits)
                .GetOptionsSets().Cast<LitigGameOptions>()
                .Where(option =>
                    Setting(option, "Costs Multiplier") == "1" &&
                    Setting(option, "Fee Regime") == "American")
                .ToList();

            foreach (LitigGameOptions option in options)
            {
                double expectedBeginningShare = Setting(option, "Specification") switch
                {
                    "All litigation costs avoidable at bargaining" => 0,
                    "All litigation costs sunk before bargaining" => 1,
                    _ => 0.5,
                };
                option.PFilingCost.Should().BeApproximately(0.30 * expectedBeginningShare, 1E-12);
                option.DAnswerCost.Should().BeApproximately(0.30 * expectedBeginningShare, 1E-12);
                option.PTrialCosts.Should().BeApproximately(0.30 * (1 - expectedBeginningShare), 1E-12);
                option.DTrialCosts.Should().BeApproximately(0.30 * (1 - expectedBeginningShare), 1E-12);
                option.PerPartyCostsLeadingUpToBargainingRound.Should().BeApproximately(0, 1E-12);
                option.RoundSpecificBargainingCosts.Should().BeNull();
            }
        }

        [TestMethod]
        public void FocusedTruthConditionedRobustness_RetainsPublishedOriginalModelPrimitives()
        {
            var option = new LitigGameCorrelatedSignalsArticleLauncher(
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits)
                .GetOptionsSets().Cast<LitigGameOptions>()
                .Single(candidate =>
                    Setting(candidate, "Specification") == "Truth-conditioned latent merits" &&
                    Setting(candidate, "Costs Multiplier") == "1" &&
                    Setting(candidate, "Fee Regime") == "American");

            option.PInitialWealth.Should().BeApproximately(10, 1E-12);
            option.DInitialWealth.Should().BeApproximately(10, 1E-12);
            option.DamagesMin.Should().BeApproximately(0, 1E-12);
            option.DamagesMax.Should().BeApproximately(1, 1E-12);
            option.DamagesMultiplier.Should().BeApproximately(1, 1E-12);
            option.NumOffers.Should().Be(10);
            option.NumLiabilityStrengthPoints.Should().Be(10);
            option.NumLiabilitySignals.Should().Be(10);
            option.IncludeEndpointsForOffers.Should().BeFalse();
            option.PLiabilityNoiseStdev.Should().BeApproximately(0.20, 1E-12);
            option.DLiabilityNoiseStdev.Should().BeApproximately(0.20, 1E-12);
            option.CourtLiabilityNoiseStdev.Should().BeApproximately(0.20, 1E-12);
            option.NumDamagesSignals.Should().Be(1);
            option.NumDamagesStrengthPoints.Should().Be(1);
            option.PDamagesNoiseStdev.Should().BeApproximately(0.10, 1E-12);
            option.DDamagesNoiseStdev.Should().BeApproximately(0.10, 1E-12);
            option.CourtDamagesNoiseStdev.Should().BeApproximately(0.15, 1E-12);
            option.PFilingCost.Should().BeApproximately(0.15, 1E-12);
            option.DAnswerCost.Should().BeApproximately(0.15, 1E-12);
            option.PTrialCosts.Should().BeApproximately(0.15, 1E-12);
            option.DTrialCosts.Should().BeApproximately(0.15, 1E-12);
            option.PerPartyCostsLeadingUpToBargainingRound.Should().BeApproximately(0, 1E-12);
            option.PFilingCost_PortionSavedIfDDoesntAnswer.Should().BeApproximately(0, 1E-12);
            option.NumPotentialBargainingRounds.Should().Be(1);
            option.BargainingRoundsSimultaneous.Should().BeTrue();
            option.SimultaneousOffersUltimatelyRevealed.Should().BeTrue();
            option.SkipFileAndAnswerDecisions.Should().BeFalse();
            option.IncludeAgreementToBargainDecisions.Should().BeFalse();
            option.AllowAbandonAndDefaults.Should().BeTrue();
            option.PredeterminedAbandonAndDefaults.Should().BeTrue();
            option.CollapseChanceDecisions.Should().BeTrue();
            option.CollapseAlternativeEndings.Should().BeTrue();
            option.PUtilityCalculator.Should().BeOfType<RiskNeutralUtilityCalculator>();
            option.DUtilityCalculator.Should().BeOfType<RiskNeutralUtilityCalculator>();
            option.RegretAversion.Should().BeApproximately(0, 1E-12);
            option.LiabilitySignalShapeParameters.Mode.Should().Be(SignalShapeMode.Identity);
            option.DamagesSignalShapeParameters.Mode.Should().Be(SignalShapeMode.Identity);

            var generator = option.LitigGameDisputeGenerator.Should()
                .BeOfType<LitigGameExogenousDisputeGenerator>().Subject;
            generator.ExogenousProbabilityTrulyLiable.Should().BeApproximately(0.5, 1E-12);
            generator.StdevNoiseToProduceLiabilityStrength.Should().BeApproximately(0.35, 1E-12);
        }

        private static void EveryReportIdentifierShouldSelectOneOption(
            LitigGameCorrelatedSignalsArticleLauncher launcher)
        {
            var optionSets = launcher.GetOptionsSets();
            foreach (var set in launcher.GetSimulationSetsIdentifiers())
            foreach (var identifier in set.simulationIdentifiers)
                optionSets.Count(option => identifier.columnMatches.All(match =>
                    option.VariableSettings.TryGetValue(match.columnName, out object actual) &&
                    string.Equals(
                        Convert.ToString(actual, CultureInfo.InvariantCulture),
                        match.expectedValue,
                        StringComparison.Ordinal))).Should().Be(
                    1,
                    $"identifier {set.nameOfSet} / {identifier.nameForSimulation} should be unique");
        }

        private static string Setting(GameOptions options, string name) =>
            Convert.ToString(options.VariableSettings[name], CultureInfo.InvariantCulture);
    }
}
