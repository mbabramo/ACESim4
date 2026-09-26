using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.Games.LitigGame.ManualReports;
using LitigCharts;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArticleReplication;

/// <summary>Reuse accepted floating results under their own criteria; never place them in the exact-primary catalog.</summary>
public static class ApproximateCache
{
    public sealed record Item(string CaseId,int StartIndex,bool Accepted,string Directory);
    public sealed record Manifest(string Schema,Item[] Attempts,BundleFile[] Files);
    public sealed record Request(FinalArticleCase Case,StrategicGameFingerprint.Snapshot ExpectedGame,Item[] Attempts,string Inputs,string Output,int StartBudget,int PivotLimit,double Cutoff,ArticleApproximateGainUnits GainUnits);
    public static void Pack(string legacy,string output)
    {
        if(Directory.Exists(output))throw new IOException("Approximate input pack requires a new directory.");
        Bundle.Open(legacy);Directory.CreateDirectory(output);
        string old=Path.Combine(legacy,"records/Supplemental materials/Multiple equilibria/Sources");var items=new List<Item>();
        foreach(string result in Directory.GetFiles(Path.Combine(old,"Attempts"),"*-result.json").Order(StringComparer.Ordinal))
        {
            var data=Files.Object(result);string id=Path.GetFileName(result)[..^"-result.json".Length],caseId=id[..id.LastIndexOf("-start-",StringComparison.Ordinal)];
            int start=data["StartIndex"]!.GetValue<int>();bool accepted=data["Accepted"]!.GetValue<bool>();
            if(data["Completed"]?.GetValue<bool>()!=true||data["PrimaryEquilibriumCatalog"]!.GetValue<bool>()||data["ExactSolves"]!.GetValue<int>()!=0)throw new InvalidDataException("Invalid saved approximate receipt.");
            string dir=Files.Under(output,id);Directory.CreateDirectory(dir);Files.CopyVerified(result,Path.Combine(dir,"result.json"));
            string[] required=accepted?["started.json","selection.json","prior.json","approximate-profile.csv","information-set-actions.csv","numeric-report.csv"]:["started.json","selection.json","prior.json"];
            foreach(string name in required)
            {
                var identity=data["Files"]!.AsArray().Single(f=>Files.LegacyBaseName(f!["Path"]!.GetValue<string>())==name)!;
                Files.CopyVerified(identity["Path"]!.GetValue<string>(),Path.Combine(dir,name),identity["Sha256"]!.GetValue<string>());
            }
            if(accepted)Files.CopyVerified(Path.Combine(old,"Profiles",id+".json"),Path.Combine(dir,"complete-profile.json"));
            items.Add(new(caseId,start,accepted,id));
        }
        var files=Directory.GetFiles(output,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f=>new BundleFile(Path.GetRelativePath(output,f).Replace('\\','/'),Files.Sha(f),new FileInfo(f).Length)).ToArray();
        Files.Save(Path.Combine(output,"approximate-inputs.json"),new Manifest("saved-approximate-computation-v1",items.ToArray(),files));
    }
    public static async Task Run(string inputs,ResolvedArticlePlan plan,Dictionary<string,(JsonObject Audit,JsonObject Profile)> primary,string output,int workers)
    {
        var manifest=Files.Read<Manifest>(Path.Combine(inputs,"approximate-inputs.json"));
        if(manifest.Schema!="saved-approximate-computation-v1"||manifest.Attempts.Select(a=>(a.CaseId,a.StartIndex)).Distinct().Count()!=manifest.Attempts.Length)throw new InvalidDataException("Invalid approximate input manifest.");
        foreach(var f in manifest.Files)if(Files.Sha(Files.Under(inputs,f.Path))!=f.Sha256)throw new InvalidDataException("Changed approximate computation: "+f.Path);
        if(Directory.Exists(output))throw new IOException("Fresh approximate stage output required.");Directory.CreateDirectory(output);
        var selected=manifest.Attempts.Where(a=>plan.CoreCases.Contains(a.CaseId)&&a.StartIndex>=0&&a.StartIndex<plan.Settings.StartsPerCore).ToArray();
        foreach(string id in plan.CoreCases)
            if(!selected.Where(a=>a.CaseId==id).Select(a=>a.StartIndex).Order().SequenceEqual(Enumerable.Range(0,plan.Settings.StartsPerCore)))throw new InvalidDataException("Missing saved approximate starts for "+id+"; no replacement search was launched.");
        await Parallel.ForEachAsync(plan.CoreCases,new ParallelOptions{MaxDegreeOfParallelism=workers},async(id,ct)=>{
            var game=primary[id].Audit["GameIdentity"]!.Deserialize<StrategicGameFingerprint.Snapshot>(Files.Json)!;
            string request=Path.Combine(output,"requests",id+".json");
            Files.Save(request,new Request(plan.Cases.Single(c=>c.Id==id),game,selected.Where(a=>a.CaseId==id).OrderBy(a=>a.StartIndex).ToArray(),inputs,Path.Combine(output,id),plan.Settings.StartsPerCore,plan.Settings.ApproximatePivotLimit,plan.Settings.ApproximateRoundingCutoff,plan.Settings.ApproximateGainUnits));
            await Commands.Worker(Path.Combine(output,"logs"),id,output,"worker-approximate-cache",request);
        });
        var results=plan.CoreCases.SelectMany(id=>Files.Object(Path.Combine(output,id,"completed.json"))["Results"]!.AsArray()).Select(x=>x!.DeepClone()).ToArray();
        Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,AttemptedStarts=selected.Length,AcceptedStarts=selected.Count(a=>a.Accepted),Results=results,PrimaryEquilibriumCatalog=false,SolvesStarted=0});
    }
    public static async Task Worker(string requestFile)
    {
        if(Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT")!="1")throw new InvalidDataException("Single-thread approximate revalidation required.");
        ACESimBase.Util.Debugging.TabbedText.DisableOutput();var request=Files.Read<Request>(requestFile);Directory.CreateDirectory(request.Output);
        var options=FinalArticleCaseFactory.Create(request.Case);var d=await ArticleWorkedPathExtraction.InitializeAsync(options);
        if(StrategicGameFingerprint.Capture(d)!=request.ExpectedGame)throw new InvalidDataException("Approximate game identity changed.");
        d.EvolutionSettings.UseAcceleratedBestResponse=true;d.EvolutionSettings.UseCurrentStrategyForBestResponse=true;d.EvolutionSettings.ParallelOptimization=false;
        d.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse=false;d.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting=false;
        var ranges=Enumerable.Range(0,2).Select(p=>d.FinalUtilitiesNodes.Max(n=>n.Utilities[p])-d.FinalUtilitiesNodes.Min(n=>n.Utilities[p])).ToArray();
        var policy=new ArticleApproximatePolicy(request.Cutoff,request.GainUnits,request.PivotLimit);var results=new List<object>();
        foreach(var item in request.Attempts)
        {
            string dir=Files.Under(request.Inputs,item.Directory);var selection=Files.Object(Path.Combine(dir,"selection.json"));var original=Files.Object(Path.Combine(dir,"result.json"));var start=Files.Object(Path.Combine(dir,"started.json"));
            if(selection["StartIndex"]!.GetValue<int>()!=item.StartIndex||selection["ActualPriorSeed"]!.GetValue<int>()!=1000000+item.StartIndex||selection["Cutoff"]!.GetValue<double>()!=request.Cutoff||selection["GainUnits"]!.GetValue<string>()!=request.GainUnits.ToString()||start["Policy"]!["StartBudget"]!.GetValue<int>()<=item.StartIndex)throw new InvalidDataException("Saved approximate initialization/policy differs from the central settings.");
            Files.EqualScience(original["Decision"],selection["Decision"],"Approximate stopping decision");
            int recordedCap=start["Policy"]!["MaximumPivots"]!.GetValue<int>(),stoppingPivot=selection["Decision"]!["StoppingPivot"]!.GetValue<int>();
            // A cap is irrelevant only when the identical early rule already stopped within both budgets.
            bool early=selection["Decision"]!["Reason"]!.GetValue<string>()=="first-below-0.001";
            if(recordedCap!=request.PivotLimit&&!(early&&stoppingPivot<=recordedCap&&stoppingPivot<=request.PivotLimit))throw new InvalidDataException("Incompatible recorded pivot cap.");
            Files.EqualScience(start["Policy"]!["Case"],JsonSerializer.SerializeToNode(request.Case,Files.Json),"Approximate case parameters");
            Files.EqualScience(start["Game"],JsonSerializer.SerializeToNode(request.ExpectedGame,Files.Json),"Approximate complete game");
            Files.EqualScience(selection["TerminalUtilityRanges"],JsonSerializer.SerializeToNode(ranges,Files.Json),"Approximate utility ranges");
            if(!item.Accepted)
            {
                if(selection["Decision"]?["Accepted"]!=null||original["Accepted"]!.GetValue<bool>())throw new InvalidDataException("Rejected attempt contains accepted profile.");
                results.Add(new{item.CaseId,item.StartIndex,Accepted=false,Decision=original["Decision"],ReusedCompletedAttempt=true});continue;
            }
            string eq=Path.Combine(dir,"approximate-profile.csv"),actions=Path.Combine(dir,"information-set-actions.csv");
            double[] vector=File.ReadAllText(eq).Trim().Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray();
            Files.EqualScience(JsonSerializer.SerializeToNode(vector,Files.Json),selection["Decision"]!["Accepted"]!["Probabilities"],"Saved approximate complete selected vector");
            var fallback=ArticleWorkedPathExtraction.LoadProfile(d,vector);
            if(fallback.Count!=0||!vector.SequenceEqual(d.GetEquilibriumFromInformationSets()))throw new InvalidDataException("Approximate complete vector changed.");
            int actionsChecked=ArticleWorkedPathExtraction.ValidateActionReport(d,1,actions);d.CalculateBestResponse(false);
            double[] raw=d.Status.BestResponseImprovement.ToArray(),gains=policy.Scale(raw,ranges);double average=gains.Average();
            string reason=selection["Decision"]!["Reason"]!.GetValue<string>();
            if(reason is not ("first-below-0.001" or "cap-accepted"))throw new InvalidDataException("Unknown approximate acceptance reason.");
            if(reason=="cap-accepted"&&(average<ArticleApproximatePolicy.EarlyThreshold||selection["Decision"]!["StoppingPivot"]!.GetValue<int>()!=request.PivotLimit))throw new InvalidDataException("Incorrect cap acceptance band.");
            double threshold=reason=="first-below-0.001"?ArticleApproximatePolicy.EarlyThreshold:ArticleApproximatePolicy.CapThreshold;
            if(threshold!=original["Validation"]!["Threshold"]!.GetValue<double>())throw new InvalidDataException("Approximate acceptance threshold changed.");
            if(!d.Status.BestResponseReflectsCurrentStrategy||!(average<threshold)||average!=original["Validation"]!["AverageGain"]!.GetValue<double>())throw new InvalidDataException("Approximate acceptance differs on full revalidation.");
            Files.EqualScience(JsonSerializer.SerializeToNode(raw,Files.Json),original["Validation"]!["FullBestResponseRawGains"],"Approximate full signed BR gains");
            d.SaveWeightedGameProgressesAfterEachReport=true;d.SavedWeightedGameProgresses.Clear();d.ActionStrategy=ActionStrategies.CurrentProbability;
            var replay=await d.GenerateReportsByPlaying(false);var welfare=SavedProfileWelfare.Evaluate(options,d.SavedWeightedGameProgresses);
            string dest=Path.Combine(request.Output,$"start-{item.StartIndex:D5}");Directory.CreateDirectory(dest);string report=Path.Combine(dest,"replayed-report.csv");File.WriteAllText(report,replay.csvReports.Single());
            int cells=MultipleEquilibriaStrategyAudit.ValidateReplay(Path.Combine(dir,"numeric-report.csv"),report);
            Files.EqualScience(JsonSerializer.SerializeToNode(welfare,Files.Json),original["Validation"]!["Welfare"],"Approximate welfare and accounting");
            AgreementToBargainStudy.ExportProfile(d,options,1,eq,actions,report,dest,fallback,()=>FinalArticleCaseFactory.Create(request.Case));
            var profile=Files.Object(Directory.GetFiles(Path.Combine(dest,"Sources/Profiles"),"*.json").Single());var prior=Files.Object(Path.Combine(dir,"complete-profile.json"));var science=(JsonObject)profile.DeepClone();
            foreach(string k in new[]{"Profile","ActionReport","ReplayReport"}){science.Remove(k);prior.Remove(k);}Files.EqualScience(science,prior,"Full approximate profile science");
            if(!vector.SequenceEqual(d.GetEquilibriumFromInformationSets()))throw new InvalidDataException("Approximate reporting changed a strategy.");
            var record=new{Passed=true,item.CaseId,item.StartIndex,Accepted=true,CompleteVectorHash=ArticleApproximateSearch.ProfileHash(vector),FullBestResponseRawGains=raw,AverageGain=average,Threshold=threshold,RecordedPivotCap=recordedCap,RequestedPivotCap=request.PivotLimit,StoppingPivot=stoppingPivot,Welfare=welfare,ActionRows=actionsChecked,NumericReplayCells=cells,PrimaryEquilibriumCatalog=false,SolvesStarted=0};
            Files.Save(Path.Combine(dest,"validation.json"),record);results.Add(record);
        }
        Files.Save(Path.Combine(request.Output,"completed.json"),new{Passed=true,Results=results,SolvesStarted=0,PrimaryEquilibriumCatalog=false});
    }
}
