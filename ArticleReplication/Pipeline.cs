using ACESim;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArticleReplication;

public sealed record PlannedJob(string Stage,string CaseId,string GameHash,JobDisposition Disposition);
public static class Pipeline
{
    public static async Task Run(Dictionary<string,string> args)
    {
        string Need(string k)=>Path.GetFullPath(args.TryGetValue(k,out var value)?value:throw new ArgumentException("Missing --"+k));
        string bundle=Need("solutions"),output=Need("output");
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Use a new disposable output directory; existing content is never deleted.");
        static bool Nested(string a,string b)=>Files.Nested(a,b);
        string source=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"."));
        if(Nested(output,bundle)||Nested(bundle,output)||Nested(source,output))throw new IOException("Source, solutions and output must be disjoint.");
        foreach(string protectedRoot in new[]{@"C:\Users\Admin\Documents\GitHub\ACESim4",@"C:\Users\Admin\source\repos\correlated-signals-article",@"C:\Users\Admin\Documents\Codex\ab-20260922",@"C:\Users\Admin\Documents\Codex\correlated-signals-final-20260923"})
            if(Nested(output,protectedRoot)||Nested(protectedRoot,output))throw new IOException("Temporary replication may not write into or above a protected workspace.");
        int workers=int.Parse(args.GetValueOrDefault("workers","1"));int externalWorkers=int.Parse(args.GetValueOrDefault("other-workers","0"));
        if(workers<1||externalWorkers<0||workers+externalWorkers>32)throw new InvalidDataException("Shared worker ceiling is 32; declare other workers.");
        var manifest=Bundle.Open(bundle);var settings=args.TryGetValue("settings",out var sf)?Files.Read<CorrelatedSignalsSettings>(sf):new();
        var plan=ArticlePlan.Resolve(settings,manifest.Calibration);
        string[] implemented=["Primary","Exhibits","Welfare","Histories","Strategic","StandardReports","Manuscript"];
        var unsupported=settings.Steps.Except(implemented).ToArray();
        if(unsupported.Length>0)throw new NotSupportedException("Stage integration is still pending: "+string.Join(", ",unsupported)+". Select implemented steps explicitly for the temporary prototype; nothing was dispatched.");
        if(!settings.Steps.Contains("Primary"))throw new InvalidDataException("This prototype requires primary revalidation before dependent stages.");
        var mode=Enum.Parse<ReplicationMode>(args.GetValueOrDefault("mode","FromSolutions"));
        var external=args.TryGetValue("external-jobs",out var ef)?Files.Read<ExternalJobs>(ef).Cases:[];
        var inputs=Files.Read<PrimaryInput[]>(Path.Combine(bundle,"inputs/primary.json")).ToDictionary(x=>x.CaseId);
        var jobs=plan.Cases.Select(c=>{
            bool valid=inputs.TryGetValue(c.Id,out var i)&&JsonNode.DeepEquals(JsonSerializer.SerializeToNode(c,Files.Json),JsonSerializer.SerializeToNode(i.Case,Files.Json));
            string hash=valid?i!.GameIdentity["CompleteSha256"]!.GetValue<string>():external.FirstOrDefault(x=>x.CaseId==c.Id)?.ScientificGameSha256??Files.Digest(c);
            return new PlannedJob("ExactPrimary",c.Id,hash,Reuse.Decide("ExactPrimary",c.Id,hash,mode,settings.Steps.Contains("Primary"),valid,false,external));
        }).ToArray();
        Directory.CreateDirectory(output);string logs=Path.Combine(output,"logs"),raw=Path.Combine(output,"ReportResults"),collection=Path.Combine(output,"article");
        Directory.CreateDirectory(collection);
        Files.Save(Path.Combine(output,"resolved-plan.json"),plan);Files.Save(Path.Combine(output,"jobs.json"),jobs);
        Files.Save(Path.Combine(output,"started.json"),new{StartedUtc=DateTime.UtcNow,Mode=mode,Workers=workers,OtherWorkers=externalWorkers,BundleManifestSha256=Files.Sha(Path.Combine(bundle,"bundle.json")),Arguments=args,SolvesStarted=0});
        try
        {
            if(jobs.Any(j=>j.Disposition==JobDisposition.Compute))throw new NotSupportedException("Fresh-computation providers are not connected yet. Plan was retained; no computations were dispatched.");
            var ready=jobs.Where(j=>j.Disposition==JobDisposition.ReuseValidated).ToArray();
            await Parallel.ForEachAsync(ready,new ParallelOptions{MaxDegreeOfParallelism=workers},async(job,token)=>{
                var r=JsonSerializer.SerializeToNode(inputs[job.CaseId],Files.Json)!.AsObject();
                foreach(string k in new[]{"ExpectedAudit","ExpectedProfile"})r[k]=Files.Under(bundle,r[k]!.GetValue<string>());
                foreach(var f in r["Inputs"]!.AsObject())f.Value!["Path"]=Files.Under(bundle,f.Value["Path"]!.GetValue<string>());
                r["Output"]=Path.Combine(raw,"Primary",job.CaseId);
                r["StandardReports"]=settings.Steps.Contains("StandardReports");
                string request=Path.Combine(output,"requests","primary-"+job.CaseId+".json");Files.Save(request,r);
                await Commands.Worker(logs,"primary-"+job.CaseId,output,"worker-primary",request);
            });
            var validations=new List<object>();var profiles=new Dictionary<string,(JsonObject Audit,JsonObject Profile)>();
            foreach(var job in ready)
            {
                var input=inputs[job.CaseId];string dir=Path.Combine(raw,"Primary",job.CaseId);
                var audit=Files.Object(Path.Combine(dir,"validation.json"));
                string profileFile=Directory.GetFiles(Path.Combine(dir,"Sources/Profiles"),"*.json").Single();
                var profile=Files.Object(profileFile);var prior=Files.Object(Files.Under(bundle,input.ExpectedProfile));
                var current=(JsonObject)profile.DeepClone();foreach(string k in new[]{"Profile","ActionReport","ReplayReport"}){current.Remove(k);prior.Remove(k);}
                Files.EqualScience(prior,current,job.CaseId+" complete scientific profile");
                Files.EqualScience(Files.Object(Files.Under(bundle,input.ExpectedAudit))["Welfare"],audit["Welfare"],job.CaseId+" welfare");
                string dest=Path.Combine(collection,"Results/Individual simulations",job.CaseId,"Sources");Directory.CreateDirectory(dest);
                Files.CopyVerified(profileFile,Path.Combine(dest,"complete-profile.json"));Files.CopyVerified(Path.Combine(dir,"validation.json"),Path.Combine(dest,"individual-audit.json"));
                Files.CopyVerified(Path.Combine(dir,"replayed-report.csv"),Path.Combine(dest,"replayed-report.csv"));
                File.WriteAllText(Path.Combine(dest,"strategy.tex"),StrategyExhibit.Generate(audit,profile));
                profiles.Add(job.CaseId,(audit,profile));validations.Add(new{job.CaseId,Passed=true,CompleteScientificProfileIdentical=true,WelfareIdentical=true});
            }
            Files.Save(Path.Combine(output,"primary-validation.json"),new{Passed=true,Profiles=validations,SolvesStarted=0});
            Files.Save(Path.Combine(collection,"Results/Aggregated Data/selected-primary-catalog.json"),new{Schema="replicated-exact-primary-catalog-v1",Cases=profiles.Select(x=>new{CaseId=x.Key,Parameters=plan.Cases.Single(c=>c.Id==x.Key),Welfare=x.Value.Audit["Welfare"],Metrics=x.Value.Profile["Metrics"]}),Pending=jobs.Where(j=>j.Disposition!=JobDisposition.ReuseValidated),Plan=plan});
            Reports.Primary(bundle,collection,plan,profiles);
            if(settings.Steps.Contains("Strategic"))StrategicCache.Import(bundle,manifest,plan,profiles,Path.Combine(raw,"Strategic"));
            if(settings.Steps.Contains("Welfare"))await Reports.Welfare(bundle,plan,profiles,collection,output,workers);
            if(settings.Steps.Contains("Histories"))
            {
                string histories=Need("histories");string historyOutput=Path.Combine(raw,"Histories");
                await Histories.Run(histories,output,historyOutput,plan.CoreCases.Where(profiles.ContainsKey).ToArray());
                foreach(string html in Directory.EnumerateFiles(historyOutput,"*.html",SearchOption.AllDirectories))Files.CopyVerified(html,Files.Under(collection,"Supplemental materials/Equilibrium solution paths/"+Path.GetFileName(html)));
            }
            if(settings.Steps.Contains("Exhibits"))await Rendering.Run(bundle,manifest,plan,profiles,collection,output,workers);
            if(settings.Steps.Contains("StandardReports"))
            {
                string request=Path.Combine(output,"requests/standard-litigcharts.json");
                var rows=plan.Cases.Where(c=>profiles.ContainsKey(c.Id)).Select(c=>{
                    string dir=Path.Combine(raw,"Primary",c.Id);return new LitigCharts.FinalArticleResultsCommand.CaseInput(c,Path.Combine(dir,"validation.json"),Directory.GetFiles(Path.Combine(dir,"Sources/Profiles"),"*.json").Single(),Path.Combine(dir,"StandardReports"),Files.Under(bundle,inputs[c.Id].Inputs["Actions"].Path));
                }).ToArray();
                Files.Save(request,new LitigCharts.FinalArticleResultsCommand.Request(rows,plan.Cases,Path.Combine(collection,"Results"),Path.Combine(collection,"Supplemental materials"),workers));
                await Commands.Run(logs,"standard-litigcharts","dotnet",[typeof(LitigCharts.FinalArticleResultsCommand).Assembly.Location,"final-article-results","--request",request],output);
                StandardCoverage.Validate(request,Path.Combine(output,"standard-coverage.json"));
            }
            if(settings.Steps.Contains("Manuscript"))await Manuscript.Run(bundle,manifest,collection,output);
            Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,CompleteArticle=false,FinishedUtc=DateTime.UtcNow,PrimaryProfiles=ready.Length,ReservedExternalCases=jobs.Count(j=>j.Disposition==JobDisposition.AwaitExternalResult),MissingCases=jobs.Count(j=>j.Disposition==JobDisposition.MissingResult),PendingIntegration=unsupported,SolvesStarted=0});
            Console.WriteLine($"Revalidated {ready.Length} complete primary profiles; {jobs.Count(j=>j.Disposition==JobDisposition.AwaitExternalResult)} externally reserved. No solves. Full integration remains pending: {string.Join(", ",unsupported)}.");
        }
        catch(Exception e){Files.Save(Path.Combine(output,"failed.json"),new{Passed=false,FinishedUtc=DateTime.UtcNow,Error=e.ToString(),SolvesStarted=0});throw;}
    }
}
