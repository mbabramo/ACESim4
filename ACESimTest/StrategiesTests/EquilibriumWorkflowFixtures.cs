using ACESim;
using ACESimBase.GameSolvingSupport.Settings;
using CsvHelper;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimTest.StrategiesTests;

// Also compiled by the one-off baseline recorder before the production refactor.
public static class EquilibriumWorkflowFixtures
{
    public sealed record Artifact(string Case, string[] Suffixes, string[][][] Reports,
        double[] RestoredPolicy, double[][] AveragePolicies, double[][][] RecordedPolicies);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static async Task<Artifact> CaptureAsync(bool endogenous)
    {
        Launcher launcher = endogenous ? new LitigGameEndogenousDisputesLauncher()
            : new LitigGameCorrelatedSignalsArticleLauncher();
        var options = endogenous ? LitigGameOptionsGenerator.PrecautionNegligenceGame()
            : (LitigGameOptions)launcher.GetDefaultSingleGameOptions();
        // Reduced grids keep the article-specific model/report path affordable in CI.
        options.NumLiabilitySignals = options.NumOffers = options.NumLiabilityStrengthPoints = 2;
        if (endogenous)
        {
            var generator = (ACESimBase.Games.LitigGame.PrecautionModel.PrecautionNegligenceDisputeGenerator)options.LitigGameDisputeGenerator;
            generator.PrecautionPowerLevels = generator.RelativePrecautionLevels = 2;
        }
        var settings = launcher.GetEvolutionSettings();
        settings.Algorithm = GameApproximationAlgorithm.SequenceForm;
        settings.CreateEFGFile = settings.CreateEquilibriaFile = false;
        settings.GenerateManualReports = settings.CreateInformationSetCharts = false;
        settings.GenerateReportsByPlaying = true;
        settings.ReportEveryNIterations = 1;
        settings.BestResponseEveryMIterations = settings.MiniReportEveryPIterations = null;
        settings.RoundOffLowProbabilitiesBeforeReporting = false;
        settings.SequenceFormNumPriorsToUseToGenerateEquilibria = 2;
        settings.UseExistingEquilibriaIfAvailable = false;
        var name = endogenous ? "endogenous" : "correlated-signals";
        var developer = (StrategiesDeveloperBase)await launcher.GetInitializedDevelper(options, name, settings);
        var nodes = developer.InformationSets.OrderBy(n => n.PlayerIndex).ThenBy(n => n.InformationSetNodeNumber).ToArray();
        // Explicit, nonuniform policies exercise every action without depending on RNG or solver convergence.
        var profiles = Enumerable.Range(0, 2).Select(profile => nodes.SelectMany(node =>
        {
            var weights = Enumerable.Range(0, node.NumPossibleActions)
                .Select(a => (double)(1 + (a * 3 + node.InformationSetNodeNumber + profile * 5) % 11)).ToArray();
            return weights.Select(w => w / weights.Sum());
        }).ToArray()).ToList();
        var reports = new ReportCollection();
        await developer.ProcessIdentifiedEquilibria(reports, profiles, true);
        return new(name, reports.ReportSuffixes.ToArray(), reports.csvReports.Select(CanonicalRows).ToArray(),
            developer.GetEquilibriumFromInformationSets(), nodes.Select(n => n.GetAverageStrategiesAsArray()).ToArray(),
            nodes.Select(n => n.PastValues.Select(p => p.ToArray()).ToArray()).ToArray());
    }

    private static string[][] CanonicalRows(string report)
    {
        using var reader = new StringReader(report);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        if (!csv.Read()) return System.Array.Empty<string[]>();
        csv.ReadHeader();
        // Runtime is intentionally excluded; all economic values, metadata and ordering are retained.
        var columns = csv.HeaderRecord.Select((name, index) => (name, index)).Where(x => x.name != "Seconds").ToArray();
        var rows = new List<string[]> { columns.Select(x => x.name).ToArray() };
        while (csv.Read()) rows.Add(columns.Select(x => csv.GetField(x.index)).ToArray());
        return rows.ToArray();
    }
}
