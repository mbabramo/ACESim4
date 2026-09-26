using System.Globalization;
using System.Text.Json.Nodes;
using CsvHelper;

namespace ArticleReplication;

public static class Reports
{
    public static Dictionary<string,string>[] ReadCsv(string path)
    {
        using var reader=new StreamReader(path);using var csv=new CsvReader(reader,CultureInfo.InvariantCulture);
        return csv.GetRecords<dynamic>().Select(r=>((IDictionary<string,object>)r).ToDictionary(x=>x.Key,x=>x.Value?.ToString()??"")).ToArray();
    }
    public static void Csv(string path,IEnumerable<Dictionary<string,object?>> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var writer=new StreamWriter(new FileStream(path,FileMode.CreateNew));
        using var csv=new CsvWriter(writer,CultureInfo.InvariantCulture);var list=rows.ToArray();if(list.Length==0)return;
        string[] headers=list[0].Keys.ToArray();foreach(var key in headers)csv.WriteField(key);csv.NextRecord();
        foreach(var row in list){foreach(var key in headers)csv.WriteField(row[key]);csv.NextRecord();}
    }
    public static void ExactCsv(Dictionary<string,object?> current,Dictionary<string,string> expected,string name)
    {
        foreach(var (key,value) in current)
        {
            bool equal=value switch{null=>expected[key]=="",double n=>n==double.Parse(expected[key],CultureInfo.InvariantCulture),int n=>n==int.Parse(expected[key],CultureInfo.InvariantCulture),byte n=>n==byte.Parse(expected[key],CultureInfo.InvariantCulture),_=>value.ToString()==expected[key]};
            if(!equal)throw new InvalidDataException($"{name}: changed CSV value {key}");
        }
    }
    public static void Primary(string bundle,string collection,ResolvedArticlePlan plan,Dictionary<string,(JsonObject Audit,JsonObject Profile)> profiles)
    {
        var expected=ReadCsv(Path.Combine(bundle,"inputs/selected-primary-outcomes.csv")).ToDictionary(r=>r["CaseId"]);var rows=new List<Dictionary<string,object?>>();
        foreach(var spec in plan.Cases.Where(c=>profiles.ContainsKey(c.Id)))
        {
            var (audit,profile)=profiles[spec.Id];var row=new Dictionary<string,object?>{{"CaseId",spec.Id},{"Family",spec.Family},{"Variant",spec.Variant},{"FeeRule",spec.FeeRule},{"AlphaP",spec.AlphaP},{"AlphaD",spec.AlphaD},{"CostMultiplier",spec.CostMultiplier},{"Signals",spec.Signals},{"Offers",spec.Offers.Length}};
            foreach(var pair in profile["Metrics"]!.AsObject())row.Add(pair.Key,pair.Value?.GetValue<double>());
            row.Add("AgreementStageReach",profile["Strategies"]!.AsArray().Where(r=>r!["Decision"]!.GetValue<string>()=="PAgreeToBargain").Sum(r=>r!["Reach"]!.GetValue<double>()));
            foreach(var pair in audit["Welfare"]!["Headline"]!.AsObject())row.Add(pair.Key,pair.Value!.GetValue<double>());
            ExactCsv(row,expected[spec.Id],spec.Id);rows.Add(row);
        }
        string aggregate=Path.Combine(collection,"Results/Aggregated Data");Csv(Path.Combine(aggregate,"selected-primary-outcomes.csv"),rows);
        Csv(Path.Combine(aggregate,"American-British-cost-outcomes/selected-exact-outcomes.csv"),rows.Where(r=>r["Family"] is "baseline" or "cost-multiplier"));
        File.WriteAllText(Path.Combine(collection,"Results/Individual simulations/README.md"),"# Individual simulations\n\nComplete profiles, saved actions, numeric replay and unrestricted best responses were checked against the freshly built code.\n\n"+string.Join('\n',rows.Select(r=>$"- [{r["CaseId"]}]({r["CaseId"]}/strategy.pdf)"))+"\n");
        File.WriteAllText(Path.Combine(aggregate,"README.md"),$"# Aggregated data\n\n{rows.Count} of {plan.Cases.Length} planned profiles are available. Missing profiles are listed in the resolved plan and never filled with zeros.\n\n- [Primary outcomes](selected-primary-outcomes.csv)\n- [Primary catalog](selected-primary-catalog.json)\n");
    }
    public static async Task Welfare(string bundle,ResolvedArticlePlan plan,Dictionary<string,(JsonObject Audit,JsonObject Profile)> profiles,string collection,string work,int workers)
    {
        var inputs=Files.Read<PrimaryInput[]>(Path.Combine(bundle,"inputs/primary.json")).ToDictionary(p=>p.CaseId);
        var ready=plan.Welfare.Where(p=>profiles.ContainsKey(p.Source)&&profiles.ContainsKey(p.Target)).ToArray();
        object Endpoint(string id){var p=inputs[id];return new{p.Case,EquilibriumFile=Files.Under(bundle,p.Inputs["Equilibrium"].Path),ActionReportFile=Files.Under(bundle,p.Inputs["Actions"].Path),NumericReportFile=Files.Under(bundle,p.Inputs["Numeric"].Path),EquilibriumNumber=1};}
        await Parallel.ForEachAsync(ready,new ParallelOptions{MaxDegreeOfParallelism=workers},async(pair,ct)=>{
            string request=Path.Combine(work,"requests/welfare-"+pair.Id+".json");Files.Save(request,new{Id=pair.Id,American=Endpoint(pair.Source),Complete=Endpoint(pair.Target),TruthMapExponents=new[]{1.0}});
            await Commands.Worker(Path.Combine(work,"logs"),"welfare-"+pair.Id,work,"worker-welfare","--request",request,"--output",Path.Combine(work,"ReportResults/Welfare",pair.Id));
        });
        var expected=ReadCsv(Path.Combine(bundle,"inputs/expected-main-decomposition.csv")).ToDictionary(r=>(r["AmericanCase"],r["Measure"]));
        string[] labels=["Meritorious plaintiff shortfall","Nonliable defendant burden","Liable defendant excess burden","Gross outcome error","Real litigation expenditures"];
        var names=labels.Zip(WelfareFigure.Measures).ToDictionary(x=>x.First,x=>x.Second);
        var rows=new List<Dictionary<string,object?>>();int exactEndpoints=0,mainRows=0;
        foreach(var pair in ready.OrderBy(x=>x.Id,StringComparer.Ordinal))
        {
            var spec=inputs[pair.Source].Case;var data=Files.Object(Path.Combine(work,"ReportResults/Welfare",pair.Id,"decomposition.json"));
            if(data["Passed"]?.GetValue<bool>()!=true||data["SolvesStarted"]!.GetValue<int>()!=0)throw new InvalidDataException("Failed welfare comparison.");
            foreach(var item in data["Components"]!.AsObject())
            {
                var c=item.Value!.AsObject();string field=names[item.Key];
                Files.EqualScience(c["AmericanWithAmericanProfile"],profiles[pair.Source].Audit["Welfare"]!["Headline"]![field],pair.Id+" American endpoint");
                Files.EqualScience(c["CompleteWithCompleteProfile"],profiles[pair.Target].Audit["Welfare"]!["Headline"]![field],pair.Id+" British endpoint");exactEndpoints+=2;
                if(Math.Abs(c["Residual"]!.GetValue<double>())>1e-10)throw new InvalidDataException("Unreconciled welfare comparison.");
                var row=new Dictionary<string,object?>{{"Risk",spec.AlphaP==0&&spec.AlphaD==0?"Risk neutral":spec.AlphaP==spec.AlphaD?"Risk averse":"Asymmetric risk"},{"CostMultiplier",spec.CostMultiplier},{"Measure",item.Key},{"AmericanCase",pair.Source},{"BritishCase",pair.Target}};
                foreach(var fieldValue in c)row[fieldValue.Key]=fieldValue.Value!.GetValue<double>();
                if(spec.Family is "baseline" or "cost-multiplier"){ExactCsv(row,expected[(pair.Source,item.Key)],pair.Id);mainRows++;}
                rows.Add(row);
            }
        }
        Csv(Path.Combine(collection,"Results/Aggregated Data/all-welfare-decompositions.csv"),rows);
        Csv(Path.Combine(collection,"Results/Aggregated Data/Main-welfare-decomposition/welfare-decomposition.csv"),rows.Where(r=>inputs[(string)r["AmericanCase"]!].Case.Family is "baseline" or "cost-multiplier"));
        Files.Save(Path.Combine(work,"welfare-validation.json"),new{Passed=true,Pairs=ready.Length,ExactEndpointComparisons=exactEndpoints,MainRowsIdentical=mainRows,SolvesStarted=0});
    }
}
