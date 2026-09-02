using ACESim;
using ACESimBase.GameSolvingSupport.Settings;
using CsvHelper;
using CsvHelper.Configuration;
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
    public class CorrelatedSignalsThreeStructureReportTests
    {
        [TestMethod]
        public void SupplementalUniformRows_JoinRetainedLegacyBaselinesIntoThreeWayComparison()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "ACESim-uniform-supplement-report-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var legacyLauncher = new LitigGameCorrelatedSignalsArticleLauncher();
                var supplementalLauncher = new LitigGameCorrelatedSignalsArticleLauncher(
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.UniformBaselineSupplement);
                string legacy = Path.Combine(directory, "legacy.csv");
                string supplemental = Path.Combine(directory, "supplemental.csv");
                string comparison = Path.Combine(directory, "comparison.csv");
                WriteSyntheticReport(legacy, legacyLauncher.GetOptionsSets());
                WriteSyntheticReport(supplemental, supplementalLauncher.GetOptionsSets());

                var summary = CorrelatedSignalsThreeStructureReport.BuildAndValidate(
                    supplementalLauncher,
                    supplemental,
                    comparison,
                    legacy);

                summary.Should().Be(new CorrelatedSignalsThreeStructureReport.ValidationSummary(250, 250, 3));
                File.ReadAllText(comparison).Should()
                    .Contain("Uniform quality OptionSetName")
                    .And.Contain("Difference (Uniform quality - Case quality)")
                    .And.Contain("Difference (Uniform quality - Binary truth)");
                File.ReadLines(comparison).Should().HaveCount(251);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public void UnifiedRun_ProducesEveryThreeStructureComparisonWithoutLegacyInput()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "ACESim-unified-report-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.UnifiedThreeStructure);
                string source = Path.Combine(directory, "unified.csv");
                string comparison = Path.Combine(directory, "comparison.csv");
                WriteSyntheticReport(source, launcher.GetOptionsSets());

                var summary = CorrelatedSignalsThreeStructureReport.BuildAndValidate(
                    launcher,
                    source,
                    comparison);

                summary.Should().Be(new CorrelatedSignalsThreeStructureReport.ValidationSummary(3000, 1000, 3));
                File.ReadLines(comparison).Should().HaveCount(1001);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static void WriteSyntheticReport(string path, IReadOnlyList<GameOptions> optionSets)
        {
            string[] variableHeaders = optionSets[0].VariableSettings.Keys
                .OrderBy(header => header, StringComparer.Ordinal)
                .ToArray();
            using var writer = new StreamWriter(path);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = Environment.NewLine,
            });
            csv.WriteField("Equilibrium Type");
            foreach (string header in variableHeaders)
                csv.WriteField(header);
            csv.WriteField("Filter");
            csv.WriteField("GroupName");
            csv.WriteField("OptionSetName");
            csv.WriteField("P Files");
            csv.WriteField("Trial");
            csv.WriteField("Total Wealth");
            csv.NextRecord();

            foreach (GameOptions optionSet in optionSets)
            {
                int value = StringComparer.Ordinal.GetHashCode(optionSet.Name) & 0x7FFF;
                foreach ((string filter, int index) in CorrelatedSignalsPairedReport.ExpectedFilters
                    .Select((filter, index) => (filter, index)))
                {
                    csv.WriteField("Only Eq");
                    foreach (string header in variableHeaders)
                        csv.WriteField(Convert.ToString(optionSet.VariableSettings[header], CultureInfo.InvariantCulture));
                    csv.WriteField(filter);
                    csv.WriteField(optionSet.GroupName ?? "");
                    csv.WriteField(optionSet.Name);
                    csv.WriteField((value + index) / 100000.0);
                    csv.WriteField((value + 2 * index) / 100000.0);
                    csv.WriteField(100.0 + (value + index) / 100000.0);
                    csv.NextRecord();
                }
            }
        }
    }
}
