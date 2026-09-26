using System.Text.Json.Nodes;

namespace ArticleReplication;

/// <summary>Descriptive clustering only. Tolerances here never participate in solver acceptance.</summary>
public static class MultipleReports
{
    public const string Stem="Figure 8 - Multiple equilibrium welfare outcomes";
    static readonly string[] HistoryKeys=["PSignal","DSignal","File","Answer","PExit","DExit","PAgree","DAgree","POffer","DOffer"];
    static readonly double[] Tolerances=[0,1e-8,1e-6,1e-4,1e-3];
    sealed record Profile(int Start,string Band,JsonArray Coordinates,double[] Vector,Dictionary<string,double> Reached,Dictionary<string,double?> Outcomes);
    sealed record Cluster(int ClusterNumber,int RepresentativeStart,int[] Starts,int RecoveryCount,double MaximumPairwiseDistance);

    // Error-free partial sums with final half-even correction. Used to preserve the established
    // descriptive metric across the Python-to-C# port; it does not change any engine arithmetic.
    public static double AccurateSum(IEnumerable<double> values)
    {
        var partials=new List<double>();
        foreach(double value in values)
        {
            if(!double.IsFinite(value))throw new InvalidDataException("Nonfinite descriptive metric.");
            double x=value;int i=0;
            foreach(double part in partials.ToArray())
            {
                double y=part;if(Math.Abs(x)<Math.Abs(y))(x,y)=(y,x);
                double hi=x+y,lo=y-(hi-x);if(lo!=0)partials[i++]=lo;x=hi;
            }
            if(i<partials.Count)partials.RemoveRange(i,partials.Count-i);partials.Add(x);
        }
        if(partials.Count==0)return 0;
        int n=partials.Count-1;double result=partials[n],low=0;
        while(n>0){double x=result,y=partials[--n];result=x+y;low=y-(result-x);if(low!=0)break;}
        if(n>0&&((low<0&&partials[n-1]<0)||(low>0&&partials[n-1]>0)))
        {double y=low*2,x=result+y;if(y==x-result)result=x;}
        return result;
    }
    static Profile Describe(int start,JsonObject audit,JsonObject profile)
    {
        var coordinates=new JsonArray();var vector=new List<double>();
        foreach(var node in profile["Strategies"]!.AsArray())
        {
            coordinates.Add(new JsonObject{["InformationSet"]=node!["InformationSet"]!.DeepClone(),["Player"]=node["Player"]!.DeepClone(),["Decision"]=node["Decision"]!.DeepClone(),["Actions"]=node["Actions"]!.DeepClone()});
            double[] ps=node["Probabilities"]!.AsArray().Select(v=>v!.GetValue<double>()).ToArray();
            if(ps.Any(p=>!double.IsFinite(p)||p<0||p>1)||Math.Abs(AccurateSum(ps)-1)>1e-9)throw new InvalidDataException("Invalid complete strategy.");vector.AddRange(ps);
        }
        var grouped=new Dictionary<string,List<double>>();
        foreach(var h in profile["ReachedHistories"]!.AsArray())
        {
            double p=h!["Probability"]!.GetValue<double>();if(!double.IsFinite(p)||p<=0)throw new InvalidDataException("Invalid reached probability.");
            string key=new JsonArray(HistoryKeys.Select(k=>h[k]?.DeepClone()).ToArray()).ToJsonString();
            if(!grouped.TryGetValue(key,out var list))grouped.Add(key,list=[]);list.Add(p);
        }
        var reached=grouped.ToDictionary(x=>x.Key,x=>AccurateSum(x.Value));
        if(Math.Abs(AccurateSum(reached.Values)-1)>1e-9)throw new InvalidDataException("Reached mass does not sum to one.");
        var outcomes=profile["Metrics"]!.AsObject().ToDictionary(x=>"metric:"+x.Key,x=>x.Value?.GetValue<double>());
        foreach(var field in audit["Welfare"]!["Headline"]!.AsObject())outcomes.Add("welfare:"+field.Key,field.Value!.GetValue<double>());
        if(outcomes.Values.Any(x=>x.HasValue&&!double.IsFinite(x.Value)))throw new InvalidDataException("Nonfinite outcome.");
        return new(start,audit["Threshold"]!.GetValue<double>()==.001?"below-0.001":"cap-0.001-to-0.0025",coordinates,vector.ToArray(),reached,outcomes);
    }
    static double Distance(Profile a,Profile b,string metric)
    {
        if(metric=="CompleteStrategies")
        {Files.EqualScience(a.Coordinates,b.Coordinates,"Complete strategy coordinates");if(a.Vector.Length!=b.Vector.Length)throw new InvalidDataException("Vector size changed.");return a.Vector.Zip(b.Vector).Max(p=>Math.Abs(p.First-p.Second));}
        if(metric=="ReachedBehavior")return .5*AccurateSum(a.Reached.Keys.Union(b.Reached.Keys).Select(k=>Math.Abs(a.Reached.GetValueOrDefault(k)-b.Reached.GetValueOrDefault(k))));
        if(!a.Outcomes.Keys.Order().SequenceEqual(b.Outcomes.Keys.Order()))throw new InvalidDataException("Outcome coordinates changed.");
        double maximum=0;foreach(string k in a.Outcomes.Keys){double? x=a.Outcomes[k],y=b.Outcomes[k];if(x.HasValue!=y.HasValue)return double.PositiveInfinity;if(x.HasValue)maximum=Math.Max(maximum,Math.Abs(x.Value-y!.Value));}return maximum;
    }
    static object[] Group(Profile[] items,double[,] distances,double tolerance)
    {
        var groups=new List<List<int>>();
        for(int i=0;i<items.Length;i++){var group=groups.FirstOrDefault(g=>g.All(j=>distances[i,j]<=tolerance));if(group==null)groups.Add([i]);else group.Add(i);}
        return groups.Select((g,n)=>(object)new{Cluster=n+1,RepresentativeStart=items[g[0]].Start,Starts=g.Select(i=>items[i].Start).ToArray(),RecoveryCount=g.Count,MaximumPairwiseDistance=g.SelectMany(i=>g.Select(j=>distances[i,j])).Max()}).ToArray();
    }
    public static void Generate(string stage,ResolvedArticlePlan plan,string collection)
    {
        var completed=Files.Object(Path.Combine(stage,"completed.json"));
        if(completed["Passed"]?.GetValue<bool>()!=true||completed["PrimaryEquilibriumCatalog"]!.GetValue<bool>())throw new InvalidDataException("No verified approximate results.");
        var records=completed["Results"]!.AsArray();var descriptions=new Dictionary<string,List<Profile>>();var rows=new List<Dictionary<string,object?>>();
        string supplement=Path.Combine(collection,"Supplemental materials/Multiple equilibria");Directory.CreateDirectory(Path.Combine(supplement,"Sources"));
        var dispositionRows=new List<Dictionary<string,object?>>();
        foreach(var a in records.OrderBy(a=>a!["CaseId"]!.GetValue<string>(),StringComparer.Ordinal).ThenBy(a=>a!["StartIndex"]!.GetValue<int>()))
        {
            if(!a!["Accepted"]!.GetValue<bool>())continue;string id=a["CaseId"]!.GetValue<string>();int start=a["StartIndex"]!.GetValue<int>();
            string dir=Path.Combine(stage,id,$"start-{start:D5}");var audit=Files.Object(Path.Combine(dir,"validation.json"));
            if(audit["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Unvalidated approximate profile.");
            string file=Directory.GetFiles(Path.Combine(dir,"Sources/Profiles"),"*.json").Single();var profile=Files.Object(file);
            if(!descriptions.TryGetValue(id,out var list))descriptions.Add(id,list=[]);list.Add(Describe(start,audit,profile));
            var spec=plan.Cases.Single(c=>c.Id==id);var row=new Dictionary<string,object?>{{"Risk",spec.AlphaP==0?"rn":"ra"},{"Rule",spec.FeeRule},{"Start",start},{"AverageGain",audit["AverageGain"]!.GetValue<double>()}};
            foreach(string field in WelfareFigure.Measures)row.Add(field,audit["Welfare"]!["Headline"]![field]!.GetValue<double>());rows.Add(row);
            var dr=new Dictionary<string,object?>(row);foreach(var metric in profile["Metrics"]!.AsObject())dr.Add(metric.Key,metric.Value?.GetValue<double>());dispositionRows.Add(dr);
            Files.CopyVerified(file,Path.Combine(supplement,"Sources/Profiles",id+$"-start-{start:D5}.json"));
            Files.CopyVerified(Path.Combine(dir,"validation.json"),Path.Combine(supplement,"Sources/Attempts",id+$"-start-{start:D5}-audit.json"));
        }
        var catalogs=new List<object>();var summaries=new List<Dictionary<string,object?>>();
        foreach(var (id,profiles) in descriptions)foreach(var band in profiles.GroupBy(p=>p.Band))
        {
            var items=band.OrderBy(p=>p.Start).ToArray();var metrics=new Dictionary<string,double[,]>();
            foreach(string metric in new[]{"CompleteStrategies","ReachedBehavior","Outcomes"})
            {var ds=new double[items.Length,items.Length];for(int i=0;i<items.Length;i++)for(int j=i+1;j<items.Length;j++)ds[i,j]=ds[j,i]=Distance(items[i],items[j],metric);metrics.Add(metric,ds);}
            foreach(double tolerance in Tolerances)
            {
                var row=new Dictionary<string,object?>{{"Case",id},{"Band",band.Key},{"Tolerance",tolerance},{"AcceptedStarts",items.Length}};var summary=new Dictionary<string,object?>(row);
                foreach(var (metric,ds) in metrics){var groups=Group(items,ds,tolerance);row.Add(metric,groups);summary.Add(metric,groups.Length);}catalogs.Add(row);summaries.Add(summary);
            }
        }
        Files.Save(Path.Combine(supplement,"Sources/catalog.json"),new{Complete=true,RequestedStarts=records.Count,AcceptedStarts=rows.Count,Catalogs=catalogs,Tolerances,Attempts=records,AcceptanceBandsKeptSeparate=true,PrimaryEquilibriumCatalog=false,ExactEquivalenceUsesTheseTolerances=false,Clustering="Start-ordered greedy complete-link; every within-group pair passes. No averaging.",Caveat="Recovery counts describe this finite search, not litigant selection probabilities or enumeration of every equilibrium."});
        Reports.Csv(Path.Combine(supplement,"Sources/grouping-summary.csv"),summaries);Reports.Csv(Path.Combine(supplement,"Sources/all-outcomes.csv"),dispositionRows);
        var ranges=dispositionRows.GroupBy(r=>(r["Risk"],r["Rule"])).Select(g=>new{Risk=g.Key.Item1,Rule=g.Key.Item2,Count=g.Count(),Ranges=g.First().Keys.Except(new[]{"Risk","Rule","Start"}).ToDictionary(k=>k,k=>new{Minimum=g.Select(r=>r[k] as double?).Min(),Maximum=g.Select(r=>r[k] as double?).Max()})});
        Files.Save(Path.Combine(supplement,"Sources/disposition-ranges.generated.json"),ranges);
        File.WriteAllText(Path.Combine(supplement,"README.md"),$"# Multiple equilibria\n\n{rows.Count} accepted of {records.Count} starts. Accepted profiles undergo complete-vector, full best-response, numeric replay and welfare checks. Strict early (<0.001) and cap (<0.0025) acceptance remain separate; gains use the configured terminal-utility range.\n\n- [All outcomes](Sources/all-outcomes.csv)\n- [Grouping](Sources/catalog.json)\n- [Grouping counts](Sources/grouping-summary.csv)\n- [Complete profiles](Sources/Profiles)\n\nGrouping is descriptive: tolerance never changes acceptance. Recovery frequencies do not estimate litigant selection probabilities.\n");
        Figure(rows,records,plan,collection);
    }
    static void Figure(List<Dictionary<string,object?>> rows,JsonArray records,ResolvedArticlePlan plan,string collection)
    {
        string[] labels=[@"Meritorious plaintiff\\shortfall",@"Nonliable defendant\\burden",@"Liable defendant\\excess burden",@"Gross outcome\\error",@"Real litigation\\expenditures"];
        var bounds=WelfareFigure.Measures.ToDictionary(f=>f,f=>Math.Ceiling(rows.Max(r=>(double)r[f]!)*1.04/.05)*.05);
        var lines=new List<string>{@"\begin{tikzpicture}[font=\small]"};var points=new List<object>();int starts=plan.Settings.StartsPerCore;
        for(int i=0;i<2;i++)
        {
            string risk=i==0?"rn":"ra";double top=18-i*9.5,bottom=top-6.7;
            lines.Add($"\\node[anchor=west,font=\\bfseries] at (.7,{top+1.45:G17}) {{{(i==0?"Risk neutral":"Risk averse")}}};");
            for(int j=0;j<WelfareFigure.Measures.Length;j++)
            {
                string field=WelfareFigure.Measures[j];double x0=.85+j*3.35,width=2.72,bound=bounds[field];if(bound<=0)bound=bounds[field]=.05;
                lines.Add($"\\node[align=center,anchor=south] at ({x0+width/2:G17},{top+.25:G17}) {{{labels[j]}}};");
                int tickStep=Math.Max(1,starts/5);var ticks=Enumerable.Range(0,starts).Where(n=>n%tickStep==0).Append(starts-1).Distinct();
                foreach(int start in ticks){double y=top-6.7*(start+.5)/starts;lines.Add($"\\draw[black!12] ({x0:G17},{y:G17})--({x0+width:G17},{y:G17});");if(j==0)lines.Add($"\\node[anchor=east,font=\\footnotesize] at ({x0-.1:G17},{y:G17}) {{{start}}};");}
                foreach(double frac in new[]{0,.5,1}){double x=x0+width*frac;lines.Add($"\\draw[black!10] ({x:G17},{bottom:G17})--({x:G17},{top:G17});");lines.Add($"\\node[anchor={(frac==0?"north west":frac==1?"north east":"north")},font=\\footnotesize,inner xsep=0pt] at ({x:G17},{bottom-.08:G17}) {{{bound*frac:0.###}}};");}
                lines.Add($"\\draw ({x0:G17},{top:G17})--({x0:G17},{bottom:G17})--({x0+width:G17},{bottom:G17});");
                foreach(string rule in new[]{"american","complete"})foreach(var row in rows.Where(r=>(string)r["Risk"]! ==risk&&(string)r["Rule"]! ==rule).OrderBy(r=>(int)r["Start"]!))
                {int start=(int)row["Start"]!;double value=(double)row[field]!,x=x0+width*value/bound,y=top-6.7*(start+.5)/starts;lines.Add(WelfareFigure.Mark(x,y,rule,1,2.6));points.Add(new{Risk=risk,Rule=rule,Start=start,Measure=field,Value=value,X=x,Y=y});}
            }
            lines.Add($"\\node[rotate=90] at (-.12,{top-3.35:G17}) {{Start index}};");
        }
        lines.AddRange([WelfareFigure.Mark(5.5,.25,"american"),@"\node[anchor=west] at (5.85,.25) {American};",WelfareFigure.Mark(10,.25,"complete"),@"\node[anchor=west] at (10.3,.25) {British};",@"\end{tikzpicture}",@"\end{document}"]);
        string sources=Path.Combine(collection,"Figures/Sources");Directory.CreateDirectory(sources);
        File.WriteAllText(Path.Combine(sources,Stem+".tex"),WelfareFigure.Preamble+string.Join('\n',lines));
        Files.Save(Path.Combine(sources,Stem+".generated-data.json"),new{Rows=rows,PlottedPoints=points,AxisMaxima=bounds,AcceptedProfiles=rows.Count,AttemptedStarts=records.Count,Rejected=records.Where(a=>!a!["Accepted"]!.GetValue<bool>()),NoAveragingOrGrouping=true});
        Reports.Csv(Path.Combine(sources,Stem+".csv"),rows);
        File.WriteAllText(Path.Combine(sources,Stem+".txt"),$"Welfare outcomes for all {rows.Count} accepted approximate profiles from {records.Count} starts. Rows identify starting profiles, not matched equilibria or a ranking; repeated outcomes remain separate. Unaccepted starts are omitted. Scales match across risk panels within each measure. Complete profiles and acceptance criteria appear in the Multiple equilibria supplement.\n");
    }
    public static async Task FromRun(string run,string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh output required.");var done=Files.Object(Path.Combine(run,"completed.json"));if(done["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Input run did not pass.");
        var plan=Files.Read<ResolvedArticlePlan>(Path.Combine(run,"resolved-plan.json"));Generate(Path.Combine(run,"ReportResults/MultipleStarts"),plan,output);
        string source=Path.Combine(output,"Figures/Sources",Stem+".tex"),pdf=Path.Combine(output,"Figures",Stem+".pdf");
        await Commands.Run(Path.Combine(output,"logs"),"latex","lualatex",["-interaction=nonstopmode","-halt-on-error","-output-directory="+Path.GetDirectoryName(pdf),source],Path.GetDirectoryName(source)!);
        await Commands.Run(Path.Combine(output,"logs"),"preview","pdftoppm",["-scale-to","1800","-singlefile","-png",pdf,Path.ChangeExtension(pdf,null)],output);
        Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,SolvesStarted=0,FreshlyGenerated=true,VisualReviewPending=true});
    }
    public static void Verify(string generated,string reference,string output)
    {
        var a=Files.Object(Path.Combine(generated,"Supplemental materials/Multiple equilibria/Sources/catalog.json"));var b=Files.Object(Path.Combine(reference,"Supplemental materials/Multiple equilibria/Sources/catalog.json"));
        Files.EqualScience(a["Catalogs"],b["Catalogs"],"Every grouping and maximum within-group distance");
        a=Files.Object(Path.Combine(generated,"Figures/Sources",Stem+".generated-data.json"));b=Files.Object(Path.Combine(reference,"Figures/Sources",Stem+".json"));
        foreach(string field in new[]{"Rows","PlottedPoints","AxisMaxima","AcceptedProfiles","AttemptedStarts"})Files.EqualScience(a[field],b[field],"Multiple welfare figure "+field);
        Files.Save(output,new{Passed=true,ExactScientificValues=true,GroupingTolerancesDoNotReplaceExactComparison=true});
    }
}
