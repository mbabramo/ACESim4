using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace ACESim
{
    /// <summary>Persistent, fail-closed claims shared by all new article solver workers.</summary>
    public sealed class ArticleSolveRegistry
    {
        public sealed record ExternalCase(string CaseId, string OriginalOptionName, string StrategicSha256,
            string OriginalSource, string OriginalQueue, string Status);
        public sealed record ExternalRegistry(string Schema, ExternalCase[] Cases);
        public sealed record Claim(string CaseId, string StrategicSha256, string BuildSha256,
            string ManifestSha256, int ClaimingProcess, DateTime ClaimedUtc, string ExternalRegistrySha256);
        public sealed record ClaimResult(bool Allowed, string Reason, string ClaimPath);

        private readonly string root;
        private readonly ExternalRegistry external;
        private readonly string externalHash;

        public ArticleSolveRegistry(string registryRoot, string externalRegistryFile)
        {
            root = Path.GetFullPath(registryRoot);
            byte[] bytes = File.ReadAllBytes(externalRegistryFile);
            external = JsonSerializer.Deserialize<ExternalRegistry>(bytes)
                ?? throw new InvalidDataException("Missing external reservations.");
            if (external.Schema != "external-original-30-v1" || external.Cases?.Length != 30 ||
                external.Cases.Select(c => c.CaseId).Distinct(StringComparer.Ordinal).Count() != 30 ||
                external.Cases.Select(c => c.OriginalOptionName).Distinct(StringComparer.Ordinal).Count() != 30 ||
                external.Cases.Any(c => string.IsNullOrWhiteSpace(c.OriginalSource) || string.IsNullOrWhiteSpace(c.OriginalQueue)))
                throw new InvalidDataException("All thirty original cases must be reserved before any new dispatch.");
            foreach (var reservation in external.Cases)
                RequireHash(reservation.StrategicSha256, "external strategic-game identity");
            if (external.Cases.Select(c => c.StrategicSha256.ToLowerInvariant()).Distinct().Count() != 30)
                throw new InvalidDataException("External game identities are not distinct; investigate the fingerprints.");
            externalHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Directory.CreateDirectory(root);
        }

        private static void RequireHash(string value, string label)
        {
            if (value == null || value.Length != 64 || !value.All(Uri.IsHexDigit))
                throw new InvalidDataException("A complete SHA256 is required for " + label + ".");
        }

        public ClaimResult TryClaimNew(string caseId, string strategicSha256, string buildSha256, string manifestSha256)
        {
            RequireHash(strategicSha256, "strategic game"); RequireHash(buildSha256, "build"); RequireHash(manifestSha256, "manifest");
            if (string.IsNullOrWhiteSpace(caseId)) throw new ArgumentException("Missing case identity.");
            // Status is deliberately irrelevant: completed, pending and incompatible
            // imports are all permanently excluded from automatic new solves.
            if (external.Cases.Any(c => c.CaseId == caseId || c.OriginalOptionName == caseId ||
                string.Equals(c.StrategicSha256, strategicSha256, StringComparison.OrdinalIgnoreCase)))
                return new(false, "original-case-reserved-for-external-import", null);
            string path = Path.Combine(root, strategicSha256.ToLowerInvariant() + ".claim.json");
            FileStream stream;
            try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); }
            catch (IOException) when (File.Exists(path)) { return new(false, "strategic-game-already-claimed", path); }
            using (stream)
            {
                JsonSerializer.Serialize(stream, new Claim(caseId, strategicSha256.ToLowerInvariant(), buildSha256,
                    manifestSha256, Environment.ProcessId, DateTime.UtcNow, externalHash));
                stream.Flush(flushToDisk: true);
            }
            return new(true, "new-strategic-game-claimed", path);
        }
    }
}
