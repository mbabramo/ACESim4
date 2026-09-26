using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using LitigCharts;

internal static class Tremble
{
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
    static void Save(string p,object x){using var s=new FileStream(p,FileMode.CreateNew);JsonSerializer.Serialize(s,x,Json);s.Flush(true);}
    static void Check(bool b,string m){if(!b)throw new InvalidDataException(m);}
    static void Near(double a,double b,double tolerance,string m)=>Check(double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=tolerance,m+$": {a:R} != {b:R}");
    static double TV(double[] a,double[] b)=>a.Zip(b,(x,y)=>Math.Abs(x-y)).Sum()/2;
    sealed record Outcome(double[] Welfare,double NotFiled,double NotAnswered,double Settlement,double Abandonment,double Default,double Trial);
    static async Task<Outcome> Replay(StrategiesDeveloperBase d,LitigGameOptions o)
    {
        d.SavedWeightedGameProgresses.Clear();
        await d.GenerateReportsByPlaying(false);
        var w=SavedProfileWelfare.Evaluate(o,d.SavedWeightedGameProgresses).Headline;
        double Sum(Func<LitigGameProgress,bool> f)=>d.SavedWeightedGameProgresses.Where(x=>f((LitigGameProgress)x.theProgress)).Sum(x=>x.weight);
        var result=new Outcome(new[]{w.MeritoriousPlaintiffShortfall,w.NonliableDefendantBurden,w.LiableDefendantExcessBurden,w.GrossOutcomeError,w.RealLitigationExpenditures},
            Sum(p=>!p.PFiles),Sum(p=>p.PFiles&&!p.DAnswers),Sum(p=>p.CaseSettles),Sum(p=>p.PAbandons),Sum(p=>p.DDefaults),Sum(p=>p.TrialOccurs));
        Near(result.NotFiled+result.NotAnswered+result.Settlement+result.Abandonment+result.Default+result.Trial,1,1e-9,"disposition accounting");
        d.SavedWeightedGameProgresses.Clear();return result;
    }
    static double[] Select(double[] original,double[] values,double tol)
    {
        var good=values.Select(v=>values.Max()-v<=tol).ToArray();double mass=original.Where((v,i)=>good[i]).Sum();int n=good.Count(x=>x);
        return original.Select((v,i)=>!good[i]?0:mass>0?v/mass:1.0/n).ToArray();
    }
    static void SelfTest()
    {
        Check(Select(new[]{.3,.7},new[]{1.0,1.0},1e-10).SequenceEqual(new[]{.3,.7}),"tie retains mixed policy");
        Check(Select(new[]{.3,.7},new[]{1.0,2.0},1e-10).SequenceEqual(new[]{0.0,1.0}),"unique optimum");
        Check(Select(new[]{1.0,0.0,0.0},new[]{0.0,1.0,1.0},1e-10).SequenceEqual(new[]{0.0,.5,.5}),"zero support tie");
        Near(TV(new[]{1.0,0.0},new[]{0.0,1.0}),1,0,"TV");
    }
    public static async Task Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;SelfTest();ACESimBase.Util.Debugging.TabbedText.DisableOutput();
        Check(Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT")=="1","single-thread worker required");
        using var doc=JsonDocument.Parse(File.ReadAllBytes(args[0]));var r=doc.RootElement;
        var spec=r.GetProperty("Case").Deserialize<FinalArticleCase>(FinalArticleExecution.Json);
        string output=r.GetProperty("Output").GetString();Check(!Directory.Exists(output),"fresh output required");Directory.CreateDirectory(output);
        Save(Path.Combine(output,"started.json"),new{StartedUtc=DateTime.UtcNow,Pid=Environment.ProcessId,Request=args[0],RequestHash=Hash(args[0]),HostHash=Hash(typeof(Tremble).Assembly.Location),GameHash=Hash(typeof(LitigGame).Assembly.Location),SolvesStarted=0});
        var clock=Stopwatch.StartNew();var o=FinalArticleCaseFactory.Create(spec);var d=await ArticleWorkedPathExtraction.InitializeAsync(o);
        var game=StrategicGameFingerprint.Capture(d);Check(game==r.GetProperty("ExpectedGame").Deserialize<StrategicGameFingerprint.Snapshot>(),"full game fingerprint");
        d.EvolutionSettings.UseAcceleratedBestResponse=true;d.EvolutionSettings.UseCurrentStrategyForBestResponse=true;
        d.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse=false;d.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting=false;
        d.EvolutionSettings.ParallelOptimization=false;d.SaveWeightedGameProgressesAfterEachReport=true;d.ActionStrategy=ActionStrategies.CurrentProbability;
        var nodes=d.InformationSets.OrderBy(n=>n.PlayerIndex).ThenBy(n=>n.InformationSetNodeNumber).ToArray();
        var ranges=Enumerable.Range(0,2).Select(p=>d.FinalUtilitiesNodes.Max(n=>n.Utilities[p])-d.FinalUtilitiesNodes.Min(n=>n.Utilities[p])).ToArray();
        var directions=Enumerable.Range(0,r.GetProperty("Directions").GetInt32()).Select(k=>nodes.Select(n=>{
            var rng=new Random(20260925+k*10000+n.InformationSetNodeNumber);
            var v=Enumerable.Range(0,n.NumPossibleActions).Select(_=>k==0?1.0:-Math.Log(Math.Max(rng.NextDouble(),1e-15))).ToArray();var sum=v.Sum();return v.Select(x=>x/sum).ToArray();
        }).ToArray()).ToArray();
        Save(Path.Combine(output,"game-and-directions.json"),new{Game=game,TerminalUtilityRanges=ranges,Nodes=nodes.Select(n=>new{n.PlayerIndex,n.InformationSetNodeNumber,n.Decision.Name,n.NumPossibleActions}),Directions=directions,InitializedSeconds=clock.Elapsed.TotalSeconds});
        int checks=0;
        foreach(var profile in r.GetProperty("Profiles").EnumerateArray())
        {
            string id=profile.GetProperty("Id").GetString(),file=profile.GetProperty("FrozenFile").GetString();
            Check(Hash(file)==profile.GetProperty("FrozenSha256").GetString(),"profile hash");
            var values=File.ReadAllText(file).Trim().Split(',').Select(ACESimBase.Games.EFGFileGame.EFGFileReader.RationalStringToDouble).ToArray();
            Check(ArticleWorkedPathExtraction.LoadProfile(d,values).Count==0,"complete original strategy");Check(values.SequenceEqual(d.GetEquilibriumFromInformationSets()),"original vector roundtrip");
            var original=nodes.Select(n=>n.GetCurrentProbabilitiesAsArray()).ToArray();
            d.CalculateBestResponse(false);var baselineGain=d.Status.BestResponseImprovement.ToArray();var baselineReach=nodes.Select(n=>n.SelfReachProbability*n.OpponentsReachProbability).ToArray();
            var expected=profile.GetProperty("RawGains").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
            for(int p=0;p<2;p++)Near(baselineGain[p],expected[p],1e-9,"unchanged baseline full BR");
            Outcome baselineOutcome=await Replay(d,o);var fields=new[]{"MeritoriousPlaintiffShortfall","NonliableDefendantBurden","LiableDefendantExcessBurden","GrossOutcomeError","RealLitigationExpenditures"};
            for(int f=0;f<5;f++)Near(baselineOutcome.Welfare[f],profile.GetProperty("Welfare").GetProperty(fields[f]).GetDouble(),1e-10,"baseline welfare replay");
            string directory=Path.Combine(output,id);Directory.CreateDirectory(directory);
            Save(Path.Combine(directory,"baseline.json"),new{Id=id,Kind=profile.GetProperty("Kind").GetString(),Source=profile,BaselineRawGain=baselineGain,Outcome=baselineOutcome});
            for(byte player=0;player<2;player++)
            {
                double[][] baselineResponse=null;Outcome baselineResponseOutcome=null;
                var settings=new List<(int direction,double epsilon)>{(0,0)};
                foreach(int k in Enumerable.Range(0,directions.Length))foreach(var eps in r.GetProperty("Epsilons").EnumerateArray())settings.Add((k,eps.GetDouble()));
                foreach(var (direction,epsilon) in settings)
                {
                    for(int i=0;i<nodes.Length;i++)nodes[i].SetCurrentProbabilities(nodes[i].PlayerIndex==player?original[i]:original[i].Select((v,a)=>(1-epsilon)*v+epsilon*directions[direction][i][a]).ToArray());
                    var perturbed=nodes.Select(n=>n.GetCurrentProbabilitiesAsArray()).ToArray();
                    for(int i=0;i<nodes.Length;i++){Near(perturbed[i].Sum(),1,1e-10,"perturbed normalization");if(nodes[i].PlayerIndex!=player&&epsilon>0)Check(perturbed[i].All(x=>x>0),"full support tremble");}
                    d.CalculateBestResponse(false);double utilityBefore=d.Status.UtilitiesOverall[player],brValue=d.Status.BestResponseUtilities[player],raw=d.Status.BestResponseImprovement[player];
                    double[] reach=nodes.Select(n=>n.SelfReachProbability*n.OpponentsReachProbability).ToArray();
                    var actionValues=nodes.Select(n=>n.BestResponseOptions.ToArray()).ToArray();var legacyActions=nodes.Select(n=>n.BestResponseAction).ToArray();
                    var selected=nodes.Select((n,i)=>n.PlayerIndex==player?Select(original[i],actionValues[i],1e-10*ranges[player]):perturbed[i]).ToArray();
                    if(epsilon==0)baselineResponse=selected.Select(x=>x.ToArray()).ToArray();
                    var detail=nodes.Select((n,i)=>new{n.PlayerIndex,n.InformationSetNodeNumber,n.Decision.Name,Original=original[i],Perturbed=perturbed[i],Selected=selected[i],ActionValues=actionValues[i],LegacyAction=legacyActions[i],OriginalReach=baselineReach[i],PerturbedReach=reach[i],TVFromOriginal=TV(original[i],selected[i]),TVFromZeroTrembleResponse=TV(baselineResponse[i],selected[i])}).Where(x=>x.PlayerIndex==player).ToArray();
                    for(int i=0;i<nodes.Length;i++)nodes[i].SetCurrentProbabilities(selected[i]);
                    d.CalculateBestResponse(false);double responseUtility=d.Status.UtilitiesOverall[player],selectionGap=d.Status.BestResponseImprovement[player]/ranges[player];
                    Near(d.Status.BestResponseUtilities[player],brValue,1e-9,"own policy cannot change opponent-fixed BR value");
                    Check(selectionGap>=-1e-9&&selectionGap<=1e-8,"tie-aware response verification");
                    Outcome responseOutcome=await Replay(d,o);if(epsilon==0)baselineResponseOutcome=responseOutcome;
                    double Weighted(bool usePerturbed){double den=detail.Sum(x=>usePerturbed?x.PerturbedReach:x.OriginalReach);return den==0?0:detail.Sum(x=>(usePerturbed?x.PerturbedReach:x.OriginalReach)*x.TVFromZeroTrembleResponse)/den;}
                    var off=detail.Where(x=>x.OriginalReach==0).ToArray();
                    Save(Path.Combine(directory,$"p{player}-d{direction}-e{epsilon.ToString("0.000",CultureInfo.InvariantCulture)}.json"),new{Player=player,Direction=direction,Epsilon=epsilon,RawGain=raw,NormalizedGain=Math.Max(0,raw)/ranges[player],BaselineNormalizedGain=Math.Max(0,baselineGain[player])/ranges[player],IncrementalNormalizedGain=(Math.Max(0,raw)-Math.Max(0,baselineGain[player]))/ranges[player],UtilityBefore=utilityBefore,BestResponseUtility=brValue,SelectedResponseUtility=responseUtility,SelectionGapNormalized=selectionGap,OriginalReachWeightedTV=Weighted(false),PerturbedReachWeightedTV=Weighted(true),MaximumTV=detail.Max(x=>x.TVFromZeroTrembleResponse),OffPathMeanTV=off.Length==0?0:off.Average(x=>x.TVFromZeroTrembleResponse),ResponseOutcome=responseOutcome,BaselineResponseOutcome=baselineResponseOutcome,InformationSets=detail});
                    checks++;
                }
            }
            ArticleWorkedPathExtraction.LoadProfile(d,values);Check(values.SequenceEqual(d.GetEquilibriumFromInformationSets()),"restored original");
            Save(Path.Combine(directory,"completed.json"),new{Passed=true,CompletedUtc=DateTime.UtcNow,SolvesStarted=0});Console.WriteLine($"Completed {id}; {clock.Elapsed.TotalSeconds:F1}s");
        }
        Check(game==StrategicGameFingerprint.Capture(d),"game unchanged after diagnostics");
        Save(Path.Combine(output,"completed.json"),new{Passed=true,FinishedUtc=DateTime.UtcNow,Profiles=r.GetProperty("Profiles").GetArrayLength(),Checks=checks,ElapsedSeconds=clock.Elapsed.TotalSeconds,SolvesStarted=0});
    }
}
