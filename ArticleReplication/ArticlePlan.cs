using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using System.Globalization;
using System.Text.Json;

namespace ArticleReplication;

/// <summary>One scientific definition for scheduling, cache identity, comparisons and exhibits.</summary>
public sealed record CorrelatedSignalsSettings
{
    public string Article { get; init; } = "CorrelatedSignals";
    public double[] MainCostMultipliers { get; init; } = new LitigGameCorrelatedSignalsArticleLauncher(
        LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain).CriticalCostsMultipliers;
    public double ReferenceCostMultiplier { get; init; } = 1;
    public byte BaselineSignals { get; init; } = 10;
    public int BaselineOffers { get; init; } = 10;
    public double[] NoiseLevels { get; init; } = [.1, .4];
    public Grid[] Grids { get; init; } = [new(8,15),new(12,8),new(8,8)];
    public int StartsPerCore { get; init; } = 50;
    public int ApproximatePivotLimit { get; init; } = 20000;
    public double ApproximateRoundingCutoff { get; init; } = .005;
    public ArticleApproximateGainUnits ApproximateGainUnits { get; init; } = ArticleApproximateGainUnits.FullTerminalUtilityRange;
    public double[] Trembles { get; init; } = [.001,.005,.01];
    public int TrembleDirections { get; init; } = 5;
    public bool IncludeExtensions { get; init; } = true;
    public bool IncludeTrialOnly { get; init; } = true;
    public string[] Steps { get; init; } = ["Primary","MultipleStarts","Welfare","Strategic","Trembles","Histories","StandardReports","Exhibits","Manuscript"];
    public sealed record Grid(byte Signals,int Offers);
}

public sealed record Calibration(double PartySigma,double CourtSigma,string Sha256);
public sealed record Comparison(string Id,string Source,string Target,string Kind);
public sealed record ResolvedArticlePlan(string Article,CorrelatedSignalsSettings Settings,FinalArticleCase[] Cases,
    Comparison[] Welfare,Comparison[] Strategic,string[] CoreCases,int ExpectedApproximateStarts,string[] Steps);

public static class ArticlePlan
{
    // Published calibration belongs to the article protocol, not to a proposed solution bundle.
    public static Calibration PublishedCalibration => new(0.3649498266166219,0.3127188271240706,"a95fdd1a1e66494a7f87f286a256d4bb4ba9f00f25ca26b9aa0ec486dba65adb");
    public static string Number(double x)=>x.ToString("G",CultureInfo.InvariantCulture).Replace(".","p");
    public static double[] Offers(int n)
    {
        if(n<2||n>255)throw new InvalidDataException("Offer count outside supported range.");
        if(n==10)return Enumerable.Range(0,n).Select(i=>(i+.5)/10).ToArray();
        // Preserve the executed explicit-grid operation order, including its binary64 values.
        var result=Enumerable.Range(0,n).Select(i=>.05+(.95-.05)*i/(n-1)).ToArray();
        result[0]=.05;result[^1]=.95;return result;
    }

