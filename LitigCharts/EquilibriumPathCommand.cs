using System;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Text.Json;
using ACESimBase.Games.LitigGame.ManualReports;

namespace LitigCharts;

public static class EquilibriumPathCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--combine")
            {
                string file=Path.GetFullPath(args[1]);
                using var collection=JsonDocument.Parse(File.ReadAllText(file));
                string directory=Path.GetDirectoryName(file);
                string output=Path.GetFullPath(collection.RootElement.GetProperty("OutputDirectory").GetString(),directory);
                var files=collection.RootElement.GetProperty("Results").EnumerateArray()
                    .Select(p=>Path.GetFullPath(p.GetString(),directory)).ToArray();
                var runs=files.Select(EquilibriumPathAnimation.ReadVerified).ToArray();
                if(runs.Length==0 || runs.Select(r=>r.Metadata.Id).Distinct().Count()!=runs.Length)
                    throw new InvalidDataException("Supply distinct verified traces.");
                Directory.CreateDirectory(output);
                foreach(var run in runs)
                    File.WriteAllText(Path.Combine(output,run.Metadata.Id+".html"),EquilibriumPathAnimation.BuildHtml(new[]{run}));
                File.WriteAllText(Path.Combine(output,"all-equilibrium-solution-paths.html"),EquilibriumPathAnimation.BuildHtml(runs));
                File.WriteAllText(Path.Combine(output,"equilibrium-paths-collection-manifest.json"),JsonSerializer.Serialize(new {
                    Schema="1",Request=ArticlePressureAnalysis.Hash(file),Results=files.Select(ArticlePressureAnalysis.Hash).ToArray()
                },ArticlePressureAnalysis.JsonOptions));
                Console.WriteLine($"Combined {runs.Length} independently verified original solution paths.");
                return 0;
            }
            if ((args.Length != 2 && args.Length != 3) || args[0] != "--request" ||
                (args.Length == 3 && args[2] != "--render-only" && args[2] != "--calculate-only"))
                throw new ArgumentException("equilibrium-paths --request <json> [--render-only|--calculate-only]");
            if (args.Length == 2 || args[2] != "--render-only")
                await ArticleEquilibriumPaths.RunAsync(args[1], Console.WriteLine);
            if (args.Length == 2 || args[2] != "--calculate-only")
                foreach (string file in EquilibriumPathAnimation.Render(args[1])) Console.WriteLine("Animation: " + file);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
