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

                summary.NumericalResultCount.Should().Be(134);
                summary.SpecificationComparisonCount.Should().Be(122);
                summary.FeeRegimeComparisonCount.Should().Be(67);
                summary.SignalStrategyCount.Should().Be(2680);
                File.ReadLines(numericalResults).Should().HaveCount(135);
                File.ReadLines(specificationPairs).Should().HaveCount(123);
                File.ReadLines(feePairs).Should().HaveCount(68);
                File.ReadLines(signalStrategies).Should().HaveCount(2681);
                File.ReadAllText(numericalResults).Should()
                    .Contain("Settlement Conditional on Reaching Bargaining")
                    .And.Contain("Real Litigation Costs")
                    .And.Contain("Liability Transfer to Plaintiff")
                    .And.Contain("Fee-Shifting Transfer to Plaintiff")
                    .And.Contain(CorrelatedSignalsFocusedReport.DefendantExcessBurdenColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.PlaintiffRecoveryShortfallColumn)
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
                Dictionary<string, string> signalRow = ReadFirstRow(signalStrategies);
                signalRow["P Abandonment Probability Unconditional"].Should().Be("0.05");
                signalRow["D Default Probability Unconditional"].Should().Be("0.05");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static void WriteNumericalSource(
            string path,
            LitigGameCorrelatedSignalsArticleLauncher launcher,
            IReadOnlyList<GameOptions> options)
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
                        "P Abandons" => "0.04",
                        "D Defaults" => "0.04",
                        CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn => "0.02",
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
