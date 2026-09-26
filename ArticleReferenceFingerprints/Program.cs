using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingAlgorithms;
using System.Security.Cryptography;
using System.Text.Json;

// Run in a copied reference dependency directory, with its original runtimeconfig
// and deps file. This executable performs full-tree initialization only, never ECTA.
if (args.Length != 1) throw new ArgumentException("Supply a new reference fingerprint output JSON.");
string output = Path.GetFullPath(args[0]);
if (File.Exists(output)) throw new IOException("Reference export already exists.");
DateTime started = DateTime.UtcNow;
var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain);
var options = launcher.GetOptionsSets().Cast<LitigGameOptions>().ToArray();
if (options.Length != 30) throw new InvalidDataException("Expected all thirty original reservations.");
var identities = new List<object>();
foreach (var option in options)
{
    var developer = await ArticleWorkedPathExtraction.InitializeAsync(option);
    var identity = StrategicGameFingerprint.Capture(developer);
    identities.Add(new { option.Name, Identity = identity });
    Console.WriteLine(option.Name + ": " + identity.CompleteSha256);
}
object FileId(string p) => new { Path = p, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) };
Directory.CreateDirectory(Path.GetDirectoryName(output));
using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
JsonSerializer.Serialize(stream, new { Schema = "original-30-reference-identities-v1", StartedUtc = started, FinishedUtc = DateTime.UtcNow,
    GameAssembly = FileId(typeof(SequenceForm).Assembly.Location), ExporterAssembly = FileId(typeof(Program).Assembly.Location),
    SolvesStarted = 0, Cases = identities }, new JsonSerializerOptions { WriteIndented = true });

namespace ACESim
{
    // Compile the exact same fingerprint writer against the immutable pre-change
    // assembly. The original ten-offer conversion is the only missing API.
    public static class OriginalOfferCompatibility
    {
        public static double[] GetOfferValues(this LitigGameOptions options) => Enumerable.Range(1, options.NumOffers)
            .Select(a => Game.ConvertActionToUniformDistributionDraw(a, options.NumOffers, options.IncludeEndpointsForOffers)).ToArray();
    }
}
