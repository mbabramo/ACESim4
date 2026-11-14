using ACESim;
using ACESimBase;
using ACESimBase.GameSolvingSupport.Evaluation;
using ACESimBase.GameSolvingSupport.Policies;
using ACESimBase.GameSolvingSupport.Reporting;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.Util.Debugging;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArticleReplication;

/// <summary>Fresh forward replay of archived strategies. No optimization,
/// training iterations, strategy substitution, rounding or input writes.</summary>
public static class EndogenousReplay
{
    public sealed record Item(string Id, string Kind, string SourceResult, string SourceResultSha256,
        string Archive, string ArchiveSha256, int? Repetition, string? Selector);
    public sealed record Plan(string Schema, string InputDirectory, string CatalogSha256,
        string CaseId, string GameHash, string OutputDirectory, Item[] Items,
        Dictionary<string,string> RuntimeSha256);
    public sealed record Difference(string Metric, double? Expected, double? Replayed,
        double? AbsoluteDifference, double Tolerance, bool BitwiseEqual, bool Passed);

    public static async Task Run(string planPath)
    {
        var plan=Files.Read<Plan>(planPath);
        if(plan.Schema!="endogenous-saved-replay-case-v1" || plan.Items.Length==0 ||
            plan.Items.Select(x=>x.Id).Distinct().Count()!=plan.Items.Length)
            throw new InvalidDataException("Invalid replay plan/duplicate output identity.");
        if(Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT")!="1" || Environment.ProcessorCount!=1)
            throw new InvalidOperationException("Each replay process must have one logical processor.");
        string runtime=Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        foreach(var (name,hash) in plan.RuntimeSha256)
            if(Files.Sha(Files.Under(runtime,name))!=hash)throw new InvalidDataException("Changed replay runtime: "+name);
        if(Files.Sha(Path.Combine(plan.InputDirectory,"catalog.json"))!=plan.CatalogSha256)
            throw new InvalidDataException("Changed input catalog.");
        var catalog=EndogenousPopulationExperiment.LoadCatalog(plan.InputDirectory);
        var spec=catalog.Cases.Single(x=>x.Id==plan.CaseId);
        var output=Path.GetFullPath(plan.OutputDirectory);
        if(Directory.Exists(output) || Files.Nested(output,plan.InputDirectory) || Files.Nested(plan.InputDirectory,output) ||
            plan.Items.Any(x=>Files.Nested(x.SourceResult,output)||Files.Nested(x.Archive,output)))
            throw new IOException("Replay needs a fresh directory separate from every source.");
        Directory.CreateDirectory(output);Files.CopyVerified(planPath,Path.Combine(output,"plan.json"));
        var started=DateTime.UtcNow;var timer=Stopwatch.StartNew();
        TabbedText.DisableOutput();TabbedText.WriteToConsole=false;TabbedText.DisableConsoleProgressString=true;
        var developer=await EndogenousPopulationExperiment.Initialize(spec,new EvolutionSettings
        {
            Algorithm=GameApproximationAlgorithm.GeneralizedVanilla,ParallelOptimization=false,
            CreateEquilibriaFile=false,CreateEFGFile=false,UseExistingEquilibriaIfAvailable=false,
            GenerateManualReports=false,GenerateReportsByPlaying=false,
            RoundOffLowProbabilitiesBeforeReporting=false,RoundOffLowProbabilitiesBeforeAcceleratedBestResponse=false
        });
        var tree=PolicyGameTree.Create(developer);
        var fingerprint=StrategicGameFingerprint.Capture(developer,roundChanceProbabilities:false);
        if(fingerprint.CompleteSha256!=plan.GameHash)throw new InvalidDataException("Fresh strategic game differs from source.");
        var options=EndogenousPopulationExperiment.OptionsFor(spec);
        var generator=(PrecautionNegligenceDisputeGenerator)options.LitigGameDisputeGenerator;
        var rows=new List<object>();bool passed=true;
        foreach(var item in plan.Items)
        {
            if(Files.Sha(item.SourceResult)!=item.SourceResultSha256 || Files.Sha(item.Archive)!=item.ArchiveSha256)
                throw new InvalidDataException("Changed replay input: "+item.Id);
            var source=ReadPhysical(item.SourceResult);
            PlayerPolicy[][] policies;double[] weights;double[] expectedValues;JsonNode? expectedOutcomes;
            if(item.Kind=="draft")
            {
                if(item.Archive!=spec.SavedProfile || item.ArchiveSha256!=spec.SavedProfileSha256)
                    throw new InvalidDataException("Wrong historical equilibrium shortcut.");
                var saved=FlatEquilibriumFiles.Read(item.Archive).Single();
                if(saved.Length!=tree.Layout.Sum(s=>s.Probabilities.Count))throw new InvalidDataException("Historical layout mismatch.");
                ((GeneralizedVanilla)developer).SetInformationSetValues(saved);
                var primary=PolicyAccess.CaptureCurrent(developer,"fresh-draft-replay");
                policies=[[primary.ForPlayer(0)],[primary.ForPlayer(1)]];weights=[1];
                expectedValues=source["Values"]!.Deserialize<double[]>(Files.Json)!;expectedOutcomes=source["Outcomes"];
            }
            else
            {
                var saved=PopulationArchiveIO.Read(item.Archive);
                if(saved.GameHash!=plan.GameHash || !saved.Keys.SequenceEqual(tree.Layout.Select(x=>x.Key)))
                    throw new InvalidDataException("Archived strategic game/layout differs.");
                policies=Enumerable.Range(0,2).Select(p=>saved.Policies.Where(x=>x.Player==p).Select(x=>PopulationExperiment.Restore(tree,x)).ToArray()).ToArray();
                if(item.Kind is "traditional-current" or "traditional-average")
                {
                    if(policies.Any(x=>x.Length!=1))throw new InvalidDataException("Traditional profile must contain one policy per player.");
                    if(source["CaseId"]!.GetValue<string>()!=plan.CaseId || source["Repetition"]!.GetValue<int>()!=item.Repetition)
                        throw new InvalidDataException("Traditional seed position differs.");
                    string key=item.Kind=="traditional-current"?"Current":"AverageDiagnostic";
                    weights=[1];expectedValues=source[key]!["ExpectedUtilities"]!.Deserialize<double[]>(Files.Json)!;
                    expectedOutcomes=item.Kind=="traditional-current"?source["Outcomes"]:null;
                }
                else if(item.Kind is "initial" or "pooled")
                {
                    if(source["CaseId"]!.GetValue<string>()!=plan.CaseId || source["Selector"]!.GetValue<string>()!=item.Selector)
                        throw new InvalidDataException("Selector identity differs.");
                    weights=source["JointWeights"]!.Deserialize<double[]>(Files.Json)!;
                    expectedValues=source["ExpectedValues"]!.Deserialize<double[]>(Files.Json)!;expectedOutcomes=source["Outcomes"];
                }
                else throw new InvalidDataException("Unknown replay kind.");
            }
            PolicyGameTree.ValidateDistribution(weights,checked(policies[0].Length*policies[1].Length));
            var values=new double[tree.ValueCount];var totals=new double[15];double mass=0;
            // The observer sums FRESH detached terminal utilities from the
            // original JOINT distribution; product marginals are never used.
            JointPolicyOutcomeReplay.Visit(developer,tree,policies[0],policies[1],weights,(progress,w)=>
                {
                        var p=(PrecautionNegligenceProgress)progress;
                        double[] v=[p.PWelfare,p.DWelfare,p.PFiles?1:0,p.DAnswers?1:0,p.TrialOccurs?1:0,p.CaseSettles?1:0,
                            p.DisputeArises?1:0,p.EngagesInActivity?1:0,p.RelativePrecautionLevel,p.OpportunityCost,p.HarmCost,
                            p.TotalExpensesIncurred,p.AccidentWronglyCausallyAttributedToDefendant?1:0,
                            p.RelativePrecautionLevel*generator.UnitPrecautionCost,p.EngagesInActivity?0:generator.BenefitToDefendantOfActivity];
                        for(int k=0;k<v.Length;k++)totals[k]+=w*v[k];mass+=w;
                },(terminal,w)=>{for(int k=0;k<values.Length;k++)values[k]+=w*terminal[k];});
            if(Math.Abs(mass-1)>1e-9)throw new ArithmeticException("Fresh native probability mass differs from one.");
            double? Divide(double a,double b)=>b>0?a/b:null;
            var measures=new Dictionary<string,double>{{"DisputeProbability",totals[6]},{"Engagement",totals[7]},
                {"PrecautionLevel",totals[8]},{"OpportunityCost",totals[9]},{"HarmCost",totals[10]},
                {"LitigationExpenditures",totals[11]},{"WrongfulAttribution",totals[12]},
                {"PrecautionCost",totals[13]},{"ForegoneActivityCost",totals[14]},
                {"SocialWelfareLoss",totals[9]+totals[10]+totals[11]}};
            var conditional=new Dictionary<string,double?>{{"Filing",Divide(totals[2],totals[6])},{"Answering",Divide(totals[3],totals[6])},
                {"Trial",Divide(totals[4],totals[6])},{"Settlement",Divide(totals[5],totals[6])}};
            var outcomes=new EndogenousPopulationExperiment.Outcomes(values.Take(2).ToArray(),totals.Take(2).ToArray(),totals[2],totals[3],
                Divide(totals[3],totals[2]),totals[4],totals[5],measures,conditional,mass);
            var comparisons=new List<Difference>();
            if(values.Length!=expectedValues.Length)throw new InvalidDataException("Utility vector length differs.");
            for(int k=0;k<values.Length;k++)comparisons.Add(Compare("ExpectedValues/"+k,expectedValues[k],values[k],k<2));
            if(expectedOutcomes is not null)
                CompareOutcomes(expectedOutcomes,JsonSerializer.SerializeToNode(outcomes,Files.Json)!,"Outcomes",comparisons);
            bool rowPassed=comparisons.All(x=>x.Passed);passed&=rowPassed;
            if(Files.Sha(item.SourceResult)!=item.SourceResultSha256 || Files.Sha(item.Archive)!=item.ArchiveSha256)
                throw new InvalidDataException("Source changed during replay.");
            rows.Add(new { item.Id,item.Kind,item.Repetition,item.Selector,Passed=rowPassed,Source=item,
                JointWeightsSha256=Files.Digest(weights),PositiveJointWeights=weights.Count(x=>x>0),
                PlaintiffPolicies=policies[0].Length,DefendantPolicies=policies[1].Length,
                Values=values,Outcomes=outcomes,Comparisons=comparisons });
            Console.WriteLine($"Replayed {item.Id}: {comparisons.Count} values; passed={rowPassed}");
        }
        string resultPath=Path.Combine(output,"replay.json");Files.SaveCompressed(resultPath,new { Passed=passed,CaseId=plan.CaseId,Rows=rows,
            GameHash=fingerprint.CompleteSha256,TreeNodes=tree.NodeCount,StartedUtc=started,FinishedUtc=DateTime.UtcNow,Seconds=timer.Elapsed.TotalSeconds,
            Qualification="Forward reproducibility only; no re-solving, optimization, marginal substitution or promotion of numerical acceptance grades." });
        Files.Save(Path.Combine(output,"completed.json"),new { Passed=passed,CaseId=plan.CaseId,Rows=rows.Count,
            PlanSha256=Files.Sha(planPath),ReplaySha256=Files.Sha(resultPath+".gz"),Seconds=timer.Elapsed.TotalSeconds,FinishedUtc=DateTime.UtcNow });
    }

    private static JsonNode ReadPhysical(string path)
    {
        if(!path.EndsWith(".gz",StringComparison.OrdinalIgnoreCase))return Files.Object(path);
        using var file=File.OpenRead(path);using var zip=new System.IO.Compression.GZipStream(file,System.IO.Compression.CompressionMode.Decompress);
        return JsonNode.Parse(zip)??throw new InvalidDataException("Empty result archive.");
    }

    public static Difference Compare(string key,double? expected,double? actual,bool utility=false)
    {
        double tolerance=utility?1e-8:1e-12+1e-10*Math.Abs(expected??0);
        bool valid=(expected is null&&actual is null)||(expected.HasValue&&actual.HasValue&&double.IsFinite(expected.Value)&&double.IsFinite(actual.Value));
        double? difference=expected.HasValue&&actual.HasValue?Math.Abs(expected.Value-actual.Value):null;
        bool equal=expected is null&&actual is null || expected.HasValue&&actual.HasValue&&BitConverter.DoubleToInt64Bits(expected.Value)==BitConverter.DoubleToInt64Bits(actual.Value);
        return new(key,expected,actual,difference,tolerance,equal,valid&&(!difference.HasValue||difference.Value<=tolerance));
    }

    private static void CompareOutcomes(JsonNode? expected,JsonNode? actual,string prefix,List<Difference> result)
    {
        if(expected is JsonObject a && actual is JsonObject b)
        {
            if(!a.Select(x=>x.Key).ToHashSet().SetEquals(b.Select(x=>x.Key)))throw new InvalidDataException("Outcome fields differ at "+prefix);
            foreach(var x in a)CompareOutcomes(x.Value,b[x.Key],prefix+"/"+x.Key,result);return;
        }
        if(expected is JsonArray x1 && actual is JsonArray x2)
        {
            if(x1.Count!=x2.Count)throw new InvalidDataException("Outcome dimensions differ.");
            for(int i=0;i<x1.Count;i++)CompareOutcomes(x1[i],x2[i],prefix+"/"+i,result);return;
        }
        result.Add(Compare(prefix,expected?.GetValue<double>(),actual?.GetValue<double>(),prefix.Contains("Utilities/")));
    }
}
