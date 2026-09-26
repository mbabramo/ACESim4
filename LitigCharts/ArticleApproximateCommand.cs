using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingAlgorithms;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Durable single-start worker for the separate approximate catalog.</summary>
public static class ArticleApproximateCommand
{
    public sealed record Request(FinalArticleCase Case, StrategicGameFingerprint.Snapshot ExpectedGame,
        FinalArticleExecution.Authorization Authorization, string ClaimsDirectory, int StartIndex, int StartBudget,
        double RoundingCutoff, ArticleApproximateGainUnits GainUnits, int MaximumPivots = ArticleApproximatePolicy.PivotCap);
    private static readonly JsonSerializerOptions Json = new(FinalArticleExecution.Json) { Converters={new JsonStringEnumConverter()} };
    private static FinalArticleExecution.FileIdentity Identity(string path) => new(Path.GetFullPath(path),FinalArticleExecution.Hash(path));
    private static void WriteNew(string file, object value)
    {
        using var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write);
        JsonSerializer.Serialize(stream,value,Json);stream.Flush(true);
    }

    public static async Task<int> RunAsync(string[] args)
    {
#if DEBUG
        throw new InvalidOperationException("Approximate article workers require Release.");
#endif
        if(args.Length!=4 || args[0]!="--request" || args[2]!="--output")
            throw new ArgumentException("Use final-approximate-start --request FILE --output NEW_DIRECTORY.");
        string requestPath=Path.GetFullPath(args[1]), output=Path.GetFullPath(args[3]);
        if(Directory.Exists(output)) throw new IOException("Approximate start output already exists.");
        if(Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT")!="1") throw new InvalidOperationException("Set DOTNET_PROCESSOR_COUNT=1 before starting this worker.");
        var request=JsonSerializer.Deserialize<Request>(File.ReadAllBytes(requestPath),Json) ?? throw new InvalidDataException("Missing approximate request.");
        FinalArticleExecution.ValidateAuthorization(request.Authorization);
        var spec=request.Case;
        if(!spec.IsExternalImport || spec.CostMultiplier!=1 || spec.FeeRule is not ("american" or "complete") || spec.AlphaP!=spec.AlphaD ||
            request.StartBudget<1 || request.StartIndex<0 || request.StartIndex>=request.StartBudget || !Path.IsPathFullyQualified(request.ClaimsDirectory))
            throw new InvalidDataException("Approximate starts are limited to the declared four ordinary-cost core games and finite budget.");
        using(var decisions=JsonDocument.Parse(File.ReadAllBytes(request.Authorization.ScientificSpecifications.Path)))
        {
            var d=decisions.RootElement;
            int authorizedCap=d.TryGetProperty("ApproximatePivotCap",out var cap) ? cap.GetInt32() : ArticleApproximatePolicy.PivotCap;
            if(request.MaximumPivots!=authorizedCap) throw new InvalidDataException("Pivot cap differs from the resolved specification.");
            if(d.GetProperty("ApproximateStartsPerCore").GetInt32()!=request.StartBudget ||
                d.GetProperty("ApproximateRoundingCutoff").GetDouble()!=request.RoundingCutoff ||
                d.GetProperty("ApproximateGainUnits").GetString()!=request.GainUnits.ToString())
                throw new InvalidDataException("Approximate policy differs from the resolved specification.");
        }
        var policy=new ArticleApproximatePolicy(request.RoundingCutoff,request.GainUnits,request.MaximumPivots);
        var options=FinalArticleCaseFactory.Create(spec);
        var developer=(SequenceForm)await ArticleWorkedPathExtraction.InitializeAsync(options);
        var game=StrategicGameFingerprint.Capture(developer);
        if(game!=request.ExpectedGame) throw new InvalidDataException("Approximate game identity differs from the frozen core game.");
        Directory.CreateDirectory(request.ClaimsDirectory);
        // Key only by game/start: a renamed output or changed request must not
        // silently add an extra search or retry an unsuccessful initialization.
        string claim=Path.Combine(request.ClaimsDirectory,$"{game.CompleteSha256}-start-{request.StartIndex:D5}.json");
        WriteNew(claim,new { Request=Identity(requestPath), Game=game, request.StartIndex, request.StartBudget,
            Output=output, Pid=Environment.ProcessId, ClaimedUtc=DateTime.UtcNow });
        Directory.CreateDirectory(output);
        WriteNew(Path.Combine(output,"started.json"),new { Request=Identity(requestPath), StartedUtc=DateTime.UtcNow,
            GameAssembly=Identity(typeof(LitigGame).Assembly.Location), ReportingAssembly=Identity(typeof(ArticleApproximateCommand).Assembly.Location),
            Game=game, Policy=request, PrimaryEquilibriumCatalog=false, ExactFallbackAllowed=false });
        var timer=System.Diagnostics.Stopwatch.StartNew();
        try
        {
            ArticleApproximateSearch.Attempt attempt;
            using(var stream=new FileStream(Path.Combine(output,"pivot-audit.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))
            {
                var compact=new JsonSerializerOptions(Json) { WriteIndented=false };
                attempt=ArticleApproximateSearch.Run(developer,options,request.StartIndex,policy,
                    pivot => { writer.WriteLine(JsonSerializer.Serialize(pivot,compact));writer.Flush();stream.Flush(true); },
                    prior => WriteNew(Path.Combine(output,"prior.json"),new { request.StartIndex, ActualSeed=1_000_000+request.StartIndex, Probabilities=prior }));
            }
            // Save the selection before replay so a reporting failure preserves all
            // numerical work and never needs another initialization.
            WriteNew(Path.Combine(output,"selection.json"),attempt);
            object validation=null;
            if(attempt.Decision?.Accepted is {} accepted)
            {
                string profile=Path.Combine(output,"approximate-profile.csv");
                File.WriteAllText(profile,string.Join(",",accepted.Probabilities.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))+"\n",new UTF8Encoding(false));
                string actions=Path.Combine(output,"information-set-actions.csv");
                File.WriteAllText(actions,InformationSetActionReport.BuildCsv(developer,1),new UTF8Encoding(false));
                developer.SaveWeightedGameProgressesAfterEachReport=true;
                developer.SavedWeightedGameProgresses.Clear();
                developer.ActionStrategy=ActionStrategies.CurrentProbability;
                var first=await developer.GenerateReportsByPlaying(false);
                string firstCsv=Path.Combine(output,"numeric-report.csv");
                File.WriteAllText(firstCsv,first.csvReports.Single(),new UTF8Encoding(false));
                // An independently initialized reload checks the COMPLETE vector,
                // including off-path information sets, without imposing primary 1e-7.
                var reload=await ArticleWorkedPathExtraction.InitializeAsync(FinalArticleCaseFactory.Create(spec));
                double[] saved=File.ReadAllText(profile).Trim().Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray();
                if(!saved.SequenceEqual(accepted.Probabilities)) throw new InvalidDataException("Saved approximate vector did not round-trip exactly.");
                var fallbacks=ArticleWorkedPathExtraction.LoadProfile(reload,saved);
                if(fallbacks.Count!=0 || !saved.SequenceEqual(reload.GetEquilibriumFromInformationSets())) throw new InvalidDataException("Reload changed the complete approximate vector.");
                int rows=ArticleWorkedPathExtraction.ValidateActionReport(reload,1,actions);
                reload.EvolutionSettings.UseAcceleratedBestResponse=true; reload.EvolutionSettings.UseCurrentStrategyForBestResponse=true;
                reload.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse=false;reload.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting=false;
                reload.CalculateBestResponse(false);
                double[] raw=reload.Status.BestResponseImprovement.ToArray(), gains=policy.Scale(raw,attempt.TerminalUtilityRanges);
                double threshold=attempt.Decision.Reason=="first-below-0.001" ? ArticleApproximatePolicy.EarlyThreshold : ArticleApproximatePolicy.CapThreshold;
                if(!reload.Status.BestResponseReflectsCurrentStrategy || gains.Average()!=accepted.AverageGain || !(gains.Average()<threshold))
                    throw new InvalidDataException("Reload changed the accepted approximate audit.");
                reload.SaveWeightedGameProgressesAfterEachReport=true;reload.ActionStrategy=ActionStrategies.CurrentProbability;
                var replay=await reload.GenerateReportsByPlaying(false);
                string replayCsv=Path.Combine(output,"replayed-report.csv"); File.WriteAllText(replayCsv,replay.csvReports.Single());
                int cells=MultipleEquilibriaStrategyAudit.ValidateReplay(firstCsv,replayCsv);
                var welfare=SavedProfileWelfare.Evaluate(((LitigGameDefinition)reload.GameDefinition).Options,reload.SavedWeightedGameProgresses);
                AgreementToBargainStudy.ExportProfile(reload,options,1,profile,actions,replayCsv,output,fallbacks,
                    () => FinalArticleCaseFactory.Create(spec));
                validation=new { Passed=true, FullBestResponseRawGains=raw, AcceptanceGains=gains, AverageGain=gains.Average(),MaximumGain=gains.Max(),
                    Threshold=threshold,CompleteVectorRoundTripExact=true,ActionRows=rows,NumericReplayCells=cells,Welfare=welfare };
            }
            WriteNew(Path.Combine(output,"result.json"),new { Completed=true, Accepted=attempt.Decision?.Accepted!=null,
                request.Case.Id,request.StartIndex,request.StartBudget,FinishedUtc=DateTime.UtcNow,ElapsedSeconds=timer.Elapsed.TotalSeconds,
                attempt.Decision,Validation=validation,PrimaryEquilibriumCatalog=false,ExactSolves=0,
                Files=Directory.GetFiles(output,"*",SearchOption.AllDirectories).Select(Identity).ToArray() });
            return 0; // A recorded unsuccessful start is an ordinary search outcome.
        }
        catch(Exception ex)
        {
            WriteNew(Path.Combine(output,"worker-failure.json"),new { Error=ex.ToString(),Utc=DateTime.UtcNow,
                ElapsedSeconds=timer.Elapsed.TotalSeconds,AutomaticRetryAllowed=false,ClaimRetained=claim });
            throw;
        }
    }
}