    public static ResolvedArticlePlan Resolve(CorrelatedSignalsSettings s,Calibration calibration)
    {
        if(s.Article!="CorrelatedSignals")throw new InvalidDataException("Only CorrelatedSignals is implemented; no fallback to another article.");
        if(s.MainCostMultipliers.Length==0||s.MainCostMultipliers.Any(x=>!double.IsFinite(x)||x<=0)||
            s.MainCostMultipliers.Distinct().Count()!=s.MainCostMultipliers.Length||!s.MainCostMultipliers.Contains(s.ReferenceCostMultiplier))
            throw new InvalidDataException("Declare distinct positive main costs including the reference cost.");
        if(s.ReferenceCostMultiplier!=1||s.BaselineSignals!=10||s.BaselineOffers!=10)
            throw new InvalidDataException("The current article protocol retains its cost-one, ten-signal, ten-offer reference. Change its scientific definition explicitly.");
        string[] supported=["Primary","MultipleStarts","Welfare","Strategic","Trembles","Histories","StandardReports","Exhibits","Manuscript"];
        if(s.Steps.Distinct().Count()!=s.Steps.Length||s.Steps.Except(supported).Any())throw new InvalidDataException("Unknown or repeated article step.");
        if(s.StartsPerCore<1||s.ApproximatePivotLimit<1)throw new InvalidDataException("Finite positive approximate search budget required.");
        _=new ArticleApproximatePolicy(s.ApproximateRoundingCutoff,s.ApproximateGainUnits,s.ApproximatePivotLimit);
        var cases=new List<FinalArticleCase>();
        foreach(double cost in s.MainCostMultipliers.Order())foreach(var risk in new[]{"rn","ra"})foreach(var fee in new[]{"american","complete"})
            Add(cost==s.ReferenceCostMultiplier?"baseline":"cost-multiplier","standard",fee,risk,cost,original:true);
        if(s.IncludeTrialOnly)foreach(var risk in new[]{"rn","ra"})Add("trial-only","standard","trial-only",risk,s.ReferenceCostMultiplier,original:true);
        if(s.IncludeExtensions)
        {
            foreach(var risk in new[]{"rn","ra"})foreach(var fee in new[]{"american","complete"})
            {
                foreach(var (variant,entry,trial) in new[]{("earlier-costs",.225,.075),("later-costs",.075,.225)})
                    Add("cost-timing",variant,fee,risk,1,entry:entry,trial:trial);
                foreach(double sigma in s.NoiseLevels)
                {
                    if(!double.IsFinite(sigma)||sigma<=0)throw new InvalidDataException("Invalid noise value.");
                    Add("private-noise","sigma-"+Number(sigma),fee,risk,1,party:sigma);
                    Add("court-noise","sigma-"+Number(sigma),fee,risk,1,court:sigma);
                }
                foreach(var grid in s.Grids)Add("grid",$"signals-{grid.Signals}-offers-{grid.Offers}",fee,risk,1,signals:grid.Signals,offers:grid.Offers);
                Add("merits-distribution","center-weighted",fee,risk,1,distribution:"beta-2-2");
                Add("merits-distribution","polarized",fee,risk,1,distribution:"beta-half-half");
                Add("direct-binary","calibrated-to-uniform-merits",fee,risk,1,distribution:"direct-binary",party:calibration.PartySigma,court:calibration.CourtSigma);
            }
            foreach(var risk in new[]{"p-only-ra","d-only-ra"})foreach(var fee in new[]{"american","complete"})
                Add("asymmetric-risk",risk,fee,risk,1);
        }
        void Add(string family,string variant,string fee,string risk,double cost,bool original=false,byte signals=10,int offers=10,
            double party=.2,double court=.2,double entry=.15,double trial=.15,string distribution="uniform")
        {
            string id=$"{family}__{variant}__{fee}__{risk}__cost-{Number(cost)}";
            string? originalName=original?"Agreement-Enabled__Specification-"+(risk=="ra"?"ModerateRiskAversion":"Baseline")+
                "__Cost-"+cost.ToString("G",CultureInfo.InvariantCulture)+"__Fee-"+(fee=="american"?"American":"British")+
                (fee=="complete"?"__ExitFees-AllUnilateralExits":""):null;
            cases.Add(new(){Id=id,Family=family,Variant=variant,FeeRule=fee,AlphaP=risk is "ra" or "p-only-ra"?2:0,
                AlphaD=risk is "ra" or "d-only-ra"?2:0,CostMultiplier=cost,Signals=signals,Offers=Offers(offers),
                PartySigma=party,CourtSigma=court,EntryCost=entry,TrialCost=trial,Distribution=distribution,
                OriginalOptionName=originalName,CalibrationSha256=distribution=="direct-binary"?calibration.Sha256:null});
        }
        if(cases.Select(c=>c.Id).Distinct().Count()!=cases.Count)throw new InvalidDataException("Scientific settings generate duplicate cases.");
        var ids=cases.ToDictionary(c=>c.Id);var welfare=new List<Comparison>();var strategic=new List<Comparison>();
        foreach(var a in cases.Where(c=>c.FeeRule=="american"))
        {
            string b=a.Id.Replace("__american__","__complete__");
            if(ids.ContainsKey(b)){welfare.Add(new(a.Id+"--"+b,a.Id,b,"fee-rule"));Pair(a.Id,b,"fee-rule");}
        }
        foreach(var c in cases.Where(c=>c.AlphaP==0&&c.AlphaD==0))
        {
            string riskPartner=c.Id.Replace("__rn__","__ra__");
            if(ids.ContainsKey(riskPartner))Pair(c.Id,riskPartner,"symmetric-risk");
        }
        foreach(var c in cases.Where(c=>c.FeeRule=="trial-only"))
            foreach(var fee in new[]{"american","complete"})
            {
                string risk=c.AlphaP==0?"rn":"ra";
                Pair($"baseline__standard__{fee}__{risk}__cost-{Number(s.ReferenceCostMultiplier)}",c.Id,"trial-only");
            }
        void Pair(string a,string b,string kind)
        {
            if(!ids.ContainsKey(a)||!ids.ContainsKey(b))throw new InvalidDataException("Missing comparison reference case.");
            if(!strategic.Any(x=>x.Source==a&&x.Target==b)){strategic.Add(new(a+"--"+b,a,b,kind));strategic.Add(new(b+"--"+a,b,a,kind));}
        }
        string[] core=cases.Where(c=>c.Family=="baseline").Select(c=>c.Id).Order().ToArray();
        return new(s.Article,s,cases.OrderBy(c=>c.Id,StringComparer.Ordinal).ToArray(),welfare.ToArray(),strategic.ToArray(),core,
            checked(core.Length*s.StartsPerCore),s.Steps);
    }
}
