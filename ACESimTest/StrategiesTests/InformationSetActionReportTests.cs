using ACESim;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingSupport.Settings;
using CsvHelper;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace ACESimTest.StrategiesTests
{
    [TestClass]
    public class InformationSetActionReportTests
    {
        [TestMethod]
        public async Task ReportContainsConditionalActionUtilitiesAndReachDiagnostics()
        {
            var developer = await StrategiesDeveloperTestsBase.GetGeneralizedVanilla(
                LitigGameOptionsGenerator.SmallGame(),
                "InformationSetActionReportTest");

            string report = InformationSetActionReport.BuildCsv(developer, 1);
            List<Dictionary<string, string>> rows = ReadRows(report);

            rows.Should().HaveCount(developer.InformationSets.Sum(node => node.NumPossibleActions));
            rows.Should().OnlyContain(row => row["OptionSetName"] == "InformationSetActionReportTest");
            rows.Should().OnlyContain(row => row["Equilibrium Number"] == "1");
            rows.Should().OnlyContain(row => !string.IsNullOrWhiteSpace(row["Action Label"]));

            foreach (IGrouping<string, Dictionary<string, string>> informationSetRows in
                rows.GroupBy(row => row["Information Set Number"], StringComparer.Ordinal))
            {
                informationSetRows.Sum(row => Value(row, "Equilibrium Action Probability"))
                    .Should().BeApproximately(1.0, 1E-12);
                bool offPath = bool.Parse(informationSetRows.First()["Off Path"]);
                if (offPath)
                {
                    informationSetRows.Should().OnlyContain(row =>
                        row["Conditional Information-Set Utility"] == string.Empty &&
                        row["Best Action Utility"] == string.Empty &&
                        row["Conditional Action Utility"] == string.Empty &&
                        row["Utility Loss from Best Action"] == string.Empty &&
                        row["Is Best Action"] == string.Empty);
                    continue;
                }

                double informationSetUtility =
                    Value(informationSetRows.First(), "Conditional Information-Set Utility");
                double weightedActionUtility = informationSetRows.Sum(row =>
                    Value(row, "Equilibrium Action Probability") *
                    Value(row, "Conditional Action Utility"));
                informationSetUtility.Should().BeApproximately(weightedActionUtility, 1E-10);
                informationSetRows.Should().Contain(row => bool.Parse(row["Is Best Action"]));
                foreach (Dictionary<string, string> row in informationSetRows)
                {
                    double expectedLoss =
                        Value(row, "Best Action Utility") -
                        Value(row, "Conditional Action Utility");
                    Value(row, "Utility Loss from Best Action")
                        .Should().BeApproximately(expectedLoss, 1E-10);
                }
            }
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public async Task SequenceFormAddsActionReportForEachReportedEquilibrium()
        {
            var launcher = new LitigGameEndogenousDisputesLauncher();
            EvolutionSettings settings = launcher.GetEvolutionSettings();
            settings.Algorithm = GameApproximationAlgorithm.SequenceForm;
            settings.CreateEFGFile = false;
            settings.CreateEquilibriaFile = false;
            settings.CalculatePerturbedBestResponseRefinement = false;
            settings.GenerateInformationSetActionReport = true;
            var developer = (SequenceForm)await launcher.GetInitializedDevelper(
                LitigGameOptionsGenerator.SmallGame(),
                "InformationSetActionReportSequenceFormTest",
                settings);

            ReportCollection reports = await developer.RunAlgorithm(
                "InformationSetActionReportSequenceFormTest");

            int reportIndex = reports.ReportSuffixes.IndexOf(
                InformationSetActionReport.ReportSuffix);
            reportIndex.Should().BeGreaterThanOrEqualTo(0);
            reports.csvReports[reportIndex].Should()
                .StartWith("\"OptionSetName\",\"Equilibrium Number\"")
                .And.Contain("\"Conditional Action Utility\"");
        }

        private static List<Dictionary<string, string>> ReadRows(string report)
        {
            using var reader = new StringReader(report);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            csv.Read();
            csv.ReadHeader();
            string[] headers = csv.HeaderRecord;
            var rows = new List<Dictionary<string, string>>();
            while (csv.Read())
            {
                rows.Add(headers.ToDictionary(
                    header => header,
                    header => csv.GetField(header),
                    StringComparer.Ordinal));
            }
            return rows;
        }

        private static double Value(
            IReadOnlyDictionary<string, string> row,
            string column) =>
            double.Parse(row[column], CultureInfo.InvariantCulture);
    }
}
