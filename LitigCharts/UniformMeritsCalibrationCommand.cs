using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;

namespace LitigCharts;

public static class UniformMeritsCalibrationCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 2 || args[0] != "--output")
            throw new ArgumentException("Use calibrate-uniform-binary --output NEW_FILE.json.");
        string path = Path.GetFullPath(args[1]);
        if (File.Exists(path)) throw new IOException("Calibration output already exists: " + path);
        var started = DateTime.UtcNow;
        var result = UniformMeritsBinaryCalibration.Run();
        var output = new { StartedUtc = started, FinishedUtc = DateTime.UtcNow,
            AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(UniformMeritsBinaryCalibration).Assembly.Location))),
            Calibration = result };
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, output, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Party sigma {result.PartyFit.Sigma:R}; court sigma {result.CourtFit.Sigma:R}; party KL {result.PartyFit.Divergence:R}; three-signal KL {result.CourtFit.Divergence:R}");
        return 0;
    }
}
