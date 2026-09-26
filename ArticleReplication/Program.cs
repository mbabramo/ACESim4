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
            {Console.WriteLine("ArticleReplication pack|plan|reproduce|self-test. Explicit commands only; no default solver launch.");return 0;}
            if(args[0]=="worker-primary")return await Entry.Main(["primary",args[1]]);
            if(args[0]=="worker-history"){Histories.Worker(args[1]);return 0;}
            if(args[0]=="worker-welfare")return await Entry.Main(new[]{"welfare"}.Concat(args.Skip(1)).ToArray());
            var options=Parse(args.Skip(1).ToArray());string Get(string key)=>options.TryGetValue(key,out var v)?v:throw new ArgumentException("Missing --"+key);
            switch(args[0])
            {
                case "pack":Bundle.Pack(Get("article"),Get("reproduction"),Get("output"));return 0;
                case "pack-histories":Histories.Pack(Get("request"),Get("output"));return 0;
                case "histories":await Histories.Run(Get("solutions"),Get("primary-run"),Get("output"));return 0;
                case "plan":
                {
                    string bundle=Get("solutions");var m=Bundle.Open(bundle);
                    var settings=options.TryGetValue("settings",out var s)?Files.Read<CorrelatedSignalsSettings>(s):new();
                    var plan=ArticlePlan.Resolve(settings,m.Calibration);Files.Save(Get("output"),plan);
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
