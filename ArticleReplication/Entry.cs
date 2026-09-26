using System.Globalization;
using System.Text.Json;
using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.Games.LitigGame.ManualReports;
using LitigCharts;

internal static class Entry
{
    public static async Task<int> Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        ACESimBase.Util.Debugging.TabbedText.DisableOutput();
        if(Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT")!="1") throw new InvalidDataException("Single-thread process required");
        switch(args[0])
        {
            case "tremble": await Tremble.Main(args.Skip(1).ToArray()); return 0;
            case "verify": await Verify.Main(args.Skip(1).ToArray()); return 0;
            case "primary": await Primary(args[1]); return 0;
            case "welfare": return await WelfareDecompositionCommand.RunAsync(args.Skip(1).ToArray());
            default: throw new ArgumentException("Expected primary, tremble, verify or welfare");
        }
    }
    static async Task Primary(string file)
    {
        var json=FinalArticleExecution.Json;
        using var doc=JsonDocument.Parse(File.ReadAllBytes(file));var request=doc.RootElement;
        string output=request.GetProperty("Output").GetString();
        if(Directory.Exists(output))throw new IOException("Fresh output required");
        using var prior=JsonDocument.Parse(File.ReadAllBytes(request.GetProperty("ExpectedAudit").GetString()));
        var audit=prior.RootElement;
        if(!audit.GetProperty("Passed").GetBoolean()||!audit.GetProperty("CompleteStrategyUnchanged").GetBoolean())throw new InvalidDataException("Unaudited input");
        var spec=request.GetProperty("Case").Deserialize<FinalArticleCase>(json);
        var options=FinalArticleCaseFactory.Create(spec);
        var developer=await ArticleWorkedPathExtraction.InitializeAsync(options);
        if(StrategicGameFingerprint.Capture(developer)!=request.GetProperty("GameIdentity").Deserialize<StrategicGameFingerprint.Snapshot>())throw new InvalidDataException("Full game changed");
        string Input(string key)
        {
            var id=request.GetProperty("Inputs").GetProperty(key).Deserialize<FinalArticleExecution.FileIdentity>(json);
            return FinalArticleExecution.Verify(id);
        }
        string eq=Input("Equilibrium"),actions=Input("Actions"),numeric=Input("Numeric");
        var lines=File.ReadAllLines(eq).Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray();
        if(lines.Length!=1)throw new InvalidDataException("One full primary profile required");
        var vector=lines[0].Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray();
        var fallbacks=ArticleWorkedPathExtraction.LoadProfile(developer,vector);
        var complete=developer.GetEquilibriumFromInformationSets();
        if(!vector.SequenceEqual(complete))throw new InvalidDataException("Complete vector changed");
        developer.EvolutionSettings.UseAcceleratedBestResponse=true;
        developer.EvolutionSettings.UseCurrentStrategyForBestResponse=true;
        developer.EvolutionSettings.ParallelOptimization=false;
        developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse=false;
        developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting=false;
        int actionRows=ArticleWorkedPathExtraction.ValidateActionReport(developer,1,actions);
        developer.CalculateBestResponse(false);
        var gains=developer.Status.BestResponseImprovement.ToArray();
        if(!developer.Status.BestResponseReflectsCurrentStrategy||gains.Length!=2||gains.Any(g=>!double.IsFinite(g)||Math.Abs(g)>1e-7))throw new InvalidDataException("Full unilateral BR failed");
        developer.SaveWeightedGameProgressesAfterEachReport=true;developer.SavedWeightedGameProgresses.Clear();
        developer.ActionStrategy=ActionStrategies.CurrentProbability;
        var replay=await developer.GenerateReportsByPlaying(false);
        var welfare=SavedProfileWelfare.Evaluate(options,developer.SavedWeightedGameProgresses);
        Directory.CreateDirectory(output);
        string report=Path.Combine(output,"replayed-report.csv");File.WriteAllText(report,replay.csvReports.Single());
        int cells=MultipleEquilibriaStrategyAudit.ValidateReplay(numeric,report);
        if(request.TryGetProperty("StandardReports",out var standard)&&standard.GetBoolean())
        {
            string directory=Path.Combine(output,"StandardReports");Directory.CreateDirectory(directory);
            var manual=developer.GameDefinition.ProduceManualReports(developer.SavedWeightedGameProgresses,"").ToArray();
            if(manual.Count(x=>Path.GetExtension(x.suffix)==".tex")!=6)throw new InvalidDataException("Expected the six standard litigation report diagrams.");
            foreach(var item in manual)
            {
                string name=item.suffix.TrimStart('-');
                if(Path.GetFileName(name)!=name)throw new InvalidDataException("Unsafe manual report name.");
                File.WriteAllText(Path.Combine(directory,name),item.reportcontent);
            }
            File.WriteAllText(Path.Combine(directory,"report.csv"),replay.csvReports.Single());
        }
        AgreementToBargainStudy.ExportProfile(developer,options,1,eq,actions,report,output,fallbacks,()=>FinalArticleCaseFactory.Create(spec));
        if(!complete.SequenceEqual(developer.GetEquilibriumFromInformationSets()))throw new InvalidDataException("Reporting changed strategy");
        if(ArticleApproximateSearch.ProfileHash(complete)!=audit.GetProperty("CompleteStrategySha256").GetString())throw new InvalidDataException("Saved strategy identity changed");
        object Identity(string path)=>new FinalArticleExecution.FileIdentity(Path.GetFullPath(path),FinalArticleExecution.Hash(path));
        File.WriteAllText(Path.Combine(output,"validation.json"),JsonSerializer.Serialize(new{
            Schema="validated-final-profile-v1",Passed=true,CaseId=spec.Id,Case=spec,
            OptionSetName=audit.GetProperty("OptionSetName").GetString(),GameIdentity=StrategicGameFingerprint.Capture(developer),
            FullBestResponseGains=gains,MaximumGain=Math.Max(0,gains.Max()),ActionRows=actionRows,ReproducedNumericCells=cells,
            CompleteStrategySha256=ArticleApproximateSearch.ProfileHash(complete),CompleteStrategyUnchanged=true,
            UnspecifiedOffPathInformationSets=fallbacks.OrderBy(x=>x).ToArray(),Welfare=welfare,
            Inputs=request.GetProperty("Inputs"),Outputs=Directory.GetFiles(output,"*",SearchOption.AllDirectories).OrderBy(x=>x).Select(Identity).ToArray(),
            GameAssembly=Identity(typeof(LitigGame).Assembly.Location),ReportingAssembly=Identity(typeof(Entry).Assembly.Location),
            OriginalValidation=Identity(request.GetProperty("ExpectedAudit").GetString()),SolvesStarted=0},json));
        Console.WriteLine($"Revalidated {spec.Id}: {actionRows} actions, {cells} numeric cells.");
    }
}
