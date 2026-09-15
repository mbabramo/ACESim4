using ACESim;
using ACESim.Util;
using ACESimBase.Util;
using System;
using System.IO;
using CsvHelper;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using ACESimBase.Util.Tikz;

namespace LitigCharts
{
    class Program
    {

        static async System.Threading.Tasks.Task<int> Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "multiple-equilibria-report")
                return await MultipleEquilibriaExhibits.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "welfare-outcomes")
                return await WelfareOutcomeExhibits.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "exit-fees")
                return await ExitFeeCharts.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "equilibrium-publication")
                return await EquilibriumPublicationTables.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "equilibrium-change-focus")
                return await EquilibriumChangeFocusCommand.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "equilibrium-mixing")
                return await EquilibriumMixingCommand.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "equilibrium-paths")
                return await EquilibriumPathCommand.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] is "pressure" or "equilibrium-changes")
                return await EquilibriumChangeTables.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "tables")
                return await PublicationTables.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "pure")
                return await EnumeratedPureReport.RunAsync(args.Skip(1).ToArray());
            return await ArticleDiagramCommand.RunAsync(args);
        }
    }
}
