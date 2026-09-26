using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FileIdentity = ACESim.FinalArticleExecution.FileIdentity;

namespace LitigCharts;

/// <summary>Validate one frozen, completed final-family profile without a solver call.</summary>
public static class FinalArticleProfileAudit
{
    public sealed record Request(string CaseId, FileIdentity PreparedInventory, FileIdentity CompletionEvidence,
        FileIdentity Equilibrium, FileIdentity Actions, FileIdentity Numeric, bool GenerateDiagrams = true);

    public static FinalArticleInventoryCommand.PreparedCase ValidateInputs(Request request)
    {
        foreach (var file in new[] { request.PreparedInventory, request.CompletionEvidence, request.Equilibrium, request.Actions, request.Numeric })
            FinalArticleExecution.Verify(file);
        using var inventory = JsonDocument.Parse(File.ReadAllBytes(request.PreparedInventory.Path));
        var root = inventory.RootElement;
        if (root.GetProperty("Schema").GetString() != "prepared-final-agreement-inventory-v1" || root.GetProperty("SolvesStarted").GetInt32() != 0)
            throw new InvalidDataException("Expected the full prepared game inventory.");
        var row = root.GetProperty("Cases").EnumerateArray().Single(c => c.GetProperty("Case").GetProperty("Id").GetString() == request.CaseId);
        var prepared = row.Deserialize<FinalArticleInventoryCommand.PreparedCase>(FinalArticleExecution.Json);
        if (Path.GetFileName(request.Equilibrium.Path) != prepared.ProfileFileName ||
            Path.GetFileName(request.Actions.Path) != prepared.ActionFileName || Path.GetFileName(request.Numeric.Path) != prepared.NumericFileName)
            throw new InvalidDataException("Frozen filenames differ from the explicit case inventory.");
        using var evidence = JsonDocument.Parse(File.ReadAllBytes(request.CompletionEvidence.Path));
        var e = evidence.RootElement;
        string schema = e.GetProperty("Schema").GetString();
        JsonElement[] files;
        if (prepared.Case.IsExternalImport)
        {
            if (schema != "validated-production-imports-v1") throw new InvalidDataException("Expected a validated original import certificate.");
            var item = e.GetProperty("Cases").EnumerateArray().Single(c => c.GetProperty("CaseId").GetString() == request.CaseId);
            if (item.GetProperty("Status").GetString() != "validated-production-import" ||
                item.GetProperty("ProductionBuildValidationPending").GetBoolean() ||
                item.GetProperty("GameIdentity").Deserialize<StrategicGameFingerprint.Snapshot>() != prepared.Identity)
                throw new InvalidDataException("The original case is pending or incompatible.");
            files = item.GetProperty("Files").EnumerateArray().ToArray();
        }
        else
        {
            if (schema != "completed-final-primary-case-v1" || e.GetProperty("CaseId").GetString() != request.CaseId ||
                !e.GetProperty("Complete").GetBoolean() || e.GetProperty("Failed").GetBoolean() || e.GetProperty("Seed").GetInt32() != 0 ||
                e.GetProperty("Identity").Deserialize<StrategicGameFingerprint.Snapshot>() != prepared.Identity)
                throw new InvalidDataException("The new case lacks compatible successful completion evidence.");
            foreach (var item in e.GetProperty("Provenance").EnumerateArray())
                FinalArticleExecution.Verify(item.Deserialize<FileIdentity>());
            FinalArticleCompletionSnapshot.ValidateCertificate(e, prepared);
            files = e.GetProperty("Files").EnumerateArray().ToArray();
        }
        foreach (var file in new[] { request.Equilibrium, request.Actions, request.Numeric })
            if (!files.Any(f => string.Equals(f.GetProperty("Destination").GetString(), file.Path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.GetProperty("Sha256").GetString(), file.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The completed profile certificate does not bind this input: " + file.Path);
        return prepared;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 || args[0] != "--request" || args[2] != "--output")
            throw new ArgumentException("Use final-profile-audit --request FILE --output NEW_DIRECTORY.");
        string requestFile = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Profile audit output already exists.");
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllBytes(requestFile), FinalArticleExecution.Json)
            ?? throw new InvalidDataException("Missing final profile audit request.");
        var prepared = ValidateInputs(request);
        var options = FinalArticleCaseFactory.Create(prepared.Case);
        DateTime started = DateTime.UtcNow;
        var developer = await ArticleWorkedPathExtraction.InitializeAsync(options);
        if (StrategicGameFingerprint.Capture(developer) != prepared.Identity)
            throw new InvalidDataException("Reporting build changed the prepared full game or probability coordinates.");
        string[] lines = File.ReadAllLines(request.Equilibrium.Path).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (lines.Length != 1) throw new InvalidDataException("Primary cases require exactly one complete saved profile.");
        var fallbacks = ArticleWorkedPathExtraction.LoadProfile(developer, lines[0].Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray());
        double[] complete = developer.GetEquilibriumFromInformationSets();
        developer.EvolutionSettings.UseAcceleratedBestResponse = true;
        developer.EvolutionSettings.UseCurrentStrategyForBestResponse = true;
        developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = false;
        developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting = false;
        int actionRows = ArticleWorkedPathExtraction.ValidateActionReport(developer, 1, request.Actions.Path);
        developer.CalculateBestResponse(false);
        double[] gains = developer.Status.BestResponseImprovement.ToArray();
        if (!developer.Status.BestResponseReflectsCurrentStrategy || gains.Length != 2 || gains.Any(g => !double.IsFinite(g) || Math.Abs(g) > 1e-7))
            throw new InvalidDataException("Full primary unilateral best-response audit failed.");
        developer.SaveWeightedGameProgressesAfterEachReport = true;
        developer.SavedWeightedGameProgresses.Clear();
        developer.ActionStrategy = ActionStrategies.CurrentProbability;
        var replay = await developer.GenerateReportsByPlaying(false);
        var welfare = SavedProfileWelfare.Evaluate(options, developer.SavedWeightedGameProgresses);
        Directory.CreateDirectory(output);
        string report = Path.Combine(output, "replayed-report.csv");
        File.WriteAllText(report, replay.csvReports.Single());
        int cells = MultipleEquilibriaStrategyAudit.ValidateReplay(request.Numeric.Path, report);
        AgreementToBargainStudy.ExportProfile(developer, options, 1, request.Equilibrium.Path, request.Actions.Path, report, output, fallbacks,
            () => FinalArticleCaseFactory.Create(prepared.Case));
        string[] diagrams = [];
        if (request.GenerateDiagrams)
        {
            string sources = Path.Combine(output, "Sources", "Diagrams");
            Directory.CreateDirectory(sources);
            diagrams = developer.GameDefinition.ProduceManualReports(developer.SavedWeightedGameProgresses, "-Eq1")
                .Where(r => r.suffix.EndsWith(".tex", StringComparison.Ordinal)).Select(r => {
                    string path = Path.Combine(sources, request.CaseId + " " + r.suffix);
                    File.WriteAllText(path, r.reportcontent); return path;
                }).ToArray();
            if (diagrams.Length != 6) throw new InvalidDataException("Expected six individual diagram sources.");
        }
        if (!complete.SequenceEqual(developer.GetEquilibriumFromInformationSets()) || StrategicGameFingerprint.Capture(developer) != prepared.Identity)
            throw new InvalidDataException("Validation or reporting changed the complete policy or game.");
        ValidateInputs(request); // Detect changes to any frozen source during processing.
        object Identity(string path) => new FileIdentity(Path.GetFullPath(path), FinalArticleExecution.Hash(path));
        File.WriteAllText(Path.Combine(output, "validation.json"), JsonSerializer.Serialize(new {
            Schema = "validated-final-profile-v1", Passed = true, StartedUtc = started, FinishedUtc = DateTime.UtcNow,
            request.CaseId, prepared.Case, prepared.OptionSetName, GameIdentity = prepared.Identity,
            Request = Identity(requestFile), Inputs = request, FullBestResponseGains = gains,
            MaximumGain = Math.Max(0, gains.Max()), ActionRows = actionRows, ReproducedNumericCells = cells,
            CompleteStrategySha256 = ArticleApproximateSearch.ProfileHash(complete), CompleteStrategyUnchanged = true,
            UnspecifiedOffPathInformationSets = fallbacks.OrderBy(x => x).ToArray(), Welfare = welfare,
            GameAssembly = Identity(typeof(LitigGame).Assembly.Location), ReportingAssembly = Identity(typeof(FinalArticleProfileAudit).Assembly.Location),
            Outputs = Directory.GetFiles(output, "*", SearchOption.AllDirectories).OrderBy(x => x).Select(Identity).ToArray(),
            DiagramSources = diagrams, SolvesStarted = 0, RenderingAndVisualQAPending = diagrams.Length > 0
        }, FinalArticleExecution.Json));
        Console.WriteLine($"Validated {request.CaseId}: {actionRows} actions, {cells} numeric cells, {diagrams.Length} diagrams; no solve.");
        return 0;
    }
}
