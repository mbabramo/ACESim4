using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport.Policies;

/// <summary>Existing -equ.csv adapter, including rational-number input and historical formatting.
/// The file contains flat profiles only; it is not a training checkpoint or a population weight file.</summary>
public static class FlatEquilibriumFiles
{
    public static string PathFor(StrategiesDeveloperBase developer) => Path.Combine(Launcher.ReportFolder(),
        developer.MasterReportName + " " + developer.GameDefinition.OptionSetName + Launcher.ManipulateSuffix("-equ.csv"));
    public static bool Exists(string path) => File.Exists(path);
    public static string Write(string path, IEnumerable<double[]> profiles)
    {
        var output = new StringBuilder();
        foreach (var profile in profiles) output.AppendLine(string.Join(",", profile));
        TextFileManage.CreateTextFile(path, output.ToString());
        return path;
    }
    public static List<double[]> Read(string path)
    {
        // Preserve the old shared-read mode and line splitting, but release the file handle.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 128);
        return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(",").Select(EFGFileReader.RationalStringToDouble).ToArray()).ToList();
    }
}
