using ACESim;
using System.Text.Json.Nodes;

namespace ArticleReplication;

/// <summary>Public replication: optional saved solves, fresh downstream computation, C# or CLI settings.</summary>
public static class SimpleRun
{
    public static async Task Run(Dictionary<string,string> args)
    {
        string[] allowed=["input","output","article","costs","steps","starts","pivots","cutoff","gain-units","extensions","trial-only","noise","grids","trembles","tremble-directions","workers","other-workers","cases","reserve-cases","missing"];
        if(args.Keys.Except(allowed,StringComparer.OrdinalIgnoreCase).Any())throw new ArgumentException("Unknown run option: "+string.Join(',',args.Keys.Except(allowed,StringComparer.OrdinalIgnoreCase)));
        if(args.GetValueOrDefault("article","CorrelatedSignals")!="CorrelatedSignals")throw new ArgumentException("Only CorrelatedSignals is implemented.");
        string output=Path.GetFullPath(args["output"]);string? input=args.TryGetValue("input",out var value)?Path.GetFullPath(value):null;
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Choose a fresh output directory.");
        if(input!=null&&(!Directory.Exists(input)||Files.Nested(output,input)||Files.Nested(input,output)))throw new IOException("Input and output must exist separately.");
        foreach(string root in new[]{AppContext.BaseDirectory,@"C:\Users\Admin\Documents\GitHub\ACESim4",@"C:\Users\Admin\source\repos\correlated-signals-article",@"C:\Users\Admin\Documents\Codex\ab-20260922",@"C:\Users\Admin\Documents\Codex\correlated-signals-final-20260923"})
            if(Files.Nested(output,Path.GetFullPath(root))||Files.Nested(Path.GetFullPath(root),output))throw new IOException("Output overlaps a protected source/workspace.");
        int workers=int.Parse(args.GetValueOrDefault("workers","1")),other=int.Parse(args.GetValueOrDefault("other-workers","0"));if(workers<1||other<0||workers+other>32)throw new ArgumentException("Shared computation ceiling is 32.");
        string missing=args.GetValueOrDefault("missing","compute");if(missing is not ("compute" or "wait"))throw new ArgumentException("--missing is compute or wait.");
        bool computeMissing=missing=="compute";var settings=RunSettings.Resolve(args);var plan=ArticlePlan.Resolve(settings,ArticlePlan.PublishedCalibration);
        if(args.TryGetValue("cases",out var selection))
        {
            var ids=selection.Split(',').ToHashSet(StringComparer.Ordinal);if(ids.Except(plan.Cases.Select(c=>c.Id)).Any())throw new ArgumentException("Unknown selected case.");
            plan=plan with{Cases=plan.Cases.Where(c=>ids.Contains(c.Id)).ToArray(),Welfare=plan.Welfare.Where(c=>ids.Contains(c.Source)&&ids.Contains(c.Target)).ToArray(),Strategic=plan.Strategic.Where(c=>ids.Contains(c.Source)&&ids.Contains(c.Target)).ToArray(),CoreCases=plan.CoreCases.Where(ids.Contains).ToArray(),ExpectedApproximateStarts=plan.CoreCases.Count(ids.Contains)*settings.StartsPerCore};
        }
        if(!plan.Steps.Contains("Primary"))throw new ArgumentException("Primary is required before downstream stages.");
        if(plan.Steps.Contains("Trembles")&&!plan.Steps.Contains("MultipleStarts"))throw new ArgumentException("Trembles requires MultipleStarts.");
        if(plan.Steps.Contains("Manuscript")&&new[]{"Exhibits","Welfare","Strategic","MultipleStarts"}.Except(plan.Steps).Any())throw new ArgumentException("Manuscript requires its data/exhibit stages.");
        var reserved=args.GetValueOrDefault("reserve-cases","").Split(',',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        // Temporary local deployment guard. It is not part of the scientific article protocol or saved inputs.
        if(OperatingSystem.IsWindows()&&Directory.Exists(@"C:\Users\Admin\Documents\Codex\correlated-signals-final-20260923"))
            reserved.UnionWith(["grid__signals-8-offers-15__complete__ra__cost-1","grid__signals-12-offers-8__complete__ra__cost-1"]);
        var jobs=plan.Cases.Select(c=>new{Case=c,Shortcut=input==null?null:SolveShortcut.PrimaryPath(input,c.Id)}).Select(j=>new{j.Case,j.Shortcut,Disposition=j.Shortcut!=null&&File.Exists(j.Shortcut)?"ReuseSavedSolve":reserved.Contains(j.Case.Id)?"AwaitExternalResult":computeMissing?"Compute":"MissingResult"}).ToArray();
        foreach(var job in jobs.Where(j=>j.Disposition=="ReuseSavedSolve"))SolveShortcut.Read(job.Shortcut!,job.Case.Id,"ExactPrimary",0,settings);
        Directory.CreateDirectory(output);string raw=Path.Combine(output,"ReportResults"),collection=Path.Combine(output,"article"),logs=Path.Combine(output,"logs");Directory.CreateDirectory(collection);
        Files.Save(Path.Combine(output,"resolved-plan.json"),plan);Files.Save(Path.Combine(output,"jobs.json"),jobs);Files.Save(Path.Combine(output,"started.json"),new{StartedUtc=DateTime.UtcNow,Arguments=args,Workers=workers,OtherWorkers=other,SettingsAuthority="C# defaults and command line",InputManifestsRequired=false});
        try
        {
            if(plan.Steps.Intersect(new[]{"StandardReports","Exhibits","Manuscript"}).Any())await Prerequisites.Run(Path.Combine(output,"prerequisites"));
            var ready=jobs.Where(j=>j.Disposition is "ReuseSavedSolve" or "Compute").ToArray();
            await Parallel.ForEachAsync(ready,new ParallelOptions{MaxDegreeOfParallelism=workers},async(job,ct)=>
            {
                string request=Path.Combine(output,"requests","primary-"+job.Case.Id+".json");
                Files.Save(request,new{job.Case,Output=Path.Combine(raw,"Primary",job.Case.Id),ShortcutFile=job.Shortcut,Compute=job.Disposition=="Compute",CaptureHistory=plan.Steps.Contains("Histories")&&plan.CoreCases.Contains(job.Case.Id),StandardReports=plan.Steps.Contains("StandardReports")});
                await Commands.Worker(logs,"primary-"+job.Case.Id,output,"worker-primary",request);
            });
            var profiles=new Dictionary<string,(JsonObject Audit,JsonObject Profile)>();var internalInputs=new List<PrimaryInput>();
            foreach(var job in ready)
            {
                string dir=Path.Combine(raw,"Primary",job.Case.Id),file=Directory.GetFiles(Path.Combine(dir,"Sources/Profiles"),"*.json").Single();var audit=Files.Object(Path.Combine(dir,"validation.json"));var profile=Files.Object(file);
                if(audit["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Primary validation failed.");profiles.Add(job.Case.Id,(audit,profile));
                string dest=Path.Combine(collection,"Results/Individual simulations",job.Case.Id,"Sources");Directory.CreateDirectory(dest);Files.CopyVerified(file,Path.Combine(dest,"complete-profile.json"));Files.CopyVerified(Path.Combine(dir,"validation.json"),Path.Combine(dest,"individual-audit.json"));Files.CopyVerified(Path.Combine(dir,"replayed-report.csv"),Path.Combine(dest,"replayed-report.csv"));File.WriteAllText(Path.Combine(dest,"strategy.tex"),StrategyExhibit.Generate(audit,profile));
                internalInputs.Add(new(job.Case.Id,job.Case,(JsonObject)audit["GameIdentity"]!.DeepClone(),null,null,[]));
            }
            Files.Save(Path.Combine(output,"primary-validation.json"),new{Passed=true,Profiles=ready.Select(j=>new{CaseId=j.Case.Id,Passed=true,FullProfileRevalidated=true}),SolvesStarted=ready.Count(j=>j.Disposition=="Compute")});
            string generated=Path.Combine(output,"generated-inputs");Files.Save(Path.Combine(generated,"inputs/primary.json"),internalInputs);
            Files.Save(Path.Combine(collection,"Results/Aggregated Data/selected-primary-catalog.json"),new{Schema="replicated-exact-primary-catalog-v1",Cases=profiles.Select(p=>new{CaseId=p.Key,Parameters=plan.Cases.Single(c=>c.Id==p.Key),Welfare=p.Value.Audit["Welfare"],Metrics=p.Value.Profile["Metrics"]}),Pending=jobs.Where(j=>!profiles.ContainsKey(j.Case.Id)),Plan=plan});
            Reports.Primary(generated,collection,plan,profiles);
            if(plan.Steps.Contains("MultipleStarts"))
            {
                if(plan.CoreCases.Any(id=>!profiles.ContainsKey(id)))throw new InvalidDataException("Search reporting requires its primary cases.");
                await SearchStage.Run(input,plan,Path.Combine(raw,"MultipleStarts"),workers,computeMissing);MultipleReports.Generate(Path.Combine(raw,"MultipleStarts"),plan,collection);
            }
            if(plan.Steps.Contains("Trembles")){await TrembleStage.Run(null,plan,output,Path.Combine(raw,"Trembles"),workers);foreach(string file in Directory.GetFiles(Path.Combine(raw,"Trembles/generated-reports")))Files.CopyVerified(file,Path.Combine(collection,"Results/Aggregated Data/Equilibrium sensitivity",Path.GetFileName(file)));}
            if(plan.Steps.Contains("Strategic"))await StrategicStage.Run(plan,output,Path.Combine(raw,"Strategic"),workers);
            if(plan.Steps.Contains("Welfare"))await Reports.Welfare(generated,plan,profiles,collection,output,workers);
            string shortcuts=Path.Combine(raw,"Shortcuts");SolveShortcut.Export(output,shortcuts);
            int historySolves=0;
            if(plan.Steps.Contains("Histories"))
            {
                var histories=new List<object>();foreach(string id in plan.CoreCases.Where(profiles.ContainsKey))
                {
                    string? existing=input==null?null:SolutionHistory.PathFor(input,id);string captured=Path.Combine(raw,"Primary",id,"solve.history");
                    string history=existing!=null&&File.Exists(existing)?existing:captured;
                    if(!File.Exists(history)&&!computeMissing)throw new InvalidDataException("Missing saved history for "+id);
                    string dir=Path.Combine(raw,"Histories",id),request=Path.Combine(output,"requests","history-"+id+".json");Files.Save(request,new{Run=output,CaseId=id,Case=plan.Cases.Single(c=>c.Id==id),History=history,Output=dir});
                    await Commands.Worker(logs,"history-"+id,output,"worker-solution-history",request);Files.CopyVerified(history,SolutionHistory.PathFor(shortcuts,id));Files.CopyVerified(Path.Combine(dir,id+".html"),Path.Combine(collection,"Supplemental materials/Equilibrium solution paths",id+".html"));var historyAudit=Files.Object(Path.Combine(dir,"validation.json"));histories.Add(historyAudit);historySolves+=historyAudit["SolvesStarted"]!.GetValue<int>();
                }
                Files.Save(Path.Combine(raw,"Histories/completed.json"),new{Passed=true,Histories=histories});
            }
            var manifest=new BundleManifest("generated-this-run",DateTime.UtcNow,"current",ArticlePlan.PublishedCalibration,[],[],profiles.Keys.ToArray(),[]);
            if(plan.Steps.Contains("Exhibits"))await Rendering.Run(generated,manifest,plan,profiles,collection,output,workers);
            if(plan.Steps.Contains("StandardReports"))
            {
                var rows=plan.Cases.Where(c=>profiles.ContainsKey(c.Id)).Select(c=>{string dir=Path.Combine(raw,"Primary",c.Id);return new LitigCharts.FinalArticleResultsCommand.CaseInput(c,Path.Combine(dir,"validation.json"),Directory.GetFiles(Path.Combine(dir,"Sources/Profiles"),"*.json").Single(),Path.Combine(dir,"StandardReports"),Path.Combine(dir,"information-set-actions.csv"));}).ToArray();
                string request=Path.Combine(output,"requests/standard-litigcharts.json");Files.Save(request,new LitigCharts.FinalArticleResultsCommand.Request(rows,plan.Cases,Path.Combine(collection,"Results"),Path.Combine(collection,"Supplemental materials"),workers));await Commands.Run(logs,"standard-litigcharts","dotnet",[typeof(LitigCharts.FinalArticleResultsCommand).Assembly.Location,"final-article-results","--request",request],output);StandardCoverage.Validate(request,Path.Combine(output,"standard-coverage.json"));
            }
            if(plan.Steps.Contains("Manuscript"))await Manuscript.Run(generated,manifest,collection,output);
            int searchSolves=plan.Steps.Contains("MultipleStarts")?Files.Object(Path.Combine(raw,"MultipleStarts/completed.json"))["SolvesStarted"]!.GetValue<int>():0;
            Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,CompleteArticle=false,FinishedUtc=DateTime.UtcNow,PrimaryProfiles=profiles.Count,ReservedExternalCases=jobs.Count(j=>j.Disposition=="AwaitExternalResult"),MissingCases=jobs.Count(j=>j.Disposition=="MissingResult"),ExactSolves=ready.Count(j=>j.Disposition=="Compute")+historySolves,HistoryOnlySolves=historySolves,ApproximateSolves=searchSolves,ShortcutDirectory=Path.GetRelativePath(output,shortcuts),FreshDownstreamCalculations=true,InputManifestsRequired=false});
        }
        catch(Exception e){Files.Save(Path.Combine(output,"failed.json"),new{Passed=false,Error=e.ToString(),FailedUtc=DateTime.UtcNow});throw;}
    }
}
