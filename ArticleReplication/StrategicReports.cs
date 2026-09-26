using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using ACESimBase.Games.LitigGame.ManualReports;
using LitigCharts;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;

namespace ArticleReplication;

public static class StrategicReports
{
    public static readonly string[] Stems=["Table 2 - Strategy mechanisms","Table 3 - Risk-averse strategy changes"];
    static readonly string[] Decisions=["P Files","D Answers","P Abandons","D Defaults","P Agrees To Bargain","D Agrees To Bargain","P Offer","D Offer"];
    static readonly string[] Labels=["P files","D answers","P commits to exit","D commits to exit","P agrees","D agrees","P offer","D offer"];
    static readonly string[] Fields=["Direct","Entry","Offers","Exit","Agreement","SelectionResidual"];
    static void Near(double a,double b){if(!double.IsFinite(a)||!double.IsFinite(b)||Math.Abs(a-b)>1e-9)throw new InvalidDataException("Strategic report accounting mismatch.");}
    static Dictionary<string,double> Allocate(double original,double target,double[] values)
    {
        if(values.Length!=16||values.Any(x=>!double.IsFinite(x)))throw new InvalidDataException("Sixteen finite coalitions required.");
        var effects=new double[4];
        // Preserve the approved excerpt's lexicographic permutation and summation order.
        foreach(int a in Enumerable.Range(0,4))foreach(int b in Enumerable.Range(0,4).Where(x=>x!=a))foreach(int c in Enumerable.Range(0,4).Where(x=>x!=a&&x!=b))
        {int d=6-a-b-c,mask=0;foreach(int component in new[]{a,b,c,d}){int next=mask|(1<<component);effects[component]+=(values[next]-values[mask])/24;mask=next;}}
        int[] factorial=[1,1,2,6];
        for(int component=0;component<4;component++)
        {double other=0;for(int mask=0;mask<16;mask++)if((mask&(1<<component))==0){int n=System.Numerics.BitOperations.PopCount((uint)mask);other+=factorial[n]*factorial[3-n]/24.0*(values[mask|(1<<component)]-values[mask]);}Near(effects[component],other);}
        var result=Fields.Zip(new[]{values[0]-original,effects[0],effects[1],effects[2],effects[3],target-values[15]}).ToDictionary(x=>x.First,x=>x.Second);Near(result.Values.Sum(),target-original);return result;
    }
    public static JsonArray Extract(ContrastResult data)
    {
        var rows=new JsonArray();var targets=data.TargetEquilibrium.InformationSets.ToDictionary(s=>s.Key);
        foreach(string decision in Decisions)
        {
            InformationSet? old=null,end=null;int action=-1,position=-1;double score=-1;
            for(int i=0;i<data.SourceEquilibrium.InformationSets.Length;i++)
            {
                var source=data.SourceEquilibrium.InformationSets[i];var target=targets[source.Key];if(source.Decision!=decision||source.ActualReach<=0||target.ActualReach<=0)continue;
                if(!source.Actions.Select(a=>a.Label).SequenceEqual(target.Actions.Select(a=>a.Label)))throw new InvalidDataException("Changed strategic action coordinates.");
                for(int a=0;a<source.Actions.Length;a++){double difference=Math.Abs(source.Actions[a].Probability-target.Actions[a].Probability);if(difference>score){score=difference;old=source;end=target;action=a;position=i;}}
            }
            if(old==null){rows.Add(new JsonObject{["Decision"]=decision,["Eligible"]=false,["Reason"]="No information set has positive reach at both endpoints"});continue;}
            var scenarios=data.Scenarios.Where(s=>s.Result.Player==old.Player).GroupBy(s=>s.Panel).ToDictionary(g=>g.Key,g=>g.ToDictionary(s=>int.Parse(s.Component),s=>s.Result.InformationSets.Single(i=>i.Key==old.Key)));
            var primary=scenarios["coalition"];if(!primary.Keys.Order().SequenceEqual(Enumerable.Range(0,16)))throw new InvalidDataException("Missing strategic coalition.");
            double Value(InformationSet s)=>100*s.Actions[action].Probability;
            var values=Enumerable.Range(0,16).Select(m=>Value(primary[m])).ToArray();var allocation=Allocate(Value(old),Value(end!),values);bool tie=false,completion=false;double maximum=0;
            foreach(var (panel,variants) in scenarios.Where(s=>s.Key!="coalition"))
            {
                double[] alternative=values.ToArray();foreach(var (mask,set) in variants)alternative[mask]=Value(set);
                var alt=Allocate(Value(old),Value(end!),alternative);double distance=Fields.Max(f=>Math.Abs(allocation[f]-alt[f]));maximum=Math.Max(maximum,distance);
                if(distance>1e-6){tie|=panel.StartsWith("tie-");completion|=panel.StartsWith("completion-");}
            }
            foreach(var prior in data.Changes.Where(c=>c.Key==old.Key&&c.Metric!="offer amount"))
            {
                int priorAction=prior.Action.HasValue?prior.Action.Value-1:Array.FindIndex(old.Actions,a=>a.Label=="Yes");if(priorAction!=action)continue;
                var pa=JsonSerializer.SerializeToNode(prior.Allocation,Files.Json)!;foreach(string field in Fields)Near(allocation[field],pa[field]!.GetValue<double>());
                if(tie!=prior.TieSensitive||completion!=prior.CompletionSensitive)throw new InvalidDataException("Strategic sensitivity changed.");
            }
            rows.Add(JsonSerializer.SerializeToNode(new{Decision=decision,Eligible=true,old.Key,InformationSetOrder=position,InformationSetNumber=old.Number,History=old.Labels,old.Player,Signal=old.SignalValue,old.ExitCommitment,ActionIndex=action,Action=old.Actions[action].Label,SourceReach=old.ActualReach,TargetReach=end!.ActualReach,SourceActions=old.Actions,TargetActions=end.Actions,SourceProbability=old.Actions[action].Probability,TargetProbability=end.Actions[action].Probability,MaximumCoordinateChange=score,CoalitionProbabilitiesPercent=values,Allocation=allocation,Sensitivity=new{Tie=tie,Completion=completion,Maximum=maximum},CounterfactualUndefined=primary.Values.Any(s=>s.CounterfactuallyUnreachable),UnreachedCoalitions=Enumerable.Range(0,16).Where(m=>primary[m].ActualOffPath).ToArray()},Files.Json));
        }
        return rows;
    }
    public static void Generate(string stage,ResolvedArticlePlan plan,string collection,bool fullSupplement=true)
    {
        var validation=Files.Object(Path.Combine(stage,"validation.json"));if(validation["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Strategic cache was not validated.");
        var contrasts=new Dictionary<(string,string),(ContrastResult Data,string Sha)>();var index=new List<object>();
        foreach(var pair in (validation["Pairs"]??validation["CachedPairs"])!.AsArray())
        {
            string id=pair!["Pair"]!.GetValue<string>(),directory=Files.Under(stage,id);
            foreach(var fingerprint in (pair["Outputs"]??pair["DecompressedOriginalHashesVerified"])!.AsArray())
            {
                string file=fingerprint!["File"]!.GetValue<string>();if(!file.StartsWith(id+"-"))continue;
                using var zip=new GZipStream(File.OpenRead(Path.Combine(directory,file+".gz")),CompressionMode.Decompress);using var dataStream=new MemoryStream();zip.CopyTo(dataStream);byte[] bytes=dataStream.ToArray();
                string sha=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();if(sha!=fingerprint["Sha256"]!.GetValue<string>())throw new InvalidDataException("Changed cached strategic computation.");
                var data=JsonSerializer.Deserialize<ContrastResult>(bytes,Files.Json)!;
                if(!plan.Strategic.Any(c=>c.Source==data.SourceCase.Id&&c.Target==data.TargetCase.Id))continue;
                if(plan.CoreCases.Contains(data.SourceCase.Id)&&plan.CoreCases.Contains(data.TargetCase.Id))contrasts.Add((data.SourceCase.Id,data.TargetCase.Id),(data,sha));
                if(!fullSupplement)continue;
                string dest=Path.Combine(collection,"Supplemental materials/Equilibrium strategy changes",id,"Sources");Directory.CreateDirectory(dest);
                File.WriteAllText(Path.Combine(dest,Path.ChangeExtension(file,".tex")),EquilibriumChangeTables.Latex([data]));
                Files.CopyVerified(Path.Combine(directory,file+".gz"),Path.Combine(dest,file+".gz"));
                index.Add(new{Pair=id,Direction=data.Contrast.Id,Source=data.SourceCase.Id,Target=data.TargetCase.Id,SourceExpandedSha256=sha,LatexSource=Path.GetRelativePath(collection,Path.Combine(dest,Path.ChangeExtension(file,".tex"))).Replace('\\','/')});
            }
        }
        var panels=new List<JsonObject>();
        string Core(string risk,string rule)=>plan.Cases.Single(c=>c.Family=="baseline"&&c.FeeRule==rule&&c.AlphaP==(risk=="rn"?0:2)).Id;
        foreach(var (title,source,target) in new[]{("American to British | risk neutral",Core("rn","american"),Core("rn","complete")),("American to British | risk averse",Core("ra","american"),Core("ra","complete")),("Risk neutral to risk averse | American",Core("rn","american"),Core("ra","american")),("Risk neutral to risk averse | British",Core("rn","complete"),Core("ra","complete"))})
        {
            var (data,sha)=contrasts[(source,target)];panels.Add(new JsonObject{["Title"]=title,["SourceCase"]=JsonSerializer.SerializeToNode(data.SourceCase,Files.Json),["TargetCase"]=JsonSerializer.SerializeToNode(data.TargetCase,Files.Json),["Rows"]=Extract(data),["CoalitionSha256"]=sha});
        }
        string[] headings=["Decision","Signal","Own exit","Action","Action %","Reach %","Direct","Entry","Offers","Exit","Agree"];
        for(int t=0;t<2;t++)
        {
            var selected=t==0?panels.Take(1):panels.Skip(1);var sections=new List<MainTables.Section>();
            foreach(var panel in selected)
            {
                var rows=new List<string[]>();int i=0;
                foreach(var row in panel["Rows"]!.AsArray())
                {
                    if(row!["Eligible"]!.GetValue<bool>()!=true){rows.Add([Labels[i++],"--","--","--","No eligible row","--","--","--","--","--","--"]);continue;}
                    string own=row["ExitCommitment"]==null?"--":row["ExitCommitment"]!.GetValue<int>()==1?"E":"C";
                    var cells=new List<string>{Labels[i++],$"{row["Signal"]!.GetValue<double>():F2}",own,row["Action"]!.GetValue<string>(),$"{100*row["SourceProbability"]!.GetValue<double>():F1} > {100*row["TargetProbability"]!.GetValue<double>():F1}",$"{100*row["SourceReach"]!.GetValue<double>():F2} > {100*row["TargetReach"]!.GetValue<double>():F2}"};
                    foreach(string field in Fields.Take(5)){double value=row["Allocation"]![field]!.GetValue<double>();cells.Add(row["CounterfactualUndefined"]!.GetValue<bool>()?"--":(double.IsNegative(value)?"-":"+")+Math.Abs(value).ToString("F1"));}rows.Add(cells.ToArray());
                }
                sections.Add(new(panel["Title"]!.GetValue<string>(),rows.ToArray()));
            }
            MainTables.Table(collection,Stems[t],sections.ToArray(),[93,32,27,35,71,79,48,48,48,48,48],headings);
            Files.Save(Path.Combine(collection,"Tables/Sources",Stems[t]+".generated-data.json"),new{Panels=selected,RecomputedFromCachedCoalitions=true,IndependentSubsetFormulaVerified=true,NoEquilibriumSolves=true});
            File.WriteAllText(Path.Combine(collection,"Tables/Sources",Stems[t]+".txt"),"Selected strategic-response decompositions. Within each decision family, retain the largest action-probability change reached at both endpoints, keeping zero changes and original order for exact ties. Opponent contributions average all 24 replacement orders. Full reconciliation, complete supports and sensitivity diagnostics remain in the supplement; selected information sets are illustrations, not aggregate causal effects.\n");
        }
        if(fullSupplement)Files.Save(Path.Combine(collection,"Supplemental materials/Equilibrium strategy changes/generated-manifest.json"),new{Passed=true,Directions=index,Pending=validation["Pending"],GeneratedFromCoalitions=true});
    }
    public static async Task FromRun(string run,string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh output required.");var plan=Files.Read<ResolvedArticlePlan>(Path.Combine(run,"resolved-plan.json"));Generate(Path.Combine(run,"ReportResults/Strategic"),plan,output,false);await MainTables.Compile(output,Stems);
    }
    public static void Verify(string generated,string reference,string output)
    {
        foreach(string stem in Stems)
        {
            Files.EqualScience(Files.Object(Path.Combine(generated,"Tables/Sources",stem+".layout.json")),Files.Object(Path.Combine(reference,stem+".layout.json")),stem+" every cell");
            string prior=Path.Combine(reference,stem+".json");if(!File.Exists(prior))prior=Path.Combine(reference,stem+".generated-data.json");
            var a=Files.Object(Path.Combine(generated,"Tables/Sources",stem+".generated-data.json"))["Panels"]!.AsArray();var b=Files.Object(prior)["Panels"]!.AsArray();
            for(int i=0;i<a.Count;i++)foreach(string k in new[]{"Title","SourceCase","TargetCase","Rows"})Files.EqualScience(a[i]![k],b[i]![k],stem+" unrounded "+k);
        }
        Files.Save(output,new{Passed=true,EveryCellIdentical=true,EveryUnroundedSelectionAndAllocationIdentical=true});
    }
}
