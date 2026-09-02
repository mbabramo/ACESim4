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
    public class CorrelatedSignalsPairedReportTests
    {
        [TestMethod]
        public void CompleteMatrix_IsPairedDeterministicallyAcrossSignalStructures()
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "ACESim-correlated-report-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            try
            {
                var launcher = new LitigGameCorrelatedSignalsArticleLauncher();
                List<GameOptions> optionSets = launcher.GetOptionsSets();
                string forwardSource = Path.Combine(temporaryDirectory, "forward.csv");
                string reverseSource = Path.Combine(temporaryDirectory, "reverse.csv");
                string forwardPaired = Path.Combine(temporaryDirectory, "forward-paired.csv");
                string reversePaired = Path.Combine(temporaryDirectory, "reverse-paired.csv");

                WriteSyntheticCombinedReport(forwardSource, optionSets);
                WriteSyntheticCombinedReport(reverseSource, optionSets.AsEnumerable().Reverse().ToList());

                var forward = CorrelatedSignalsPairedReport.BuildAndValidate(
                    launcher, forwardSource, forwardPaired);
                var reverse = CorrelatedSignalsPairedReport.BuildAndValidate(
                    launcher, reverseSource, reversePaired);

                forward.Should().Be(new CorrelatedSignalsPairedReport.ValidationSummary(2000, 1000, 3));
                reverse.Should().Be(forward);
                File.ReadAllText(reversePaired).Should().Be(File.ReadAllText(forwardPaired));

                string pairedText = File.ReadAllText(forwardPaired);
                pairedText.Should().Contain("Case quality Party Signal Sigma");
                pairedText.Should().Contain("Binary truth Court Signal Sigma");
                pairedText.Should().Contain("Difference (Binary truth - Case quality)");
            }
            finally
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }

        [TestMethod]
        public void MissingOrDuplicateSimulationRows_AreRejected()
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "ACESim-correlated-report-rejection-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            try
            {
                var launcher = new LitigGameCorrelatedSignalsArticleLauncher();
                List<GameOptions> optionSets = launcher.GetOptionsSets();
                string source = Path.Combine(temporaryDirectory, "complete.csv");
                WriteSyntheticCombinedReport(source, optionSets);
                string[] lines = File.ReadAllLines(source);

                string missing = Path.Combine(temporaryDirectory, "missing.csv");
                File.WriteAllLines(missing, lines.Take(lines.Length - 1));
                Action buildMissing = () => CorrelatedSignalsPairedReport.BuildAndValidate(
                    launcher, missing, Path.Combine(temporaryDirectory, "missing-paired.csv"));
                buildMissing.Should().Throw<InvalidDataException>().WithMessage("*missing 1 expected rows*");

                string duplicate = Path.Combine(temporaryDirectory, "duplicate.csv");
                File.WriteAllLines(duplicate, lines.Concat(new[] { lines[1] }));
                Action buildDuplicate = () => CorrelatedSignalsPairedReport.BuildAndValidate(
                    launcher, duplicate, Path.Combine(temporaryDirectory, "duplicate-paired.csv"));
                buildDuplicate.Should().Throw<InvalidDataException>().WithMessage("*duplicate option/filter/equilibrium rows*");
            }
            finally
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }

        private static void WriteSyntheticCombinedReport(
            string path,
            IReadOnlyList<GameOptions> optionSets)
        {
            string[] variableHeaders = optionSets[0].VariableSettings.Keys
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = Environment.NewLine,
            };
            using var writer = new StreamWriter(path);
            using var csv = new CsvWriter(writer, configuration);

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
                int optionValue = StringComparer.Ordinal.GetHashCode(optionSet.Name) & 0x7FFF;
                foreach ((string filter, int filterIndex) in CorrelatedSignalsPairedReport.ExpectedFilters
                    .Select((filter, index) => (filter, index)))
                {
                    csv.WriteField("Only Eq");
                    foreach (string header in variableHeaders)
                        csv.WriteField(Convert.ToString(optionSet.VariableSettings[header], CultureInfo.InvariantCulture));
                    csv.WriteField(filter);
                    csv.WriteField(optionSet.GroupName ?? "");
                    csv.WriteField(optionSet.Name);
                    csv.WriteField((optionValue + filterIndex) / 100000.0);
                    csv.WriteField((optionValue + 2 * filterIndex) / 100000.0);
                    csv.WriteField(100.0 + (optionValue + filterIndex) / 100000.0);
                    csv.NextRecord();
                }
            }
        }
    }
}
