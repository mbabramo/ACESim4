using System.Text.Json.Nodes;

namespace ArticleReplication;

/// <summary>Resume presentation after a renderer failure, without repeating validated calculations.</summary>
public static class FinishReporting
{
    /// <summary>Preserve rendered outputs while rebuilding current authored text in a fresh directory.</summary>
    public static async Task ManuscriptOnly(Dictionary<string,string> args)
    {
        string reporting=Path.GetFullPath(args["reporting"]),output=Path.GetFullPath(args["output"]);
        if(Directory.Exists(output)||Files.Nested(output,reporting)||Files.Nested(reporting,output))throw new IOException("Use a separate fresh manuscript directory.");
        var evidence=Files.Object(Path.Combine(reporting,"calculation-evidence.json"));
        if(evidence["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Validated calculation evidence required.");
        string run=evidence["Run"]!.GetValue<string>();
        foreach(var record in evidence["Records"]!.AsArray())
            if(Files.Sha(Files.Under(run,record!["Path"]!.GetValue<string>()))!=record["Sha256"]!.GetValue<string>())throw new InvalidDataException("Changed calculation evidence.");
        string old=Path.Combine(reporting,"article"),collection=Path.Combine(output,"article");
        foreach(string receipt in new[]{"rendering.json","standard-coverage.json"})
            if(Files.Object(Path.Combine(reporting,receipt))["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Rendering must be complete.");
        foreach(var artifact in Files.Object(Path.Combine(reporting,"rendering.json"))["Artifacts"]!.AsArray())
            foreach(var (key,hash) in new[]{("Source","SourceSha256"),("Output","PdfSha256")})
                if(Files.Sha(Files.Under(old,artifact![key]!.GetValue<string>()))!=artifact[hash]!.GetValue<string>())throw new InvalidDataException("Changed rendered output.");
        foreach(var artifact in Files.Object(Path.Combine(reporting,"standard-coverage.json"))["Files"]!.AsArray())
            if(Files.Sha(artifact!["Path"]!.GetValue<string>())!=artifact["Sha256"]!.GetValue<string>())throw new InvalidDataException("Changed standard output.");
        var plan=Files.Read<ResolvedArticlePlan>(Path.Combine(reporting,"resolved-plan.json"));
        Directory.CreateDirectory(output);
        foreach(string file in Directory.GetFiles(old,"*",SearchOption.AllDirectories))
        {
            string relative=Path.GetRelativePath(old,file).Replace('\\','/');
            if(relative.StartsWith("Article and bibliography/")||relative.StartsWith("Supplemental materials/Risk aversion utility curves/"))continue;
            Files.CopyVerified(file,Files.Under(collection,relative));
        }
        foreach(string receipt in new[]{"resolved-plan.json","calculation-evidence.json","rendering.json","standard-coverage.json","solution-path-packaging.json"})Files.CopyVerified(Path.Combine(reporting,receipt),Path.Combine(output,receipt));
        var manifest=new BundleManifest("generated-this-run",DateTime.UtcNow,"current",ArticlePlan.PublishedCalibration,[],[],plan.Cases.Select(c=>c.Id).ToArray(),[]);
        await Manuscript.Run(Path.Combine(run,"generated-inputs"),manifest,collection,output,run);
        CollectionDocumentation.Generate(plan,collection,plan.Cases.Length,output);
        Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,CompleteArticle=false,FinishedUtc=DateTime.UtcNow,CalculationRun=run,ReportingRun=reporting,PrimaryProfiles=plan.Cases.Length,SolvesStarted=0,CalculationsRepeated=false,VisualReviewRequired=true});
    }

