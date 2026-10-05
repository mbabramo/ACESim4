using System.Text.Json.Nodes;

namespace ArticleReplication;

public static class ManuscriptValues
{
    public static void Generate(string collection,string work)
    {
        var plan=Files.Read<ResolvedArticlePlan>(Path.Combine(work,"resolved-plan.json"));var values=new Dictionary<string,string>();
        JsonObject Audit(string id)=>Files.Object(Path.Combine(work,"ReportResults/Primary",id,"validation.json"));
        string rn="baseline__standard__american__rn__cost-1",ra="baseline__standard__american__ra__cost-1";
        var a=Audit(rn);values.Add("ArticleBaselineNodes",a["GameIdentity"]!["TreeNodes"]!.GetValue<int>().ToString("N0"));
        var firstGrid=plan.Settings.Grids.First(g=>g.Risk is null or "rn");
        string grid=plan.Cases.Single(c=>c.Family=="grid"&&c.Signals==firstGrid.Signals&&c.Offers.Length==firstGrid.Offers&&c.FeeRule=="american"&&c.AlphaP==0&&c.AlphaD==0).Id;
        values.Add("ArticleGridNodes",Audit(grid)["GameIdentity"]!["TreeNodes"]!.GetValue<int>().ToString("N0"));
        values.Add("ArticleGridSpecifications",string.Join(", ",plan.Settings.Grids.Select(g=>$"{g.Signals} signals and {g.Offers} offers"+(g.Risk==null?"":g.Risk=="rn"?" under risk neutrality":" under risk aversion"))));
        var panel=Files.Object(Path.Combine(collection,"Figures/Sources/Figure 1 - Information structure.generated-data.json"))["Panels"]![0]!;
        int source=panel["Sources"]!.AsArray().Select(s=>s!.GetValue<string>()).ToList().IndexOf("0.60--0.80");if(source<0)throw new InvalidDataException("Manuscript highlighted interval changed.");
        double[] probabilities=panel["JointMass"]![source]!.AsArray().Select(p=>p!.GetValue<double>()).ToArray();double denominator=probabilities.Sum();var labels=panel["Destinations"]!.AsArray().Select(s=>double.Parse(s!.GetValue<string>())).ToArray();
        values.Add("ArticleSignalWithin",(100*probabilities.Where((p,i)=>labels[i]>.6&&labels[i]<.8).Sum()/denominator).ToString("F1"));values.Add("ArticleSignalBelow",(100*probabilities.Where((p,i)=>labels[i]<.5).Sum()/denominator).ToString("F1"));
        var ap=Files.Object(Directory.GetFiles(Path.Combine(work,"ReportResults/Primary",ra,"Sources/Profiles"),"*.json").Single());values.Add("ArticleAmericanRaSettlement",(100*ap["Metrics"]!["Settlement"]!.GetValue<double>()).ToString("F1"));
        var data=Files.Object(Path.Combine(collection,MultipleReports.Folder,"Sources",MultipleReports.Stem+".generated-data.json"));var rows=data["Rows"]!.AsArray();
        values.Add("ArticleStartsPerCore",plan.Settings.StartsPerCore.ToString());values.Add("ArticleAttemptedStarts",data["AttemptedStarts"]!.ToString());values.Add("ArticleAcceptedStarts",data["AcceptedProfiles"]!.ToString());
        var counts=new[]{("rn","american"),("rn","complete"),("ra","american"),("ra","complete")}.Select(key=>rows.Count(r=>r!["Risk"]!.GetValue<string>()==key.Item1&&r["Rule"]!.GetValue<string>()==key.Item2)).ToArray();values.Add("ArticleAcceptedByCase",$"{counts[0]}, {counts[1]}, {counts[2]} and {counts[3]}");
        var selected=rows.Where(r=>r!["Risk"]!.GetValue<string>()=="ra"&&r["Rule"]!.GetValue<string>()=="american").ToArray();
        foreach(var (prefix,field) in new[]{("ArticleRaExpenditure","RealLitigationExpenditures"),("ArticleRaGross","GrossOutcomeError")}){values.Add(prefix+"Min",selected.Min(r=>r![field]!.GetValue<double>()).ToString("F3"));values.Add(prefix+"Max",selected.Max(r=>r![field]!.GetValue<double>()).ToString("F3"));}
        var outcomes=Reports.ReadCsv(Path.Combine(collection,"Supplemental materials/Multiple equilibria/Sources/all-outcomes.csv")).Where(r=>r["Risk"]=="ra"&&r["Rule"]=="american").ToArray();values.Add("ArticleRaSettlementMin",(100*outcomes.Min(r=>double.Parse(r["Settlement"]))).ToString("F1"));values.Add("ArticleRaSettlementMax",(100*outcomes.Max(r=>double.Parse(r["Settlement"]))).ToString("F1"));
        values.Add("ArticleRnExpenditure",a["Welfare"]!["Headline"]!["RealLitigationExpenditures"]!.GetValue<double>().ToString("F3"));values.Add("ArticleRnGross",a["Welfare"]!["Headline"]!["GrossOutcomeError"]!.GetValue<double>().ToString("F3"));
        JsonObject Profile(string id)=>Files.Object(Directory.GetFiles(Path.Combine(work,"ReportResults/Primary",id,"Sources/Profiles"),"*.json").Single());
        double Metric(string id,string name)=>Profile(id)["Metrics"]![name]!.GetValue<double>();
        void Number(string key,double n,string format="F1")=>values.Add(key,n.ToString(format,System.Globalization.CultureInfo.InvariantCulture));
        foreach(var (prefix,risk,rule) in new[]{("AmericanRn","rn","american"),("BritishRn","rn","complete"),("AmericanRa","ra","american"),("BritishRa","ra","complete")})
        {
            string id=$"baseline__standard__{rule}__{risk}__cost-1";
            Number("Article"+prefix+"Nonfiling",100*(1-Metric(id,"Filing")));
            Number("Article"+prefix+"Nonanswering",100*(Metric(id,"Filing")-Metric(id,"JointFileAnswer")));
            foreach(string metric in new[]{"Settlement","Trial","Abandonment","Default"})
                if(!values.ContainsKey("Article"+prefix+metric))Number("Article"+prefix+metric,100*Metric(id,metric));
        }
        foreach(string rule in new[]{"american","complete"})
        {
            var noisy=plan.Cases.Single(c=>c.Family=="court-noise"&&c.CourtSigma==plan.Settings.NoiseLevels.Max()&&c.FeeRule==rule&&c.AlphaP==0);
            Number("ArticleNoisyCourt"+(rule=="american"?"American":"British")+"Settlement",100*Metric(noisy.Id,"Settlement"));
        }
        double gridSettlement=100*Metric(grid,"Settlement"),baselineSettlement=100*Metric(rn,"Settlement");
        string movement=gridSettlement<baselineSettlement?"falling":gridSettlement>baselineSettlement?"rising":"remaining unchanged";
        values.Add("ArticleGridSettlementComparison",$"{movement} from ${baselineSettlement:F1}\\%$ to ${gridSettlement:F1}\\%$ with {firstGrid.Signals} signals and {firstGrid.Offers} offers");
        Number("ArticlePivotLimit",plan.Settings.ApproximatePivotLimit,"N0");
        Number("ArticleRoundingCutoff",plan.Settings.ApproximateRoundingCutoff,"G");
        var britishRa=rows.Where(r=>r!["Risk"]!.GetValue<string>()=="ra"&&r["Rule"]!.GetValue<string>()=="complete").ToArray();
        var reference=ApproximateAmericanBenchmark.From(selected.Select(r=>(r!["MeritoriousPlaintiffShortfall"]!.GetValue<double>(),r["NonliableDefendantBurden"]!.GetValue<double>())));
        var reversals=britishRa.Select(r=>reference.Compare(r!["MeritoriousPlaintiffShortfall"]!.GetValue<double>(),r["NonliableDefendantBurden"]!.GetValue<double>())).ToArray();
        int pReversals=reversals.Count(r=>r.Plaintiff),dReversals=reversals.Count(r=>r.Defendant);
        int both=reversals.Count(r=>r.Plaintiff&&r.Defendant);
        values.Add("ArticleAcceptedBritishRa",britishRa.Length.ToString());values.Add("ArticlePlaintiffReversals",pReversals.ToString());values.Add("ArticleDefendantReversals",dReversals.ToString());
        values.Add("ArticleBothReversalsSentence",both==0?"None reversed both comparisons":$"{both} reversed both comparisons");
        var sensitivity=Reports.ReadCsv(Path.Combine(collection,"Results/Aggregated Data/Equilibrium sensitivity/profiles.csv"))
            .Where(r=>r["Risk"]=="ra"&&r["Rule"]=="complete"&&r["Kind"]=="approximate").ToArray();
        var expectedGroups=britishRa.ToDictionary(r=>r!["Start"]!.GetValue<int>(),r=>reference.Group(r!["MeritoriousPlaintiffShortfall"]!.GetValue<double>(),r["NonliableDefendantBurden"]!.GetValue<double>()));
        if(sensitivity.Length!=britishRa.Length||sensitivity.Select(r=>int.Parse(r["Start"])).Distinct().Count()!=britishRa.Length||sensitivity.Any(r=>!expectedGroups.TryGetValue(int.Parse(r["Start"]),out string? group)||group!=r["Group"]))
            throw new InvalidDataException("Tremble groups do not match the American approximate outcome comparison.");
        double Median(params string[] groups)
        {
            var x=sensitivity.Where(r=>groups.Contains(r["Group"])).Select(r=>double.Parse(r["WorstAdditionalGainAt1Percent"])).Order().ToArray();
            if(x.Length==0)throw new InvalidDataException("No profiles for the stated tremble comparison.");
            return x.Length%2==1?x[x.Length/2]:(x[x.Length/2-1]+x[x.Length/2])/2;
        }
        Number("ArticlePlaintiffTrembleRatio",Median("plaintiff reversal","both reversals")/Median("other"));Number("ArticleDefendantTrembleRatio",Median("defendant reversal","both reversals")/Median("other"));
        var strategyRows=Files.Object(Path.Combine(collection,"Tables/Sources/Table 2 - Strategy mechanisms.layout.json"))["Sections"]![0]![1]!.AsArray();
        foreach(var (name,decision) in new[]{("ArticleFilingBefore","P files"),("ArticleAnsweringBefore","D answers")})
            values.Add(name,strategyRows.Single(r=>r![0]!.GetValue<string>()==decision)![4]!.GetValue<string>().Split('>')[0].Trim());
        string dir=Path.Combine(collection,"Article and bibliography");File.WriteAllText(Path.Combine(dir,"generated-values.tex"),"% Generated only from current validated data.\n"+string.Join('\n',values.Select(p=>$"\\newcommand{{\\{p.Key}}}{{{p.Value}}}"))+"\n");
        string manuscript=File.ReadAllText(Path.Combine(dir,"corr_signals.tex"));
        var used=System.Text.RegularExpressions.Regex.Matches(manuscript,@"\\(Article[A-Za-z]+)").Select(m=>m.Groups[1].Value).Distinct().Order().ToArray();
        if(!manuscript.Contains(@"\input{generated-values.tex}")||used.Except(values.Keys).Any())throw new InvalidDataException("Manuscript numerical bindings are missing or undefined.");
        Files.Save(Path.Combine(dir,"generated-values.json"),new{Values=values,UsedBindings=used,Source="Current validated profiles, search catalog, tremble statistics, strategy tables and model signal matrix",AuthoredProsePreserved=true,AuthoredNumericalClaimsRequireReview=true});
    }
}
