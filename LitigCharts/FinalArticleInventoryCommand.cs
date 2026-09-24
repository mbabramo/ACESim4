using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Non-solving full-game export and baseline compatibility check.</summary>
public static class FinalArticleInventoryCommand
{
    public sealed record Request(FinalArticleCase[] Cases, string OriginalReferenceIdentities,
        string OriginalSource, string OriginalQueue, string CalibrationFile);
    public sealed record PreparedCase(FinalArticleCase Case, string OptionSetName,
        StrategicGameFingerprint.Snapshot Identity, int LcpDimension, object Options, object Solver,
        string ProfileFileName, string ActionFileName, string NumericFileName);
    public sealed record PreparedInventory(string Schema, DateTime CreatedUtc, PreparedCase[] Cases,
        object RequestIdentity, object ReferenceIdentity, object GameAssembly, object ReportingAssembly,
        bool SolverDispatchAllowed, int SolvesStarted);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static object FileIdentity(string path) => new { Path = Path.GetFullPath(path), Sha256 = Hash(path) };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 || args[0] != "--request" || args[2] != "--output")
            throw new ArgumentException("Use final-agreement-inventory --request FILE --output NEW_DIRECTORY.");
        string requestFile = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Inventory output already exists.");
        string Resolve(string value) => Path.GetFullPath(value, Path.GetDirectoryName(requestFile));
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestFile), Json)
            ?? throw new InvalidDataException("Missing inventory request.");
        if (request.Cases == null || request.Cases.Length < 30 || request.Cases.Select(c => c.Id).Distinct().Count() != request.Cases.Length ||
            request.Cases.Count(c => c.IsExternalImport) != 30 || string.IsNullOrWhiteSpace(request.OriginalSource) || string.IsNullOrWhiteSpace(request.OriginalQueue))
            throw new InvalidDataException("Supply a unique explicit inventory containing all thirty external reservations.");
        string referenceFile = Resolve(request.OriginalReferenceIdentities);
        using var reference = JsonDocument.Parse(File.ReadAllBytes(referenceFile));
        if (reference.RootElement.GetProperty("Schema").GetString() != "original-30-reference-identities-v1" ||
            reference.RootElement.GetProperty("SolvesStarted").GetInt32() != 0)
            throw new InvalidDataException("Expected the non-solving immutable-reference fingerprint export.");
        var original = reference.RootElement.GetProperty("Cases").EnumerateArray().ToDictionary(
            c => c.GetProperty("Name").GetString(), c => c.GetProperty("Identity").Deserialize<StrategicGameFingerprint.Snapshot>(Json));
        if (original.Count != 30 || !original.Keys.ToHashSet().SetEquals(request.Cases.Where(c => c.IsExternalImport).Select(c => c.OriginalOptionName)))
            throw new InvalidDataException("External reservations do not match the original thirty-case inventory.");
        var launcher = new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain);
        var prepared = new List<PreparedCase>();
        var external = new List<ArticleSolveRegistry.ExternalCase>();
        Directory.CreateDirectory(output);
        foreach (var spec in request.Cases)
        {
            if (spec.Distribution == "direct-binary")
            {
                if (request.CalibrationFile == null || !string.Equals(Hash(Resolve(request.CalibrationFile)), spec.CalibrationSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Binary calibration artifact is absent or does not match its hash.");
                using var calibration = JsonDocument.Parse(File.ReadAllBytes(Resolve(request.CalibrationFile)));
                var fit = calibration.RootElement.GetProperty("Calibration");
                if (fit.GetProperty("Signals").GetInt32() != spec.Signals || fit.GetProperty("QuadratureOrder").GetInt32() != 64 ||
                    fit.GetProperty("TargetPartySigma").GetDouble() != 0.2 || fit.GetProperty("TargetCourtSigma").GetDouble() != 0.2 ||
                    !fit.GetProperty("Target").GetString().StartsWith("Uniform(0,1);", StringComparison.Ordinal) ||
                    fit.GetProperty("PartyFit").GetProperty("Sigma").GetDouble() != spec.PartySigma ||
                    fit.GetProperty("CourtFit").GetProperty("Sigma").GetDouble() != spec.CourtSigma)
                    throw new InvalidDataException("Binary case does not reproduce the declared uniform-merits calibration.");
            }
            var options = FinalArticleCaseFactory.Create(spec);
            var developer = await ArticleWorkedPathExtraction.InitializeAsync(options);
            var identity = StrategicGameFingerprint.Capture(developer);
            if (spec.IsExternalImport && original[spec.OriginalOptionName] != identity)
                throw new InvalidDataException("Original strategic game or probability coordinates changed: " + spec.Id);
            int sequences = developer.InformationSets.Sum(i => i.NumPossibleActions)+2;
            int dimension = sequences+developer.InformationSets.Count+2;
            var settings = launcher.GetEvolutionSettings(); options.ModifyEvolutionSettings(settings);
            string prefix = spec.IsExternalImport ? "CS007AB " : "FinalAgreement ";
            prepared.Add(new(spec, options.Name, identity, dimension, AgreementToBargainStudy.Configuration(options),
                AgreementToBargainStudy.Configuration(settings), prefix+options.Name+" -equ.csv",
                prefix+options.Name+" -InformationSetActions.csv", prefix+options.Name+".csv"));
            if (spec.IsExternalImport)
                external.Add(new(spec.Id, spec.OriginalOptionName, identity.CompleteSha256, request.OriginalSource,
                    request.OriginalQueue, "reserved-for-import; completion status independent of dispatch exclusion"));
            // Retain completed exports even if a later case fails; never mistake this
            // checkpoint for the complete inventory or for simulation authorization.
            File.WriteAllText(Path.Combine(output, spec.Id+".json"), JsonSerializer.Serialize(prepared[^1], Json));
            Console.WriteLine($"Initialized {spec.Id}: nodes={identity.TreeNodes}; information sets={identity.InformationSets}; LCP={dimension}; no solve.");
        }
        if (prepared.Select(c => c.Identity.CompleteSha256).Distinct().Count() != prepared.Count)
            throw new InvalidDataException("Two declared cases represent the same strategic game.");
        File.WriteAllText(Path.Combine(output, "external-registry.json"), JsonSerializer.Serialize(
            new ArticleSolveRegistry.ExternalRegistry("external-original-30-v1", external.ToArray()), Json));
        var inventory = new PreparedInventory("prepared-final-agreement-inventory-v1", DateTime.UtcNow, prepared.ToArray(),
            FileIdentity(requestFile), FileIdentity(referenceFile), FileIdentity(typeof(LitigGame).Assembly.Location),
            FileIdentity(typeof(FinalArticleInventoryCommand).Assembly.Location), false, 0);
        File.WriteAllText(Path.Combine(output, "inventory.json"), JsonSerializer.Serialize(inventory, Json));
        return 0;
    }
}
