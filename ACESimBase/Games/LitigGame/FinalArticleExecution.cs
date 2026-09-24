using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace ACESim;

/// <summary>Frozen dispatch contract. Read-only inventories are never executable manifests.</summary>
public static class FinalArticleExecution
{
    public sealed record FileIdentity(string Path, string Sha256);
    public sealed record Authorization(FileIdentity ExactVerification, FileIdentity ScientificSpecifications);
    public sealed record Case(FinalArticleCase Parameters, StrategicGameFingerprint.Snapshot Identity);
    public sealed record ResourceBudget(int GlobalCeiling, int ExternalWorkersReserved, int OtherWorkersReserved,
        int MaximumNewWorkers, double EstimatedPeakWorkerGiB, double MemoryHeadroomGiB);
    public sealed record Manifest(string Schema, string ExecutionRoot, string ResultsDirectory, string ClaimsDirectory,
        Authorization Authorization, FileIdentity ExternalRegistry, FileIdentity PreparedInventory,
        FileIdentity Calibration, FileIdentity[] BuildFiles, ResourceBudget Resources, Case[] Cases);
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public static string Verify(FileIdentity file)
    {
        if (file == null || !System.IO.Path.IsPathFullyQualified(file.Path) || file.Sha256?.Length != 64 ||
            !string.Equals(Hash(file.Path), file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Absent or changed frozen input: " + file?.Path);
        return file.Path;
    }
    public static bool Inside(string child, string parent) => System.IO.Path.GetFullPath(child).StartsWith(
        System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(parent))+System.IO.Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    public static void ValidateAuthorization(Authorization authorization)
    {
        if (authorization == null) throw new InvalidDataException("Dispatch requires completed exact verification and resolved specifications.");
        using var exact = JsonDocument.Parse(File.ReadAllBytes(Verify(authorization.ExactVerification)));
        var e = exact.RootElement;
        if (!e.GetProperty("AcceptanceComplete").GetBoolean() || !e.GetProperty("InstrumentedAndOrdinaryOutputsEqual").GetBoolean() ||
            e.GetProperty("Cases").GetArrayLength() != 3 || e.GetProperty("Cases").EnumerateArray().Any(c => !c.GetProperty("CompletedEquivalence").GetBoolean()) ||
            e.GetProperty("ComparedTimingPairs").GetArrayLength() != 5 ||
            e.GetProperty("ComparedTimingPairs").EnumerateArray().Any(p => !p.GetProperty("ExactOutputsEqual").GetBoolean()))
            throw new InvalidDataException("Exact ECTA verification is incomplete.");
        using var specifications = JsonDocument.Parse(File.ReadAllBytes(Verify(authorization.ScientificSpecifications)));
        var s = specifications.RootElement;
        if (s.GetProperty("Schema").GetString() != "resolved-final-article-specifications-v1" ||
            !s.GetProperty("Resolved").GetBoolean() || string.IsNullOrWhiteSpace(s.GetProperty("UserDecisionRecord").GetString()))
            throw new InvalidDataException("Scientific specifications remain unresolved.");
    }

    public static Manifest Read(string path)
    {
        var result = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(path), Json)
            ?? throw new InvalidDataException("Missing execution manifest.");
        if (result.Schema != "executable-final-agreement-single-v1") throw new InvalidDataException("Not an executable primary manifest.");
        ValidateAuthorization(result.Authorization);
        if (!System.IO.Path.IsPathFullyQualified(result.ExecutionRoot) ||
            !Inside(result.ResultsDirectory, System.IO.Path.Combine(result.ExecutionRoot,"production")) ||
            !Inside(result.ClaimsDirectory, System.IO.Path.Combine(result.ExecutionRoot,"production")))
            throw new InvalidDataException("Queue and claims must remain inside this isolated workspace's production directory.");
        Verify(result.ExternalRegistry); Verify(result.PreparedInventory); Verify(result.Calibration);
        if (result.Cases == null || result.Cases.Length < 30 || result.Cases.Count(c => c.Parameters.IsExternalImport) != 30 ||
            result.Cases.Select(c => c.Parameters.Id).Distinct().Count() != result.Cases.Length ||
            result.Cases.Select(c => c.Identity.CompleteSha256).Distinct().Count() != result.Cases.Length)
            throw new InvalidDataException("Manifest requires a unique full inventory with all thirty external reservations.");
        using var prepared = JsonDocument.Parse(File.ReadAllBytes(result.PreparedInventory.Path));
        var p = prepared.RootElement;
        if (p.GetProperty("Schema").GetString() != "prepared-final-agreement-inventory-v1" ||
            p.GetProperty("SolvesStarted").GetInt32() != 0 || p.GetProperty("Cases").GetArrayLength() != result.Cases.Length)
            throw new InvalidDataException("Prepared full-game inventory is missing or incomplete.");
        var preparedCases = p.GetProperty("Cases").EnumerateArray().ToDictionary(c => c.GetProperty("Case").GetProperty("Id").GetString());
        foreach (var item in result.Cases)
        {
            if (!preparedCases.TryGetValue(item.Parameters.Id, out var match) ||
                match.GetProperty("Identity").Deserialize<StrategicGameFingerprint.Snapshot>(Json) != item.Identity ||
                JsonSerializer.Serialize(match.GetProperty("Case").Deserialize<FinalArticleCase>(Json), Json) != JsonSerializer.Serialize(item.Parameters, Json))
                throw new InvalidDataException("Case differs from the prepared inventory: " + item.Parameters.Id);
            FinalArticleCaseFactory.Create(item.Parameters);
            if (item.Parameters.Distribution == "direct-binary" && !string.Equals(item.Parameters.CalibrationSha256,result.Calibration.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Direct-binary calibration hash changed.");
        }
        using var specifications = JsonDocument.Parse(File.ReadAllBytes(result.Authorization.ScientificSpecifications.Path));
        if (!specifications.RootElement.GetProperty("ApprovedCaseIds").EnumerateArray().Select(x => x.GetString()).ToHashSet().SetEquals(result.Cases.Select(c => c.Parameters.Id)))
            throw new InvalidDataException("Dispatch inventory differs from the resolved scientific specification.");
        var registry = JsonSerializer.Deserialize<ArticleSolveRegistry.ExternalRegistry>(File.ReadAllBytes(result.ExternalRegistry.Path));
        foreach (var item in result.Cases.Where(c => c.Parameters.IsExternalImport))
            if (registry.Cases.Count(c => c.CaseId == item.Parameters.Id && c.OriginalOptionName == item.Parameters.OriginalOptionName && c.StrategicSha256 == item.Identity.CompleteSha256) != 1)
                throw new InvalidDataException("External reservation differs from the instantiated original game.");
        _ = new ArticleSolveRegistry(result.ClaimsDirectory, result.ExternalRegistry.Path);
        if (result.BuildFiles == null || result.BuildFiles.Length < 3 || result.BuildFiles.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.BuildFiles.Length)
            throw new InvalidDataException("Missing frozen executable dependency manifest.");
        foreach (var build in result.BuildFiles) Verify(build);
        var current = result.BuildFiles.SingleOrDefault(f => string.Equals(System.IO.Path.GetFullPath(f.Path),typeof(FinalArticleExecution).Assembly.Location,StringComparison.OrdinalIgnoreCase));
        if (current == null) throw new InvalidDataException("This game assembly is not part of the frozen build.");
        ValidateBudget(result.Resources, result.Resources.MaximumNewWorkers);
        return result;
    }

    public static void ValidateBudget(ResourceBudget budget, int workers)
    {
        if (budget == null || budget.GlobalCeiling < 1 || budget.GlobalCeiling > 30 || budget.ExternalWorkersReserved < 0 || budget.OtherWorkersReserved < 0 ||
            workers < 1 || workers > budget.MaximumNewWorkers || workers+budget.ExternalWorkersReserved+budget.OtherWorkersReserved > budget.GlobalCeiling ||
            !double.IsFinite(budget.EstimatedPeakWorkerGiB) || budget.EstimatedPeakWorkerGiB <= 0 ||
            !double.IsFinite(budget.MemoryHeadroomGiB) || budget.MemoryHeadroomGiB < 8)
            throw new InvalidDataException("Requested workers exceed the declared shared CPU/memory budget.");
    }
}
