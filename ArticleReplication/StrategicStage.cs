using System.IO.Compression;
using System.Text.Json.Nodes;
using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;

namespace ArticleReplication;

public static class StrategicStage
{
    public static async Task Run(ResolvedArticlePlan plan,string run,string output,int workers)
    {
        Directory.CreateDirectory(output);var requests=new List<string>();
        var groups=plan.Strategic.GroupBy(c=>string.Join('|',new[]{c.Source,c.Target}.Order(StringComparer.Ordinal))).ToArray();
        var pending=new List<Comparison>();var pairs=new List<object>();
        for(int i=0;i<groups.Length;i++)
        {
            var first=groups[i].First();string[] ids=[first.Source,first.Target];
            if(ids.Any(id=>!File.Exists(Path.Combine(run,"ReportResults/Primary",id,"validation.json")))){pending.AddRange(groups[i]);continue;}
            string pair=$"pair-{i:D3}",directory=Path.Combine(output,pair);
            Source Source(string id,string name){var c=plan.Cases.Single(c=>c.Id==id);string dir=Path.Combine(run,"ReportResults/Primary",id);return new(name,FinalArticleCaseFactory.Create(c).Name,Path.Combine(dir,"equilibrium.equ"),Path.Combine(dir,"information-set-actions.csv"),FinalCase:c);}
            var contrasts=groups[i].Select(c=>new Contrast(pair+(c.Source==ids[0]?"-forward":"-reverse"),c.Kind,c.Source==ids[0]?"left":"right",c.Target==ids[0]?"left":"right")).ToArray();
            string request=Path.Combine(output,"requests",pair+".json");Files.Save(request,new Request(directory,[Source(ids[0],"left"),Source(ids[1],"right")],contrasts));
            requests.Add(request);pairs.Add(new{Pair=pair,CaseIds=ids,Request=request});
        }
        await Parallel.ForEachAsync(requests,new ParallelOptions{MaxDegreeOfParallelism=workers},async(q,ct)=>await Commands.Worker(Path.Combine(output,"logs"),Path.GetFileNameWithoutExtension(q),run,"worker-strategic",q,run));
        var results=requests.Select(q=>{var r=ReadRequest(q);return new{Pair=Path.GetFileName(r.OutputDirectory),CaseIds=r.Sources.Select(s=>s.FinalCase.Id),Outputs=Files.Object(Path.Combine(r.OutputDirectory,"validation.json"))["Outputs"]};}).ToArray();
        Files.Save(Path.Combine(output,"validation.json"),new{Passed=true,Pairs=results,FreshCalculations=results.Length,AvailableDirections=groups.Sum(g=>g.Count())-pending.Count,ExpectedDirections=plan.Strategic.Length,Pending=pending,SolvesStarted=0});
    }
    static void Near(double x,double y,double tolerance,string message){if(!double.IsFinite(x)||!double.IsFinite(y)||Math.Abs(x-y)>tolerance)throw new InvalidDataException(message);}
    public static async Task Worker(string request,string run)
    {
        if(Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT")!="1")throw new InvalidDataException("Single-thread decomposition worker required.");
        ACESimBase.Util.Debugging.TabbedText.DisableOutput();var m=await RunAsync(request,Console.WriteLine);var q=ReadRequest(request);int rows=0;
        if(m.Schema!="3"||m.Contrasts.Length!=q.Contrasts.Length)throw new InvalidDataException("Missing strategic coverage.");
        foreach(var source in m.Sources)
        {
            var profile=Files.Object(Directory.GetFiles(Path.Combine(run,"ReportResults/Primary",source.Selection.FinalCase.Id,"Sources/Profiles"),"*.json").Single());
            var sets=profile["Strategies"]!.AsArray().ToDictionary(p=>p!["InformationSet"]!.GetValue<int>());
            if(sets.Count!=source.Reference.InformationSets.Length||source.Reference.InformationSets.Select(s=>s.Number).Distinct().Count()!=sets.Count)throw new InvalidDataException("Incomplete strategic endpoint.");
            foreach(var info in source.Reference.InformationSets)
            {
                var p=sets[info.Number]!;
                if(info.Player!=p["Player"]!.GetValue<byte>()||info.Signal!=p["Signal"]!.GetValue<int>()||info.ExitCommitment!=p["OwnExit"]?.GetValue<int>()||
                    !info.Actions.Select(a=>a.Label).SequenceEqual(p["Actions"]!.AsArray().Select(a=>a!.GetValue<string>()))||
                    !info.Actions.Select(a=>a.Probability).SequenceEqual(p["Probabilities"]!.AsArray().Select(a=>a!.GetValue<double>())))throw new InvalidDataException("Complete strategic endpoint changed.");
            }
            Near(source.Reference.TerminalProbability,1,1e-10,"Endpoint mass");if(source.Controls.Any(c=>c.Gain>m.Tolerances.Numerical))throw new InvalidDataException("Endpoint full BR failed.");
        }
        foreach(string file in m.OutputJsonFiles)
        {
            var result=Files.Read<ContrastResult>(file);
            foreach(byte player in new byte[]{0,1})foreach(string panel in new[]{"coalition","tie-low","tie-high"})
                if(!result.Scenarios.Where(s=>s.Panel==panel&&s.Result.Player==player).Select(s=>int.Parse(s.Component)).Order().SequenceEqual(Enumerable.Range(0,16)))throw new InvalidDataException("Incomplete coalition/tie checks.");
            foreach(var s in result.Scenarios)
            {
                Near(s.Result.ResponseUtility,s.Result.BestResponseUtility,1e-7,"Independent response replay");Near(s.Result.TerminalProbability,1,1e-10,"Response mass");Near(s.Result.MaxActionValueError,0,1e-7,"Independent action values");
                if(s.Panel=="coalition"&&s.Result.ExposedOpponentSets.Length>0)foreach(string prefix in new[]{"completion-low-","completion-high-"})
                    if(!result.Scenarios.Any(x=>x.Panel==prefix+s.Component&&x.Result.Player==s.Result.Player))throw new InvalidDataException("Missing completion stress.");
                if(s.Panel=="coalition"&&s.Component=="15")Near(s.Result.BestResponseUtility,result.TargetEquilibrium.Utilities[s.Result.Player],1e-7,"Target response");
            }
            foreach(var row in result.Changes)
            {
                double Value(InformationSet set)
                {int a=row.Metric=="offer amount"?Array.FindIndex(set.Actions,x=>x.Probability>1e-6):row.Action.HasValue?row.Action.Value-1:Array.FindIndex(set.Actions,x=>x.Label=="Yes");return row.Metric=="offer amount"?result.OfferValues[a]:100*set.Actions[a].Probability;}
                var values=Enumerable.Range(0,16).Select(mask=>Value(result.Scenarios.Single(s=>s.Panel=="coalition"&&s.Component==mask.ToString()&&s.Result.Player==row.Player).Result.InformationSets.Single(s=>s.Key==row.Key))).ToArray();
                double[] effects=new double[4];foreach(int a in Enumerable.Range(0,4))foreach(int b in Enumerable.Range(0,4).Where(x=>x!=a))foreach(int c in Enumerable.Range(0,4).Where(x=>x!=a&&x!=b))
                {int mask=0;foreach(int component in new[]{a,b,c,6-a-b-c}){int next=mask|(1<<component);effects[component]+=(values[next]-values[mask])/24;mask=next;}}
                var allocation=row.Allocation;double[] actual=[allocation.Entry,allocation.Offers,allocation.Exit,allocation.Agreement];for(int c=0;c<4;c++)Near(effects[c],actual[c],1e-9,"Independent 24-order allocation");
                Near(allocation.Direct,values[0]-allocation.Original,1e-9,"Direct allocation");Near(allocation.SelectionResidual,allocation.Target-values[15],1e-9,"Selection allocation");Near(allocation.Explained+allocation.SelectionResidual,allocation.Change,1e-9,"Total allocation");rows++;
            }
            using var input=File.OpenRead(file);using var gzip=new GZipStream(new FileStream(file+".gz",FileMode.CreateNew),CompressionLevel.SmallestSize);input.CopyTo(gzip);
        }
        Files.Save(Path.Combine(q.OutputDirectory,"validation.json"),new{Passed=true,CompleteEndpointProbabilitiesIdentical=true,CoalitionsPerPlayer=16,OrdersIndependentlyChecked=24,TieAndOffPathChecksComplete=true,AllocationRows=rows,SolvesStarted=0,Outputs=m.OutputJsonFiles.Select(f=>new{File=Path.GetFileName(f),Sha256=Files.Sha(f)})});
    }
}
