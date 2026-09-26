using System.Text.Json;
using System.Text.Json.Nodes;
using LitigCharts;
using ACESimBase.Games.LitigGame.ManualReports;

namespace ArticleReplication;

public sealed record HistoryInput(string CaseId,string Metadata,string Receipt);
public sealed record HistoryPackage(string Schema,HistoryInput[] Histories,BundleFile[] Files);
public static class Histories
{
    public static void Pack(string request,string output)
    {
        if(Directory.Exists(output))throw new IOException("Choose a new history bundle directory.");
        Directory.CreateDirectory(output);var sources=Files.Read<HistoryInput[]>(request);var packaged=new List<HistoryInput>();
        foreach(var input in sources)
        {
            var receipt=Files.Object(input.Receipt);if(receipt["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("History has no passed receipt.");
            string metadataHash=Files.Sha(input.Metadata);
            bool bound=receipt["Result"]?["Sha256"]?.GetValue<string>()==metadataHash||
                receipt["OriginalFilesUnchanged"]?.AsArray().Any(x=>x?["Sha256"]?.GetValue<string>()==metadataHash)==true;
            if(!bound)throw new InvalidDataException("Receipt does not bind this history metadata.");
            var metadata=Files.Object(input.Metadata);string stem="Histories/"+input.CaseId+"/"+Path.GetFileNameWithoutExtension(input.Metadata);
            var files=new List<JsonObject>();
            foreach(var node in metadata["Inputs"]!.AsArray().Append(metadata["Frames"]).Append(metadata["CoreAssembly"]))
            {
                var item=node!.AsObject();string original=item["Path"]!.GetValue<string>(),sha=item["Sha256"]!.GetValue<string>();
                string rel="objects/"+sha+Path.GetExtension(original);
                string dest=Files.Under(output,rel);
                if(!File.Exists(dest))Files.CopyVerified(original,dest,sha);
                else if(Files.Sha(dest)!=sha)throw new InvalidDataException("Conflicting content object.");
                files.Add(new(){["OriginalPath"]=original,["Path"]=rel,["Sha256"]=sha});
            }
            // Preserve original signed-by-hash evidence; relocation is a separate record, never an overwrite.
            Files.CopyVerified(input.Metadata,Files.Under(output,stem+".original.json"));
            Files.CopyVerified(input.Receipt,Files.Under(output,stem+".receipt.json"));
            Files.Save(Files.Under(output,stem+".relocation.json"),files);
            packaged.Add(new(input.CaseId,stem+".original.json",stem+".receipt.json"));
        }
        var all=Directory.GetFiles(output,"*",SearchOption.AllDirectories).Order().Select(f=>new BundleFile(Path.GetRelativePath(output,f).Replace('\\','/'),Files.Sha(f),new FileInfo(f).Length)).ToArray();
        Files.Save(Path.Combine(output,"histories.json"),new HistoryPackage("verified-saved-histories-v1",packaged.ToArray(),all));
        Console.WriteLine($"Packaged {packaged.Count} completed histories; no replay.");
    }
    public static async Task Run(string bundle,string primaryRun,string output,string[]? selected=null)
    {
        if(Directory.Exists(output))throw new IOException("Choose a new history-check directory.");
        Directory.CreateDirectory(output);var package=Files.Read<HistoryPackage>(Path.Combine(bundle,"histories.json"));
        if(package.Schema!="verified-saved-histories-v1")throw new InvalidDataException("Unknown history format.");
        foreach(var f in package.Files)if(Files.Sha(Files.Under(bundle,f.Path))!=f.Sha256)throw new InvalidDataException("Changed saved history object.");
        var completed=new List<object>();
        foreach(var input in package.Histories.Where(h=>selected==null||selected.Contains(h.CaseId)))
        {
            string work=Files.Under(output,input.CaseId);Directory.CreateDirectory(work);
            var m=Files.Object(Files.Under(bundle,input.Metadata));string stem=input.Metadata[..^".original.json".Length];
            var relocation=Files.Read<JsonObject[]>(Files.Under(bundle,stem+".relocation.json"));
            foreach(var node in m["Inputs"]!.AsArray().Append(m["Frames"]).Append(m["CoreAssembly"]))
            {
                var item=node!.AsObject();string original=item["Path"]!.GetValue<string>();
                var r=relocation.Single(x=>x["OriginalPath"]!.GetValue<string>()==original);
                item["Path"]=Files.Under(bundle,r["Path"]!.GetValue<string>());
            }
            string metadata=Path.Combine(work,m["Id"]!.GetValue<string>()+".json");Files.Save(metadata,m);
            Files.CopyVerified(m["Frames"]!["Path"]!.GetValue<string>(),Path.ChangeExtension(metadata,".jsonl"),m["Frames"]!["Sha256"]!.GetValue<string>());
            string request=Path.Combine(work,"request.json");Files.Save(request,new{CaseId=input.CaseId,Metadata=metadata,Audit=Path.Combine(primaryRun,"ReportResults/Primary",input.CaseId,"validation.json"),Profile=Directory.GetFiles(Path.Combine(primaryRun,"ReportResults/Primary",input.CaseId,"Sources/Profiles"),"*.json").Single(),Output=work});
            // One process at a time: the longest stream is large, and GC memory is released after each viewer.
            await Commands.Worker(Path.Combine(output,"logs"),input.CaseId,output,"worker-history",request);
            completed.Add(Files.Object(Path.Combine(work,"validation.json")));
        }
        Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,Histories=completed,SolvesStarted=0,FreshPivots=0,VisualReviewPending=true});
    }
    public static void Worker(string request)
    {
        var q=Files.Object(request);string metadata=q["Metadata"]!.GetValue<string>(),output=q["Output"]!.GetValue<string>();
        var run=EquilibriumPathAnimation.ReadVerified(metadata);
        var audit=Files.Object(q["Audit"]!.GetValue<string>());var profile=Files.Object(q["Profile"]!.GetValue<string>());
        if(audit["Passed"]?.GetValue<bool>()!=true||audit["CompleteStrategyUnchanged"]?.GetValue<bool>()!=true||run.Metadata.OptionSet!=audit["OptionSetName"]!.GetValue<string>())throw new InvalidDataException("History endpoint is not the revalidated game/profile.");
        foreach(string key in new[]{"Equilibrium","Actions"})if(!run.Metadata.Inputs.Any(i=>i.Sha256==audit["Inputs"]![key]!["Sha256"]!.GetValue<string>()))throw new InvalidDataException("History used different saved input: "+key);
        var rows=profile["Strategies"]!.AsArray().Select(x=>x!.AsObject()).ToArray();
        int entries=0;double difference=0;
        if(rows.Length!=run.Metadata.InformationSets.Length)throw new InvalidDataException("History omits saved information sets.");
        foreach(var set in run.Metadata.InformationSets)
        {
            string decision=set.Decision switch{"P Files"=>"PFile","D Answers"=>"DAnswer","P Abandons"=>"PAbandon","D Defaults"=>"DDefault","P Agrees To Bargain"=>"PAgreeToBargain","D Agrees To Bargain"=>"DAgreeToBargain","P Offer"=>"POffer","D Offer"=>"DOffer",_=>throw new InvalidDataException("Unknown history decision: "+set.Decision)};
            var row=rows.Single(r=>r["Player"]!.GetValue<byte>()==set.Player&&r["Decision"]!.GetValue<string>()==decision&&r["Signal"]!.GetValue<int>()==set.Signal&&r["OwnExit"]?.GetValue<int>()==set.ExitCommitment);
            var p=row["Probabilities"]!.AsArray().Select(v=>v!.GetValue<double>()).ToArray();
            if(p.Length!=set.Actions.Length||!row["Actions"]!.AsArray().Select(x=>x!.GetValue<string>()).SequenceEqual(set.Actions))throw new InvalidDataException("History action coordinates changed.");
            for(int j=0;j<p.Length;j++){difference=Math.Max(difference,Math.Abs(p[j]-run.Frames[^1].Strategy.Probabilities[set.FirstAction+j]));entries++;}
        }
        if(difference>1e-10)throw new InvalidDataException("History does not end at the complete saved policy.");
        string html=Path.Combine(output,run.Metadata.Id+".html");File.WriteAllText(html,EquilibriumPathAnimation.BuildHtml([run]));
        Files.Save(Path.Combine(output,"validation.json"),new{Passed=true,CaseId=q["CaseId"],run.Metadata.Pivots,run.Metadata.OriginalPivots,run.Metadata.Steps,CompletePolicyEntries=entries,MaximumSavedPolicyDifference=difference,FrameSha256=run.Metadata.Frames.Sha256,HtmlSha256=Files.Sha(html),EndpointAuditSha256=Files.Sha(q["Audit"]!.GetValue<string>()),FreshPivots=0,SolvesStarted=0,Criteria="Original trajectory replay thresholds and complete endpoint checks; not a new pivot-algebra equivalence proof.",VisualReviewPending=true});
        Console.WriteLine($"Verified {run.Metadata.Id}: {run.Metadata.Pivots} saved pivots; no replay.");
    }
}
