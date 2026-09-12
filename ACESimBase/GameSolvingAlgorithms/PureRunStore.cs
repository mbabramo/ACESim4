using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms
{
    public sealed record PureRunManifest(int Schema, string Fingerprint, int Rows, int Columns,
        int BlockRows, string ConfigurationJson, string SourceStateJson, double NumericalTolerance,
        string CatalogJson, string Arithmetic);

    /// <summary>Immutable, checksummed matrix blocks plus atomic completion markers; safe to resume after interruption.</summary>
    public sealed class PureRunStore : IDisposable
    {
        public const int Schema = 1;
        public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
        public string DirectoryPath { get; }
        public PureRunManifest Manifest { get; }
        public string Fingerprint => Manifest.Fingerprint;
        private readonly FileStream runLock;
        private PureRunStore(string directory, PureRunManifest manifest, FileStream runLock = null)
        { DirectoryPath = directory; Manifest = manifest; this.runLock = runLock; }
        public void Dispose() => runLock?.Dispose();
        public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        private static string Identity(string configuration, string source, string catalog, double tolerance, byte[] terminals) =>
            Hash(Encoding.UTF8.GetBytes(string.Join("\n", Schema, configuration, source, catalog,
                tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture), Hash(terminals))));

        public static PureRunStore Open(EnumeratedPureSettings settings, IEnumeratedPureStrategySpace space, List<PureTerminal> terminals)
        {
            if (string.IsNullOrWhiteSpace(settings.OutputDirectory)) throw new ArgumentException("An isolated output directory is required.");
            string directory = Path.GetFullPath(settings.OutputDirectory);
            bool exists = Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();
            if (exists && !settings.Resume) throw new IOException("Output directory is not empty. Use a fresh directory or explicit --resume.");
            Directory.CreateDirectory(directory);
            var runLock = new FileStream(Path.Combine(directory, "run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                byte[] termBytes = JsonSerializer.SerializeToUtf8Bytes(terminals, Json);
                string fingerprint = Identity(settings.ConfigurationJson, settings.SourceStateJson, space.DescriptionJson, settings.NumericalTolerance, termBytes);
                var manifest = new PureRunManifest(Schema, fingerprint, space.Count(0), space.Count(1), 64,
                    settings.ConfigurationJson, settings.SourceStateJson, settings.NumericalTolerance, space.DescriptionJson,
                    "IEEE 754 binary64 accumulation in tree order; SequenceForm terminal utility rounding and rationalized chance probabilities. Numerical verification, not symbolic exactness.");
                string manifestPath = Path.Combine(directory, "run.json");
                if (File.Exists(manifestPath))
                {
                    var old = JsonSerializer.Deserialize<PureRunManifest>(File.ReadAllText(manifestPath), Json);
                    if (old != manifest) throw new InvalidDataException("Checkpoint identity differs (options, source, numerical settings, catalog, or game tree). Use a new directory.");
                }
                else if (exists) throw new InvalidDataException("Existing directory has no recognized run manifest.");
                var store = new PureRunStore(directory, manifest, runLock);
                store.WriteOnce("run.json", JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
                store.WriteOnce("catalog.json", Encoding.UTF8.GetBytes(space.DescriptionJson));
                using var compressed = new MemoryStream();
                using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, true)) gzip.Write(termBytes);
                store.WriteOnce("terminals.json.gz", compressed.ToArray());
                return store;
            }
            catch { runLock.Dispose(); throw; }
        }

        public static PureRunStore Read(string directory)
        {
            directory = Path.GetFullPath(directory);
            var manifest = JsonSerializer.Deserialize<PureRunManifest>(ReadChecked(Path.Combine(directory, "run.json")), Json);
            if (manifest.Schema != Schema || manifest.Rows < 1 || manifest.Columns < 1 || manifest.BlockRows != 64)
                throw new InvalidDataException("Unsupported run schema.");
            return new PureRunStore(directory, manifest);
        }

        public List<PureTerminal> ReadTerminals()
        {
            using var file = new MemoryStream(ReadChecked(Path.Combine(DirectoryPath, "terminals.json.gz")));
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var uncompressed = new MemoryStream(); gzip.CopyTo(uncompressed);
            byte[] bytes = uncompressed.ToArray();
            if (Identity(Manifest.ConfigurationJson, Manifest.SourceStateJson, Manifest.CatalogJson, Manifest.NumericalTolerance, bytes) != Fingerprint)
                throw new InvalidDataException("Terminal cache fingerprint mismatch.");
            return JsonSerializer.Deserialize<List<PureTerminal>>(bytes, Json);
        }

        public PurePayoffMatrix LoadMatrix()
        {
            var matrix = new PurePayoffMatrix(Manifest.Rows, Manifest.Columns);
            for (int start = 0; start < matrix.Rows; start += Manifest.BlockRows) ReadBlock(matrix, start);
            return matrix;
        }

        public PurePayoffMatrix BuildOrLoadMatrix(List<PureTerminal> terms, int workers)
        {
            if (workers < 1) throw new ArgumentOutOfRangeException(nameof(workers));
            var matrix = new PurePayoffMatrix(Manifest.Rows, Manifest.Columns);
            var byRow = Enumerable.Range(0, matrix.Rows).Select(_ => new List<PureTerminal>()).ToArray();
            foreach (var term in terms) foreach (int row in term.Rows) byRow[row].Add(term);
            int blocks = (matrix.Rows + Manifest.BlockRows - 1) / Manifest.BlockRows;
            Parallel.For(0, blocks, new ParallelOptions { MaxDegreeOfParallelism = workers }, block =>
            {
                int start = block * Manifest.BlockRows;
                if (File.Exists(BlockPath(start))) { ReadBlock(matrix, start); return; }
                int end = Math.Min(matrix.Rows, start + Manifest.BlockRows);
                for (int r = start; r < end; r++)
                {
                    var mass = new double[matrix.Columns];
                    foreach (var term in byRow[r])
                    {
                        double p = term.ChanceProbability * term.PlaintiffUtility;
                        double d = term.ChanceProbability * term.DefendantUtility;
                        foreach (int c in term.Columns)
                        {
                            int index = r * matrix.Columns + c;
                            matrix.Plaintiff[index] += p;
                            matrix.Defendant[index] += d;
                            mass[c] += term.ChanceProbability;
                        }
                    }
                    if (mass.Any(x => Math.Abs(x - 1) > 1E-10)) throw new InvalidDataException("Terminal probability mass differs from one.");
                }
                WriteBlock(matrix, start, end);
            });
            return matrix;
        }

        private string BlockPath(int start) => Path.Combine(DirectoryPath, $"payoffs-{start:D6}.bin");
        private void WriteBlock(PurePayoffMatrix matrix, int start, int end)
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            {
                writer.Write("ACESim-Pure-Payoffs-1"); writer.Write(Fingerprint);
                writer.Write(start); writer.Write(end); writer.Write(matrix.Columns);
                for (int i = start * matrix.Columns; i < end * matrix.Columns; i++)
                { writer.Write(matrix.Plaintiff[i]); writer.Write(matrix.Defendant[i]); }
            }
            byte[] content = buffer.ToArray();
            buffer.Write(SHA256.HashData(content));
            WriteOnce(Path.GetFileName(BlockPath(start)), buffer.ToArray());
        }
        private void ReadBlock(PurePayoffMatrix matrix, int start)
        {
            byte[] bytes = File.ReadAllBytes(BlockPath(start));
            if (bytes.Length < 32 || !SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).AsSpan().SequenceEqual(bytes.AsSpan(bytes.Length - 32)))
                throw new InvalidDataException("Payoff checkpoint checksum failed.");
            using var buffer = new MemoryStream(bytes, 0, bytes.Length - 32);
            using var reader = new BinaryReader(buffer);
            int end = Math.Min(matrix.Rows, start + Manifest.BlockRows);
            if (reader.ReadString() != "ACESim-Pure-Payoffs-1" || reader.ReadString() != Fingerprint || reader.ReadInt32() != start || reader.ReadInt32() != end || reader.ReadInt32() != matrix.Columns)
                throw new InvalidDataException("Payoff block identity mismatch.");
            for (int i = start * matrix.Columns; i < end * matrix.Columns; i++)
            { matrix.Plaintiff[i] = reader.ReadDouble(); matrix.Defendant[i] = reader.ReadDouble(); }
            if (buffer.Position != buffer.Length) throw new InvalidDataException("Trailing payoff block data.");
        }

        public void SaveScores(PureMatrixScores scores)
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            {
                writer.Write("ACESim-Pure-Gains-1"); writer.Write(Fingerprint);
                writer.Write(Manifest.Rows); writer.Write(Manifest.Columns);
                foreach (var score in scores.Profiles()) { writer.Write(score.PlaintiffGain); writer.Write(score.DefendantGain); }
            }
            buffer.Write(SHA256.HashData(buffer.ToArray()));
            WriteOnce("profile-gains.bin", buffer.ToArray());
            WriteOnce("restricted-best-responses.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                scores.TieTolerance, scores.PlaintiffBestUtilities, scores.DefendantBestUtilities,
                scores.PlaintiffBestResponses, scores.DefendantBestResponses,
                Note = "Indices refer to catalog order. All responses within the numerical tie tolerance are retained. Gains use the actual maximum, without tie rounding."
            }, Json));
        }
        public void Complete(double initializationSeconds, double solveSeconds, int terminalCount, int workers)
        {
            if (File.Exists(Path.Combine(DirectoryPath, "complete.json"))) return;
            WriteOnce("complete.json", JsonSerializer.SerializeToUtf8Bytes(new
            { Fingerprint, MatrixComplete = true, ProfileScoresComplete = true, initializationSeconds, solveSeconds, terminalCount, workers }, Json));
        }
        public PureBestResponse LoadBestResponse(byte player, int opponent)
        {
            string name = $"br-{player}-{opponent:D6}.json";
            string path = Path.Combine(DirectoryPath, "verification", name);
            if (!File.Exists(path)) return null;
            var response = JsonSerializer.Deserialize<PureBestResponse>(ReadChecked(path), Json);
            if (response.Fingerprint != Fingerprint || response.Player != player || response.OpponentStrategy != opponent || !double.IsFinite(response.Utility))
                throw new InvalidDataException("Best-response checkpoint identity mismatch.");
            return response;
        }
        public void SaveBestResponse(PureBestResponse response)
        {
            Directory.CreateDirectory(Path.Combine(DirectoryPath, "verification"));
            WriteOnce(Path.Combine("verification", $"br-{response.Player}-{response.OpponentStrategy:D6}.json"), JsonSerializer.SerializeToUtf8Bytes(response, Json));
        }
        public void WriteOnce(string relativePath, byte[] bytes)
        {
            string path = Path.Combine(DirectoryPath, relativePath);
            WriteAtomicOnce(path, bytes);
            if (!relativePath.EndsWith(".bin", StringComparison.Ordinal))
                WriteAtomicOnce(path + ".sha256", Encoding.ASCII.GetBytes(Hash(bytes)));
        }
        public static byte[] ReadChecked(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (File.ReadAllText(path + ".sha256") != Hash(bytes)) throw new InvalidDataException($"Artifact checksum failed: {path}");
            return bytes;
        }
        private static void WriteAtomicOnce(string path, byte[] bytes)
        {
            if (File.Exists(path))
            {
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException($"Refusing to replace different existing data: {path}");
                return;
            }
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, false);
        }
    }
}
