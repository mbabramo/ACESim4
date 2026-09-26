using System.Globalization;

namespace ArticleReplication;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;CultureInfo.CurrentUICulture=CultureInfo.InvariantCulture;
        try
        {
            if(args.Length==0||args[0] is "help" or "--help")
            {Console.WriteLine("ArticleReplication doctor|container-build|pack|pack-histories|plan|reproduce|rebuild|histories|self-test|verify-standard. Explicit commands only; no default solver launch.");return 0;}
            if(args[0]=="worker-primary")return await Entry.Main(["primary",args[1]]);
            if(args[0]=="worker-approximate-cache"){await ApproximateCache.Worker(args[1]);return 0;}
            if(args[0]=="worker-tremble"){await Tremble.Main([args[1]]);return 0;}
            if(args[0]=="worker-tremble-verify"){await Verify.Main([args[1]]);return 0;}
            if(args[0]=="worker-history"){Histories.Worker(args[1]);return 0;}
            if(args[0]=="worker-welfare")return await Entry.Main(new[]{"welfare"}.Concat(args.Skip(1)).ToArray());
            var options=Parse(args.Skip(1).ToArray());string Get(string key)=>options.TryGetValue(key,out var v)?v:throw new ArgumentException("Missing --"+key);
            switch(args[0])
            {
                case "export-primary-cache":PrimaryCache.Export(Get("run"),Get("output"));return 0;
                case "test-primary-cache-rejections":await PrimaryCache.NegativeTests(Get("solutions"),Get("output"));return 0;
                case "verify-primary-reproduction":PrimaryCache.Compare(Get("generated"),Get("reference"),Get("output"));return 0;
                case "trembles":
                {int n=int.Parse(options.GetValueOrDefault("workers","1")),other=int.Parse(options.GetValueOrDefault("other-workers","0"));if(n<1||other<0||n+other>32)throw new InvalidDataException("Shared worker ceiling is 32.");Bundle.Open(Get("solutions"));await TrembleStage.Run(Get("solutions"),Files.Read<ResolvedArticlePlan>(Path.Combine(Get("run"),"resolved-plan.json")),Get("run"),Get("output"),n);return 0;}
                case "strategic-tables":await StrategicReports.FromRun(Get("run"),Get("output"));return 0;
                case "verify-strategic-tables":StrategicReports.Verify(Get("generated"),Get("reference"),Get("output"));return 0;
                case "multiple-reports":await MultipleReports.FromRun(Get("run"),Get("output"));return 0;
                case "verify-multiple-reports":MultipleReports.Verify(Get("generated"),Get("reference"),Get("output"));return 0;
                case "doctor":await Prerequisites.Run(Get("output"));return 0;
                case "main-figures":await MainFigures.FromValidatedRun(Get("primary-run"),Get("output"));return 0;
                case "main-tables":await MainFigures.FromValidatedRun(Get("primary-run"),Get("output"),true);return 0;
                case "verify-main-tables":MainTables.Verify(Get("generated"),Get("reference"),Get("output"));return 0;
                case "verify-main-figures":MainFigureVerification.Run(Get("generated"),Get("reference"),Get("output"));return 0;
                case "container-build":await ContainerBuild.Run(options);return 0;
                case "pack":Bundle.Pack(Get("article"),Get("reproduction"),Get("output"));return 0;
                case "pack-computations":ComputationBundle.Pack(Get("legacy-solutions"),Get("output"),options.GetValueOrDefault("approximate-inputs"),options.GetValueOrDefault("histories"));return 0;
                case "pack-approximate":ApproximateCache.Pack(Get("legacy-solutions"),Get("output"));return 0;
                case "pack-histories":Histories.Pack(Get("request"),Get("output"));return 0;
                case "histories":await Histories.Run(Get("solutions"),Get("primary-run"),Get("output"));return 0;
                case "plan":
                {
                    string bundle=Get("solutions");var m=Bundle.Open(bundle);
                    var settings=options.TryGetValue("settings",out var s)?Files.Read<CorrelatedSignalsSettings>(s):new();
                    if(m.Calibration!=ArticlePlan.PublishedCalibration)throw new InvalidDataException("Proposed inputs cannot redefine the article calibration.");
                    var plan=ArticlePlan.Resolve(settings,ArticlePlan.PublishedCalibration);Files.Save(Get("output"),plan);
                    Console.WriteLine($"{plan.Cases.Length} primary cases; {plan.Welfare.Length} welfare pairs; {plan.Strategic.Length} strategic directions; {plan.ExpectedApproximateStarts} starts.");return 0;
                }
                case "self-test":await SelfTest.Run(Get("solutions"),Get("output"));return 0;
                case "reproduce":await Pipeline.Run(options);return 0;
                case "rebuild":await Rebuild.Run(options);return 0;
                case "verify-standard":StandardCoverage.Validate(Get("request"),Get("output"));return 0;
                default:throw new ArgumentException("Unknown command; no implicit solve: "+args[0]);
            }
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    public static Dictionary<string,string> Parse(string[] args)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<args.Length;i+=2)
            if(i+1>=args.Length||!args[i].StartsWith("--")||!result.TryAdd(args[i][2..],args[i+1]))throw new ArgumentException("Expected unique --option value pairs.");
        return result;
    }
}
