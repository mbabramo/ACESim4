using ACESim;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArticleReplication;

/// <summary>The producer and consumer share this format; scientific reports are always outputs.</summary>
public static class PrimaryCache
{
    public static async Task NegativeTests(string cache,string output)
    {
        if(Directory.Exists(output))throw new IOException("New negative-test output required.");
        var manifest=Bundle.Open(cache);var input=Files.Read<PrimaryInput[]>(Path.Combine(cache,"inputs/primary.json")).First(p=>p.Case.Family=="baseline");
        Directory.CreateDirectory(output);string settings=Path.Combine(output,"settings.json");
        Files.Save(settings,new CorrelatedSignalsSettings{MainCostMultipliers=[1],IncludeExtensions=false,IncludeTrialOnly=false,Steps=["Primary"]});
        var tests=new List<object>();
        foreach(string test in new[]{"hash-mismatch","invalid-probability","game-mismatch","calibration-mismatch"})
        {
            string dir=Path.Combine(output,test,"input"),eq=Path.Combine(dir,"equilibrium.equ");Directory.CreateDirectory(dir);
            Files.CopyVerified(Files.Under(cache,input.Inputs["Equilibrium"].Path),eq,input.Inputs["Equilibrium"].Sha256);
            var game=(JsonObject)input.GameIdentity.DeepClone();if(test=="game-mismatch")game["CompleteSha256"]=new string('0',64);
            if(test=="invalid-probability"){var values=File.ReadAllText(eq).Trim().Split(',');values[0]="2";File.WriteAllText(eq,string.Join(',',values));}
            var proposed=input with{GameIdentity=game,Inputs=new(){{"Equilibrium",new("equilibrium.equ",Files.Sha(eq))}},ExpectedAudit=null,ExpectedProfile=null};
            Files.Save(Path.Combine(dir,"inputs/primary.json"),new[]{proposed});
            var files=Directory.GetFiles(dir,"*",SearchOption.AllDirectories).Select(f=>new BundleFile(Path.GetRelativePath(dir,f).Replace('\\','/'),Files.Sha(f),new FileInfo(f).Length)).ToArray();
            var m=manifest with{Files=files,PrimaryCaseIds=[input.CaseId],Renders=[],Strategic=[],Calibration=test=="calibration-mismatch"?manifest.Calibration with{PartySigma=.1}:manifest.Calibration};
            Files.Save(Path.Combine(dir,"bundle.json"),m);
            if(test=="hash-mismatch")File.AppendAllText(eq," ");
            string run=Path.Combine(output,test,"run");Exception? failure=null;
            try{await Pipeline.Run(new(){{"solutions",dir},{"output",run},{"settings",settings},{"workers","1"},{"other-workers","0"}});}catch(Exception e){failure=e;}
            if(failure==null)throw new InvalidDataException("Invalid cache was accepted: "+test);
            string expected=test switch{"hash-mismatch"=>"Bundle changed","invalid-probability"=>"Invalid saved action probability","game-mismatch"=>"Full game changed",_=>"cannot redefine the article calibration"};
            string details=failure.ToString();if(Directory.Exists(Path.Combine(run,"logs")))details+=string.Join('\n',Directory.GetFiles(Path.Combine(run,"logs"),"*.stderr.log").Select(File.ReadAllText));
            if(!details.Contains(expected))throw new InvalidDataException("Unexpected failure during "+test+": "+details);
            tests.Add(new{Test=test,RejectedFor=expected,Passed=true});
        }
        Files.Save(Path.Combine(output,"validation.json"),new{Passed=true,Tests=tests,SolvesStarted=0});
    }

