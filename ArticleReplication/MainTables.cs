using ACESim;
using System.Text.Json.Nodes;

namespace ArticleReplication;

public static class MainTables
{
    public const string Primitives="Table 1 - Model primitives",Summary="Table 5 - Overall results summary";
    public sealed record Section(string Subtitle,string[][] Rows);
    public static void Verify(string generated,string reference,string output)
    {
        foreach(string stem in new[]{Primitives,Summary})Files.EqualScience(Files.Object(Path.Combine(generated,"Tables/Sources",stem+".layout.json")),Files.Object(Path.Combine(reference,stem+".layout.json")),stem+" all cells, ordering and formatting");
        Files.Save(output,new{Passed=true,EveryDisplayedCellAndPanelOrderIdentical=true});
    }
    public static async Task Compile(string output)
    {
        foreach(string stem in new[]{Primitives,Summary})
        {
            string source=Path.Combine(output,"Tables/Sources",stem+".tex");string text=File.ReadAllText(source);
            string[] parts=text.Contains("\\documentclass")?[source]:text.Split('\n').Where(l=>l.StartsWith("% ")&&l.EndsWith(".tex")).Select(l=>Files.Under(Path.GetDirectoryName(source)!,l[2..])).ToArray();
            var pdfs=new List<string>();string render=Path.Combine(output,"render",stem);Directory.CreateDirectory(render);
            foreach(string part in parts)
            {
                await Commands.Run(Path.Combine(output,"logs"),Path.GetFileNameWithoutExtension(part),"lualatex",["-interaction=nonstopmode","-halt-on-error","-output-directory="+render,part],render);
                pdfs.Add(Path.Combine(render,Path.GetFileNameWithoutExtension(part)+".pdf"));
            }
            string pdf=Path.Combine(output,"Tables",stem+".pdf");
            if(pdfs.Count==1)Files.CopyVerified(pdfs[0],pdf);else await Commands.Run(Path.Combine(output,"logs"),stem+"-merge","pdfunite",pdfs.Append(pdf),render);
            await Commands.Run(Path.Combine(output,"logs"),stem+"-preview","pdftoppm",["-png","-r","110",pdf,Path.Combine(output,"Tables",stem)],render);
        }
        Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,Tables=new[]{Primitives,Summary},VisualReviewPending=true});
    }
    public static bool Owns(string path)=>new[]{Primitives,Summary}.Any(s=>path.StartsWith("Tables/Sources/"+s,StringComparison.Ordinal));
    public static string Escape(string text)=>string.Concat(text.Select(c=>c switch{'&'=>@"\&",'%'=>@"\%",'_'=>@"\_",'#'=>@"\#",'>'=>@"$\to$",'×'=>@"$\times$",'α'=>@"$\alpha$",_=>c.ToString()}));
    public static void Table(string collection,string title,Section[] sections,double[] widths,string[] headings)
    {
        string dir=Path.Combine(collection,"Tables/Sources");Directory.CreateDirectory(dir);var pieces=new List<string>();
        Files.Save(Path.Combine(dir,title+".layout.json"),new{Headings=headings,Sections=sections.Select(s=>new object[]{s.Subtitle,s.Rows}),EmbeddedTitle=false,Typeface="Latin Modern"});
        for(int n=0;n<sections.Length;n++)
        {
            double w=headings.Length>7?18.4:16.25;var sizes=widths.Select(v=>Math.Max(.55,(w-.22*widths.Length)*v/widths.Sum())).ToArray();
            if(title.StartsWith("Table 2")||title.StartsWith("Table 3"))sizes=[2.6,1.15,.7,1.15,2.3,2.35,1.15,1.15,1.15,1.15,1.15];
            string cols="@{}"+string.Concat(sizes.Select((v,i)=>(i==0||title==Primitives?@">{\raggedright\arraybackslash}":@">{\centering\arraybackslash}")+$"p{{{v:F3}cm}}"))+"@{}";
            var body=new List<string>{$"\\begin{{minipage}}{{{sizes.Sum()+.22*widths.Length:F3}cm}}",@"\small",@"\setlength{\tabcolsep}{3pt}",@"\renewcommand{\arraystretch}{1.25}"};
            if(sections[n].Subtitle!=""&&title!=Primitives)body.Add(@"\noindent\textbf{"+Escape(sections[n].Subtitle)+@"}\par\smallskip");
            body.AddRange([@"\begin{tabular}{"+cols+"}",@"\toprule",string.Join(" & ",headings.Select(x=>@"\textbf{"+Escape(x)+"}"))+@"\\\midrule"]);
            foreach(var row in sections[n].Rows)
                body.Add(row.Skip(1).All(x=>x=="Pending")?Escape(row[0])+$" & \\multicolumn{{{row.Length-1}}}{{c}}{{Pending}}"+@"\\":string.Join(" & ",row.Select(Escape))+@"\\");
            body.Add(@"\bottomrule\end{tabular}");
            if(title.StartsWith("Table 2")||title.StartsWith("Table 3"))body.Add(@"\par\smallskip{\footnotesize Action/reach: percent. Contributions: percentage points. C/E: continue/exit.}");
            body.AddRange([@"\end{minipage}",@"\end{document}"]);
            string name=title+(sections.Length>1?$" - panel {n+1}":"");pieces.Add(name+".tex");
            File.WriteAllText(Path.Combine(dir,name+".tex"),WelfareFigure.Preamble.Replace("[10pt,tikz,border=5pt]","[10pt,border=5pt]")+string.Join('\n',body));
        }
        if(pieces.Count>1)File.WriteAllText(Path.Combine(dir,title+".tex"),"% Compile each panel source independently; merge in the listed order.\n"+string.Join('\n',pieces.Select(p=>"% "+p))+"\n");
    }
    public static void Generate(ResolvedArticlePlan plan,Dictionary<string,(JsonObject Audit,JsonObject Profile)> profiles,string collection)
    {
        var c=plan.Cases.Single(c=>c.Family=="baseline"&&c.FeeRule=="american"&&c.AlphaP==0);var options=FinalArticleCaseFactory.Create(c);
        if(options.PInitialWealth!=options.DInitialWealth||options.NumDamagesStrengthPoints!=1)throw new InvalidDataException("Primitives template requires the declared equal-wealth, fixed-damages model.");
        double alpha=plan.Cases.Single(c=>c.Family=="baseline"&&c.FeeRule=="american"&&c.AlphaP>0).AlphaP;
        string[][] primitives=[
            ["Merits and truth","Q is uniform on [0,1]; true liability T | Q is Bernoulli(Q)."],
            ["Signals",$"{c.Signals} private signal bins per party; party noise {c.PartySigma:G}; court noise {c.CourtSigma:G}; two court findings."],
            ["Damages and wealth",$"Damages {options.DamagesMax*options.DamagesMultiplier:G}; initial wealth {options.PInitialWealth:G} per party. Monetary quantities are in units of damages."],
            ["Preferences",$"Risk neutral or symmetric CARA risk aversion with alpha = {alpha:G}."],
            ["Costs",$"Filing / answering: {c.EntryCost:G} each; additional trial costs: {c.TrialCost:G} each; ordinary multiplier {c.CostMultiplier:G}."],
            ["Private exit commitments","After filing and answering, each party commits whether to exit if bargaining fails."],
            ["Agreement and offers","Simultaneous agreement; offers only if both agree. Separate information sets; own commitment is remembered."],
            ["Offer actions",string.Join(", ",c.Offers.Select(x=>x.ToString("F2")))+"; overlapping offers settle at their midpoint."],
            ["Unsuccessful bargaining","Apply private exit commitments: abandonment, default, mutual-exit lottery, or trial. Refusal is not a terminal disposition."],
            ["Fee rules","American: own costs. British: loser pays at trial and the specified unilateral exits."+(plan.Settings.IncludeTrialOnly?$" Trial-only fee shifting is a separate cost-{plan.Settings.ReferenceCostMultiplier:G} extension.":"")]];
        Table(collection,Primitives,[new("Baseline specification; case-specific departures are reported in the robustness materials",primitives)],[156,620],["Primitive","Value / interpretation"]);
        var sections=new List<Section>();var records=new List<object>();
        double Headline(string id,string measure)=>profiles[id].Audit["Welfare"]!["Headline"]![measure]!.GetValue<double>();
        double Metric(string id,string measure)=>profiles[id].Profile["Metrics"]![measure]!.GetValue<double>();
        string Signed(double n,int digits)=>(double.IsNegative(n)?"-":"+")+Math.Abs(n).ToString("F"+digits);
        string[] Row(FinalArticleCase b,string label)
        {
            string a=b.FeeRule=="trial-only"?plan.Cases.Single(x=>x.Family=="baseline"&&x.FeeRule=="american"&&x.AlphaP==b.AlphaP).Id:b.Id.Replace("__complete__","__american__");
            if(!profiles.ContainsKey(a)||!profiles.ContainsKey(b.Id)){records.Add(new{American=a,British=b.Id,Status="pending"});return new[]{label}.Concat(Enumerable.Repeat("Pending",7)).ToArray();}
            double[] delta=WelfareFigure.Measures.Select(m=>Headline(b.Id,m)-Headline(a,m)).ToArray();double trial=100*(Metric(b.Id,"Trial")-Metric(a,"Trial")),settlement=100*(Metric(b.Id,"Settlement")-Metric(a,"Settlement"));
            records.Add(new{American=a,British=b.Id,Status="audited",Differences=WelfareFigure.Measures.Zip(delta).ToDictionary(x=>x.First,x=>x.Second),TrialPercentagePoints=trial,SettlementPercentagePoints=settlement});
            return new[]{label}.Concat(delta.Select(x=>Signed(x,3))).Concat([Signed(trial,1),Signed(settlement,1)]).ToArray();
        }
        int FamilyOrder(FinalArticleCase x)=>x.Family switch{"baseline" or "cost-multiplier"=>0,"cost-timing"=>1,"trial-only"=>2,"private-noise"=>3,"court-noise"=>4,"grid"=>5,"merits-distribution"=>6,"direct-binary"=>7,_=>8};
        double Within(FinalArticleCase x)=>x.Family switch{"baseline" or "cost-multiplier"=>x.CostMultiplier,"private-noise"=>x.PartySigma,"court-noise"=>x.CourtSigma,"grid"=>Array.FindIndex(plan.Settings.Grids,g=>g.Signals==x.Signals&&g.Offers==x.Offers.Length),_=>0};
        string Label(FinalArticleCase x)=>x.Family switch{
            "baseline" or "cost-multiplier"=>$"Cost multiplier {x.CostMultiplier:G}","cost-timing"=>"Costs: "+x.Variant.Replace('-',' '),"trial-only"=>"Trial-only fee shifting",
            "private-noise"=>$"Private noise {x.PartySigma:G}","court-noise"=>$"Court noise {x.CourtSigma:G}","grid"=>$"Grid: {x.Signals} signals / {x.Offers.Length} offers",
            "merits-distribution"=>"Merits: "+x.Variant.Replace('-',' '),"direct-binary"=>"Calibrated direct binary","asymmetric-risk"=>x.AlphaP>0?"Plaintiff-only risk aversion":"Defendant-only risk aversion",_=>throw new InvalidDataException("Undescribed article family")};
        foreach(int risk in new[]{0,2})
        {
            var rows=plan.Cases.Where(x=>x.FeeRule is "complete" or "trial-only"&&x.AlphaP==risk&&x.AlphaD==risk).OrderBy(FamilyOrder).ThenBy(Within).ThenBy(x=>x.Variant,StringComparer.Ordinal).Select(x=>Row(x,Label(x))).ToArray();
            for(int i=0;i<rows.Length;i+=24)sections.Add(new((risk==0?"Risk neutral":"Symmetric risk aversion")+(i==0?" | British minus American":" | continuation"),rows.Skip(i).Take(24).ToArray()));
        }
        var asymmetric=plan.Cases.Where(x=>x.Family=="asymmetric-risk"&&x.FeeRule=="complete").OrderByDescending(x=>x.AlphaP).Select(x=>Row(x,Label(x))).ToArray();
        if(asymmetric.Length>0)sections.Add(new("Asymmetric risk aversion | British minus American",asymmetric));
        Table(collection,Summary,sections.ToArray(),[254,76,76,73,73,73,75,75],["Specification","P shortfall","D nonliable","D excess","Gross error","Real costs","Trial (pp)","Settle (pp)"]);
        Files.Save(Path.Combine(collection,"Tables/Sources",Summary+".generated-data.json"),new{Comparisons=records,Source="Central plan and newly revalidated complete profiles"});
        File.WriteAllText(Path.Combine(collection,"Tables",Primitives+".txt"),"Baseline model primitives from the central case definition.\n");
        File.WriteAllText(Path.Combine(collection,"Tables",Summary+".txt"),"British minus American for matched cases. Monetary columns use damages; trial and settlement use percentage points among all potential disputes. Pending comparisons have an unavailable endpoint and are never replaced by zero.\n");
    }
}