    public static async Task Run(Dictionary<string,string> args)
    {
        string run=Path.GetFullPath(args["run"]),output=Path.GetFullPath(args["output"]);
        int workers=int.Parse(args.GetValueOrDefault("workers","1")),other=int.Parse(args.GetValueOrDefault("other-workers","0"));
        if(workers<1||other<0||workers+other>32)throw new InvalidDataException("Shared computation ceiling is 32.");
        if(Directory.Exists(output)||Files.Nested(output,run)||Files.Nested(run,output))throw new IOException("Use a separate fresh reporting directory.");
        var plan=Files.Read<ResolvedArticlePlan>(Path.Combine(run,"resolved-plan.json"));
        string[] required=["primary-validation.json","ReportResults/MultipleStarts/completed.json","ReportResults/Trembles/suite-completed.json","ReportResults/Strategic/validation.json","welfare-validation.json","ReportResults/Histories/completed.json","article/Results/Aggregated Data/Truth sensitivity/validation.json"];
        foreach(string relative in required)if(Files.Object(Files.Under(run,relative))["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Calculation stage did not pass: "+relative);
        var primary=Files.Object(Path.Combine(run,"primary-validation.json"));
        string[] ids=primary["Profiles"]!.AsArray().Select(p=>p!["CaseId"]!.GetValue<string>()).ToArray();
        if(!ids.Order().SequenceEqual(plan.Cases.Select(c=>c.Id).Order()))throw new InvalidDataException("Complete primary coverage required.");
        var profiles=plan.Cases.ToDictionary(c=>c.Id,c=>{
            string dir=Path.Combine(run,"ReportResults/Primary",c.Id);var audit=Files.Object(Path.Combine(dir,"validation.json"));
            if(audit["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Unvalidated primary profile.");
            foreach(var item in audit["Outputs"]!.AsArray())
                if(Files.Sha(item!["Path"]!.GetValue<string>())!=item["Sha256"]!.GetValue<string>())throw new InvalidDataException("Changed validated primary output.");
            return(Audit:audit,Profile:Files.Object(Directory.GetFiles(Path.Combine(dir,"Sources/Profiles"),"*.json").Single()));
        });
        Directory.CreateDirectory(output);string collection=Path.Combine(output,"article"),old=Path.Combine(run,"article");
        Files.Save(Path.Combine(output,"calculation-evidence.json"),new{Passed=true,Run=run,Records=required.Select(r=>new{Path=r,Sha256=Files.Sha(Files.Under(run,r))}),NoCalculationsRepeated=true});
        Files.Save(Path.Combine(output,"resolved-plan.json"),plan);
        foreach(string file in Directory.GetFiles(old,"*",SearchOption.AllDirectories))
        {
            string relative=Path.GetRelativePath(old,file).Replace('\\','/');
            if(relative.StartsWith("Article and bibliography/",StringComparison.Ordinal)||relative.StartsWith("Supplemental materials/Risk aversion utility curves/",StringComparison.Ordinal)||relative.StartsWith("Results/Run records/",StringComparison.Ordinal)||Path.GetExtension(file) is ".pdf" or ".png")continue;
            Files.CopyVerified(file,Files.Under(collection,relative));
        }
        Files.Save(Path.Combine(output,"solution-path-packaging.json"),new{Passed=true,Packaged=SolutionPathPackaging.Package(Path.Combine(collection,"Supplemental materials/Equilibrium solution paths")),NoFramesRemoved=true,NoNumericalValuesChanged=true});
        await Rendering.Compile(Rendering.Artifacts(collection,plan,ids),collection,output,workers);
        var rows=plan.Cases.Select(c=>{string dir=Path.Combine(run,"ReportResults/Primary",c.Id);return new LitigCharts.FinalArticleResultsCommand.CaseInput(c,Path.Combine(dir,"validation.json"),Directory.GetFiles(Path.Combine(dir,"Sources/Profiles"),"*.json").Single(),Path.Combine(dir,"StandardReports"),Path.Combine(dir,"information-set-actions.csv"));}).ToArray();
        string request=Path.Combine(output,"standard-litigcharts.json");Files.Save(request,new LitigCharts.FinalArticleResultsCommand.Request(rows,plan.Cases,Path.Combine(collection,"Results"),Path.Combine(collection,"Supplemental materials"),workers));
        await Commands.Run(Path.Combine(output,"logs"),"standard-litigcharts","dotnet",[typeof(LitigCharts.FinalArticleResultsCommand).Assembly.Location,"final-article-results","--request",request],output);
        StandardCoverage.Validate(request,Path.Combine(output,"standard-coverage.json"));
        var manifest=new BundleManifest("generated-this-run",DateTime.UtcNow,"current",ArticlePlan.PublishedCalibration,[],[],ids,[]);
        await Manuscript.Run(Path.Combine(run,"generated-inputs"),manifest,collection,output,run);
        CollectionDocumentation.Generate(plan,collection,ids.Length,output);
        Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,CompleteArticle=false,FinishedUtc=DateTime.UtcNow,CalculationRun=run,PrimaryProfiles=ids.Length,SolvesStarted=0,CalculationsRepeated=false,VisualReviewRequired=true});
    }
}
