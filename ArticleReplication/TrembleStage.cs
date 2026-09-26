using System.Text.Json.Nodes;
using ACESimBase.Games.EFGFileGame;

namespace ArticleReplication;

/// <summary>Fresh opponent-tremble diagnostics, with full original strategies and no equilibrium solves.</summary>
public static class TrembleStage
{
    public static async Task Run(string bundle,ResolvedArticlePlan plan,string run,string output,int workers)
    {
        if(Directory.Exists(output))throw new IOException("Fresh tremble output required.");
        if(plan.Settings.Trembles.Length==0||plan.Settings.Trembles.Any(e=>!double.IsFinite(e)||e<=0||e>=1)||plan.Settings.Trembles.Distinct().Count()!=plan.Settings.Trembles.Length||plan.Settings.TrembleDirections<1)throw new InvalidDataException("Finite distinct trembles in (0,1) and positive direction count required.");
        if(plan.Settings.Trembles.Any(e=>double.Parse(e.ToString("0.000"))!=e))throw new InvalidDataException("Tremble file coordinates currently require precision of 0.001.");
        var approximate=Files.Object(Path.Combine(run,"ReportResults/MultipleStarts/completed.json"));if(approximate["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Revalidate multiple starts before trembles.");
        var accepted=approximate["Results"]!.AsArray().Where(a=>a!["Accepted"]!.GetValue<bool>()).ToDictionary(a=>a!["CaseId"]!.GetValue<string>()+$"-start-{a["StartIndex"]!.GetValue<int>():D5}");
        var available=JsonNode.Parse(File.ReadAllText(Path.Combine(bundle,"inputs/profiles.json")))!.AsArray();var selected=new List<JsonObject>();
        foreach(var p in available)
        {
            string id=p!["Id"]!.GetValue<string>(),caseId=p["CaseId"]!.GetValue<string>(),kind=p["Kind"]!.GetValue<string>();
            if(!plan.CoreCases.Contains(caseId)||(kind=="approximate"&&!accepted.ContainsKey(id)))continue;
            string directory=kind=="exact-primary"?Path.Combine(run,"ReportResults/Primary",caseId):Path.Combine(run,"ReportResults/MultipleStarts",caseId,$"start-{p["Start"]!.GetValue<int>():D5}");
            var audit=Files.Object(Path.Combine(directory,"validation.json"));if(audit["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Unvalidated tremble endpoint.");
            var profile=Files.Object(Directory.GetFiles(Path.Combine(directory,"Sources/Profiles"),"*.json").Single());
            string file=Files.Under(bundle,p["FrozenFile"]!.GetValue<string>());if(Files.Sha(file)!=p["FrozenSha256"]!.GetValue<string>())throw new InvalidDataException("Changed tremble input.");
            var vector=File.ReadAllText(file).Trim().Split(',').Select(EFGFileReader.RationalStringToDouble);var current=profile["Strategies"]!.AsArray().SelectMany(s=>s!["Probabilities"]!.AsArray().Select(v=>v!.GetValue<double>()));
            if(!vector.SequenceEqual(current))throw new InvalidDataException("Tremble input differs from newly validated complete profile.");
            Files.EqualScience(p["Welfare"],audit["Welfare"]!["Headline"],"Tremble original welfare");
            var copy=(JsonObject)p.DeepClone();copy["FrozenFile"]=file;selected.Add(copy);
        }
        if(selected.Count!=accepted.Count+plan.CoreCases.Length||selected.Select(p=>p["Id"]!.GetValue<string>()).Distinct().Count()!=selected.Count)throw new InvalidDataException("Tremble profile inventory incomplete.");
        Directory.CreateDirectory(output);Files.Save(Path.Combine(output,"profiles.json"),selected);Files.Save(Path.Combine(output,"protocol.json"),new{Epsilons=plan.Settings.Trembles,Directions=plan.Settings.TrembleDirections,Seed=20260925,TieToleranceNormalized=1e-10,MaximumSelectedPolicyResidual=1e-8,ChanceUnchanged=true,FullOffPathStrategiesRetained=true,NoEquilibriumSolves=true});
        var requests=new List<string>();
        foreach(string id in plan.CoreCases)
        {
            var profiles=selected.Where(p=>p["CaseId"]!.GetValue<string>()==id).OrderBy(p=>p["Id"]!.GetValue<string>(),StringComparer.Ordinal).ToArray();
            // Small finite shards allow configurable process concurrency without parallelizing a solve.
            int count=Math.Min(8,profiles.Length);for(int part=0;part<count;part++)
            {
                string request=Path.Combine(output,"inputs/run-v1",id+$"-part-{part}.json");var game=Files.Object(Path.Combine(run,"ReportResults/Primary",id,"validation.json"))["GameIdentity"];
                Files.Save(request,new{Case=plan.Cases.Single(c=>c.Id==id),ExpectedGame=game,Profiles=profiles.Where((p,i)=>i%count==part),Epsilons=plan.Settings.Trembles,Directions=plan.Settings.TrembleDirections,Output=Path.Combine(output,"results",id+$"-part-{part}")});requests.Add(request);
            }
        }
        await Parallel.ForEachAsync(requests,new ParallelOptions{MaxDegreeOfParallelism=workers},async(request,ct)=>await Commands.Worker(Path.Combine(output,"logs"),Path.GetFileNameWithoutExtension(request),output,"worker-tremble",request));
        Files.Save(Path.Combine(output,"suite-completed.json"),new{Passed=true,Profiles=selected.Count,SolvesStarted=0});
        Directory.CreateDirectory(Path.Combine(output,"reports"));
        await Commands.Worker(Path.Combine(output,"logs"),"independent-response-checks",output,"worker-tremble-verify",output);
        Report(output,Path.Combine(output,"generated-reports"),bundle);
    }
    public static void Report(string stage,string output,string? referenceBundle=null)
    {
        if(Files.Object(Path.Combine(stage,"suite-completed.json"))["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Incomplete tremble stage.");
        if(Directory.Exists(output))throw new IOException("Fresh sensitivity report required.");
        var profiles=JsonNode.Parse(File.ReadAllText(Path.Combine(stage,"profiles.json")))!.AsArray().ToDictionary(p=>p!["Id"]!.GetValue<string>());
        var primary=profiles.Values.Where(p=>p!["Kind"]!.GetValue<string>()=="exact-primary").ToDictionary(p=>p!["CaseId"]!.GetValue<string>());
        var protocol=Files.Object(Path.Combine(stage,"protocol.json"));double maxEpsilon=protocol["Epsilons"]!.AsArray().Max(v=>v!.GetValue<double>());int directions=protocol["Directions"]!.GetValue<int>();
        var data=new List<Dictionary<string,object?>>();var summary=new List<Dictionary<string,object?>>();var files=new List<object>();
        string[] welfare=["PlaintiffShortfall","NonliableDefendantBurden","LiableDefendantExcess","GrossError","LitigationCosts"],dispositions=["NotFiled","NotAnswered","Settlement","Abandonment","Default","Trial"];
        foreach(string shard in Directory.GetDirectories(Path.Combine(stage,"results")).Order(StringComparer.Ordinal))
        {
            if(Files.Object(Path.Combine(shard,"completed.json"))["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Incomplete tremble shard.");
            foreach(string folder in Directory.GetDirectories(shard).Order(StringComparer.Ordinal))
            {
                string id=Path.GetFileName(folder);var original=profiles[id]!;string caseId=original["CaseId"]!.GetValue<string>(),risk=caseId.Split("__")[3],rule=caseId.Split("__")[2];
                var baseline=Files.Object(Path.Combine(folder,"baseline.json"));var reference=primary[$"baseline__standard__american__{risk}__cost-1"]!["Welfare"]!;
                string group=rule!="complete"?"American":baseline["Outcome"]!["Welfare"]![0]!.GetValue<double>()>reference["MeritoriousPlaintiffShortfall"]!.GetValue<double>()?"plaintiff reversal":baseline["Outcome"]!["Welfare"]![1]!.GetValue<double>()>reference["NonliableDefendantBurden"]!.GetValue<double>()?"defendant reversal":"other";
                var these=new List<Dictionary<string,object?>>();
                foreach(string file in Directory.GetFiles(folder,"p*.json").Order(StringComparer.Ordinal))
                {
                    var x=Files.Object(file);if(x["SelectionGapNormalized"]!.GetValue<double>()>1e-8||(x["Epsilon"]!.GetValue<double>()==0&&x["OriginalReachWeightedTV"]!.GetValue<double>()!=0))throw new InvalidDataException("Invalid selected response.");
                    var row=new Dictionary<string,object?>{{"Id",id},{"Kind",original["Kind"]!.GetValue<string>()},{"Risk",risk},{"Rule",rule},{"Group",group},{"Start",original["Start"]?.GetValue<int>()},{"Player",x["Player"]!.GetValue<int>()},{"Direction",x["Direction"]!.GetValue<int>()}};
                    foreach(string k in new[]{"Epsilon","NormalizedGain","BaselineNormalizedGain","IncrementalNormalizedGain","OriginalReachWeightedTV","PerturbedReachWeightedTV","OffPathMeanTV","SelectionGapNormalized"})row.Add(k,x[k]!.GetValue<double>());
                    for(int i=0;i<welfare.Length;i++){double v=x["ResponseOutcome"]!["Welfare"]![i]!.GetValue<double>();row.Add(welfare[i],v);row.Add(welfare[i]+"ChangeFromZeroResponse",v-x["BaselineResponseOutcome"]!["Welfare"]![i]!.GetValue<double>());}
                    foreach(string k in dispositions)row.Add(k+"ChangeFromZeroResponse",x["ResponseOutcome"]![k]!.GetValue<double>()-x["BaselineResponseOutcome"]![k]!.GetValue<double>());
                    these.Add(row);files.Add(new{Path=Path.GetRelativePath(stage,file).Replace('\\','/'),Sha256=Files.Sha(file)});
                }
                int expected=2*(1+directions*protocol["Epsilons"]!.AsArray().Count);
                if(these.Count!=expected||these.Select(r=>(r["Player"],r["Direction"],r["Epsilon"])).Distinct().Count()!=expected)throw new InvalidDataException("Missing/repeated tremble coordinates.");
                data.AddRange(these);var z=these.Where(r=>(double)r["Epsilon"]! ==maxEpsilon).ToArray();
                var pr=these[0].Take(6).ToDictionary(x=>x.Key,x=>x.Value);string suffix=maxEpsilon==.01?"At1Percent":"AtMaximumTremble";
                pr.Add("WorstAdditionalGain"+suffix,z.Max(r=>(double)r["IncrementalNormalizedGain"]!));pr.Add("WorstTotalGain"+suffix,z.Max(r=>(double)r["NormalizedGain"]!));pr.Add("WorstReachWeightedTV"+suffix,z.Max(r=>(double)r["OriginalReachWeightedTV"]!));pr.Add("WorstOffPathMeanTV"+suffix,z.Max(r=>(double)r["OffPathMeanTV"]!));
                pr.Add("LargestWelfareChange"+suffix,z.Max(r=>welfare.Max(k=>Math.Abs((double)r[k+"ChangeFromZeroResponse"]!))));pr.Add("LargestDispositionChange"+suffix,z.Max(r=>dispositions.Max(k=>Math.Abs((double)r[k+"ChangeFromZeroResponse"]!))));summary.Add(pr);
            }
        }
        if(summary.Count!=profiles.Count||summary.Select(r=>r["Id"]).Distinct().Count()!=profiles.Count)throw new InvalidDataException("Missing tremble profiles.");
        int identical=0;if(referenceBundle!=null)
        {
            var expected=Reports.ReadCsv(Path.Combine(referenceBundle,"inputs/expected-tremble-tests.csv")).ToDictionary(r=>(r["Id"],int.Parse(r["Player"]),int.Parse(r["Direction"]),double.Parse(r["Epsilon"])));
            foreach(var row in data)if(expected.TryGetValue(((string)row["Id"]!,(int)row["Player"]!,(int)row["Direction"]!,(double)row["Epsilon"]!),out var old)){Reports.ExactCsv(row,old,"Tremble report cell");identical++;}
        }
        Directory.CreateDirectory(output);Reports.Csv(Path.Combine(output,"profiles.csv"),summary);Reports.Csv(Path.Combine(output,"all-tremble-tests.csv"),data);
        var groups=summary.Where(r=>(string)r["Kind"]! =="approximate").GroupBy(r=>(r["Risk"],r["Rule"],r["Group"])).Select(g=>new{Risk=g.Key.Item1,Rule=g.Key.Item2,Group=g.Key.Item3,Profiles=g.Count(),Statistics=g.First().Keys.Skip(6).ToDictionary(k=>k,k=>{var v=g.Select(r=>(double)r[k]!).Order().ToArray();return new{Minimum=v[0],Median=v.Length%2==1?v[v.Length/2]:(v[v.Length/2-1]+v[v.Length/2])/2,Maximum=v[^1]};})});
        Files.Save(Path.Combine(output,"validation-and-summary.json"),new{Passed=true,Profiles=profiles.Count,Checks=data.Count,ExactReferenceRows=identical,Groups=groups,MaximumSelectionGap=data.Max(r=>(double)r["SelectionGapNormalized"]!),Protocol=protocol,Files=files,SolvesStarted=0});
        File.WriteAllText(Path.Combine(output,"Report.md"),$"# Equilibrium tremble sensitivity\n\n{profiles.Count} profiles and {data.Count} unilateral response checks. The original complete policies, including off-path actions, were retained. Opponent trembles mix each saved policy with uniform or fixed positive distributions; chance and own pre-response policies remain unchanged.\n\n[Per-profile statistics](profiles.csv) · [Every check](all-tremble-tests.csv) · [Protocol and validation](validation-and-summary.json)\n\nGains use the full terminal utility range. Behavior distances compare with the zero-tremble selected response, with original-reach and perturbed-reach weights reported separately. Tie selection retains original probabilities within the existing 1e-10 threshold and checks full regret at 1e-8. These are local response diagnostics, not new equilibria, a perfection certificate, or selection probabilities.\n");
    }
}
