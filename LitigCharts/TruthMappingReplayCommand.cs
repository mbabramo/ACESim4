using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace LitigCharts;

public static class TruthMappingReplayCommand
{
    public sealed record Request(FinalArticleCase Case, string EquilibriumFile, string ActionReportFile,
        string NumericReportFile, int EquilibriumNumber, double[] Exponents);

    private static object FileIdentity(string path) => new { Path = Path.GetFullPath(path), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 || args[0] != "--request" || args[2] != "--output")
            throw new ArgumentException("Use truth-replay --request FILE.json --output NEW_DIRECTORY.");
        string requestFile = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Replay output already exists: " + output);
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestFile), Json)
            ?? throw new InvalidDataException("Missing truth replay request.");
        if (request.EquilibriumNumber < 1) throw new ArgumentException("Equilibrium number must be positive.");
        string Resolve(string file) => Path.GetFullPath(file, Path.GetDirectoryName(requestFile));
        string profile = Resolve(request.EquilibriumFile), actions = Resolve(request.ActionReportFile), numeric = Resolve(request.NumericReportFile);
        DateTime started = DateTime.UtcNow;
        var options = FinalArticleCaseFactory.Create(request.Case);
        var developer = await ArticleWorkedPathExtraction.InitializeAsync(options);
        string line = File.ReadLines(profile).Skip(request.EquilibriumNumber-1).FirstOrDefault()
            ?? throw new InvalidDataException("Requested profile is absent.");
        ArticleWorkedPathExtraction.LoadProfile(developer, line.Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray());
        int actionRows = ArticleWorkedPathExtraction.ValidateActionReport(developer, request.EquilibriumNumber, actions);
        developer.EvolutionSettings.UseAcceleratedBestResponse = true;
        developer.EvolutionSettings.UseCurrentStrategyForBestResponse = true;
        developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = false;
        developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting = false;
        developer.CalculateBestResponse(false);
        double[] gains = developer.Status.BestResponseImprovement.ToArray();
        if (!developer.Status.BestResponseReflectsCurrentStrategy || gains.Any(g => !double.IsFinite(g) || Math.Abs(g) > 1e-7))
            throw new InvalidDataException("Primary profile failed the existing unrestricted best-response criterion.");
        var before = StrategicGameFingerprint.Capture(developer);
        developer.SaveWeightedGameProgressesAfterEachReport = true;
        developer.SavedWeightedGameProgresses.Clear();
        developer.ActionStrategy = ActionStrategies.CurrentProbability;
        var replay = await developer.GenerateReportsByPlaying(false);
        var truth = TruthMappingReplay.Evaluate(options, developer.SavedWeightedGameProgresses, request.Exponents);
        var after = StrategicGameFingerprint.Capture(developer);
        if (before != after) throw new InvalidDataException("Truth analysis changed the strategic game.");
        developer.CalculateBestResponse(false);
        if (!gains.SequenceEqual(developer.Status.BestResponseImprovement))
            throw new InvalidDataException("Truth analysis changed the profile's full best-response gains.");
        Directory.CreateDirectory(output);
        string reportFile = Path.Combine(output, "replayed-report.csv");
        File.WriteAllText(reportFile, replay.csvReports.Single());
        int cells = MultipleEquilibriaStrategyAudit.ValidateReplay(numeric, reportFile);
        var results = new { Passed = true, StartedUtc = started, FinishedUtc = DateTime.UtcNow, request.Case.Id,
            request.EquilibriumNumber, Inputs = new[] { FileIdentity(requestFile), FileIdentity(profile), FileIdentity(actions), FileIdentity(numeric) },
            GameAssembly = FileIdentity(typeof(LitigGame).Assembly.Location), ReportingAssembly = FileIdentity(typeof(TruthMappingReplayCommand).Assembly.Location),
            GameIdentity = before, FullBestResponseGains = gains, ValidatedActionRows = actionRows, ReproducedNumericCells = cells,
            StrategicGameUnchanged = true, BestResponseGainsUnchanged = true, TruthAnalysis = truth };
        File.WriteAllText(Path.Combine(output, "truth-mapping-results.json"), JsonSerializer.Serialize(results, Json));
        string[] header = { "CaseId", "Equilibrium", "Exponent", "ModelTruthPrior", "ReplayedTruthMass", "MeritoriousPlaintiffShortfall",
            "NonliableDefendantBurden", "LiableDefendantExcessBurden", "GrossOutcomeError", "RealLitigationExpenditures" };
        string F(double value) => value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        var lines = truth.Mappings.Select(m => string.Join(",", new[] { request.Case.Id, request.EquilibriumNumber.ToString(), F(m.Exponent), F(m.ModelTruthPrior),
            F(m.ReplayedTruthMass), F(m.Headline.MeritoriousPlaintiffShortfall), F(m.Headline.NonliableDefendantBurden),
            F(m.Headline.LiableDefendantExcessBurden), F(m.Headline.GrossOutcomeError), F(m.Headline.RealLitigationExpenditures) }));
        File.WriteAllLines(Path.Combine(output, "truth-mapping-welfare.csv"), new[] { string.Join(",", header) }.Concat(lines));
        Console.WriteLine($"Validated {request.Case.Id}: {truth.Mappings.Length} truth maps, {truth.MarginalizedHistories} histories; no equilibrium searches.");
        return 0;
    }
}
