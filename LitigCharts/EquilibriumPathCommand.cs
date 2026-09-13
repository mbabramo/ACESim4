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
            if (args.Length != 2 || args[0] != "--request")
                throw new ArgumentException("equilibrium-paths --request <json>");
            await ArticleEquilibriumPaths.RunAsync(args[1], Console.WriteLine);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
