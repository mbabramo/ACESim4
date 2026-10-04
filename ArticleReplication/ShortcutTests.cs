namespace ArticleReplication;

public static class ShortcutTests
{
    public static async Task ManuscriptBoundary(string output)
    {
        if(Directory.Exists(output))throw new IOException("Use a fresh test directory.");
        var defaults=RunSettings.Resolve(new());
        if(defaults.Steps.Contains("Manuscript")||!defaults.Steps.Contains("Exhibits"))throw new InvalidDataException("Default replication must generate exhibits without the manuscript.");
        var optional=RunSettings.Resolve(new(){{"manuscript","true"}});
        if(!optional.Steps.Contains("Manuscript")||!optional.Steps.Except(["Manuscript"]).SequenceEqual(defaults.Steps))throw new InvalidDataException("Author opt-in changed research stages.");
        var disabled=RunSettings.Resolve(new(){{"steps","Primary,Manuscript"},{"manuscript","false"}});
        if(disabled.Steps.Contains("Manuscript"))throw new InvalidDataException("Explicit exclusion failed.");
        string collection=Path.Combine(output,"article");
        await UtilityCurves.Run(collection,output);
        Directory.CreateDirectory(Path.Combine(collection,"Results/Run records/obsolete-empty"));
        CollectionDocumentation.Generate(ArticlePlan.Resolve(defaults,ArticlePlan.PublishedCalibration),collection,0,output);
        if(Directory.Exists(Path.Combine(collection,"Results/Run records")))throw new InvalidDataException("Empty execution directories leaked into the published collection.");
        if(Directory.Exists(Path.Combine(collection,"Article and bibliography")))throw new InvalidDataException("Default presentation created manuscript assets.");
        string utility=Path.Combine(collection,"Supplemental materials/Risk aversion utility curves");
        foreach(string file in new[]{"risk aversion v2.tex","risk aversion v2.pdf","risk aversion.pdf","README.md"})
            if(!File.Exists(Path.Combine(utility,file)))throw new InvalidDataException("Missing supplemental utility asset: "+file);
        if(File.ReadAllText(Path.Combine(collection,"README.md")).Contains("](Article%20and%20bibliography/corr_signals.pdf)"))throw new InvalidDataException("Default README links to an ungenerated manuscript.");
        Files.Save(Path.Combine(output,"passed.json"),new{Passed=true,DefaultManuscript=false,ExplicitAuthorOptIn=true,UtilityCurvesGenerated=true,ManuscriptFolderCreated=false,ScientificStagesUnchanged=true});
    }
    public static void CompareCsv(string generated,string reference,string output)
    {
        var a=Reports.ReadCsv(generated);var b=Reports.ReadCsv(reference);
        Files.EqualScience(System.Text.Json.JsonSerializer.SerializeToNode(a,Files.Json),System.Text.Json.JsonSerializer.SerializeToNode(b,Files.Json),"Every parsed CSV cell");
        Files.Save(output,new{Passed=true,Rows=a.Length,EveryCellExactlyIdentical=true});
    }
    public static void CompareHistory(string generated,string reference,string output)
    {
        using var a=SolutionHistory.Open(generated);using var b=SolutionHistory.Open(reference);int records=0;
        while(true)
        {
            string? x=a.ReadLine(),y=b.ReadLine();if(x==null&&y==null)break;
            if(x==null||y==null)throw new InvalidDataException("History lengths differ.");
            Files.EqualScience(System.Text.Json.Nodes.JsonNode.Parse(x),System.Text.Json.Nodes.JsonNode.Parse(y),"Complete history record "+records);records++;
        }
        if(records<3)throw new InvalidDataException("Completed pivot history required.");
        Files.Save(output,new{Passed=true,Frames=records-1,HeadersNativePivotsAndCompleteDiagnosticsExactlyIdentical=true,NoToleranceSubstitution=true,FullTableauEquivalenceProof=false});
    }
    public static void Compare(string generated,string reference,string output)
    {
        var completed=Files.Object(Path.Combine(generated,"completed.json"));if(completed["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Completed run required.");
        var rows=Files.Object(Path.Combine(generated,"primary-validation.json"))["Profiles"]!.AsArray();int primary=0,search=0;
        void Profile(string a,string b)
        {
            var x=Files.Object(Directory.GetFiles(Path.Combine(a,"Sources/Profiles"),"*.json").Single());var y=Files.Object(Directory.GetFiles(Path.Combine(b,"Sources/Profiles"),"*.json").Single());
            foreach(string k in new[]{"Profile","ActionReport","ReplayReport"}){x.Remove(k);y.Remove(k);}Files.EqualScience(x,y,"Complete scientific profile");
            string oldActions=Path.Combine(b,"information-set-actions.csv");
            if(File.Exists(oldActions))Files.EqualScience(System.Text.Json.JsonSerializer.SerializeToNode(Reports.ReadCsv(Path.Combine(a,"information-set-actions.csv")),Files.Json),System.Text.Json.JsonSerializer.SerializeToNode(Reports.ReadCsv(oldActions),Files.Json),"Every action-report field: "+a);
            var csvA=Reports.ReadCsv(Path.Combine(a,"replayed-report.csv"));var csvB=Reports.ReadCsv(Path.Combine(b,"replayed-report.csv"));
            foreach(var row in csvA.Concat(csvB))row.Remove("Seconds"); // runtime metadata, not an outcome
            Files.EqualScience(System.Text.Json.JsonSerializer.SerializeToNode(csvA,Files.Json),System.Text.Json.JsonSerializer.SerializeToNode(csvB,Files.Json),"All report values except elapsed seconds: "+a);
        }
        foreach(var row in rows)
        {
            string id=row!["CaseId"]!.GetValue<string>(),a=Path.Combine(generated,"ReportResults/Primary",id),b=Path.Combine(reference,"ReportResults/Primary",id);Profile(a,b);
            var x=Files.Object(Path.Combine(a,"validation.json"));var y=Files.Object(Path.Combine(b,"validation.json"));
            foreach(string k in new[]{"GameIdentity","FullBestResponseGains","CompleteStrategySha256","UnspecifiedOffPathInformationSets","Welfare"})Files.EqualScience(x[k],y[k],id+" "+k);primary++;
        }
        string attempts=Path.Combine(generated,"ReportResults/MultipleStarts/completed.json");
        var referenceAttempts=File.Exists(attempts)?Files.Object(Path.Combine(reference,"ReportResults/MultipleStarts/completed.json"))["Results"]!.AsArray().ToDictionary(r=>(r!["CaseId"]!.GetValue<string>(),r["StartIndex"]!.GetValue<int>())):null;
        if(File.Exists(attempts))foreach(var row in Files.Object(attempts)["Results"]!.AsArray())
        {
            string id=row!["CaseId"]!.GetValue<string>();int start=row["StartIndex"]!.GetValue<int>();string relative=$"ReportResults/MultipleStarts/{id}/start-{start:D5}";
            string a=Path.Combine(generated,relative),b=Path.Combine(reference,relative);var y=referenceAttempts![(id,start)]!;
            Files.EqualScience(row["Accepted"],y["Accepted"],"Search acceptance");
            if(row["Accepted"]!.GetValue<bool>())
            {Profile(a,b);foreach(string k in new[]{"FullBestResponseRawGains","AverageGain","Threshold","StoppingPivot","Welfare"})Files.EqualScience(row[k],y[k],id+" "+k);}
            else Files.EqualScience(row["Decision"],y["Decision"],"Failed attempt decision");
            search++;
        }
        if(primary==0)throw new InvalidDataException("Nonempty comparison required.");
        Files.Save(output,new{Passed=true,PrimaryCases=primary,SearchAttempts=search,CompleteProfilesAndNumericReportsExactlyIdentical=true,Scope="Generated subset compared with the same cases/starts in the reference; no tolerance substitution."});
    }
    public static void Run(string output)
    {
        var settings=new CorrelatedSignalsSettings();var passed=new List<string>();
        var plan=ArticlePlan.Resolve(settings,ArticlePlan.PublishedCalibration);
        if(plan.Cases.Length!=74||plan.Welfare.Length!=36||plan.Strategic.Length!=150||plan.Cases.Any(c=>c.Family=="grid"&&(c.Signals==15||c.Offers.Length==15))||plan.Cases.Count(c=>c.Family=="grid"&&c.Signals==8&&c.Offers.Length==12)!=4)throw new InvalidDataException("Selected-grid plan is inconsistent.");
        passed.Add("Default plan selects both risks and rules at 8/12, 12/8 and the 8/8 control, without 15-based grids");
        var legacy=RunSettings.Resolve(new(){["grids"]="8x15,12x8,8x8"});
        if(ArticlePlan.Resolve(legacy,ArticlePlan.PublishedCalibration).Strategic.Length!=150)throw new InvalidDataException("Unqualified grid override must apply to both risk preferences.");
        passed.Add("Unqualified CLI grids preserve the original both-risk interpretation");
        var explicitGrids=RunSettings.Resolve(new(){["grids"]="8x12:rn,8x12:ra,12x8,8x8"});
        if(!ArticlePlan.Resolve(explicitGrids,ArticlePlan.PublishedCalibration).Cases.Select(c=>c.Id).SequenceEqual(plan.Cases.Select(c=>c.Id)))throw new InvalidDataException("Risk-qualified CLI grids differ from the defaults.");
        passed.Add("Explicit CLI grid qualifiers reproduce the default case selection");
        void Accept(string name,SolveShortcut r,CorrelatedSignalsSettings? s=null){r.Check("case","ApproximateStart",0,s??settings);passed.Add(name);}
        void Reject(string name,SolveShortcut r,CorrelatedSignalsSettings? s=null)
        {try{r.Check("case","ApproximateStart",0,s??settings);}catch(InvalidDataException){passed.Add(name);return;}throw new InvalidDataException("Unexpected acceptance: "+name);}
        var failed=new SolveShortcut("case","ApproximateStart","NoEquilibriumFound",0,1000000,20000,20000,"cap-unsuccessful",.005,settings.ApproximateGainUnits.ToString(),[]);
        Accept("Completed unsuccessful start is a reusable record",failed);
        Reject("Failed attempt cannot carry a strategy",failed with{Probabilities=[.5,.5]});
        Reject("Longer budget reruns failed attempts",failed,settings with{ApproximatePivotLimit=30000});
        Reject("Changed cutoff reruns failed attempts",failed,settings with{ApproximateRoundingCutoff=.01});
        Reject("Changed seed rejected",failed with{PriorSeed=42});
        Reject("Unfinished cap rejected",failed with{StoppingPivot=19999});
        Reject("Nonexistence claim is not a search status",failed with{Status="NoEquilibriumExists"});
        Reject("Unknown failure rejected",failed with{Reason="unknown"});
        var early=failed with{Status="Equilibrium",Reason="first-below-0.001",StoppingPivot=50,Probabilities=[.5,.5]};
        Accept("Same early stopping rule with larger budget",early,settings with{ApproximatePivotLimit=30000});
        Reject("Smaller cap before recorded hit rejected",early,settings with{ApproximatePivotLimit=49});
        Reject("NaN policy rejected",early with{Probabilities=[double.NaN]});
        Reject("Out of range policy rejected",early with{Probabilities=[-.1,1.1]});
        Reject("Empty accepted policy rejected",early with{Probabilities=[]});
        var capped=early with{Reason="cap-accepted",StoppingPivot=20000};Accept("Accepted cap matches policy",capped);
        Reject("Changed accepted cap requires rerun",capped,settings with{ApproximatePivotLimit=30000});
        if(failed.MatchesSearchPolicy("case",0,settings with{ApproximatePivotLimit=30000})||!early.MatchesSearchPolicy("case",0,settings with{ApproximatePivotLimit=30000}))throw new InvalidDataException("Changed-budget dispatch failed.");passed.Add("Only compatible shortcuts bypass recomputation");
        Files.Save(output,new{Passed=true,Checks=passed.Count,Tests=passed,FullGameValidationRequiredSeparately=true});
    }
}
