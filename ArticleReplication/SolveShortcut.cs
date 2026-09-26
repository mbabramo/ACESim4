using System.Text.Json.Nodes;
using ACESimBase.Games.LitigGame.ManualReports;

namespace ArticleReplication;

/// <summary>A saved solve, not a saved report or validation certificate.</summary>
public sealed record SolveShortcut(string CaseId,string Kind,string Status,int StartIndex,int PriorSeed,
    int MaximumPivots,int StoppingPivot,string Reason,double Cutoff,string GainUnits,double[] Probabilities)
{
    public string Format {get;init;}="correlated-signals-equ-v1";
    public static string PrimaryPath(string root,string id)=>Files.Under(root,"Equilibria/"+id+".equ");
    public static string SearchPath(string root,string id,int start)=>Files.Under(root,$"Search/{id}/start-{start:D5}.equ");
    public static SolveShortcut Primary(string id,double[] vector)=>new(id,"ExactPrimary","Equilibrium",0,0,0,0,"exact-complete",0,"NotApplicable",vector);
    public void Check(string id,string kind,int start,CorrelatedSignalsSettings settings)
    {
        if(Format!="correlated-signals-equ-v1"||CaseId!=id||Kind!=kind||StartIndex!=start||
            Status is not ("Equilibrium" or "NoEquilibriumFound")||Probabilities==null||
            (Status=="Equilibrium")!=(Probabilities.Length>0)||Probabilities.Any(p=>!double.IsFinite(p)||p<0||p>1))
            throw new InvalidDataException("Incompatible or malformed saved solve: "+id);
        if(kind=="ExactPrimary")
        {
            if(Status!="Equilibrium"||PriorSeed!=0||start!=0||MaximumPivots!=0||StoppingPivot!=0||Cutoff!=0||GainUnits!="NotApplicable"||Reason!="exact-complete")throw new InvalidDataException("Expected a complete seed-zero exact primary solve.");
        }
        else if(kind=="ApproximateStart")
        {
            if(PriorSeed!=1_000_000+start||Cutoff!=settings.ApproximateRoundingCutoff||GainUnits!=settings.ApproximateGainUnits.ToString()||MaximumPivots<1||StoppingPivot<0||StoppingPivot>MaximumPivots)
                throw new InvalidDataException("Saved attempt does not match the requested search policy.");
            bool early=Status=="Equilibrium"&&Reason=="first-below-0.001";
            if(MaximumPivots!=settings.ApproximatePivotLimit&&!(early&&StoppingPivot<=settings.ApproximatePivotLimit))throw new InvalidDataException("Saved attempt has a different search budget.");
            if(Status=="Equilibrium"&&(!early&&(Reason!="cap-accepted"||StoppingPivot!=MaximumPivots)))throw new InvalidDataException("Invalid accepted search stopping record.");
            if(Status=="Equilibrium"&&StoppingPivot==0)throw new InvalidDataException("Accepted search must identify a pivot.");
            if(Status=="NoEquilibriumFound"&&Reason is not ("cap-unsuccessful" or "algorithm-ended-without-early-threshold" or "algorithm-error-before-cap"))throw new InvalidDataException("Unknown failed search reason.");
            if(Status=="NoEquilibriumFound"&&Reason=="cap-unsuccessful"&&StoppingPivot!=MaximumPivots)throw new InvalidDataException("Incomplete unsuccessful capped attempt.");
        }
        else throw new InvalidDataException("Unknown saved solve kind.");
    }
    public static SolveShortcut Read(string path,string id,string kind,int start,CorrelatedSignalsSettings settings)
    {var r=Files.Read<SolveShortcut>(path);r.Check(id,kind,start,settings);return r;}
    public bool MatchesSearchPolicy(string id,int start,CorrelatedSignalsSettings settings)
    {
        // First reject malformed records under their own stated policy. A valid but
        // different policy is simply not a shortcut for the requested calculation.
        if(!Enum.TryParse<ArticleApproximateGainUnits>(GainUnits,out var units)||!Enum.IsDefined(units))throw new InvalidDataException("Unknown saved gain units.");
        _=new ArticleApproximatePolicy(Cutoff,units,MaximumPivots);
        Check(id,"ApproximateStart",start,settings with{ApproximateRoundingCutoff=Cutoff,ApproximateGainUnits=units,ApproximatePivotLimit=MaximumPivots});
        return Cutoff==settings.ApproximateRoundingCutoff&&units==settings.ApproximateGainUnits&&
            (MaximumPivots==settings.ApproximatePivotLimit||(Status=="Equilibrium"&&Reason=="first-below-0.001"&&StoppingPivot<=settings.ApproximatePivotLimit));
    }
    public void Save(string path)=>Files.Save(path,this);

    public static void Export(string run,string output)
    {
        if(Directory.Exists(output))throw new IOException("Choose a fresh shortcuts directory.");
        var plan=Files.Read<ResolvedArticlePlan>(Path.Combine(run,"resolved-plan.json"));
        var validation=Files.Object(Path.Combine(run,"primary-validation.json"));if(validation["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Primary validation required.");
        foreach(var row in validation["Profiles"]!.AsArray())
        {
            string id=row!["CaseId"]!.GetValue<string>(),dir=Path.Combine(run,"ReportResults/Primary",id);
            if(Files.Object(Path.Combine(dir,"validation.json"))["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Failed primary profile.");
            var profile=Files.Object(Directory.GetFiles(Path.Combine(dir,"Sources/Profiles"),"*.json").Single());
            Primary(id,Vector(profile)).Save(PrimaryPath(output,id));
        }
        string search=Path.Combine(run,"ReportResults/MultipleStarts/completed.json");if(!File.Exists(search))return;
        var completed=Files.Object(search);if(completed["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Incomplete search.");
        foreach(var row in completed["Results"]!.AsArray())
        {
            string id=row!["CaseId"]!.GetValue<string>();int start=row["StartIndex"]!.GetValue<int>();bool accepted=row["Accepted"]!.GetValue<bool>();
            string dir=Path.Combine(run,"ReportResults/MultipleStarts",id,$"start-{start:D5}");
            SolveShortcut record;
            if(File.Exists(Path.Combine(dir,"solve.equ")))record=Read(Path.Combine(dir,"solve.equ"),id,"ApproximateStart",start,plan.Settings);
            else
            {
                var decision=row["Decision"];int pivot=accepted?row["StoppingPivot"]!.GetValue<int>():decision!["StoppingPivot"]!.GetValue<int>();
                string reason=accepted?(row["Threshold"]!.GetValue<double>()==.001?"first-below-0.001":"cap-accepted"):decision!["Reason"]!.GetValue<string>();
                double[] vector=accepted?Vector(Files.Object(Directory.GetFiles(Path.Combine(dir,"Sources/Profiles"),"*.json").Single())):[];
                record=new(id,"ApproximateStart",accepted?"Equilibrium":"NoEquilibriumFound",start,1_000_000+start,row["RecordedPivotCap"]?.GetValue<int>()??plan.Settings.ApproximatePivotLimit,pivot,reason,plan.Settings.ApproximateRoundingCutoff,plan.Settings.ApproximateGainUnits.ToString(),vector);
            }
            record.Check(id,"ApproximateStart",start,plan.Settings);record.Save(SearchPath(output,id,start));
        }
    }
    public static double[] Vector(JsonObject profile)=>profile["Strategies"]!.AsArray().SelectMany(p=>p!["Probabilities"]!.AsArray().Select(v=>v!.GetValue<double>())).ToArray();
}
