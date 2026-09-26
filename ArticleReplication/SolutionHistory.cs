using System.Text.Json;
using ACESim;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;
using ACESimBase.GameSolvingSupport.ExactValues;
using ACESimBase.Games.LitigGame.ManualReports;
using LitigCharts;
using static ACESimBase.Games.LitigGame.ManualReports.ArticleEquilibriumPaths;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;

namespace ArticleReplication;

public static class SolutionHistory
{
    public sealed record Header(string Format,string CaseId,int Seed,int Pivots,SetMetadata[] InformationSets);
    public static string PathFor(string root,string id)=>Files.Under(root,"Histories/"+id+".history");
    public sealed class Capture : IDisposable
    {
        readonly SequenceForm game;readonly string path;readonly StreamWriter writer;
        ECTAStrategyDiagnostics<ExactValue>? engine;SetMetadata[] sets=[];int steps,pivots;
        public Capture(SequenceForm game,string path)
        {
            this.game=game;this.path=path;Directory.CreateDirectory(Path.GetDirectoryName(path)!);writer=new StreamWriter(new FileStream(path+".frames",FileMode.CreateNew));
            game.ExactTraceBeforeSolve=tree=>
            {
                engine=new(tree,game.TraceOutcomeUtilities());
                var reference=Describe(game,InformationSetPressureAnalysis.Capture(game,"prior"));int offset=0;
                sets=engine.InformationSetIndices.Select((i,n)=>{var node=game.TraceInformationSets[i].InformationSetNode;var info=reference.InformationSets.Single(s=>s.Key==Key(node,game.GameDefinition));var set=new SetMetadata(n,i,info.Key,info.Number,info.Player,info.Decision,info.Signal,info.SignalValue,info.ExitCommitment,offset,info.Actions.Select(a=>a.Label).ToArray());offset+=set.Actions.Length;return set;}).ToArray();
                Write(new(steps++,"initial-prior",null,0,[],engine.Evaluate(engine.PriorProbabilities)));
            };
            game.ExactTraceAfterPivot=(tree,pivot)=>{var p=engine!.Project(pivot);Write(new(steps++,pivot.Final?"final-pivot":"pivot",pivot,p.FlowResidual,p.PriorCompletedInformationSets,engine.Evaluate(p.Probabilities)));pivots=pivot.Pivot;};
        }
        void Write(PathFrame frame){writer.WriteLine(JsonSerializer.Serialize(frame,CompactJson));writer.Flush();}
        public void Complete(string caseId)
        {
            writer.Flush();writer.Dispose();using var output=new StreamWriter(new FileStream(path,FileMode.CreateNew));output.WriteLine(JsonSerializer.Serialize(new Header("correlated-signals-history-v1",caseId,0,pivots,sets),CompactJson));
            using var input=new StreamReader(path+".frames");string? line;while((line=input.ReadLine())!=null)output.WriteLine(line);
        }
        public void Dispose(){game.ExactTraceBeforeSolve=null;game.ExactTraceAfterPivot=null;writer.Dispose();}
    }
    public static void ExportLegacy(string legacy,string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh history output required.");
        var package=Files.Read<HistoryPackage>(Path.Combine(legacy,"histories.json"));
        foreach(var item in package.Histories)
        {
            var m=Files.Read<PathResult>(Files.Under(legacy,item.Metadata));string stem=item.Metadata[..^".original.json".Length];var relocation=Files.Read<System.Text.Json.Nodes.JsonObject[]>(Files.Under(legacy,stem+".relocation.json"));
            string file=Files.Under(legacy,relocation.Single(r=>r["OriginalPath"]!.GetValue<string>()==m.Frames.Path)["Path"]!.GetValue<string>());
            string dest=PathFor(output,item.CaseId);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);using var writer=new StreamWriter(new FileStream(dest,FileMode.CreateNew));
            writer.WriteLine(JsonSerializer.Serialize(new Header("correlated-signals-history-v1",item.CaseId,m.Seed,m.Pivots,m.InformationSets),CompactJson));using var reader=new StreamReader(file);string? line;while((line=reader.ReadLine())!=null)writer.WriteLine(line);
        }
    }
    sealed class Prepared : Exception { }
    public static async Task Worker(string request)
    {
        if(Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT")!="1")throw new InvalidDataException("Single-thread history worker required.");
        var q=Files.Object(request);string run=q["Run"]!.GetValue<string>(),id=q["CaseId"]!.GetValue<string>(),file=q["History"]!.GetValue<string>(),output=q["Output"]!.GetValue<string>();Directory.CreateDirectory(output);
        var spec=q["Case"]!.Deserialize<FinalArticleCase>(Files.Json)!;var game=(SequenceForm)await ArticleWorkedPathExtraction.InitializeAsync(FinalArticleCaseFactory.Create(spec));
        bool computed=!File.Exists(file);
        if(computed)
        {
            // A saved strategy cannot provide its history; reconstruct only when no history shortcut exists.
            using var capture=new Capture(game,file);game.EvolutionSettings.UseExistingEquilibriaIfAvailable=false;game.EvolutionSettings.CreateEquilibriaFile=false;game.EvolutionSettings.ParallelOptimization=false;game.EvolutionSettings.SequenceFormNumPriorsToUseToGenerateEquilibria=1;game.EvolutionSettings.TryInexactArithmeticForAdditionalEquilibria=false;game.EvolutionSettings.ConsiderInitializingToMostRecentEquilibrium=false;
            game.EvolutionSettings.CustomSequenceFormInitialization=false;game.EvolutionSettings.SequenceFormUseRandomSeed=false;
            await game.RunAlgorithm(game.GameDefinition.OptionSetName);capture.Complete(id);
        }
        using var reader=new StreamReader(file);var header=JsonSerializer.Deserialize<Header>(reader.ReadLine()!,CompactJson)!;
        if(header.Format!="correlated-signals-history-v1"||header.CaseId!=id||header.Seed!=0)throw new InvalidDataException("History identity or initialization differs.");
        ECTAStrategyDiagnostics<ExactValue>? engine=null;
        try{game.TraceECTA<ExactValue>(beforeSolve:tree=>{engine=new(tree,game.TraceOutcomeUtilities());throw new Prepared();});}catch(Prepared){ }
        if(engine==null)throw new InvalidDataException("Cannot initialize current-game history checks.");
        var frames=new List<PathFrame>();string? line;double maximum=0;
        void Check(double a,double b){if(!double.IsFinite(a)||!double.IsFinite(b)||Math.Abs(a-b)>1e-7)throw new InvalidDataException("Saved history diagnostics differ from current-game evaluation.");maximum=Math.Max(maximum,Math.Abs(a-b));}
        while((line=reader.ReadLine())!=null)
        {
            var frame=JsonSerializer.Deserialize<PathFrame>(line,CompactJson)!;var actual=engine.Evaluate(frame.Strategy.Probabilities);var old=frame.Strategy;
            Check(actual.Epsilon,old.Epsilon);
            void Values(double[] a,double[] b){if(a.Length!=b.Length)throw new InvalidDataException("History dimensions differ.");for(int i=0;i<a.Length;i++)Check(a[i],b[i]);}
            if(frame.Native!=null)
            {
                var projection=engine.Project(frame.Native);Values(projection.Probabilities,old.Probabilities);Check(projection.FlowResidual,frame.ProjectionFlowResidual);
                if(!projection.PriorCompletedInformationSets.SequenceEqual(frame.PriorCompletedInformationSets))throw new InvalidDataException("History completion convention changed.");
            }
            Values(actual.UnilateralGains,old.UnilateralGains);Check(actual.NashConv,old.NashConv);Values(actual.CounterfactualReach,old.CounterfactualReach);
            Values(actual.Utilities,old.Utilities);Values(actual.BestResponseUtilities,old.BestResponseUtilities);Values(actual.ActualReach,old.ActualReach);
            void Nullable(double?[] a,double?[] b){if(a.Length!=b.Length)throw new InvalidDataException("History dimensions differ.");for(int i=0;i<a.Length;i++){if(a[i].HasValue!=b[i].HasValue)throw new InvalidDataException("History reach changed.");if(a[i].HasValue)Check(a[i]!.Value,b[i]!.Value);}}
            Nullable(actual.ActionUtilities,old.ActionUtilities);Nullable(actual.ActionAdvantages,old.ActionAdvantages);Nullable(actual.LocalGaps,old.LocalGaps);Nullable(actual.OutsideSupportGaps,old.OutsideSupportGaps);Nullable(actual.SupportSpreads,old.SupportSpreads);frames.Add(frame);
        }
        var audit=Files.Object(Path.Combine(run,"ReportResults/Primary",id,"validation.json"));var profile=Files.Object(Directory.GetFiles(Path.Combine(run,"ReportResults/Primary",id,"Sources/Profiles"),"*.json").Single());var vector=SolveShortcut.Vector(profile);
        var complete=frames[^1].Strategy;if(vector.Length!=complete.Probabilities.Length||vector.Zip(complete.Probabilities).Any(p=>Math.Abs(p.First-p.Second)>1e-10)||complete.Epsilon>1e-7)throw new InvalidDataException("History endpoint differs from the validated complete equilibrium.");
        if(!frames[0].Strategy.Probabilities.SequenceEqual(engine.PriorProbabilities))throw new InvalidDataException("Saved history has a different starting profile.");
        var rows=profile["Strategies"]!.AsArray().ToDictionary(r=>r!["InformationSet"]!.GetValue<int>());
        if(rows.Count!=header.InformationSets.Length||header.InformationSets.Select(s=>s.Number).Distinct().Count()!=rows.Count)throw new InvalidDataException("History omitted or repeated information sets.");
        ArticleWorkedPathExtraction.LoadProfile(game,vector);var reference=Describe(game,InformationSetPressureAnalysis.Capture(game,"validated-endpoint"));int offset=0;
        for(int i=0;i<header.InformationSets.Length;i++)
        {
            var set=header.InformationSets[i];var info=reference.InformationSets.Single(s=>s.Number==set.Number);
            if(set.Index!=i||set.TreeIndex!=engine.InformationSetIndices[i]||set.FirstAction!=offset||set.Key!=info.Key||set.Decision!=info.Decision||set.SignalValue!=info.SignalValue)throw new InvalidDataException("History display coordinates differ from the current game.");offset+=set.Actions.Length;
        }
        foreach(var set in header.InformationSets){var r=rows[set.Number]!;if(set.Player!=r["Player"]!.GetValue<byte>()||set.Signal!=r["Signal"]!.GetValue<int>()||set.ExitCommitment!=r["OwnExit"]?.GetValue<int>()||!set.Actions.SequenceEqual(r["Actions"]!.AsArray().Select(a=>a!.GetValue<string>())))throw new InvalidDataException("History coordinates changed.");}
        var metadata=new PathResult("2",id,game.GameDefinition.OptionSetName,"Uniform",0,true,frames.Count,header.Pivots,header.Pivots,0,[],new(file,Files.Sha(file)),new(typeof(SequenceForm).Assembly.Location,Files.Sha(typeof(SequenceForm).Assembly.Location)),0,header.InformationSets,frames[0].Strategy.Epsilon,complete.Epsilon,[],complete.Epsilon,0,frames.Max(f=>f.ProjectionFlowResidual),0,100000,6,["Recomputed every saved frame's diagnostics from the current full game; complete equilibrium endpoint revalidated. No pivot algebra equivalence claim."]);
        EquilibriumPathAnimation.ValidateFrames(metadata,frames.ToArray());File.WriteAllText(Path.Combine(output,id+".html"),EquilibriumPathAnimation.BuildHtml([new(metadata,frames.ToArray())]));
        Files.Save(Path.Combine(output,"validation.json"),new{Passed=true,CaseId=id,Pivots=header.Pivots,FramesChecked=frames.Count,CompletePolicyEntries=vector.Length,MaximumDiagnosticDifference=maximum,InputHashRequired=false,SolvesStarted=computed?1:0});
    }
}
