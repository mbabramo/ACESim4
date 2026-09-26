using ACESim;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Standard LitigCharts reports for a resolved final-article plan, without legacy case-name parsing.</summary>
public static class FinalArticleResultsCommand
{
    public sealed record CaseInput(FinalArticleCase Case,string Audit,string Profile,string StandardReports,string Actions);
    public sealed record Request(CaseInput[] Cases,FinalArticleCase[] PlannedCases,string ResultsDirectory,string SupplementalDirectory,int Compilers=1);
    public sealed record Artifact(string Kind,string[] CaseIds,string Source,string Output,string Preview,string SourceSha256,string OutputSha256=null,string PreviewSha256=null);
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    static string Hash(string p)=>FinalArticleExecution.Hash(p);
    static JsonObject Read(string p)=>JsonNode.Parse(File.ReadAllText(p)).AsObject();
    static string Fee(FinalArticleCase c)=>c.FeeRule switch{"american"=>"American","complete"=>"British","trial-only"=>"Trial-only fee shifting",_=>throw new InvalidDataException("Fee rule")};
    static string Risk(FinalArticleCase c)=>c.AlphaP==c.AlphaD?(c.AlphaP==0?"Risk Neutral":"Risk Averse"):$"Plaintiff alpha {c.AlphaP:G}; defendant alpha {c.AlphaD:G}";
    static string Family(FinalArticleCase c)=>c.Family is "baseline" or "cost-multiplier"?"Baseline":c.Family+" - "+c.Variant;
    static double Num(JsonNode n)=>n.GetValue<double>();
    public static async Task<int> RunAsync(string[] args)
    {
        if(args.Length!=2||args[0]!="--request")throw new ArgumentException("Use final-article-results --request FILE.");
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;CultureInfo.CurrentUICulture=CultureInfo.InvariantCulture;
        var request=JsonSerializer.Deserialize<Request>(File.ReadAllText(args[1]),Json)??throw new InvalidDataException("Empty final results request.");
        if(request.Cases.Length==0||request.Cases.Select(c=>c.Case.Id).Distinct().Count()!=request.Cases.Length||request.Compilers<1||request.Compilers>32)throw new InvalidDataException("Invalid case inventory or compiler budget.");
        var artifacts=new List<Artifact>();var scientific=new List<object>();
        var audited=request.Cases.ToDictionary(c=>c.Case.Id,c=>(Input:c,Audit:Read(c.Audit),Profile:Read(c.Profile)));
        foreach(var row in audited.Values)
        {
            if(row.Audit["Passed"]?.GetValue<bool>()!=true||row.Audit["CompleteStrategyUnchanged"]?.GetValue<bool>()!=true)throw new InvalidDataException("Standard diagrams require audited complete profiles.");
            if(!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(row.Input.Case,Json),row.Audit["Case"]))throw new InvalidDataException("Standard request changed the audited case specification.");
            var validatedFiles=row.Audit["Outputs"].AsArray().ToDictionary(x=>Path.GetFullPath(x["Path"].GetValue<string>()),x=>x["Sha256"].GetValue<string>(),StringComparer.OrdinalIgnoreCase);
            foreach(string file in Directory.GetFiles(row.Input.StandardReports).Append(row.Input.Profile))
                if(!validatedFiles.TryGetValue(Path.GetFullPath(file),out var recorded)||!Hash(file).Equals(recorded,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Standard source was not covered by the individual audit: "+file);
            if(Hash(row.Input.Actions)!=row.Audit["Inputs"]["Actions"]["Sha256"].GetValue<string>())throw new InvalidDataException("Standard action source changed.");
            var expected=new[]{"costbreakdownlight.tex","costbreakdowndark.tex","stagecostlight.tex","stagecostdark.tex","offers.tex","fileans.tex"};
            var actual=Directory.GetFiles(row.Input.StandardReports,"*.tex").Select(Path.GetFileName).Order().ToArray();
            if(!actual.SequenceEqual(expected.Order()))throw new InvalidDataException("Missing or extra standard individual source: "+row.Input.Case.Id);
            string dir=Path.Combine(request.ResultsDirectory,"Individual simulations",row.Input.Case.Id);
            foreach(string input in Directory.GetFiles(row.Input.StandardReports).Order())
            {
                string text=File.ReadAllText(input);
                string target=ArticleResultsLayout.Source(dir,"standard-"+Path.GetFileNameWithoutExtension(input),Path.GetExtension(input));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using(var writer=new StreamWriter(new FileStream(target,FileMode.CreateNew)))writer.Write(text);
                if(target.EndsWith(".tex"))Add("standard-individual",[row.Input.Case.Id],target);
            }
        }
        void Add(string kind,string[] cases,string source)=>artifacts.Add(new(kind,cases,source,ArticleResultsLayout.RenderedArtifact(source,".pdf"),ArticleResultsLayout.RenderedArtifact(source,".png"),Hash(source)));
        void Source(string directory,string stem,string latex,object data,string kind,string[] ids)
        {
            string file=ArticleResultsLayout.Source(directory,stem,".tex");Directory.CreateDirectory(Path.GetDirectoryName(file));
            if(File.Exists(file))throw new IOException("Standard report collision: "+file);
            File.WriteAllText(file,latex);File.WriteAllText(Path.ChangeExtension(file,".json"),JsonSerializer.Serialize(data,Json));Add(kind,ids,file);
        }
        foreach(var group in request.Cases.GroupBy(c=>(Family:Family(c.Case),Cost:c.Case.CostMultiplier)))
        {
            var riskGroups=group.GroupBy(c=>Risk(c.Case)).ToArray();
            var views=riskGroups.Select(g=>(Risk:g.Key,Rows:g.ToArray())).ToList();
            if(riskGroups.Length>1)views.Add(("Risk Comparison",group.ToArray()));
            foreach(var view in views)
            {
                var cases=view.Rows.OrderBy(c=>c.Case.AlphaP).ThenBy(c=>c.Case.AlphaD).ThenBy(c=>c.Case.FeeRule).ToArray();var ids=cases.Select(c=>c.Case.Id).ToArray();
                string dir=Path.Combine(request.ResultsDirectory,"Aggregated Data",group.Key.Family,view.Risk),stem=ArticleResultsLayout.Cost(group.Key.Cost);
                var headings=new[]{"Fee rule","P shortfall","D nonliable","D excess","Gross error","Real costs"};
                string[] fields={"MeritoriousPlaintiffShortfall","NonliableDefendantBurden","LiableDefendantExcessBurden","GrossOutcomeError","RealLitigationExpenditures"};
                var panels=cases.GroupBy(c=>Risk(c.Case)).Select(g=>new PublicationTables.Panel(g.Key,"@{}Xccccc@{}",headings,g.Select(c=>new PublicationTables.Row(new[]{new PublicationTables.Cell(Fee(c.Case))}.Concat(fields.Select(f=>{double value=Num(audited[c.Case.Id].Audit["Welfare"]["Headline"][f]);return new PublicationTables.Cell(value.ToString("0.000"),value,f);})).ToArray())).ToArray())).ToArray();
                var table=new PublicationTables.Table(stem+"-welfare","",panels,"","Welfare from complete individually revalidated profiles.",cases.Select(c=>new PublicationTables.Source(c.Audit,Hash(c.Audit))).ToArray(),[]);
                Source(dir,stem+"-welfare",PublicationTables.Standalone("",PublicationTables.RenderFragment(table)),table,"standard-welfare",ids);
                var bars=cases.Select(c=>{
                    var rows=PublicationFigures.ReadCsv(Path.Combine(c.StandardReports,"report.csv"));var all=rows.Single(r=>r["Filter"]=="All");
                    double N(string k)=>double.Parse(all[k],CultureInfo.InvariantCulture);
                    var values=new[]{N("PDoesntFile"),N("DDoesntAnswer"),N("Settles"),N("PAbandons"),N("DDefaults"),N("Trial")-N("P Wins"),N("P Wins")};
                    PublicationFigures.ValidateDisposition(values,N("Trial"));
                    return new PublicationFigures.DispositionBar(Risk(c.Case),Fee(c.Case),audited[c.Case.Id].Audit["OptionSetName"].GetValue<string>(),values,values.Sum(),N("BothReadyToGiveUp"),N("Trial"));
                }).ToArray();
                var disposition=new PublicationFigures.DispositionData(new(args[1],Hash(args[1])),new(cases[0].Audit,Hash(cases[0].Audit)),PublicationFigures.CategoryLabels,bars);
                Source(dir,stem+"-dispositions",PublicationFigures.RenderDispositions(disposition,true),disposition,"standard-dispositions",ids);
                if(view.Risk!="Risk Comparison")
                {
                    var selections=cases.Select(c=>new PublicationFigures.StrategyCase(Fee(c.Case),audited[c.Case.Id].Audit["OptionSetName"].GetValue<string>(),c.Actions)).ToArray();
                    var grids=cases.ToDictionary(c=>audited[c.Case.Id].Audit["OptionSetName"].GetValue<string>(),c=>((int)c.Case.Signals,c.Case.Offers));
                    var strategy=ArticleStrategyFigures.Generate(selections,grids);
                    Source(dir,stem+"-participation-and-offers",strategy.Latex,new{strategy.Cases,strategy.Caption,CompleteAgreementPolicies=cases.Select(c=>audited[c.Case.Id].Profile["Strategies"])},"standard-participation",ids);
                }
            }
        }
        // Illustrative signals depend only on information primitives, so do not duplicate them for costs/risk/fees.
        foreach(var group in request.PlannedCases.GroupBy(c=>(c.Distribution,c.PartySigma,c.CourtSigma,c.Signals)))
        {
            var c=group.OrderBy(c=>c.Id,StringComparer.Ordinal).First();var options=FinalArticleCaseFactory.Create(c);string spec=FinalArticleSignalCommand.Specification(c);
            string dir=Path.Combine(request.SupplementalDirectory,"Liability signals diagrams",$"{c.Distribution}-party-{c.PartySigma:G17}-court-{c.CourtSigma:G17}-signals-{c.Signals}");
            foreach(bool bw in new[]{false,true})foreach(var diagram in ArticleSignalDiagrams.Generate(spec,bw,options).Concat(ArticleSignalDiagrams.GenerateInverse(spec,bw,options)).Concat(ArticleSignalDiagrams.GeneratePartyToParty(spec,bw,options)))
                Source(dir,diagram.FileStem,diagram.Latex,new{Case=c,diagram.Panels,diagram.Description},"standard-signals",group.Select(x=>x.Id).ToArray());
        }
        string trees=Path.Combine(request.SupplementalDirectory,"Game tree diagrams/Standard/Sources");
        await FinalArticleStructureCommand.RunAsync(["--output",trees]);
        foreach(string tex in Directory.GetFiles(trees,"*.tex"))Add("standard-structure",[],tex);
        var outputs=artifacts.Select(a=>a.Output).ToArray();if(outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputs.Length)throw new InvalidDataException("Standard output collision.");
        string inventory=Path.Combine(request.ResultsDirectory,"Run records/standard-diagram-inventory.json");Directory.CreateDirectory(Path.GetDirectoryName(inventory));
        File.WriteAllText(inventory,JsonSerializer.Serialize(new{Passed=false,Stage="SourcesGenerated",ExpectedArtifacts=artifacts.Count,Artifacts=artifacts,PendingCases=request.PlannedCases.Where(c=>!audited.ContainsKey(c.Id)).Select(c=>c.Id)},Json));
        await DiagramCompiler.CompileAllAsync(artifacts.Select(a=>a.Source).ToArray(),new ArticleDiagramCommand.Configuration{MaxParallelCompilers=request.Compilers,ProcessTimeoutSeconds=300},request.Compilers);
        var finished=artifacts.Select(a=>a with{OutputSha256=Hash(a.Output),PreviewSha256=Hash(a.Preview)}).ToArray();
        if(finished.Any(a=>!File.Exists(a.Output)||!File.Exists(a.Preview)))throw new InvalidDataException("Missing filed standard artifact.");
        foreach(var c in request.Cases)
            if(finished.Count(a=>a.Kind=="standard-individual"&&a.CaseIds.SequenceEqual(new[]{c.Case.Id}))!=6)throw new InvalidDataException("Six-artifact standard coverage failed: "+c.Case.Id);
        foreach(var section in finished.GroupBy(a=>Path.GetDirectoryName(a.Output)))
        {
            string index=Path.Combine(section.Key,"Standard reports.md");
            File.WriteAllText(index,"# Standard reports\n\nRegenerated by LitigCharts from the resolved article plan and validated inputs.\n\n"+string.Join("\n",section.OrderBy(a=>a.Output,StringComparer.Ordinal).Select(a=>"- ["+Path.GetFileNameWithoutExtension(a.Output)+"]("+Uri.EscapeDataString(Path.GetFileName(a.Output))+")"))+"\n");
        }
        File.WriteAllText(inventory,JsonSerializer.Serialize(new{Passed=true,Stage="CompiledAndFiled",ExpectedArtifacts=finished.Length,Artifacts=finished,Counts=finished.GroupBy(a=>a.Kind).ToDictionary(g=>g.Key,g=>g.Count()),Cases=request.Cases.Length,PendingCases=request.PlannedCases.Where(c=>!audited.ContainsKey(c.Id)).Select(c=>c.Id),SolvesStarted=0,VisualReviewPending=true},Json));
        Console.WriteLine($"Generated, compiled and filed {finished.Length} standard LitigCharts artifacts for {request.Cases.Length} completed cases.");return 0;
    }
}
