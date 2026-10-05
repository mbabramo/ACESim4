using System.Text.Json.Nodes;

namespace ArticleReplication;

/// <summary>Release checks against published results; never an input to replication.</summary>
public static class ContainerRelease
{
    public const string Image = "ghcr.io/mbabramo/acesim-correlated-signals:2026-10-04.1";
    public const string Instructions = "https://github.com/mbabramo/ACESim4/blob/correlated-signals/ArticleReplication/INSTALL.md";

    public static void Verify(string run,string reference,string output)
    {
        var reporting=new ReportingComparison();
        var completed=Files.Object(Path.Combine(run,"completed.json"));
        if(completed["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Completed full run required.");
        foreach(string key in new[]{"ReservedExternalCases","MissingCases","ExactSolves","HistoryOnlySolves","ApproximateSolves"})
            if(completed[key]?.GetValue<int>()!=0)throw new InvalidDataException(key+" must be zero for a saved-solutions release test.");
        var expected=ArticlePlan.Resolve(new CorrelatedSignalsSettings(),ArticlePlan.PublishedCalibration);
        var plan=Files.Read<ResolvedArticlePlan>(Path.Combine(run,"resolved-plan.json"));
        if(!plan.Cases.Select(c=>c.Id).SequenceEqual(expected.Cases.Select(c=>c.Id))||!plan.Steps.SequenceEqual(expected.Steps))
            throw new InvalidDataException("Release test must use the complete default article plan.");
        if(completed["PrimaryProfiles"]!.GetValue<int>()!=expected.Cases.Length)throw new InvalidDataException("Missing profiles.");
        string collection=Path.Combine(run,"article");
        if(Directory.Exists(Path.Combine(collection,"Article and bibliography")))throw new InvalidDataException("Default run generated a manuscript.");
        foreach(string relative in new[]{"primary-validation.json","welfare-validation.json","rendering.json","standard-coverage.json","utility-curves-validation.json","ReportResults/Trembles/suite-completed.json","ReportResults/Strategic/validation.json","ReportResults/Histories/completed.json","article/Results/Aggregated Data/Truth sensitivity/validation.json"})
            if(Files.Object(Path.Combine(run,relative))["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Failed stage: "+relative);
        void Profile(string a,string b)
        {
            var x=Files.Object(a);var y=Files.Object(b);
            reporting.Profile(x,y,"Profile: "+a);
        }
        void Fields(JsonObject a,JsonObject b,params string[] keys)
        {foreach(string key in keys)Files.EqualScience(a[key],b[key],key);}
        ComparePrimary(collection,reference,expected.Cases,reporting);
        string multiple="Supplemental materials/Multiple equilibria/Sources";
        var current=Files.Object(Path.Combine(collection,multiple,"catalog.json"))["Attempts"]!.AsArray();
        var previous=Files.Object(Path.Combine(reference,multiple,"catalog.json"))["Attempts"]!.AsArray().ToDictionary(r=>(r!["CaseId"]!.GetValue<string>(),r["StartIndex"]!.GetValue<int>()));
        if(current.Count!=expected.ExpectedApproximateStarts||current.Count!=previous.Count)throw new InvalidDataException("Incomplete search records.");
        int accepted=0;
        foreach(var node in current)
        {
            var a=node!.AsObject();string id=a["CaseId"]!.GetValue<string>();int start=a["StartIndex"]!.GetValue<int>();var b=previous[(id,start)]!.AsObject();
            Fields(a,b,"Accepted");
            if(a["Accepted"]!.GetValue<bool>())
            {
                string name=id+$"-start-{start:D5}.json";
                Profile(Path.Combine(collection,multiple,"Profiles",name),Path.Combine(reference,multiple,"Profiles",name));
                Fields(a,b,"FullBestResponseRawGains","AverageGain","Threshold","StoppingPivot");
                reporting.Compare(a["Welfare"],b["Welfare"],id+" start "+start+" Welfare");accepted++;
            }
            else Fields(a,b,"Decision");
        }
        var table=Files.Object(Path.Combine(collection,"Tables/Sources",MainTables.Summary+".generated-data.json"));
        var oldTable=Files.Object(Path.Combine(reference,"Tables/Sources",MainTables.Summary+".generated-data.json"));
        reporting.Compare(table["Comparisons"],oldTable["Comparisons"],"Every outcome-summary comparison");
        if(table["Comparisons"]!.AsArray().Any(c=>c!["Status"]!.GetValue<string>()!="audited"))throw new InvalidDataException("Pending outcome-summary row.");
        var inventory=Files.Object(Path.Combine(collection,"Results/Aggregated Data/reporting-inventory.json"))["Files"]!.AsArray().Select(n=>n!.GetValue<string>()).Append("Results/Aggregated Data/reporting-inventory.json").ToHashSet(StringComparer.Ordinal);
        var actual=Directory.GetFiles(collection,"*",SearchOption.AllDirectories).Select(f=>Path.GetRelativePath(collection,f).Replace('\\','/')).ToHashSet(StringComparer.Ordinal);
        if(!actual.SetEquals(inventory))throw new InvalidDataException("Collection inventory differs from generated files.");
        Files.Save(output,new{Passed=true,PrimaryProfiles=expected.Cases.Length,SearchAttempts=current.Count,AcceptedSearchProfiles=accepted,
            CompleteStrategiesAndBestResponsesExactlyIdentical=true,ExactSolverChecksUnchanged=true,
            ReportingAbsoluteTolerance=ReportingComparison.AbsoluteTolerance,ReportingDifferenceCount=reporting.Differences.Count,
            MaximumReportingDifference=reporting.Differences.Count==0?0:reporting.Differences.Max(d=>d.AbsoluteDifference),ReportingDifferences=reporting.Differences,
            ExplainedDisplayDifferences=reporting.DisplayDifferences,
            ExpensiveSolvesStarted=0,ManuscriptGenerated=false,GeneratedFiles=actual.Count,OutcomeSummaryComparisons=table["Comparisons"]!.AsArray().Count,AllDefaultStagesPassed=true,ReferenceUsedOnlyForReleaseRegression=true});
    }

    static void ComparePrimary(string collection,string reference,IEnumerable<ACESim.FinalArticleCase> cases,ReportingComparison reporting)
    {
        foreach(var c in cases)
        {
            string relative="Results/Individual simulations/"+c.Id+"/Sources";
            string a=Path.Combine(collection,relative),b=Path.Combine(reference,relative);
            reporting.Profile(Files.Object(Path.Combine(a,"complete-profile.json")),Files.Object(Path.Combine(b,"complete-profile.json")),c.Id+" profile");
            var audit=Files.Object(Path.Combine(a,"individual-audit.json"));var referenceAudit=Files.Object(Path.Combine(b,"individual-audit.json"));
            foreach(string key in new[]{"GameIdentity","FullBestResponseGains","CompleteStrategySha256","UnspecifiedOffPathInformationSets"})
                Files.EqualScience(audit[key],referenceAudit[key],c.Id+" "+key);
            reporting.Compare(audit["Welfare"],referenceAudit["Welfare"],c.Id+" Welfare");
            var x=Reports.ReadCsv(Path.Combine(a,"replayed-report.csv"));var y=Reports.ReadCsv(Path.Combine(b,"replayed-report.csv"));
            var fullX=Reports.ReadCsv(Path.Combine(a,"replayed-report-full-precision.csv"));var fullY=Reports.ReadCsv(Path.Combine(b,"replayed-report-full-precision.csv"));
            reporting.Csv(x,y,fullX,fullY,c.Id+" numeric reports");
        }
    }

    public static void VerifyPrimary(string collection,string reference,string output)
    {
        var reporting=new ReportingComparison();var expected=ArticlePlan.Resolve(new CorrelatedSignalsSettings(),ArticlePlan.PublishedCalibration);
        ComparePrimary(collection,reference,expected.Cases,reporting);
        Files.Save(output,new{Passed=true,Scope="Primary release regression only; not a complete container release",PrimaryProfiles=expected.Cases.Length,
            CompleteStrategiesAndBestResponsesExactlyIdentical=true,ReportingAbsoluteTolerance=ReportingComparison.AbsoluteTolerance,
            ReportingDifferenceCount=reporting.Differences.Count,ReportingDifferences=reporting.Differences,ExplainedDisplayDifferences=reporting.DisplayDifferences});
    }
}
