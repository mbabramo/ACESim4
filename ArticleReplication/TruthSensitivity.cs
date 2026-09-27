using System.Text.Json.Nodes;

namespace ArticleReplication;

/// <summary>Fresh ex-post truth assessment of the main cost series; never starts a solve.</summary>
public static class TruthSensitivity
{
    public static async Task Run(ResolvedArticlePlan plan,Dictionary<string,(JsonObject Audit,JsonObject Profile)> profiles,string collection,string work,int workers)
    {
        var cases=plan.Cases.Where(c=>c.Family is "baseline" or "cost-multiplier").ToArray();
        if(cases.Any(c=>!profiles.ContainsKey(c.Id)))throw new InvalidDataException("Truth sensitivity requires every selected main profile.");
        var exponents=plan.Settings.TruthExponents.Order().ToArray();
        await Parallel.ForEachAsync(cases,new ParallelOptions{MaxDegreeOfParallelism=workers},async(c,ct)=>{
            string dir=Path.Combine(work,"ReportResults/Primary",c.Id),request=Path.Combine(work,"requests","truth-"+c.Id+".json");
            Files.Save(request,new{Case=c,EquilibriumFile=Path.Combine(dir,"equilibrium.equ"),ActionReportFile=Path.Combine(dir,"information-set-actions.csv"),NumericReportFile=Path.Combine(dir,"replayed-report.csv"),EquilibriumNumber=1,Exponents=exponents});
            await Commands.Worker(Path.Combine(work,"logs"),"truth-"+c.Id,work,"worker-truth","--request",request,"--output",Path.Combine(work,"ReportResults/TruthSensitivity",c.Id));
        });
        var maps=new Dictionary<string,Dictionary<double,JsonObject>>();
        foreach(var c in cases)
        {
            var result=Files.Object(Path.Combine(work,"ReportResults/TruthSensitivity",c.Id,"truth-mapping-results.json"));
            foreach(string flag in new[]{"Passed","CompleteStrategyUnchanged","StrategicGameUnchanged","BestResponseGainsUnchanged"})
                if(result[flag]?.GetValue<bool>()!=true)throw new InvalidDataException("Truth replay failed: "+c.Id);
            var m=result["TruthAnalysis"]!["Mappings"]!.AsArray().Select(x=>x!.AsObject()).ToDictionary(x=>x["Exponent"]!.GetValue<double>());
            if(!m.Keys.Order().SequenceEqual(exponents))throw new InvalidDataException("Truth map coverage differs.");
            foreach(string measure in WelfareFigure.Measures)
            {
                double identity=m[1]["Headline"]![measure]!.GetValue<double>(),baseline=profiles[c.Id].Audit["Welfare"]!["Headline"]![measure]!.GetValue<double>();
                if(Math.Abs(identity-baseline)>1e-10)throw new InvalidDataException("Identity map fails baseline reproduction.");
            }
            if(m.Values.Any(x=>x["Headline"]!["RealLitigationExpenditures"]!.GetValue<double>()!=m[1]["Headline"]!["RealLitigationExpenditures"]!.GetValue<double>()))throw new InvalidDataException("Truth mapping changed real costs.");
            maps.Add(c.Id,m);
        }
        var rows=new List<Dictionary<string,object?>>();
        var text=new List<string>{"# Truth-formula robustness across costs","","Merits, signals, court decisions and complete equilibrium strategies are held fixed. Only Pr(true liability | q) = q^k / (q^k + (1-q)^k) changes. k=1 is the baseline; smaller k weakens the link and larger k strengthens it. These are selected-equilibrium welfare comparisons, not additional equilibrium searches.","","All entries below are British minus American; negative values indicate a reduction in the specified loss or expenditure. Exact zeros are retained; near-zero contrasts are not relabelled as zero.",""};
        foreach(var a in cases.Where(c=>c.FeeRule=="american").OrderBy(c=>c.AlphaP).ThenBy(c=>c.CostMultiplier))
        {
            var b=cases.Single(c=>c.FeeRule=="complete"&&c.AlphaP==a.AlphaP&&c.CostMultiplier==a.CostMultiplier);
            text.Add($"## {(a.AlphaP==0?"Risk neutral":"Risk averse")}, cost multiplier {a.CostMultiplier:G}");text.Add("");
            text.Add("| Welfare measure | "+string.Join(" | ",exponents.Select(k=>$"k={k:G}"))+" |");text.Add("|---|"+string.Join("|",exponents.Select(_=>"---:"))+"|");
            foreach(string measure in WelfareFigure.Measures)
            {
                var contrasts=new List<double>();
                double baseline=maps[b.Id][1]["Headline"]![measure]!.GetValue<double>()-maps[a.Id][1]["Headline"]![measure]!.GetValue<double>();
                foreach(double k in exponents)
                {
                    double av=maps[a.Id][k]["Headline"]![measure]!.GetValue<double>(),bv=maps[b.Id][k]["Headline"]![measure]!.GetValue<double>();
                    double delta=bv-av;contrasts.Add(delta);
                    rows.Add(new(){{"Risk",a.AlphaP==0?"Risk neutral":"Risk averse"},{"CostMultiplier",a.CostMultiplier},{"Exponent",k},{"Measure",measure},{"American",av},{"British",bv},{"BritishMinusAmerican",delta},{"BaselineContrast",baseline},{"SameSignAsBaseline",Math.Sign(delta)==Math.Sign(baseline)}});
                }
                text.Add("| "+measure+" | "+string.Join(" | ",contrasts.Select(x=>x.ToString("G6")))+" |");
            }
            text.Add("");
        }
        string output=Path.Combine(collection,"Results/Aggregated Data/Truth sensitivity");Directory.CreateDirectory(output);
        Reports.Csv(Path.Combine(output,"truth-sensitivity.csv"),rows);
        File.WriteAllLines(Path.Combine(output,"truth-sensitivity.md"),text);
        Files.Save(Path.Combine(output,"validation.json"),new{Passed=true,Profiles=cases.Length,Exponents=exponents,Rows=rows.Count,IdentityTolerance=1e-10,RealCostsExactlyInvariant=true,SolvesStarted=0});
    }
}
