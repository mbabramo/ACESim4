using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace LitigCharts;

public static class WelfareDecompositionCommand
{
    public sealed record Source(FinalArticleCase Case, string EquilibriumFile, string ActionReportFile,
        string NumericReportFile, int EquilibriumNumber = 1);
    public sealed record Request(string Id, Source American, Source Complete);
    private sealed record Evaluation(string RuleCase, string ProfileCase, bool Endpoint,
        SavedProfileWelfare.Result Welfare, StrategicGameFingerprint.Snapshot Game, string CompleteProfileSha256,
        double[] EndpointBestResponseGains, int? ReproducedNumericCells);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private static string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 || args[0] != "--request" || args[2] != "--output")
            throw new ArgumentException("Use welfare-decomposition --request FILE --output NEW_DIRECTORY.");
        string file = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Decomposition output already exists.");
        string Resolve(string p) => Path.GetFullPath(p, Path.GetDirectoryName(file));
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(file), Json)
            ?? throw new InvalidDataException("Missing welfare decomposition request.");
        if (request.American.Case.FeeRule != "american" || request.Complete.Case.FeeRule != "complete")
            throw new InvalidDataException("Expected a declared American/complete pair.");
        ArticlePressureAnalysis.ValidateMatchedOptions(FinalArticleCaseFactory.Create(request.American.Case),
            FinalArticleCaseFactory.Create(request.Complete.Case));
        Source[] sources = { request.American, request.Complete };
        var inputs = new[] { file }.Concat(sources.SelectMany(s => new[] { s.EquilibriumFile, s.ActionReportFile, s.NumericReportFile }).Select(Resolve))
            .Distinct().ToDictionary(p => p, Hash);
        var vectors = sources.Select(s => {
            if (s.EquilibriumNumber < 1) throw new InvalidDataException("Expected positive equilibrium number.");
            string line = File.ReadLines(Resolve(s.EquilibriumFile)).Skip(s.EquilibriumNumber-1).FirstOrDefault()
                ?? throw new InvalidDataException("Missing complete saved profile.");
            return line.Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray();
        }).ToArray();
        var results = new Evaluation[2,2];
        string coordinates = null;
        Directory.CreateDirectory(output);
        // Validate each endpoint first; cross-rule profiles need not be equilibria.
        foreach (var (rule,profile) in new[] { (0,0), (1,1), (1,0), (0,1) })
        {
            bool endpoint = rule == profile;
            var options = FinalArticleCaseFactory.Create(sources[rule].Case);
            var developer = await ArticleWorkedPathExtraction.InitializeAsync(options);
            var identity = StrategicGameFingerprint.Capture(developer);
            if (coordinates != null && identity.CoordinatesSha256 != coordinates)
                throw new InvalidDataException("Rule/profile transplant has incompatible information/action coordinates.");
            coordinates = identity.CoordinatesSha256;
            ArticleWorkedPathExtraction.LoadProfile(developer, vectors[profile]);
            double[] complete = developer.GetEquilibriumFromInformationSets();
            developer.EvolutionSettings.UseAcceleratedBestResponse = true;
            developer.EvolutionSettings.UseCurrentStrategyForBestResponse = true;
            developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = false;
            developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting = false;
            double[] gains = null;
            if (endpoint)
            {
                ArticleWorkedPathExtraction.ValidateActionReport(developer, sources[profile].EquilibriumNumber, Resolve(sources[profile].ActionReportFile));
                developer.CalculateBestResponse(false);
                gains = developer.Status.BestResponseImprovement.ToArray();
                if (!developer.Status.BestResponseReflectsCurrentStrategy || gains.Any(g => !double.IsFinite(g) || Math.Abs(g) > 1e-7))
                    throw new InvalidDataException("Endpoint failed existing full unilateral best-response validation.");
            }
            developer.SaveWeightedGameProgressesAfterEachReport = true;
            developer.SavedWeightedGameProgresses.Clear();
            developer.ActionStrategy = ActionStrategies.CurrentProbability;
            var replay = await developer.GenerateReportsByPlaying(false);
            var welfare = SavedProfileWelfare.Evaluate(options, developer.SavedWeightedGameProgresses);
            if (!complete.SequenceEqual(developer.GetEquilibriumFromInformationSets()))
                throw new InvalidDataException("Replay changed the transplanted complete strategy.");
            string csv = Path.Combine(output, $"rule-{rule}-profile-{profile}.csv");
            File.WriteAllText(csv, replay.csvReports.Single());
            int? cells = endpoint ? MultipleEquilibriaStrategyAudit.ValidateReplay(Resolve(sources[profile].NumericReportFile), csv) : null;
            results[rule,profile] = new(sources[rule].Case.Id, sources[profile].Case.Id, endpoint, welfare, identity,
                ArticleApproximateSearch.ProfileHash(complete), gains, cells);
            File.WriteAllText(Path.ChangeExtension(csv, ".json"), JsonSerializer.Serialize(results[rule,profile], Json));
        }
        var selectors = new Dictionary<string,Func<TruthMappingReplay.Measures,double>> {
            ["Meritorious plaintiff shortfall"] = m => m.MeritoriousPlaintiffShortfall,
            ["Nonliable defendant burden"] = m => m.NonliableDefendantBurden,
            ["Liable defendant excess burden"] = m => m.LiableDefendantExcessBurden,
            ["Gross outcome error"] = m => m.GrossOutcomeError,
            ["Real litigation expenditures"] = m => m.RealLitigationExpenditures
        };
        var components = selectors.ToDictionary(s => s.Key, s => WelfareRuleBehaviorDecomposition.Calculate(
            s.Value(results[0,0].Welfare.Headline), s.Value(results[1,0].Welfare.Headline),
            s.Value(results[0,1].Welfare.Headline), s.Value(results[1,1].Welfare.Headline)));
        if (components.Values.Any(c => Math.Abs(c.Residual) > 1e-10))
            throw new InvalidDataException("Symmetric welfare decomposition identity failed.");
        foreach (string name in new[] { "Gross outcome error", "Real litigation expenditures" })
        {
            var c = components[name];
            if (Math.Abs(c.CompleteWithAmericanProfile-c.AmericanWithAmericanProfile) > 1e-10 ||
                Math.Abs(c.CompleteWithCompleteProfile-c.AmericanWithCompleteProfile) > 1e-10)
                throw new InvalidDataException("Fixed-strategy fee effect must be zero for " + name);
        }
        foreach (var input in inputs)
            if (Hash(input.Key) != input.Value) throw new IOException("Input changed during replay: " + input.Key);
        File.WriteAllText(Path.Combine(output, "decomposition.json"), JsonSerializer.Serialize(new {
            Passed = true, request.Id, CreatedUtc = DateTime.UtcNow, Inputs = inputs,
            GameAssemblySha256 = Hash(typeof(LitigGame).Assembly.Location), ReportingAssemblySha256 = Hash(typeof(WelfareDecompositionCommand).Assembly.Location),
            Contrast = "Complete minus American", MechanicalLabel = "Mechanical fee effect", BehavioralLabel = "Behavioral welfare effect",
            Components = components, SolvesStarted = 0 }, Json));
        return 0;
    }
}
