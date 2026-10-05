namespace ArticleReplication;

/// <summary>Outcome comparisons with every accepted American approximate profile of the same risk case.</summary>
public sealed record ApproximateAmericanBenchmark(double PlaintiffMaximum,double DefendantMaximum)
{
    public static ApproximateAmericanBenchmark From(IEnumerable<(double Plaintiff,double Defendant)> outcomes)
    {
        var rows=outcomes.ToArray();
        if(rows.Length==0||rows.Any(r=>!double.IsFinite(r.Plaintiff)||!double.IsFinite(r.Defendant)))
            throw new InvalidDataException("A nonempty, finite American approximate comparison set is required.");
        // The maxima may belong to different American profiles: each claim concerns its respective measure.
        return new(rows.Max(r=>r.Plaintiff),rows.Max(r=>r.Defendant));
    }

    public (bool Plaintiff,bool Defendant) Compare(double plaintiff,double defendant)
    {
        if(!double.IsFinite(plaintiff)||!double.IsFinite(defendant))throw new InvalidDataException("Nonfinite British outcome.");
        return (plaintiff>PlaintiffMaximum,defendant>DefendantMaximum);
    }

    public string Group(double plaintiff,double defendant)=>Compare(plaintiff,defendant) switch
    {
        (true,true)=>"both reversals",
        (true,false)=>"plaintiff reversal",
        (false,true)=>"defendant reversal",
        _=>"other"
    };

    public static void Test(string output)
    {
        var tests=new List<string>();
        void Check(string name,bool passed){if(!passed)throw new InvalidDataException(name);tests.Add(name);}
        void Reject(string name,Action action)
        {try{action();}catch(InvalidDataException){tests.Add(name);return;}throw new InvalidDataException(name);}
        var benchmark=From([(.2,.8),(.7,.3)]);
        Check("Use separate American maxima, not one selected American profile",benchmark==new ApproximateAmericanBenchmark(.7,.8));
        Check("Worse than one American profile is insufficient",benchmark.Compare(.5,.5)==(false,false));
        Check("Equality with a maximum is not strictly greater",benchmark.Compare(.7,.8)==(false,false));
        Check("One representable step above each maximum counts without a tolerance",benchmark.Compare(Math.BitIncrement(.7),Math.BitIncrement(.8))==(true,true));
        Check("Plaintiff-only reversal",benchmark.Group(.9,.5)=="plaintiff reversal");
        Check("Defendant-only reversal",benchmark.Group(.5,.9)=="defendant reversal");
        Check("Both reversals have a distinct group",benchmark.Group(.9,.9)=="both reversals");
        Check("Other does not assert superiority to every American profile",benchmark.Group(.5,.5)=="other");
        Reject("Empty American set rejected",()=>From([]));
        Reject("Nonfinite American outcome rejected",()=>From([(double.NaN,.2)]));
        Reject("Nonfinite British outcome rejected",()=>benchmark.Compare(.2,double.PositiveInfinity));
        Files.Save(output,new{Passed=true,Tests=tests,ExactOutcomeComparisons=true,EquilibriumAcceptanceUnchanged=true});
    }
}
