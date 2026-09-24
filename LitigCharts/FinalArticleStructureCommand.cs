using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ACESim;

namespace LitigCharts;

/// <summary>Explicit agreement-enabled illustrations; initialization and enumeration only.</summary>
public static class FinalArticleStructureCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if(args.Length!=2 || args[0]!="--output")throw new ArgumentException("Use final-structure-sources --output NEW_DIRECTORY.");
        string output=Path.GetFullPath(args[1]);
        if(Directory.Exists(output))throw new IOException("Structural output already exists.");
        var diagrams=await ArticleGameTreeDiagrams.GenerateAsync(true);
        Directory.CreateDirectory(output);var files=new List<object>();
        static FinalArticleExecution.FileIdentity Id(string path)=>new(path,FinalArticleExecution.Hash(path));
        foreach(var diagram in diagrams)
        {
            string stem=Path.Combine(output,diagram.FileStem);
            File.WriteAllText(stem+".tex",diagram.Latex);File.WriteAllText(stem+".txt",diagram.Description+"\n");
            files.Add(new{diagram.FileStem,TeX=Id(stem+".tex"),Caption=Id(stem+".txt")});
        }
        File.WriteAllText(Path.Combine(output,"README.md"),"""
            # Agreement-enabled structural illustrations

            These views enumerate the actual extensive-form rules with two signal bins and two offers
            solely for illustration. They are not equilibria, benchmark games, or substitutes for the
            unchanged production trees. Continuous uniform merits are integrated, not discrete states.
            Party and court sigma are 0.2; signal and offer representatives are 0.25 and 0.75.
            Initial wealth is 10 each, damages 1, entry and trial cost 0.15 each; American rule, risk neutral.

            Private exit commitments precede simultaneous agreement choices. A player observes its own
            signal and commitment, not the other player's private signal, commitment or simultaneous
            agreement choice. Repeated P/D numbers mark the same information set. Only mutual agreement
            reaches the simultaneous offers. Refusal and failed offers use the original commitment,
            mutual-exit lottery, or trial outcome. Court findings are not true liability.

            The beginning view stops after private signals. The end views use the first signal pair
            (0.25, 0.25) after filing and answering. Full views preserve all branches. Simplified views
            integrate terminal lotteries and show expected final wealth, not a realized monetary split.
            Chance probabilities are rounded to three decimals, wealth to four. Captions are companion
            text; strategy probabilities are deliberately absent because no equilibrium was computed.

            Regenerate through the recorded frozen LitigCharts build using final-structure-sources
            --output NEW_DIRECTORY. Compile the retained TeX with LuaLaTeX; original sources/builds
            and active production are not used or modified by this reporting command.
            """);
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{
            Schema="final-structure-sources-v1",CreatedUtc=DateTime.UtcNow,Passed=true,AgreementEnabled=true,
            SolvesStarted=0,IllustrativeOnly=true,SignalBins=2,OfferValues=new[]{.25,.75},
            Artifacts=files,Readme=Id(Path.Combine(output,"README.md")),
            CoreAssembly=Id(typeof(LitigGame).Assembly.Location),ReportingAssembly=Id(typeof(FinalArticleStructureCommand).Assembly.Location),
            RenderingAndVisualQAPending=true},FinalArticleExecution.Json));
        return 0;
    }
}
