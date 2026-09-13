using System;
using System.Threading.Tasks;
using ACESimBase.Games.LitigGame.ManualReports;

namespace LitigCharts;

public static class EquilibriumPathCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
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
