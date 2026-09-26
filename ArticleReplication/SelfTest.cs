using ACESim;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArticleReplication;

public static class SelfTest
{
    public static Task Run(string bundle,string output)
    {
        var manifest=Bundle.Open(bundle);var tests=new List<string>();
        void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);tests.Add(name);}
        void Throws(Action action,string name){bool failed=false;try{action();}catch(Exception e)when(e is InvalidDataException or ArgumentException){failed=true;}Check(failed,name);}
        var settings=new CorrelatedSignalsSettings();var plan=ArticlePlan.Resolve(settings,manifest.Calibration);
        Check(plan.Cases.Length==74&&plan.Welfare.Length==36&&plan.Strategic.Length==150&&plan.ExpectedApproximateStarts==200,"Existing article has 74 cases, 36 welfare pairs, 150 strategic directions and 200 starts");
        var primary=Files.Read<PrimaryInput[]>(Path.Combine(bundle,"inputs/primary.json"));
        foreach(var old in primary)
        {
            var spec=plan.Cases.Single(x=>x.Id==old.CaseId);
            Files.EqualScience(JsonSerializer.SerializeToNode(old.Case,Files.Json),JsonSerializer.SerializeToNode(spec,Files.Json),old.CaseId);
            // This also exercises the original C# constructor, without initializing or solving a game.
            FinalArticleCaseFactory.Create(spec);
        }
        Check(primary.Length==72,"All 72 frozen case specifications exactly equal the central plan");
        var reduced=ArticlePlan.Resolve(settings with{MainCostMultipliers=[.5,1,2]},manifest.Calibration);
        Check(reduced.Cases.Length==66&&reduced.Welfare.Length==32&&reduced.Strategic.Length==134,"One cost setting propagates to case and comparison counts");
        var expanded=ArticlePlan.Resolve(settings with{MainCostMultipliers=[.5,.75,1,2]},manifest.Calibration);
        Check(expanded.Cases.Count(x=>x.CostMultiplier==.75)==4,"New cost produces all four risk/rule cases");
        foreach(var c in expanded.Cases.Where(x=>x.CostMultiplier==.75))FinalArticleCaseFactory.Create(c);
        tests.Add("Existing C# game factory accepts a new cost without an independent whitelist");
        Check(ArticlePlan.Resolve(settings with{IncludeExtensions=false,IncludeTrialOnly=false},manifest.Calibration).Cases.Length==20,"Main-only plan retains all main cost cases");
        ExternalJob[] external=[new("reserved","game-hash","ExactPrimary","AwaitExternalResult")];
        Check(Reuse.Decide("ExactPrimary","renamed","game-hash",ReplicationMode.FromScratch,true,false,true,external)==JobDisposition.AwaitExternalResult,"Scratch mode cannot bypass external reservation by renaming a case");
        Check(Reuse.Decide("ExactPrimary","reserved","changed",ReplicationMode.FromScratch,true,false,true,external)==JobDisposition.AwaitExternalResult,"External reservation also protects case ID");
        Check(Reuse.Decide("ExactPrimary","x","x",ReplicationMode.FromSolutions,true,false,false,[])==JobDisposition.MissingResult,"Missing saved solution does not start a solve");
        Check(Reuse.Decide("ExactPrimary","x","x",ReplicationMode.FromSolutions,false,false,true,[])==JobDisposition.Skipped,"Disabled step never dispatches");
        Throws(()=>ArticlePlan.Resolve(settings with{Article="EndogenousDisputes"},manifest.Calibration),"Unimplemented article is rejected");
        Throws(()=>ArticlePlan.Resolve(settings with{MainCostMultipliers=[1,1]},manifest.Calibration),"Duplicate scientific settings rejected");
        Throws(()=>Files.Under(bundle,"../escape"),"Traversal rejected");
        Throws(()=>Files.Under(bundle,"..\\escape"),"Windows traversal rejected on every platform");
        Throws(()=>Files.Under(bundle,"C:\\outside\\result.json"),"Windows absolute path rejected on every platform");
        Check(Files.LegacyBaseName(@"C:\receipts\result.json")=="result.json","Historical Windows receipt basename is portable");
        Throws(()=>Files.EqualScience(JsonNode.Parse("[0,1]"),JsonNode.Parse("[0,0.9999999999999999]"),"exact"),"Scientific equality has no tolerance");
        var science=primary.ToDictionary(p=>p.CaseId,p=>(Audit:Files.Object(Files.Under(bundle,p.ExpectedAudit)),Profile:Files.Object(Files.Under(bundle,p.ExpectedProfile))));
        string fixtures=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,Path.GetFileNameWithoutExtension(output)+"-exhibits");
        WelfareFigure.Generate(plan,science,fixtures);
        var generated=Files.Object(Path.Combine(fixtures,"Figures/Sources/Figure 7 - Welfare outcomes.generated-data.json"));
        var expected=Files.Object(Path.Combine(bundle,"records/Figures/Sources/Figure 7 - Welfare outcomes.json"));
        Files.EqualScience(expected["PlottedValues"],generated["PlottedValues"],"All default welfare plot data and scales");
        tests.Add("Native C# welfare figure reproduces all 100 plotted values and scales exactly");
        string changed=Path.Combine(fixtures,"changed-costs");WelfareFigure.Generate(reduced,science,changed);
        Check(Files.Object(Path.Combine(changed,"Figures/Sources/Figure 7 - Welfare outcomes.generated-data.json"))["PlottedValues"]!.AsArray().Count==60,"Changed costs regenerate 60 plotted values with three rows per risk panel");
        foreach(var p in primary)
        {
            var pair=science[p.CaseId];string native=StrategyExhibit.Generate(pair.Audit,pair.Profile);
            string old=File.ReadAllText(Path.Combine(bundle,"records/Results/Individual simulations",p.CaseId,"Sources/strategy.tex"));
            string Points(string text)=>string.Join("\n",System.Text.RegularExpressions.Regex.Matches(text,@"coordinates \{([^}]*)\}|(?m)^\d+ \d+ [^\r\n]+$").Select(m=>m.Value));
            // Compare parsed numeric policy coordinates, not language-specific floating formatting.
            double[] Numbers(string text)=>System.Text.RegularExpressions.Regex.Matches(Points(text),@"[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?").Select(m=>double.Parse(m.Value)).ToArray();
            Check(Numbers(old).SequenceEqual(Numbers(native)),"Native complete policy plot equals saved source: "+p.CaseId);
        }
        Files.Save(output,new{Passed=true,CreatedUtc=DateTime.UtcNow,Tests=tests,SolvesStarted=0});
        Console.WriteLine($"Passed {tests.Count} plan, cache and isolation checks; no solves.");return Task.CompletedTask;
    }
}
