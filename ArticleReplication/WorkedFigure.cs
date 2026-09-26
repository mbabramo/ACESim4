using ACESim;
using LitigCharts;
using System.Text.Json.Nodes;
using static ACESimBase.Games.LitigGame.ManualReports.ArticleWorkedPathExtraction;

namespace ArticleReplication;

public static class WorkedFigure
{
    public const string Stem="Figure 2 - Worked equilibrium path";
    public static async Task Generate(ResolvedArticlePlan plan,Dictionary<string,(JsonObject Audit,JsonObject Profile)> profiles,string collection)
    {
        var c=plan.Cases.Single(c=>c.Family=="baseline"&&c.AlphaP==0&&c.FeeRule=="american");var audit=profiles[c.Id].Audit;
        Choice[] Bargain(byte p,byte d,byte demand,byte offer=1)=>[
            new(LitigGameDecisions.PLiabilitySignal,p),new(LitigGameDecisions.DLiabilitySignal,d),new(LitigGameDecisions.PFile,1),new(LitigGameDecisions.DAnswer,1),
            new(LitigGameDecisions.PAbandon,2),new(LitigGameDecisions.DDefault,2),new(LitigGameDecisions.PAgreeToBargain,1),new(LitigGameDecisions.DAgreeToBargain,1),
            new(LitigGameDecisions.POffer,demand),new(LitigGameDecisions.DOffer,offer)];
        PathRequest Path(string name,Choice[] choices)=>new(name,"Selected illustrative history; verified against the complete equilibrium",choices);
        PathRequest[] paths=[Path("trial",Bargain(4,3,8)),Path("settlement",Bargain(3,3,1)),Path("adjacent-defendant-signal",Bargain(4,4,8)),
            Path("lower-demand-deviation",Bargain(4,3,1)),Path("higher-offer-deviation",Bargain(4,3,8,8)),
            Path("no-filing",Bargain(3,3,1).Take(2).Append(new(LitigGameDecisions.PFile,2)).ToArray()),
            Path("no-answer-deviation",Bargain(4,3,8).Take(3).Append(new(LitigGameDecisions.DAnswer,2)).ToArray())];
        string Input(string key)
        {
            var i=audit["Inputs"]![key]!;string file=i["Path"]!.GetValue<string>();
            if(!Files.Sha(file).Equals(i["Sha256"]!.GetValue<string>(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Changed worked-path input.");return file;
        }
        var request=new Request(audit["OptionSetName"]!.GetValue<string>(),Input("Equilibrium"),Input("Actions"),1,paths);
        var data=await ExtractAsync(request,collection);
        if(data.Paths.Where(p=>p.Name is "trial" or "settlement").SelectMany(p=>p.Steps).Where(s=>s.Decision is LitigGameDecisions.PAbandon or LitigGameDecisions.DDefault or LitigGameDecisions.PAgreeToBargain or LitigGameDecisions.DAgreeToBargain).Any(s=>s.Actions.Single(a=>a.Action==s.SelectedAction).Probability!=1))throw new InvalidDataException("A displayed zero-probability exit/refusal is now possible; update the illustrative layout.");
        if(data.Paths.Where(p=>p.Name is "trial" or "settlement" or "adjacent-defendant-signal").Any(p=>p.EquilibriumProbability<=0))throw new InvalidDataException("Illustrative reached path is no longer reached; select a new example explicitly.");
        using var resource=typeof(WorkedFigure).Assembly.GetManifestResourceStream("ArticleReplication.Templates.WorkedPath.tex")??throw new FileNotFoundException("Worked-path layout missing.");
        using var reader=new StreamReader(resource);string layout=await reader.ReadToEndAsync();
        string latex=layout.Replace("% INSERT_EXTRACTED_VALUES",ArticleWorkedPathLatexData.Build(data));
        string directory=System.IO.Path.Combine(collection,"Figures/Sources");Directory.CreateDirectory(directory);
        File.WriteAllText(System.IO.Path.Combine(directory,Stem+".tex"),latex);
        Files.Save(System.IO.Path.Combine(directory,Stem+".generated-data.json"),new{Case=c,Extraction=data,Request=request,VerifiedInformationSetLinks=true,Layout="embedded data-free template"});
        File.WriteAllText(System.IO.Path.Combine(collection,"Figures",Stem+".txt"),"An actual American-rule risk-neutral equilibrium history, with adjacent signal histories and verified zero-probability deviations. Both parties agree to bargain. Dotted links join the same information set. Utilities condition on each player's information; payoffs are changes in wealth. Omitted branches remain in the game.\n");
    }
}
