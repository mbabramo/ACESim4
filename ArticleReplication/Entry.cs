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
        string? priorPath=request.TryGetProperty("ExpectedAudit",out var expectedAudit)&&expectedAudit.ValueKind==JsonValueKind.String?expectedAudit.GetString():null;
        using var prior=priorPath==null?null:JsonDocument.Parse(File.ReadAllBytes(priorPath));
        if(prior!=null&&(!prior.RootElement.GetProperty("Passed").GetBoolean()||!prior.RootElement.GetProperty("CompleteStrategyUnchanged").GetBoolean()))throw new InvalidDataException("Failed regression audit");
        var spec=request.GetProperty("Case").Deserialize<FinalArticleCase>(json);
        var options=FinalArticleCaseFactory.Create(spec);
        var developer=await ArticleWorkedPathExtraction.InitializeAsync(options);
        if(request.TryGetProperty("GameIdentity",out var gameIdentity)&&StrategicGameFingerprint.Capture(developer)!=gameIdentity.Deserialize<StrategicGameFingerprint.Snapshot>())throw new InvalidDataException("Full game changed");
        string Input(string key)
        {
            var id=request.GetProperty("Inputs").GetProperty(key).Deserialize<FinalArticleExecution.FileIdentity>(json);
            return FinalArticleExecution.Verify(id);
        }
        bool compute=request.TryGetProperty("Compute",out var computeElement)&&computeElement.GetBoolean();
        string eq;
        if(compute||request.TryGetProperty("ShortcutFile",out _))
        {
            double[] saved;
            if(compute)
            {
                bool captureHistory=request.TryGetProperty("CaptureHistory",out var historyFlag)&&historyFlag.GetBoolean();
                using var capture=captureHistory?new ArticleReplication.SolutionHistory.Capture((ACESimBase.GameSolvingAlgorithms.SequenceForm)developer,Path.Combine(output,"solve.history")):null;
                var settings=developer.EvolutionSettings;settings.UseExistingEquilibriaIfAvailable=false;settings.CreateEquilibriaFile=false;
                settings.SequenceFormNumPriorsToUseToGenerateEquilibria=1;settings.ParallelOptimization=false;settings.TryInexactArithmeticForAdditionalEquilibria=false;
                settings.ConsiderInitializingToMostRecentEquilibrium=false;settings.CustomSequenceFormInitialization=false;settings.SequenceFormUseRandomSeed=false;
                await developer.RunAlgorithm(options.Name);saved=developer.GetEquilibriumFromInformationSets();
                // Preserve the completed solve even if subsequent history/report export fails.
                Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"equilibrium.equ"),string.Join(',',saved.Select(p=>p.ToString("R",CultureInfo.InvariantCulture))));
                capture?.Complete(spec.Id);
            }
            else saved=ArticleReplication.SolveShortcut.Read(request.GetProperty("ShortcutFile").GetString()!,spec.Id,"ExactPrimary",0,new()).Probabilities;
            Directory.CreateDirectory(output);eq=Path.Combine(output,"equilibrium.equ");File.WriteAllText(eq,string.Join(',',saved.Select(p=>p.ToString("R",CultureInfo.InvariantCulture))));
        }
        else eq=Input("Equilibrium");
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
        Directory.CreateDirectory(output);
        string actions=Path.Combine(output,"information-set-actions.csv");
        File.WriteAllText(actions,InformationSetActionReport.BuildCsv(developer,1));
        int actionRows=ArticleWorkedPathExtraction.ValidateActionReport(developer,1,actions);
        bool hasInputs=request.TryGetProperty("Inputs",out var inputFields);
        if(hasInputs&&inputFields.TryGetProperty("Actions",out _))ArticleWorkedPathExtraction.ValidateActionReport(developer,1,Input("Actions"));
        developer.CalculateBestResponse(false);
        var gains=developer.Status.BestResponseImprovement.ToArray();
        if(!developer.Status.BestResponseReflectsCurrentStrategy||gains.Length!=2||gains.Any(g=>!double.IsFinite(g)||Math.Abs(g)>1e-7))throw new InvalidDataException("Full unilateral BR failed");
        developer.SaveWeightedGameProgressesAfterEachReport=true;developer.SavedWeightedGameProgresses.Clear();
        developer.ActionStrategy=ActionStrategies.CurrentProbability;
        var replay=await developer.GenerateReportsByPlaying(false);
        var welfare=SavedProfileWelfare.Evaluate(options,developer.SavedWeightedGameProgresses);
        Directory.CreateDirectory(output);
        string report=Path.Combine(output,"replayed-report.csv");File.WriteAllText(report,replay.csvReports.Single());
        int? cells=hasInputs&&inputFields.TryGetProperty("Numeric",out _)?MultipleEquilibriaStrategyAudit.ValidateReplay(Input("Numeric"),report):null;
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
        if(prior!=null&&ArticleApproximateSearch.ProfileHash(complete)!=prior.RootElement.GetProperty("CompleteStrategySha256").GetString())throw new InvalidDataException("Saved strategy identity changed");
        object Identity(string path)=>new FinalArticleExecution.FileIdentity(Path.GetFullPath(path),FinalArticleExecution.Hash(path));
        string savedEquilibrium=Path.Combine(output,"equilibrium.equ");if(eq!=savedEquilibrium)File.Copy(eq,savedEquilibrium);
        var generatedInputs=new Dictionary<string,object>{{"Equilibrium",Identity(savedEquilibrium)},{"Actions",Identity(actions)},{"Numeric",Identity(report)}};
        File.WriteAllText(Path.Combine(output,"validation.json"),JsonSerializer.Serialize(new{
            Schema="validated-final-profile-v1",Passed=true,CaseId=spec.Id,Case=spec,
            OptionSetName=options.Name,GameIdentity=StrategicGameFingerprint.Capture(developer),
            FullBestResponseGains=gains,MaximumGain=Math.Max(0,gains.Max()),ActionRows=actionRows,ReproducedNumericCells=cells,
            CompleteStrategySha256=ArticleApproximateSearch.ProfileHash(complete),CompleteStrategyUnchanged=true,
            UnspecifiedOffPathInformationSets=fallbacks.OrderBy(x=>x).ToArray(),Welfare=welfare,
            Inputs=hasInputs?(object)inputFields:generatedInputs,GeneratedInputs=generatedInputs,Outputs=Directory.GetFiles(output,"*",SearchOption.AllDirectories).OrderBy(x=>x).Select(Identity).ToArray(),
            GameAssembly=Identity(typeof(LitigGame).Assembly.Location),ReportingAssembly=Identity(typeof(Entry).Assembly.Location),
            OriginalValidation=priorPath==null?null:Identity(priorPath),SolvesStarted=compute?1:0},json));
        Console.WriteLine($"Revalidated {spec.Id}: {actionRows} actions, {cells} numeric cells.");
    }
}
