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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ACESimBase.Games.LitigGame.ManualReports;

namespace ACESimTest
{
    [TestClass]
    [DoNotParallelize]
    public class CorrelatedSignalsMultipleEquilibriaReportTests
    {
        [TestMethod]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public async Task IndividualDiagramsExcludePreviousEquilibriumPathsButKeepCombinedReportPaths()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ACESim4-profile-diagrams-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string previous = Environment.GetEnvironmentVariable(FolderFinder.ReportResultsDirectoryEnvironmentVariable);
            Environment.SetEnvironmentVariable(FolderFinder.ReportResultsDirectoryEnvironmentVariable, directory);
            try
            {
                var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.MultipleEquilibriaRobustness);
                var option = (LitigGameOptions)launcher.GetOptionsSets().First();
                var developer = await ArticleWorkedPathExtraction.InitializeAsync(option);
                developer.EvolutionSettings.GenerateReportsByPlaying = true;
                developer.EvolutionSettings.GenerateManualReports = true;
                developer.EvolutionSettings.ReportEveryNIterations = 1;
                developer.EvolutionSettings.BestResponseEveryMIterations = null;
                developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting = false;
                developer.EvolutionSettings.SequenceFormNumPriorsToUseToGenerateEquilibria = 2;
                developer.SaveWeightedGameProgressesAfterEachReport = true;
                var nodes = developer.InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetNodeNumber).ToArray();
                // First profile always files; second never files. Pooling their paths
                // would incorrectly turn the second profile's filing chart into 50%.
                var first = nodes.SelectMany(n => Enumerable.Range(1, n.NumPossibleActions).Select(a => a == 1 ? 1.0 : 0)).ToArray();
                var second = nodes.SelectMany(n => Enumerable.Range(1, n.NumPossibleActions).Select(a => a == (n.Decision.Name == "P Files" ? 2 : 1) ? 1.0 : 0)).ToArray();
                developer.SetInformationSetsToEquilibrium(first);
                await developer.AddReportForEquilibrium(new ReportCollection(), 2, 0);
                developer.SetInformationSetsToEquilibrium(second);
                await developer.AddReportForEquilibrium(new ReportCollection(), 2, 1);
                foreach (int eq in new[] { 1, 2 })
                {
                    string file = Directory.GetFiles(directory, "*-fileans-Eq" + eq + ".tex", SearchOption.AllDirectories).Single();
                    var percentages = Regex.Matches(File.ReadAllText(file), @"\{(\d+)\\%\}")
                        .Select(m => int.Parse(m.Groups[1].Value)).Take(10).ToArray();
                    percentages.Should().HaveCount(10).And.OnlyContain(p => p == (eq == 1 ? 100 : 0));
                }
                developer.SavedWeightedGameProgresses.Sum(x => x.weight).Should().BeApproximately(2, 1e-5);
            }
            finally
            {
                Environment.SetEnvironmentVariable(FolderFinder.ReportResultsDirectoryEnvironmentVariable, previous);
                Directory.Delete(directory, true);
            }
        }

        [DataTestMethod]
        [DataRow(50, true)]
        [DataRow(33, true)]
        [DataRow(35, true)]
        [DataRow(0, false)]
        [DataRow(51, false)]
        public void BuilderExportsEveryEquilibriumAndTruthRelativeRanges(int verifiedRecoveries, bool valid)
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
                        RecoveryReport(option.Name, verifiedRecoveries));
                    File.WriteAllText(
                        launcher.GetReportFullPath(option.Name, "-Eq1.csv"),
                        RawReport(0.60));
                    File.WriteAllText(
                        launcher.GetReportFullPath(option.Name, "-Eq2.csv"),
                        RawReport(0.70));
                }

                string outcomesPath = launcher.GetReportFullPath("equilibrium outcomes", ".csv");
                string rangesPath = launcher.GetReportFullPath("equilibrium ranges", ".csv");
                if (!valid)
                {
                    Action build = () => CorrelatedSignalsMultipleEquilibriaReport.BuildAndValidate(
                        launcher, outcomesPath, rangesPath);
                    build.Should().Throw<InvalidDataException>();
                    return;
                }
                CorrelatedSignalsMultipleEquilibriaReport.ValidationSummary summary =
                    CorrelatedSignalsMultipleEquilibriaReport.BuildAndValidate(
                        launcher,
                        outcomesPath,
                        rangesPath);

                summary.OptionSetCount.Should().Be(6);
                summary.EquilibriumCount.Should().Be(12);
                summary.RangeRowCount.Should().Be(6);
                File.ReadLines(outcomesPath).Should().HaveCount(13);
                File.ReadLines(rangesPath).Should().HaveCount(7);
                File.ReadAllText(rangesPath).Should().Contain("Complete Fee-Shifting").And.Contain("Moderately Risk Averse");
                var row = PublicationFigures.ReadCsv(outcomesPath).First();
                row["Verified Recoveries"].Should().Be(verifiedRecoveries.ToString(CultureInfo.InvariantCulture));
                var expected = new[] { 0.2, 0.2, 0.05, 0.5, 0.3 };
                for (int i = 0; i < expected.Length; i++)
                    double.Parse(row[CorrelatedSignalsMultipleEquilibriaReport.WelfareMeasures[i]], CultureInfo.InvariantCulture)
                        .Should().BeApproximately(expected[i], 1e-12);
                string exhibits = Path.Combine(directory, "exhibits");
                MultipleEquilibriaExhibits.RunAsync(new[] { "--input", directory, "--output", exhibits, "--sources-only" })
                    .GetAwaiter().GetResult().Should().Be(0);
                Directory.GetFiles(exhibits, "*.tex", SearchOption.AllDirectories).Should().HaveCount(7);
                File.ReadAllText(Path.Combine(exhibits, "Risk Comparison", "Sources", "cost-1-welfare-outcome-ranges.tex"))
                    .Should().Contain(@"Risk Neutral & Complete Fee-Shifting & 0.2000--0.2000 & 0.2000--0.2000 & 0.0500--0.0500 & 0.5000--0.5000 & 0.3000--0.3000 \\");
                string outcomes = File.ReadAllText(outcomesPath);
                outcomes.Should()
                    .Contain(CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn)
                    .And.Contain(CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn)
                    .And.Contain("Attempted Solves")
                    .And.Contain("Equilibrium Recovery Count")
                    .And.Contain("Plaintiff shortfall contribution")
                    .And.Contain(WelfareOutcomeExhibits.ErrorColumn)
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
                "PDoesntFile", "DDoesntAnswer", "ValIfSettled",
            };
            string[] all =
            {
                "All", "0", "12", "0.8", "0.7", Format(plaintiffOffer), "0.1",
                "0.2", "0.1", "0.1", "0.1", "0.2", "0.1", "0.1", "0.3",
                "0.25", "0.2", "19.7", "0.2", "0.1", "0.5",
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

        private static string RecoveryReport(string optionSetName, int verifiedRecoveries)
        {
            int first = (int)Math.Ceiling(verifiedRecoveries * 0.6);
            int second = verifiedRecoveries - first;
            string Share(int count) => Format(verifiedRecoveries == 0 ? 0 : (double)count / verifiedRecoveries);
            return
            "OptionSetName,Requested Priors,Attempted Solves,Inexact Attempts,Exact Attempts," +
            "Verified Recoveries,Distinct Reported Strategy Profiles,Equilibrium Number," +
            "Recovery Count,Recovery Share of Verified Recoveries,Verification Status,Distinctness Criterion" +
            Environment.NewLine +
            $"\"{optionSetName}\",50,99,49,50,{verifiedRecoveries},2,1,{first},{Share(first)},Verified,Exact equality" + Environment.NewLine +
            $"\"{optionSetName}\",50,99,49,50,{verifiedRecoveries},2,2,{second},{Share(second)},Verified,Exact equality" + Environment.NewLine;
        }

        private static string Format(double value) =>
            value.ToString("G17", CultureInfo.InvariantCulture);
    }
}
