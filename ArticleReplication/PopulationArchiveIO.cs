using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArticleReplication;

/// <summary>Optional lossless shared storage for large repeated policy experiments.</summary>
public static class PopulationArchiveIO
{
    private sealed record Reference(string Name, byte Player, string Sha256);
    private sealed record Index(string Schema, string GameHash, string[] Keys, string StoreRelativePath, Reference[] Policies, Reference[] Probes);
    public static string PhysicalPath(string path) => File.Exists(path) ? path : path + ".gz";
    public static PopulationExperiment.Archive Read(string path)
    {
        string actual = PhysicalPath(path);
        if (!actual.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) return Files.Read<PopulationExperiment.Archive>(actual);
        using var input = File.OpenRead(actual);
        using var zip = new GZipStream(input, CompressionMode.Decompress);
        var index = JsonSerializer.Deserialize<Index>(zip, Files.Json) ?? throw new InvalidDataException("Empty policy index.");
        if (index.Schema != "population-policy-reference-v1") throw new InvalidDataException("Unknown policy index schema.");
        string store = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, index.StoreRelativePath));
        PopulationExperiment.SavedPolicy Restore(Reference item)
        {
            if (item.Sha256.Length != 64 || item.Sha256.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid policy hash.");
            using var file = File.OpenRead(Path.Combine(store, item.Sha256[..2], item.Sha256 + ".bin.gz"));
            using var compressed = new GZipStream(file, CompressionMode.Decompress);
            using var bytes = new MemoryStream(); compressed.CopyTo(bytes);
            if (Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant() != item.Sha256) throw new InvalidDataException("Changed policy blob.");
            bytes.Position = 0; using var reader = new BinaryReader(bytes, Encoding.UTF8, true);
            if (reader.ReadUInt32() != 0x314C4F50 || reader.ReadByte() != item.Player) throw new InvalidDataException("Wrong policy blob/player.");
            int count = reader.ReadInt32();
            if (count < 0 || count > 1_000_000) throw new InvalidDataException("Invalid policy row count.");
            var rows = new double[count][];
            for (int i = 0; i < count; i++)
            {
                int actions = reader.ReadInt32();
                if (actions < 1 || actions > 255) throw new InvalidDataException("Invalid policy action count.");
                rows[i] = Enumerable.Range(0, actions).Select(_ => reader.ReadDouble()).ToArray();
            }
            if (bytes.Position != bytes.Length) throw new InvalidDataException("Trailing policy bytes.");
            return new(item.Name, item.Player, rows);
        }
        return new("population-policy-archive-v1", index.GameHash, index.Keys, index.Policies.Select(Restore).ToArray(), index.Probes.Select(Restore).ToArray());
    }
    public static void Save(string path, PopulationExperiment.Archive archive, string? store)
    {
        if (store == null) { Files.Save(path, archive); return; }
        store = Path.GetFullPath(store); Directory.CreateDirectory(store);
        Reference Keep(PopulationExperiment.SavedPolicy policy)
        {
            using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
            writer.Write(0x314C4F50u); writer.Write(policy.Player); writer.Write(policy.Probabilities.Length);
            foreach (var row in policy.Probabilities) { writer.Write(row.Length); foreach (double value in row) writer.Write(value); }
            writer.Flush(); byte[] data = bytes.ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            string file = Path.Combine(store, hash[..2], hash + ".bin.gz");
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var output = File.Create(temporary)) using (var zip = new GZipStream(output, CompressionLevel.Fastest)) zip.Write(data);
                try { File.Move(temporary, file); }
                catch (IOException) when (File.Exists(file)) { File.Delete(temporary); }
            }
            return new(policy.Name, policy.Player, hash);
        }
        var index = new Index("population-policy-reference-v1", archive.GameHash, archive.Keys,
            Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(path))!, store), archive.Policies.Select(Keep).ToArray(), archive.Probes.Select(Keep).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var target = new FileStream(path + ".gz", FileMode.CreateNew);
        using var gzip = new GZipStream(target, CompressionLevel.Fastest);
        JsonSerializer.Serialize(gzip, index, Files.Json);
    }
}
