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

    private static void ValidateAdoptionCertificate(JsonElement certificate)
    {
        JsonDocument Evidence(JsonElement item) => JsonDocument.Parse(File.ReadAllBytes(Verify(item.Deserialize<FileIdentity>(Json))));
        if(!certificate.GetProperty("Passed").GetBoolean() || !certificate.GetProperty("ExactEqualityRequirementsUnchanged").GetBoolean() ||
            certificate.GetProperty("AdditionalTimingPairsRequiredBeforeProduction").GetBoolean() ||
            string.IsNullOrWhiteSpace(certificate.GetProperty("UserInstruction").GetString()) ||
            certificate.GetProperty("CandidateCommit").GetString()!="56053d69fb4b9762f15538d897bf25d56151601a")
            throw new InvalidDataException("Incomplete exact adoption certificate.");
        using var scope=Evidence(certificate.GetProperty("Scope"));
        if(scope.RootElement.GetProperty("Schema").GetString()!="exact-verification-scope-v2" ||
            !scope.RootElement.GetProperty("RequiredCases").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(new[] {"rn","ra"}))
            throw new InvalidDataException("Adoption certificate scope changed.");
        var cases=certificate.GetProperty("Cases");
        if(cases.GetArrayLength()!=2 || !cases.EnumerateArray().Select(c=>c.GetProperty("CaseId").GetString()).ToHashSet().SetEquals(new[] {"rn","ra"}))
            throw new InvalidDataException("Adoption requires both full exact cases.");
        foreach(var c in cases.EnumerateArray())
        {
            int expected=c.GetProperty("CaseId").GetString()=="rn" ? 315 : 413;
            using var exact=Evidence(c.GetProperty("FullExactResult")); var result=exact.RootElement;
            if(c.GetProperty("ExpectedPivots").GetInt32()!=expected || result.GetProperty("PivotCount").GetInt32()!=expected ||
                new[] {"Passed","InitialEqual","ExactComparison","CompleteStrategyEqual","SavedReloadedEqual","FrozenProductionStrategyEqual"}.Any(k=>!result.GetProperty(k).GetBoolean()))
                throw new InvalidDataException("Full exact adoption evidence failed.");
            using var reference=Evidence(c.GetProperty("ReferenceResult"));
            if(!reference.RootElement.GetProperty("Passed").GetBoolean() || reference.RootElement.GetProperty("PivotCount").GetInt32()!=expected)
                throw new InvalidDataException("Reference completion evidence failed.");
            using var comparison=Evidence(c.GetProperty("ExactOutputComparison"));
            var report=comparison.RootElement;
            if(!report.GetProperty("Passed").GetBoolean() || report.GetProperty("Checks").GetArrayLength()!=5 ||
                report.GetProperty("Checks").EnumerateArray().Any(x=>!x.GetProperty("exactEqual").GetBoolean()))
                throw new InvalidDataException("Complete strategy/action/outcome equality has not passed.");
        }
        var timings=certificate.GetProperty("OrdinaryTimingEvidence");
        if(timings.GetArrayLength()!=2 || timings.EnumerateArray().Select(x=>x.GetProperty("Path").GetString()).Distinct().Count()!=2)
            throw new InvalidDataException("Repeated ordinary timing evidence is missing.");
        foreach(var item in timings.EnumerateArray())
        {
            using var comparison=Evidence(item);
            if(!comparison.RootElement.GetProperty("Passed").GetBoolean()) throw new InvalidDataException("Ordinary output comparison failed.");
        }
        var fixtures=certificate.GetProperty("FocusedFixtureEvidence");
        if(fixtures.GetArrayLength()!=4) throw new InvalidDataException("Focused fixture evidence missing.");
        foreach(var item in fixtures.EnumerateArray())
        {
            using var fixture=Evidence(item); var value=fixture.RootElement;
            if(value.TryGetProperty("exitCode",out var code) ? code.GetInt32()!=0 : !value.GetProperty("Passed").GetBoolean())
                throw new InvalidDataException("Focused fixture evidence failed.");
        }
        Verify(certificate.GetProperty("IntegralityProof").Deserialize<FileIdentity>(Json));
        using var sources=Evidence(certificate.GetProperty("SourceIdentities"));
        if(sources.RootElement.GetProperty("CandidateCommit").GetString()!=certificate.GetProperty("CandidateCommit").GetString())
            throw new InvalidDataException("Verified optimization source identity changed.");
    }

    public static void ValidateAuthorization(Authorization authorization)
    {
        if (authorization == null) throw new InvalidDataException("Dispatch requires completed exact verification and resolved specifications.");
        using var exact = JsonDocument.Parse(File.ReadAllBytes(Verify(authorization.ExactVerification)));
        var e = exact.RootElement;
        if(e.TryGetProperty("Schema",out var schema) && schema.GetString()=="exact-ecta-adoption-v1")
            ValidateAdoptionCertificate(e);
        else
        {
        // Preserve the original three-case contract for historical manifests.
        // The user's later scope reduction is accepted only through its frozen,
        // hash-bound decision record; no equality requirement is weakened.
        int requiredCases=3, requiredPairs=5;
        if (e.TryGetProperty("VerificationScope",out var scopeIdentity))
        {
            using var scope=JsonDocument.Parse(File.ReadAllBytes(Verify(scopeIdentity.Deserialize<FileIdentity>(Json))));
            var v=scope.RootElement;
            if(v.GetProperty("Schema").GetString()!="exact-verification-scope-v2" ||
                !v.GetProperty("RequiredCases").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(new[] { "rn","ra" }) ||
                v.GetProperty("RequiredTimingPairs").GetInt32()!=4 || !v.GetProperty("ExactEqualityRequirementsUnchanged").GetBoolean() ||
                string.IsNullOrWhiteSpace(v.GetProperty("UserSteering").GetString()))
                throw new InvalidDataException("Unrecognized revised exact-verification scope.");
            requiredCases=2; requiredPairs=4;
            if(!e.GetProperty("Cases").EnumerateArray().Select(c=>c.GetProperty("CaseId").GetString()).ToHashSet().SetEquals(new[] { "rn","ra" }) ||
                !e.GetProperty("ComparedTimingPairs").EnumerateArray().Select(p=>p.GetProperty("CaseId").GetString()+":"+p.GetProperty("Repetition").GetInt32())
                    .ToHashSet().SetEquals(new[] { "rn:1","rn:2","ra:1","ra:2" }))
                throw new InvalidDataException("Revised verification omits a required case or timing repetition.");
            foreach(var c in e.GetProperty("Cases").EnumerateArray())
            {
                int expected=c.GetProperty("CaseId").GetString()=="rn" ? 315 : 413;
                var result=c.GetProperty("EquivalenceResult");
                if(result.GetProperty("PivotCount").GetInt32()!=expected ||
                    new[] { "Passed","InitialEqual","ExactComparison","CompleteStrategyEqual","SavedReloadedEqual","FrozenProductionStrategyEqual" }
                        .Any(key=>!result.GetProperty(key).GetBoolean()))
                    throw new InvalidDataException("Required full exact comparison is incomplete.");
            }
        }
        if (!e.GetProperty("AcceptanceComplete").GetBoolean() || !e.GetProperty("InstrumentedAndOrdinaryOutputsEqual").GetBoolean() ||
            e.GetProperty("Cases").GetArrayLength() != requiredCases || e.GetProperty("Cases").EnumerateArray().Any(c => !c.GetProperty("CompletedEquivalence").GetBoolean()) ||
            e.GetProperty("ComparedTimingPairs").GetArrayLength() != requiredPairs ||
            e.GetProperty("ComparedTimingPairs").EnumerateArray().Any(p => !p.GetProperty("ExactOutputsEqual").GetBoolean()))
            throw new InvalidDataException("Exact ECTA verification is incomplete.");
        }
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
