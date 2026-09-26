namespace ArticleReplication;

public static class ShortcutTests
{
    public static void CompareCsv(string generated,string reference,string output)
    {
        var a=Reports.ReadCsv(generated);var b=Reports.ReadCsv(reference);
        Files.EqualScience(System.Text.Json.JsonSerializer.SerializeToNode(a,Files.Json),System.Text.Json.JsonSerializer.SerializeToNode(b,Files.Json),"Every parsed CSV cell");
        Files.Save(output,new{Passed=true,Rows=a.Length,EveryCellExactlyIdentical=true});
    }
    public static void CompareHistory(string generated,string reference,string output)
    {
        using var a=new StreamReader(generated);using var b=new StreamReader(reference);int records=0;
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
