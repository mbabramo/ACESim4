using ACESim;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Serialization;
using FluentAssertions;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ACESimTest
{
    [TestClass]
    [DoNotParallelize]
    public class CorrelatedSignalsMultipleEquilibriaReportTests
    {
        [TestMethod]
        public void BuilderExportsEveryEquilibriumAndTruthRelativeRanges()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "ACESim4-CS004ME-report-" + Guid.NewGuid().ToString("N"));
            string previous = Environment.GetEnvironmentVariable(
                FolderFinder.ReportResultsDirectoryEnvironmentVariable);
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable(
                FolderFinder.ReportResultsDirectoryEnvironmentVariable,
                directory);
            try
            {
                var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.MultipleEquilibriaRobustness);
                foreach (GameOptions option in launcher.GetOptionsSets())
                {
                    File.WriteAllLines(
                        launcher.GetReportFullPath(option.Name, "-equ.csv"),
                        new[] { "0.5,0.5", "0.4,0.6" });
                    File.WriteAllText(
                        launcher.GetReportFullPath(option.Name, "-EquilibriumRecoveries.csv"),
                        RecoveryReport(option.Name));
                    File.WriteAllText(
                        launcher.GetReportFullPath(option.Name, "-Eq1.csv"),
                        RawReport(0.60));
                    File.WriteAllText(
                        launcher.GetReportFullPath(option.Name, "-Eq2.csv"),
                        RawReport(0.70));
                }

                string outcomesPath = launcher.GetReportFullPath("equilibrium outcomes", ".csv");
                string rangesPath = launcher.GetReportFullPath("equilibrium ranges", ".csv");
                CorrelatedSignalsMultipleEquilibriaReport.ValidationSummary summary =
                    CorrelatedSignalsMultipleEquilibriaReport.BuildAndValidate(
                        launcher,
                        outcomesPath,
                        rangesPath);

                summary.OptionSetCount.Should().Be(2);
                summary.EquilibriumCount.Should().Be(4);
                summary.RangeRowCount.Should().Be(2);
                File.ReadLines(outcomesPath).Should().HaveCount(5);
                File.ReadLines(rangesPath).Should().HaveCount(3);
                string outcomes = File.ReadAllText(outcomesPath);
                outcomes.Should()
                    .Contain(CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn)
                    .And.Contain("Attempted Solves")
                    .And.Contain("Equilibrium Recovery Count")
                    .And.Contain("\"0.45000000000000001\"");
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    FolderFinder.ReportResultsDirectoryEnvironmentVariable,
                    previous);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        private static string RawReport(double plaintiffOffer)
        {
            string[] headers =
            {
                "Filter", "Exploit", "Seconds", "PFiles", "DAnswers", "POffer1", "DOffer1",
                "SettlesBR1", "PAbandonsBR1", "DDefaultsBR1", "BothReadyToGiveUp", "Trial",
                "P Loses", "P Wins", "TotExpense", "False+", "False-", "TotWealth",
                "PDoesntFile", "DDoesntAnswer",
            };
            string[] all =
            {
                "All", "0", "12", "0.8", "0.7", Format(plaintiffOffer), "0.1",
                "0.2", "0.1", "0.1", "0.1", "0.2", "0.1", "0.1", "0.3",
                "0.25", "0.2", "19.7", "0.2", "0.1",
            };
            string[] liable = all.ToArray();
            liable[0] = "Truly Liable";
            liable[15] = "0.1";
            liable[16] = "0.4";
            string[] nonliable = all.ToArray();
            nonliable[0] = "Truly Not Liable";
            nonliable[15] = "0.4";
            nonliable[16] = "0";
            return string.Join(",", headers) + Environment.NewLine +
                string.Join(",", all) + Environment.NewLine +
                string.Join(",", liable) + Environment.NewLine +
                string.Join(",", nonliable) + Environment.NewLine;
        }

        private static string RecoveryReport(string optionSetName) =>
            "OptionSetName,Requested Priors,Attempted Solves,Inexact Attempts,Exact Attempts," +
            "Verified Recoveries,Distinct Reported Strategy Profiles,Equilibrium Number," +
            "Recovery Count,Recovery Share of Verified Recoveries,Verification Status,Distinctness Criterion" +
            Environment.NewLine +
            $"\"{optionSetName}\",50,56,49,7,50,2,1,30,0.6,Verified,Exact equality" + Environment.NewLine +
            $"\"{optionSetName}\",50,56,49,7,50,2,2,20,0.4,Verified,Exact equality" + Environment.NewLine;

        private static string Format(double value) =>
            value.ToString("G17", CultureInfo.InvariantCulture);
    }
}
