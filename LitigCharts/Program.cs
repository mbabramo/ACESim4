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
            if (args.Length > 0 && args[0] == "final-structure-sources")
                return await FinalArticleStructureCommand.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "final-signal-sources")
                return FinalArticleSignalCommand.Run(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "final-completion-snapshot")
                return FinalArticleCompletionSnapshot.Run(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "final-profile-audit")
                return await FinalArticleProfileAudit.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "final-approximate-start")
                return await ArticleApproximateCommand.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "calibrate-uniform-binary")
                return UniformMeritsCalibrationCommand.Run(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "truth-replay")
                return await TruthMappingReplayCommand.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "final-agreement-inventory")
                return await FinalArticleInventoryCommand.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "welfare-decomposition")
                return await WelfareDecompositionCommand.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "agreement-study-audit")
                return await AgreementToBargainStudy.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "completed-cases")
                return await ArticleCompletedCasesCommand.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "multiple-equilibria-report")
                return await MultipleEquilibriaExhibits.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "welfare-outcomes")
                return await WelfareOutcomeExhibits.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "exit-fees")
                return await ExitFeeCharts.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "equilibrium-publication")
                return await EquilibriumPublicationTables.RunAsync(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "equilibrium-manuscript")
                return await EquilibriumManuscriptTable.RunAsync(args.Skip(1).ToArray());
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
