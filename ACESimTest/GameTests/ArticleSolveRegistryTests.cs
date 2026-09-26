using ACESim;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACESimTest.GameTests
{
    [TestClass]
    public class ArticleSolveRegistryTests
    {
        private static readonly string ScratchRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ArticleRegistryTests"));
        private static string NewScratch() => Path.Combine(ScratchRoot, "registry-" + Guid.NewGuid().ToString("N"));
        private static void RemoveScratch(string path)
        {
            string resolved = Path.GetFullPath(path);
            if (!resolved.StartsWith(ScratchRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test cleanup escaped its isolated scratch directory.");
            Directory.Delete(resolved, recursive: true);
        }
        [TestMethod]
        public void OriginalCasesNeverDispatchAndCompetingClaimsHaveOneWinner()
        {
            string root = NewScratch();
            Directory.CreateDirectory(root);
            try
            {
                var reservations = Enumerable.Range(0, 30).Select(i => new ArticleSolveRegistry.ExternalCase(
                    "original-" + i, "option-" + i, i.ToString("X64"), "protected-source", "protected-queue", i < 27 ? "validated" : "pending")).ToArray();
                string external = Path.Combine(root, "external.json");
                File.WriteAllText(external, JsonSerializer.Serialize(new ArticleSolveRegistry.ExternalRegistry("external-original-30-v1", reservations)));
                var registry = new ArticleSolveRegistry(Path.Combine(root, "claims"), external);
                string build = new('b', 64), manifest = new('c', 64), newGame = new('d', 64);
                foreach (var item in reservations)
                {
                    Assert.IsFalse(registry.TryClaimNew(item.CaseId, newGame, build, manifest).Allowed);
                    Assert.IsFalse(registry.TryClaimNew("renamed-" + item.CaseId, item.StrategicSha256, build, manifest).Allowed);
                }
                Assert.AreEqual(0, Directory.GetFiles(Path.Combine(root, "claims")).Length);
                // Registry contention only: no solver or game calculation runs in parallel.
                var claims = new ArticleSolveRegistry.ClaimResult[8];
                Parallel.For(0, claims.Length, i => claims[i] = registry.TryClaimNew("new-case-" + i, newGame, build, manifest));
                Assert.AreEqual(1, claims.Count(c => c.Allowed));
                Assert.AreEqual(1, Directory.GetFiles(Path.Combine(root, "claims")).Length);
                var afterRestart = new ArticleSolveRegistry(Path.Combine(root, "claims"), external);
                Assert.IsFalse(afterRestart.TryClaimNew("renamed-again", newGame, build, manifest).Allowed);
                Assert.AreEqual(JsonSerializer.Serialize(new ArticleSolveRegistry.ExternalRegistry("external-original-30-v1", reservations)), File.ReadAllText(external));
            }
            finally { RemoveScratch(root); }
        }

        [TestMethod]
        public void MissingOriginalFingerprintsBlockDispatchRatherThanBecomingNewJobs()
        {
            string root = NewScratch();
            Directory.CreateDirectory(root);
            try
            {
                string external = Path.Combine(root, "external.json");
                var cases = Enumerable.Range(0, 30).Select(i => new ArticleSolveRegistry.ExternalCase(
                    "original-" + i, "option-" + i, i == 29 ? null : i.ToString("X64"), "source", "queue", "pending")).ToArray();
                File.WriteAllText(external, JsonSerializer.Serialize(new ArticleSolveRegistry.ExternalRegistry("external-original-30-v1", cases)));
                Assert.ThrowsException<InvalidDataException>(() => new ArticleSolveRegistry(Path.Combine(root, "claims"), external));
                Assert.IsFalse(Directory.Exists(Path.Combine(root, "claims")));
            }
            finally { RemoveScratch(root); }
        }
    }
}
