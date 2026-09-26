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
        string grid="grid__signals-8-offers-15__american__rn__cost-1";values.Add("ArticleGridNodes",Audit(grid)["GameIdentity"]!["TreeNodes"]!.GetValue<int>().ToString("N0"));
        var panel=Files.Object(Path.Combine(collection,"Figures/Sources/Figure 1 - Information structure.generated-data.json"))["Panels"]![0]!;
        int source=panel["Sources"]!.AsArray().Select(s=>s!.GetValue<string>()).ToList().IndexOf("0.60--0.80");if(source<0)throw new InvalidDataException("Manuscript highlighted interval changed.");
        double[] probabilities=panel["JointMass"]![source]!.AsArray().Select(p=>p!.GetValue<double>()).ToArray();double denominator=probabilities.Sum();var labels=panel["Destinations"]!.AsArray().Select(s=>double.Parse(s!.GetValue<string>())).ToArray();
        values.Add("ArticleSignalWithin",(100*probabilities.Where((p,i)=>labels[i]>.6&&labels[i]<.8).Sum()/denominator).ToString("F1"));values.Add("ArticleSignalBelow",(100*probabilities.Where((p,i)=>labels[i]<.5).Sum()/denominator).ToString("F1"));
        var ap=Files.Object(Directory.GetFiles(Path.Combine(work,"ReportResults/Primary",ra,"Sources/Profiles"),"*.json").Single());values.Add("ArticleAmericanRaSettlement",(100*ap["Metrics"]!["Settlement"]!.GetValue<double>()).ToString("F1"));
        var data=Files.Object(Path.Combine(collection,"Figures/Sources",MultipleReports.Stem+".generated-data.json"));var rows=data["Rows"]!.AsArray();
        values.Add("ArticleStartsPerCore",plan.Settings.StartsPerCore.ToString());values.Add("ArticleAttemptedStarts",data["AttemptedStarts"]!.ToString());values.Add("ArticleAcceptedStarts",data["AcceptedProfiles"]!.ToString());
        var counts=new[]{("rn","american"),("rn","complete"),("ra","american"),("ra","complete")}.Select(key=>rows.Count(r=>r!["Risk"]!.GetValue<string>()==key.Item1&&r["Rule"]!.GetValue<string>()==key.Item2)).ToArray();values.Add("ArticleAcceptedByCase",$"{counts[0]}, {counts[1]}, {counts[2]} and {counts[3]}");
        var selected=rows.Where(r=>r!["Risk"]!.GetValue<string>()=="ra"&&r["Rule"]!.GetValue<string>()=="american").ToArray();
        foreach(var (prefix,field) in new[]{("ArticleRaExpenditure","RealLitigationExpenditures"),("ArticleRaGross","GrossOutcomeError")}){values.Add(prefix+"Min",selected.Min(r=>r![field]!.GetValue<double>()).ToString("F3"));values.Add(prefix+"Max",selected.Max(r=>r![field]!.GetValue<double>()).ToString("F3"));}
        var outcomes=Reports.ReadCsv(Path.Combine(collection,"Supplemental materials/Multiple equilibria/Sources/all-outcomes.csv")).Where(r=>r["Risk"]=="ra"&&r["Rule"]=="american").ToArray();values.Add("ArticleRaSettlementMin",(100*outcomes.Min(r=>double.Parse(r["Settlement"]))).ToString("F1"));values.Add("ArticleRaSettlementMax",(100*outcomes.Max(r=>double.Parse(r["Settlement"]))).ToString("F1"));
        values.Add("ArticleRnExpenditure",a["Welfare"]!["Headline"]!["RealLitigationExpenditures"]!.GetValue<double>().ToString("F3"));values.Add("ArticleRnGross",a["Welfare"]!["Headline"]!["GrossOutcomeError"]!.GetValue<double>().ToString("F3"));
        string dir=Path.Combine(collection,"Article and bibliography");File.WriteAllText(Path.Combine(dir,"generated-values.tex"),"% Generated only from current validated data.\n"+string.Join('\n',values.Select(p=>$"\\newcommand{{\\{p.Key}}}{{{p.Value}}}"))+"\n");Files.Save(Path.Combine(dir,"generated-values.json"),new{Values=values,Source="Current validated profiles, search catalog and model signal matrix",ObsoleteProseRemainsExplicitlyMarked=true});
    }
}
