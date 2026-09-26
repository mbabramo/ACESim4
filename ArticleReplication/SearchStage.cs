using ACESim;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.Games.LitigGame.ManualReports;
using LitigCharts;
using System.Text.Json.Nodes;

namespace ArticleReplication;

public static class SearchStage
{
    public sealed record Request(FinalArticleCase Case,CorrelatedSignalsSettings Settings,string? Input,string Output,int[] Starts,bool ComputeMissing);
    public static async Task Run(string? input,ResolvedArticlePlan plan,string output,int workers,bool computeMissing)
    {
        Directory.CreateDirectory(output);var requests=new List<string>();
        foreach(string id in plan.CoreCases)
        {
            int shards=Math.Min(8,plan.Settings.StartsPerCore);
            for(int part=0;part<shards;part++)
            {
                string request=Path.Combine(output,"requests",id+$"-part-{part}.json");
                Files.Save(request,new Request(plan.Cases.Single(c=>c.Id==id),plan.Settings,input,Path.Combine(output,id),Enumerable.Range(0,plan.Settings.StartsPerCore).Where(i=>i%shards==part).ToArray(),computeMissing));requests.Add(request);
            }
        }
        await Parallel.ForEachAsync(requests,new ParallelOptions{MaxDegreeOfParallelism=workers},async(q,ct)=>await Commands.Worker(Path.Combine(output,"logs"),Path.GetFileNameWithoutExtension(q),output,"worker-search",q));
        var results=plan.CoreCases.SelectMany(id=>Enumerable.Range(0,plan.Settings.StartsPerCore).Select(i=>Files.Object(Path.Combine(output,id,$"start-{i:D5}","validation.json")))).ToArray();
        Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,AttemptedStarts=results.Length,AcceptedStarts=results.Count(r=>r["Accepted"]!.GetValue<bool>()),Results=results,PrimaryEquilibriumCatalog=false,SolvesStarted=results.Sum(r=>r["SolvesStarted"]!.GetValue<int>())});
    }
    public static async Task Worker(string file)
    {
        if(Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT")!="1")throw new InvalidDataException("Single-thread search worker required.");
        ACESimBase.Util.Debugging.TabbedText.DisableOutput();var r=Files.Read<Request>(file);
        var options=FinalArticleCaseFactory.Create(r.Case);var d=(SequenceForm)await ArticleWorkedPathExtraction.InitializeAsync(options);
        d.EvolutionSettings.UseAcceleratedBestResponse=true;d.EvolutionSettings.UseCurrentStrategyForBestResponse=true;d.EvolutionSettings.ParallelOptimization=false;
        d.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse=false;d.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting=false;
        var policy=new ArticleApproximatePolicy(r.Settings.ApproximateRoundingCutoff,r.Settings.ApproximateGainUnits,r.Settings.ApproximatePivotLimit);
        var ranges=Enumerable.Range(0,2).Select(p=>d.FinalUtilitiesNodes.Max(n=>n.Utilities[p])-d.FinalUtilitiesNodes.Min(n=>n.Utilities[p])).ToArray();
        foreach(int start in r.Starts)
        {
            string dest=Path.Combine(r.Output,$"start-{start:D5}");if(Directory.Exists(dest))throw new IOException("Search already attempted.");Directory.CreateDirectory(dest);
            string? shortcut=r.Input==null?null:SolveShortcut.SearchPath(r.Input,r.Case.Id,start);
            var proposed=shortcut!=null&&File.Exists(shortcut)?Files.Read<SolveShortcut>(shortcut):null;
            bool reused=proposed!=null&&proposed.MatchesSearchPolicy(r.Case.Id,start,r.Settings);SolveShortcut saved;
            if(reused)saved=proposed!;
            else
            {
                if(!r.ComputeMissing)throw new InvalidDataException("Missing compatible saved search: "+r.Case.Id+" start "+start);
                using var log=new StreamWriter(Path.Combine(dest,"pivot-checks.jsonl"));
                var attempt=ArticleApproximateSearch.Run(d,options,start,policy,p=>{log.WriteLine(System.Text.Json.JsonSerializer.Serialize(p,ArticleEquilibriumPaths.CompactJson));log.Flush();},p=>Files.Save(Path.Combine(dest,"prior.json"),p));
                Files.Save(Path.Combine(dest,"attempt.json"),attempt);
                var chosen=attempt.Decision; saved=new(r.Case.Id,"ApproximateStart",chosen.Accepted==null?"NoEquilibriumFound":"Equilibrium",start,attempt.ActualPriorSeed,policy.MaximumPivots,chosen.StoppingPivot,chosen.Reason,policy.Cutoff,policy.GainUnits.ToString(),chosen.Accepted?.Probabilities??[]);
            }
            saved.Check(r.Case.Id,"ApproximateStart",start,r.Settings);saved.Save(Path.Combine(dest,"solve.equ"));
            if(saved.Status=="NoEquilibriumFound")
            {
                Files.Save(Path.Combine(dest,"validation.json"),new{Passed=true,CaseId=r.Case.Id,StartIndex=start,Accepted=false,Decision=new{saved.Reason,saved.StoppingPivot,Accepted=(object?)null},ReusedCompletedAttempt=reused,FailureRecordIsNotNonexistenceProof=true,SolvesStarted=reused?0:1});continue;
            }
            var vector=saved.Probabilities;var fallback=ArticleWorkedPathExtraction.LoadProfile(d,vector);
            if(fallback.Count!=0||!vector.SequenceEqual(d.GetEquilibriumFromInformationSets()))throw new InvalidDataException("Complete search vector changed.");
            d.CalculateBestResponse(false);double[] raw=d.Status.BestResponseImprovement.ToArray();double average=policy.Scale(raw,ranges).Average();
            double threshold=saved.Reason=="first-below-0.001"?ArticleApproximatePolicy.EarlyThreshold:ArticleApproximatePolicy.CapThreshold;
            if(!d.Status.BestResponseReflectsCurrentStrategy||!(average<threshold)||(threshold==ArticleApproximatePolicy.CapThreshold&&average<ArticleApproximatePolicy.EarlyThreshold))throw new InvalidDataException("Saved approximate profile fails its acceptance criterion.");
            string eq=Path.Combine(dest,"equilibrium.equ"),actions=Path.Combine(dest,"information-set-actions.csv"),report=Path.Combine(dest,"replayed-report.csv");
            File.WriteAllText(eq,string.Join(',',vector.Select(p=>p.ToString("R"))));File.WriteAllText(actions,InformationSetActionReport.BuildCsv(d,1));
            int rows=ArticleWorkedPathExtraction.ValidateActionReport(d,1,actions);
            d.SaveWeightedGameProgressesAfterEachReport=true;d.SavedWeightedGameProgresses.Clear();d.ActionStrategy=ActionStrategies.CurrentProbability;
            var replay=await d.GenerateReportsByPlaying(false);File.WriteAllText(report,replay.csvReports.Single());var welfare=SavedProfileWelfare.Evaluate(options,d.SavedWeightedGameProgresses);
            AgreementToBargainStudy.ExportProfile(d,options,1,eq,actions,report,dest,fallback,()=>FinalArticleCaseFactory.Create(r.Case));
            if(!vector.SequenceEqual(d.GetEquilibriumFromInformationSets()))throw new InvalidDataException("Reports changed a search strategy.");
            Files.Save(Path.Combine(dest,"validation.json"),new{Passed=true,CaseId=r.Case.Id,StartIndex=start,Accepted=true,CompleteVectorHash=ArticleApproximateSearch.ProfileHash(vector),FullBestResponseRawGains=raw,AverageGain=average,Threshold=threshold,RecordedPivotCap=saved.MaximumPivots,RequestedPivotCap=policy.MaximumPivots,StoppingPivot=saved.StoppingPivot,Welfare=welfare,ActionRows=rows,ReusedCompletedAttempt=reused,PrimaryEquilibriumCatalog=false,SolvesStarted=reused?0:1});
        }
    }
}