    public static void Export(string run,string output)
    {
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("A new cache directory is required.");
        var plan=Files.Read<ResolvedArticlePlan>(Path.Combine(run,"resolved-plan.json"));
        if(Files.Object(Path.Combine(run,"primary-validation.json"))["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Primary validation did not pass.");
        var records=new List<PrimaryInput>();Directory.CreateDirectory(output);
        foreach(var spec in plan.Cases)
        {
            string auditPath=Path.Combine(run,"ReportResults/Primary",spec.Id,"validation.json");if(!File.Exists(auditPath))continue;
            var audit=Files.Object(auditPath);
            if(audit["Passed"]?.GetValue<bool>()!=true||audit["CompleteStrategyUnchanged"]?.GetValue<bool>()!=true)throw new InvalidDataException("Primary cache requires a passed complete-profile check.");
            Files.EqualScience(audit["Case"],JsonSerializer.SerializeToNode(spec,Files.Json),"Exported cache case");
            var identity=(audit["GeneratedInputs"]??audit["Inputs"])!["Equilibrium"]!.Deserialize<FinalArticleExecution.FileIdentity>(Files.Json)!;
            string rel="computations/primary/"+spec.Id+"/equilibrium.equ";Files.CopyVerified(identity.Path,Files.Under(output,rel),identity.Sha256);
            records.Add(new(spec.Id,spec,(JsonObject)audit["GameIdentity"]!.DeepClone(),null,null,new(){{"Equilibrium",new(rel,identity.Sha256)}}));
        }
        Files.Save(Path.Combine(output,"inputs/primary.json"),records);
        var files=Directory.GetFiles(output,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f=>new BundleFile(Path.GetRelativePath(output,f).Replace('\\','/'),Files.Sha(f),new FileInfo(f).Length)).ToArray();
        Files.Save(Path.Combine(output,"bundle.json"),new BundleManifest("correlated-signals-computations-v3",DateTime.UtcNow,"see producer receipt",ArticlePlan.PublishedCalibration,files,[],records.Select(r=>r.CaseId).ToArray(),["Proposed complete equilibrium vectors and their case/game identities only.","Reusing this cache reruns normalization, complete-vector checks, full best responses, accounting and reports. No old report or audit is required."]));
        Files.Save(Path.Combine(output,"producer.json"),new{Schema="article-computation-producer-v1",Stage="ExactPrimary",ProducedUtc=DateTime.UtcNow,PlanSha256=Files.Sha(Path.Combine(run,"resolved-plan.json")),ValidationSha256=Files.Sha(Path.Combine(run,"primary-validation.json")),AssemblySha256=Files.Sha(typeof(PrimaryCache).Assembly.Location),Cases=records.Count});
    }

    public static void Compare(string generated,string reference,string output)
    {
        string[] ValidatedCases(string root)
        {
            var receipt=Files.Object(Path.Combine(root,"primary-validation.json"));
            if(receipt["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Comparison requires passed primary validation.");
            return receipt["Profiles"]!.AsArray().Select(p=>p!["Passed"]?.GetValue<bool>()==true?p["CaseId"]!.GetValue<string>():throw new InvalidDataException("Failed primary case.")).Order(StringComparer.Ordinal).ToArray();
        }
        var expected=ValidatedCases(generated);
        if(expected.Length==0||!expected.SequenceEqual(ValidatedCases(reference)))throw new InvalidDataException("Comparison requires identical nonempty validated case sets.");
        var plan=Files.Read<ResolvedArticlePlan>(Path.Combine(generated,"resolved-plan.json"));var checkedCases=new List<string>();
        foreach(var spec in plan.Cases.Where(c=>expected.Contains(c.Id)))
        {
            string dir=Path.Combine(generated,"ReportResults/Primary",spec.Id);
            string old=Path.Combine(reference,"ReportResults/Primary",spec.Id);
            var a=Files.Object(Directory.GetFiles(Path.Combine(dir,"Sources/Profiles"),"*.json").Single());var b=Files.Object(Directory.GetFiles(Path.Combine(old,"Sources/Profiles"),"*.json").Single());
            foreach(string k in new[]{"Profile","ActionReport","ReplayReport"}){a.Remove(k);b.Remove(k);}Files.EqualScience(a,b,spec.Id+" full scientific profile");
            a=Files.Object(Path.Combine(dir,"validation.json"));b=Files.Object(Path.Combine(old,"validation.json"));
            foreach(string k in new[]{"GameIdentity","FullBestResponseGains","CompleteStrategySha256","UnspecifiedOffPathInformationSets","Welfare"})Files.EqualScience(a[k],b[k],spec.Id+" "+k);
            checkedCases.Add(spec.Id);
        }
        if(!checkedCases.Order(StringComparer.Ordinal).SequenceEqual(expected))throw new InvalidDataException("Validated case absent from the central plan.");
        foreach(string name in new[]{"selected-primary-outcomes.csv","all-welfare-decompositions.csv"})
        {
            string file=Path.Combine(generated,"article/Results/Aggregated Data",name);if(!File.Exists(file))continue;
            var a=Reports.ReadCsv(file);var b=Reports.ReadCsv(Path.Combine(reference,"article/Results/Aggregated Data",name));
            // Same selected settings and row order are required; compare parsed values as exact strings.
            Files.EqualScience(JsonSerializer.SerializeToNode(a,Files.Json),JsonSerializer.SerializeToNode(b,Files.Json),name);
        }
        Files.Save(output,new{Passed=true,Cases=checkedCases,CompleteScientificProfilesExactlyIdentical=true,FullBestResponsesExactlyIdentical=true,WelfareAndCsvExactlyIdentical=true,GeneratedManifestSha256=Files.Sha(Path.Combine(generated,"primary-validation.json")),ReferenceManifestSha256=Files.Sha(Path.Combine(reference,"primary-validation.json"))});
    }
}
