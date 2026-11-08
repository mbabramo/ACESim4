using ACESim;
using ACESimBase;
using ACESimBase.GameSolvingSupport.Evaluation;
using ACESimBase.GameSolvingSupport.FastCFR;
using ACESimBase.GameSolvingSupport.Policies;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.Util.Debugging;
using System.Diagnostics;
using System.Reflection;
using static ArticleReplication.PopulationExperiment;

namespace ArticleReplication;

/// <summary>Independent starts of the article's generalized regret-matching CFR method.
/// No Gini/Liu selection, pooling, saved-equilibrium imports or numerical fallback.</summary>
public static class EndogenousTraditionalExperiment
{
    public sealed record Plan(string Schema, string InputDirectory, string CatalogSha256, string CaseId,
        string OutputDirectory, int Repetition, int Seed, int Iterations, int CheckEvery,
        double NormalizedNashTarget, double EconomicScreenFractionOfBenefit, string Initialization,
        string Flavor, Dictionary<string,string> RuntimeSha256);

    public static async Task Run(string planPath)
    {
        var plan = Files.Read<Plan>(planPath);
        if (plan.Schema != "endogenous-traditional-job-v1" || plan.Seed < 0 || plan.Repetition < 1 ||
            plan.Iterations < 3 || plan.CheckEvery < 1 || plan.Iterations % plan.CheckEvery != 0 ||
            !double.IsFinite(plan.NormalizedNashTarget) || plan.NormalizedNashTarget <= 0 ||
            !double.IsFinite(plan.EconomicScreenFractionOfBenefit) || plan.EconomicScreenFractionOfBenefit <= 0 ||
            plan.Initialization is not ("uniform" or "dirichlet-one") ||
            plan.Flavor is not ("Fast" or "Regular" or "Unrolled"))
            throw new InvalidDataException("Invalid independent-start plan.");
        if (Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT") != "1" || Environment.ProcessorCount != 1)
            throw new InvalidOperationException("Each traditional worker must have DOTNET_PROCESSOR_COUNT=1.");
        string runtime = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        foreach (var (name,hash) in plan.RuntimeSha256)
            if (Files.Sha(Files.Under(runtime,name)) != hash) throw new InvalidDataException("Runtime changed: " + name);
        if (Files.Sha(Path.Combine(plan.InputDirectory,"catalog.json")) != plan.CatalogSha256)
            throw new InvalidDataException("Catalog changed.");
        var catalog = EndogenousPopulationExperiment.LoadCatalog(plan.InputDirectory);
        var spec = catalog.Cases.Single(c=>c.Id==plan.CaseId);
        if (Files.Sha(spec.SavedProfile)!=spec.SavedProfileSha256 || Files.Sha(spec.SourceLog)!=spec.SourceLogSha256)
            throw new InvalidDataException("Historical source identity changed.");
        string output=Path.GetFullPath(plan.OutputDirectory);
        if (Directory.Exists(output) || Files.Nested(output,plan.InputDirectory) || Files.Nested(plan.InputDirectory,output))
            throw new IOException("Use a fresh output directory separate from inputs.");
        Directory.CreateDirectory(output);
        Files.CopyVerified(planPath,Path.Combine(output,"plan.json"));
        var started=DateTime.UtcNow;var watch=Stopwatch.StartNew();
        Console.WriteLine($"Started {spec.Id} repetition {plan.Repetition} seed {plan.Seed} ({plan.Initialization})");
        TabbedText.DisableOutput();TabbedText.WriteToConsole=false;TabbedText.DisableConsoleProgressString=true;
        var settings=new EvolutionSettings
        {
            Algorithm=GameApproximationAlgorithm.GeneralizedVanilla,
            GeneralizedVanillaFlavor=Enum.Parse<GeneralizedVanillaFlavor>(plan.Flavor),
            TotalIterations=plan.Iterations,BestResponseEveryMIterations=plan.CheckEvery,
            ReportEveryNIterations=null,MiniReportEveryPIterations=EvolutionSettings.EffectivelyNever,
            ParallelOptimization=false,DynamicSetParallel=false,MaxParallelDepth=0,
            UseExistingEquilibriaIfAvailable=false,CreateEquilibriaFile=false,CreateEFGFile=false,
            GenerateManualReports=false,GenerateReportsByPlaying=false,
            CalculatePerturbedBestResponseRefinement=false,
            RoundOffLowProbabilitiesBeforeAcceleratedBestResponse=false,RoundOffLowProbabilitiesBeforeReporting=false,
            UseCurrentStrategyForBestResponse=false,RememberBestResponseExploitability=true
        };
        var developer=(GeneralizedVanilla)await EndogenousPopulationExperiment.Initialize(spec,settings);
        if (settings.Algorithm!=GameApproximationAlgorithm.GeneralizedVanilla || settings.TotalIterations!=plan.Iterations ||
            settings.ParallelOptimization || settings.UseExistingEquilibriaIfAvailable)
            throw new InvalidDataException("Case modified frozen solver settings.");
        var settingsSnapshot=typeof(EvolutionSettings).GetFields(BindingFlags.Public|BindingFlags.Instance)
            .Where(f=>f.FieldType.IsPrimitive||f.FieldType.IsEnum||f.FieldType==typeof(string)||Nullable.GetUnderlyingType(f.FieldType)?.IsPrimitive==true)
            .OrderBy(f=>f.Name).ToDictionary(f=>f.Name,f=>f.GetValue(settings));
        Files.Save(Path.Combine(output,"settings.json"),new { Fields=settingsSnapshot,
            Algorithm="Existing generalized vanilla CFR / regret matching, double precision",
            Selection="Existing best-average-exploitability checkpoint rule; primary output is that checkpoint's current behavioral profile, as in the legacy equilibrium file.",
            DifferencesFromLegacyReporting="Explicit random initial behavior; unrounded checks; no report/file reuse; report generation disabled. Update equations are unchanged." });
        var tree=PolicyGameTree.Create(developer);
        var fingerprint=StrategicGameFingerprint.Capture(developer,roundChanceProbabilities:false);
        Files.Save(Path.Combine(output,"game.json"),new { Case=spec,Fingerprint=fingerprint,tree.UtilityMinimum,tree.UtilityScale,
            tree.ChanceBoundaryCorrections,tree.MaximumChanceBoundaryCorrection });
        var random=new Random(plan.Seed);
        foreach (var node in developer.InformationSets)
        {
            var probabilities=Enumerable.Range(0,node.NumPossibleActions).Select(_=>plan.Initialization=="uniform" ? 1.0 : -Math.Log(Math.Max(1e-15,random.NextDouble()))).ToArray();
            double sum=probabilities.Sum();for(int i=0;i<probabilities.Length;i++)probabilities[i]/=sum;
            node.SetCurrentAndAverageProbabilities(probabilities);
        }
        var initial=PolicyAccess.CaptureCurrent(developer,"independent-start");
        var initialArchive=ArchiveProfile(tree,fingerprint.CompleteSha256,initial);
        PopulationArchiveIO.Save(Path.Combine(output,"initial-population.json"),initialArchive,null);
        Files.Save(Path.Combine(output,"initialization.json"),new { plan.Seed,plan.Repetition,plan.Initialization,
            Algorithm="System.Random(int seed); action weights -log(max(1e-15,U)), normalized within each information set; information sets in recorded layout order. Zero initial regrets, initial strategy tallies equal the initial probabilities.",
            ArchiveSha256=Files.Sha(PopulationArchiveIO.PhysicalPath(Path.Combine(output,"initial-population.json"))),
            SemanticSha256=Files.Digest(initialArchive),GiniOrLiuUsed=false,SavedEquilibriumUsed=false });
        double setupSeconds=watch.Elapsed.TotalSeconds;
        await developer.RunInitializedIterations();
        double solveSeconds=watch.Elapsed.TotalSeconds-setupSeconds;
        var current=PolicyAccess.CaptureCurrent(developer,"traditional-current");
        var average=PolicyAccess.CaptureAverage(developer,"traditional-average-diagnostic");
        var currentAudit=Audit(developer,tree,current,spec,plan);
        var averageAudit=Audit(developer,tree,average,spec,plan);
        PolicyAccess.ApplyCurrent(developer,current);
        var currentValues=tree.Evaluate(current.ForPlayer(0),current.ForPlayer(1));
        var options=EndogenousPopulationExperiment.OptionsFor(spec);
        var outcomes=EndogenousPopulationExperiment.Evaluate(developer,tree,options,[current.ForPlayer(0)],[current.ForPlayer(1)],[1],currentValues);
        var m=outcomes.Measures;
        if (Math.Abs(outcomes.TotalProbability-1)>1e-9 || Math.Abs(m["SocialWelfareLoss"]-m["ForegoneActivityCost"]-m["PrecautionCost"]-m["HarmCost"]-m["LitigationExpenditures"])>1e-10)
            throw new ArithmeticException("Outcome accounting failed.");
        PopulationArchiveIO.Save(Path.Combine(output,"population.json"),ArchiveProfile(tree,fingerprint.CompleteSha256,current),null);
        PopulationArchiveIO.Save(Path.Combine(output,"average-population.json"),ArchiveProfile(tree,fingerprint.CompleteSha256,average),null);
        Files.SaveCompressed(Path.Combine(output,"training-state.json"),new { Schema="traditional-cfr-final-state-v1",Iteration=developer.Status.IterationNum,
            BestIteration=developer.BestIteration,BestExploitability=developer.BestExploitability,
            Nodes=developer.InformationSets.Select(n=>new { Key=PolicyAccess.Key(n,developer.GameDefinition),
                Values=Enumerable.Range(0,n.NodeInformation.GetLength(0)).Select(d=>Enumerable.Range(0,n.NodeInformation.GetLength(1)).Select(a=>n.NodeInformation[d,a]).ToArray()).ToArray() }).ToArray(),
            Note="Auditable final node arrays, not a promise of a resumable mid-iteration checkpoint. Interrupted jobs retain their attempts and restart from the recorded initial seed." });
        Files.Save(Path.Combine(output,"audit.json"),new { Passed=true,Primary=currentAudit,AverageDiagnostic=averageAudit,
            PrimarySemanticSha256=Files.Digest(ArchiveProfile(tree,fingerprint.CompleteSha256,current)),
            Concept="Independent player behavior: unrestricted unilateral best-response checks establish approximate Nash, not CCE-only feasibility." });
        Files.Save(Path.Combine(output,"result.json"),new { CaseId=spec.Id,plan.Repetition,plan.Seed,
            SolutionConcept="independent behavioral approximate Nash candidate",Current=currentAudit,AverageDiagnostic=averageAudit,Outcomes=outcomes,
            Iterations=developer.Status.IterationNum,BestIteration=developer.BestIteration,
            Timing=new { SetupSeconds=setupSeconds,SolveSeconds=solveSeconds,TotalSeconds=watch.Elapsed.TotalSeconds } });
        var evidence=Directory.GetFiles(output).OrderBy(Path.GetFileName).ToDictionary(p=>Path.GetFileName(p)!,p=>Files.Sha(p));
        Files.Save(Path.Combine(output,"completed.json"),new { Passed=true,ComputationCompleted=true,CaseId=spec.Id,plan.Repetition,plan.Seed,
            StartedUtc=started,FinishedUtc=DateTime.UtcNow,Files=evidence,
            Note="Completion verifies execution, provenance, oracle agreement and accounting. Nash/economic acceptance is separately reported in result.json; a fixed iteration budget does not guarantee convergence." });
        Console.WriteLine($"Completed {spec.Id} seed {plan.Seed} in {watch.Elapsed.TotalSeconds:F2}s; best checkpoint {developer.BestIteration}");
    }

    private static Archive ArchiveProfile(PolicyGameTree tree,string hash,BehavioralProfile profile) => new(
        "population-policy-archive-v1",hash,tree.Layout.Select(s=>s.Key).ToArray(),
        Enumerable.Range(0,2).Select(p=>new SavedPolicy(profile.Name,(byte)p,tree.Layout.Where(s=>s.Player==p)
            .Select(s=>profile.InformationSets[s.Key].Probabilities.ToArray()).ToArray())).ToArray(),[]);

    private static object Audit(StrategiesDeveloperBase developer,PolicyGameTree tree,BehavioralProfile profile,
        EndogenousPopulationExperiment.Case spec,Plan plan)
    {
        PolicyAccess.ApplyCurrent(developer,profile);
        var values=tree.Evaluate(profile.ForPlayer(0),profile.ForPlayer(1));
        var br=Enumerable.Range(0,2).Select(p=>tree.BestResponse((byte)p,[profile.ForPlayer((byte)(1-p))],[1]).Utility).ToArray();
        var original=Enumerable.Range(0,2).Select(p=>developer.CalculateBestResponse((byte)p,ActionStrategies.CurrentProbability)).ToArray();
        var differences=br.Zip(original,(a,b)=>Math.Abs(a-b)).ToArray();
        if (differences.Any(x=>!double.IsFinite(x)||x>1e-8))throw new ArithmeticException("Independent GEBR and policy-tree best responses disagree.");
        var raw=br.Zip(values,(b,v)=>b-v).Take(2).ToArray();
        var normalized=raw.Select((g,p)=>Math.Max(0,g)/tree.UtilityScale[p]).ToArray();
        if(values.Any(v=>!double.IsFinite(v))||raw.Any(v=>!double.IsFinite(v))||normalized.Any(v=>!double.IsFinite(v)))
            throw new ArithmeticException("Nonfinite Nash audit.");
        var alpha=spec.Parameters["Risk Aversion"] switch
        {
            "Risk Neutral"=>new[]{0.0,0.0},"Mildly Risk Averse"=>new[]{1.0,1.0},"Moderately Risk Averse"=>new[]{2.0,2.0},
            "Highly Risk Averse"=>new[]{4.0,4.0},"P Risk Averse"=>new[]{2.0,0.0},"D Risk Averse"=>new[]{0.0,2.0},
            "P More Risk Averse"=>new[]{4.0,2.0},"D More Risk Averse"=>new[]{2.0,4.0},_=>throw new InvalidDataException("Unknown risk utility.")
        };
        var ce=raw.Select((g,p)=>CertaintyEquivalentGain(values[p],Math.Max(0,g),alpha[p])).ToArray();
        return new { RawNashGains=raw,NormalizedNashGains=normalized,ExpectedUtilities=values,BestResponseUtilities=br,
            IndependentGebrUtilities=original,IndependentOracleDifferences=differences,
            NashAtTarget=normalized.Max()<=plan.NormalizedNashTarget,NashAt1eMinus7=normalized.Max()<=1e-7,NashAt1eMinus5=normalized.Max()<=1e-5,
            CertaintyEquivalentGains=ce,ActivityBenefit=.001,CeGainFractionsOfActivityBenefit=ce.Select(x=>x/.001).ToArray(),
            EconomicScreenPassed=ce.All(x=>x.HasValue&&x.Value<=plan.EconomicScreenFractionOfBenefit*.001),
            EconomicScreenFractionOfBenefit=plan.EconomicScreenFractionOfBenefit,
            Qualification="Economic screen is reported separately from legacy normalized Nash grades. It is not a bound on social-cost error; in misperception cases money equivalents concern perceived payoffs." };
    }

    public static double? CertaintyEquivalentGain(double utility,double gain,double alpha)
    {
        if(gain==0)return 0;
        if(alpha==0)return gain;
        double scale=10/(Math.Exp(2*alpha)-1),denominator=scale+(1000-utility)-gain;
        if(denominator<=0)return null;
        double x=gain/denominator;
        return (Math.Abs(x)<1e-4 ? x-x*x/2+x*x*x/3-x*x*x*x/4 : Math.Log(1+x))/alpha;
    }
}
