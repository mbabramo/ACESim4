using ACESim;
using ACESimBase.GameSolvingSupport.Settings;
using CsvHelper;
using FluentAssertions;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ACESimTest
{
    [TestClass]
    public class CorrelatedSignalsFocusedReportTests
    {
        [TestMethod]
        public void FocusedReport_ProducesValidatedTidyPairsFeesAndSignalStrategies()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "ACESim-focused-report-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits);
                List<GameOptions> options = launcher.GetOptionsSets();
                string numericalSource = Path.Combine(directory, "output.csv");
                string signalSource = Path.Combine(directory, "signal-output.csv");
                string numericalResults = Path.Combine(directory, "numerical-results.csv");
                string specificationPairs = Path.Combine(directory, "specification-pairs.csv");
                string feePairs = Path.Combine(directory, "fee-pairs.csv");
                string signalStrategies = Path.Combine(directory, "signal-strategies.csv");
                WriteNumericalSource(numericalSource, launcher, options);
                WriteSignalSource(signalSource, launcher, options);
                CorrelatedSignalsFocusedReport.AllocateMutualGiveUpInCsv(numericalSource);
                CorrelatedSignalsFocusedReport.AllocateMutualGiveUpInCsv(signalSource);

                CorrelatedSignalsFocusedReport.ValidationSummary summary =
                    CorrelatedSignalsFocusedReport.BuildAndValidate(
                        launcher,
                        numericalSource,
                        signalSource,
                        numericalResults,
                        specificationPairs,
                        feePairs,
                        signalStrategies);

                summary.NumericalResultCount.Should().Be(184);
                summary.SpecificationComparisonCount.Should().Be(172);
                summary.FeeRegimeComparisonCount.Should().Be(92);
                summary.SignalStrategyCount.Should().Be(3680);
                File.ReadLines(numericalResults).Should().HaveCount(185);
                File.ReadLines(specificationPairs).Should().HaveCount(173);
                File.ReadLines(feePairs).Should().HaveCount(93);
                File.ReadLines(signalStrategies).Should().HaveCount(3681);
                File.ReadAllText(numericalResults).Should()
                    .Contain("Settlement Conditional on Reaching Bargaining")
                    .And.Contain("Real Litigation Costs")
                    .And.Contain("Liability Transfer to Plaintiff")
                    .And.Contain("Fee-Shifting Transfer to Plaintiff")
                    .And.Contain(CorrelatedSignalsFocusedReport.DefendantExcessBurdenColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.PlaintiffRecoveryShortfallColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn)
                    .And.NotContain("False Positive Inaccuracy")
                    .And.NotContain("False Negative Inaccuracy");
                File.ReadAllText(signalStrategies).Should()
                    .Contain("Offer-Overlap Acceptance Conditional on Reaching Bargaining")
                    .And.Contain("P Offer 1 Action 10")
                    .And.Contain("D Offer 1 Action 10");

                Dictionary<string, string> numericalRow = ReadFirstRow(numericalResults);
                numericalRow["P Abandons"].Should().Be("0.05");
                numericalRow["D Defaults"].Should().Be("0.05");
                numericalRow[CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn]
                    .Should().Be("0.02");
                numericalRow[CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn]
                    .Should().Be("0.4");
                numericalRow[CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn]
                    .Should().Be("0.4");
                numericalRow[CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn]
                    .Should().Be("0.1");
                numericalRow[CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn]
                    .Should().Be("0.45");
                Dictionary<string, string> signalRow = ReadFirstRow(signalStrategies);
                signalRow["P Abandonment Probability Unconditional"].Should().Be("0.05");
                signalRow["D Default Probability Unconditional"].Should().Be("0.05");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public void ExitFeeReport_IncludesNonanswerAndLaterExitTransfersWithoutAmericanDuplicateRuns()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ACESim-exit-fees-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.ExitFeeShifting);
                var options = launcher.GetOptionsSets();
                string numerical = Path.Combine(directory, "source.csv");
                string signals = Path.Combine(directory, "signals.csv");
                string results = Path.Combine(directory, "results.csv");
                WriteNumericalSource(numerical, launcher, options, asymmetricExit: true);
                WriteSignalSource(signals, launcher, options);
                var summary = CorrelatedSignalsFocusedReport.BuildAndValidate(launcher,
                    numerical, signals, results, Path.Combine(directory, "preferences.csv"),
                    Path.Combine(directory, "fees.csv"), Path.Combine(directory, "strategies.csv"));
                summary.NumericalResultCount.Should().Be(92);
                summary.SpecificationComparisonCount.Should().Be(86);
                summary.FeeRegimeComparisonCount.Should().Be(0);
                summary.SignalStrategyCount.Should().Be(1840);
                var row = ReadFirstRow(results);
                // Trial win/loss masses cancel. The fixture has 10% nonanswers and,
                // after allocating mutual exit, 8% defaults versus 2% abandonment.
                double cost = double.Parse(row["Costs Multiplier"], CultureInfo.InvariantCulture);
                double.Parse(row["Fee-Shifting Transfer to Plaintiff"], CultureInfo.InvariantCulture)
                    .Should().BeApproximately((0.1 + 0.08 - 0.02) * 0.30 *
                        double.Parse(row["Proportion of Costs at Beginning"], CultureInfo.InvariantCulture) * cost, 1E-12);
                File.Exists(Path.Combine(directory, "fees.csv")).Should().BeFalse();
            }
            finally { Directory.Delete(directory, recursive: true); }
        }

        [TestMethod]
        public void MutualGiveUpAllocation_PreservesUndefinedZeroMassFilterRows()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "ACESim-mutual-give-up-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                File.WriteAllLines(path, new[]
                {
                    $"Filter,P Abandons,D Defaults,{CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn}",
                    "Zero Mass Filter,,,",
                    "Positive Mass Filter,0.1,0.2,0.4",
                });

                CorrelatedSignalsFocusedReport.AllocateMutualGiveUpInCsv(path);

                string[] lines = File.ReadAllLines(path);
                lines[1].Should().Be("Zero Mass Filter,,,");
                lines[2].Should().Be("Positive Mass Filter,0.3,0.4,0.4");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ExitFeeCharts_SelectMatchedUnconditionalControlsAndPopulationWeightComponents()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ACESim-exit-chart-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string[] results = new string[2];
                var plans = new[] { LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits,
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.ExitFeeShifting };
                for (int i = 0; i < plans.Length; i++)
                {
                    var launcher = new LitigGameCorrelatedSignalsArticleLauncher(plans[i]);
                    var options = launcher.GetOptionsSets();
                    string numerical = Path.Combine(directory, i + "source.csv"), signals = Path.Combine(directory, i + "signals.csv");
                    results[i] = Path.Combine(directory, i + "results.csv");
                    WriteNumericalSource(numerical, launcher, options);
                    WriteSignalSource(signals, launcher, options);
                    CorrelatedSignalsFocusedReport.BuildAndValidate(launcher, numerical, signals, results[i],
                        Path.Combine(directory, i + "spec.csv"), Path.Combine(directory, i + "fees.csv"), Path.Combine(directory, i + "strategy.csv"));
                }
                var selected = ExitFeeCharts.SelectComparisons(results[0], results[1]);
                selected.Should().HaveCount(30);
                selected.Count(row => row["Source Plan"] == "CS006EF").Should().Be(10);
                selected.Should().OnlyContain(row => row["Filter"] == "All" && row["Number of Offers"] == "10");
                selected[0]["Plaintiff shortfall contribution"].Should().Be("0.2");
                selected[0]["Nonliable defendant contribution"].Should().Be("0.2");
                selected[0]["Liable defendant contribution"].Should().Be("0.05");
                File.WriteAllText(results[1], File.ReadAllText(results[1]).Replace("Trial and unilateral exit", "Trial only"));
                Action mismatch = () => ExitFeeCharts.SelectComparisons(results[0], results[1]);
                mismatch.Should().Throw<InvalidDataException>("an extension label cannot substitute for the correct fee trigger metadata");
            }
            finally { Directory.Delete(directory, recursive: true); }
        }

        private static void WriteNumericalSource(
            string path,
            LitigGameCorrelatedSignalsArticleLauncher launcher,
            IReadOnlyList<GameOptions> options,
            bool asymmetricExit = false)
        {
            string[] settings = launcher.DefaultVariableValues.Select(setting => setting.Item1).ToArray();
            string[] measures =
            {
                "P Files",
                "D Answers",
                "Trial",
                "No Suit",
                "Settles",
                "No Answer",
                "P Abandons",
                "D Defaults",
                CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn,
                "P Loses",
                "P Wins",
                "Value If Settled",
                "Expenditures",
                CorrelatedSignalsFocusedReport.DefendantExcessBurdenColumn,
                CorrelatedSignalsFocusedReport.PlaintiffRecoveryShortfallColumn,
                "Total Wealth",
                "P Welfare",
                "D Welfare",
                "Social Welfare Loss",
            };
            using var writer = new StreamWriter(path);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            WriteHeader(csv, settings, measures);
            foreach (GameOptions option in options)
            foreach (string filter in CorrelatedSignalsFocusedReport.NumericalFilters)
            {
                WriteMetadata(csv, settings, option, filter);
                foreach (string measure in measures)
                {
                    string value = measure switch
                    {
                        "P Files" => "0.6",
                        "D Answers" => "0.5",
                        "Trial" => "0.2",
                        "No Suit" => "0.4",
                        "Settles" => "0.2",
                        "No Answer" => "0.1",
                        "P Abandons" => asymmetricExit ? "0.01" : "0.04",
                        "D Defaults" => asymmetricExit ? "0.07" : "0.04",
                        CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn => "0.02",
                        CorrelatedSignalsFocusedReport.DefendantExcessBurdenColumn => filter switch
                        {
                            "All" => "0.25",
                            "Truly Liable" => "0.1",
                            "Truly Not Liable" => "0.4",
                            _ => "0.01",
                        },
                        CorrelatedSignalsFocusedReport.PlaintiffRecoveryShortfallColumn => filter switch
                        {
                            "All" => "0.2",
                            "Truly Liable" => "0.4",
                            "Truly Not Liable" => "0",
                            _ => "0.01",
                        },
                        "P Loses" => "0.1",
                        "P Wins" => "0.1",
                        "Value If Settled" => "0.4",
                        "Expenditures" => "0.2",
                        "Total Wealth" => "19.8",
                        _ => "0.01",
                    };
                    csv.WriteField(value);
                }
                csv.NextRecord();
            }
        }

        private static void WriteSignalSource(
            string path,
            LitigGameCorrelatedSignalsArticleLauncher launcher,
            IReadOnlyList<GameOptions> options)
        {
            string[] settings = launcher.DefaultVariableValues.Select(setting => setting.Item1).ToArray();
            string[] measures = new[]
            {
                "Signal Probability",
                "P Files",
                "D Answers",
                "P Offer",
                "D Offer",
                "Settles",
                "P Abandons",
                "D Defaults",
                CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn,
                "Trial",
                "P Loses",
                "P Wins",
            }.Concat(Enumerable.Range(1, 10).SelectMany(action => new[]
            {
                $"P Offer 1 Action {action}",
                $"D Offer 1 Action {action}",
            })).ToArray();
            using var writer = new StreamWriter(path);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            WriteHeader(csv, settings, measures);
            foreach (GameOptions option in options)
            foreach (string filter in CorrelatedSignalsFocusedReport.SignalFilters)
            {
                WriteMetadata(csv, settings, option, filter);
                foreach (string measure in measures)
                {
                    string value = measure switch
                    {
                        "Signal Probability" => "0.1",
                        "P Files" => "0.8",
                        "D Answers" => "0.4",
                        "P Offer" => "0.3",
                        "D Offer" => "0.6",
                        "Settles" => "0.2",
                        "P Abandons" => "0.04",
                        "D Defaults" => "0.04",
                        CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn => "0.02",
                        "Trial" => "0.1",
                        "P Loses" => "0.04",
                        "P Wins" => "0.06",
                        "P Offer 1 Action 1" => "1",
                        "D Offer 1 Action 1" => "1",
                        _ => "0",
                    };
                    csv.WriteField(value);
                }
                csv.NextRecord();
            }
        }

        private static void WriteHeader(
            CsvWriter csv,
            IEnumerable<string> settings,
            IEnumerable<string> measures)
        {
            csv.WriteField("Equilibrium Type");
            foreach (string setting in settings)
                csv.WriteField(setting);
            csv.WriteField("Filter");
            csv.WriteField("GroupName");
            csv.WriteField("OptionSetName");
            foreach (string measure in measures)
                csv.WriteField(measure);
            csv.NextRecord();
        }

        private static void WriteMetadata(
            CsvWriter csv,
            IEnumerable<string> settings,
            GameOptions option,
            string filter)
        {
            csv.WriteField("Only Eq");
            foreach (string setting in settings)
                csv.WriteField((Convert.ToString(
                    option.VariableSettings[setting],
                    CultureInfo.InvariantCulture) ?? string.Empty).Replace(",", "-"));
            csv.WriteField(filter);
            csv.WriteField(option.Name);
            csv.WriteField(option.Name);
        }

        private static Dictionary<string, string> ReadFirstRow(string path)
        {
            using var reader = new StreamReader(path);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            csv.Read();
            csv.ReadHeader();
            csv.Read();
            return csv.HeaderRecord.ToDictionary(
                header => header,
                header => csv.GetField(header),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
