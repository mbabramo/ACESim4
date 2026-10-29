using ACESim;
using ACESimBase.GameSolvingSupport.Evaluation;
using ACESimBase.GameSolvingSupport.Policies;
using ACESimBase.GameSolvingSupport.Population;
using ACESimBase.GameSolvingSupport.Reporting;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.Games.LitigGame.Options;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.Util.Debugging;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace ArticleReplication;

public static class EndogenousPopulationExperiment
{
    public sealed record Case(string Id, string Family, string OptionSetName, Dictionary<string, string> Parameters,
        string SavedProfile, string SavedProfileSha256, string SourceLog, string SourceLogSha256,
        Dictionary<string, string> Historical, Dictionary<string, string> HistoricalGivenDispute);
    public sealed record Catalog(string Schema, string ArticleCsv, string ArticleCsvSha256, Case[] Cases);
    public sealed record Outcomes(double[] SolverUtilities, double[] NativeUtilities, double Filing,
        double AnsweringUnconditional, double? AnsweringGivenFiling, double Trial, double Settlement,
        Dictionary<string, double> Measures, Dictionary<string, double?> GivenDispute, double TotalProbability);

    private sealed class ArticleLauncher : LitigGameEndogenousDisputesLauncher
    {
        public override GameOptions GetDefaultSingleGameOptions()
        {
            var options = LitigGameOptionsGenerator.PrecautionNegligenceGame(new PrecautionNegligenceOptionsGeneratorSettings
                { ParameterForMultipleOptions = 8, PredeterminedAbandonAndDefaults = false });
            options.PLiabilityNoiseStdev = options.DLiabilityNoiseStdev = options.CourtLiabilityNoiseStdev = .2;
            return options;
        }
    }
    private static Dictionary<string, LitigGameOptions>? optionSets;
    public static LitigGameOptions OptionsFor(Case spec)
    {
        optionSets ??= new ArticleLauncher().GetOptionsSets().Cast<LitigGameOptions>().ToDictionary(x => x.Name.Replace(',', '-'));
        return optionSets.TryGetValue(spec.OptionSetName, out var options) ? options : throw new InvalidDataException("Unknown endogenous article case: " + spec.OptionSetName);
    }
    public static async Task<StrategiesDeveloperBase> Initialize(Case spec)
    {
        var options = OptionsFor(spec);
        var settings = new EvolutionSettings
        {
            Algorithm = GameApproximationAlgorithm.GeneralizedVanilla, ParallelOptimization = false,
            CreateEFGFile = false, CreateEquilibriaFile = false, GenerateManualReports = false, GenerateReportsByPlaying = false,
            RoundOffLowProbabilitiesBeforeReporting = false, RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = false
        };
        return (StrategiesDeveloperBase)await new ArticleLauncher().GetInitializedDevelper(options, options.Name, settings);
    }
    public static Catalog LoadCatalog(string input)
    {
        var catalog = Files.Read<Catalog>(Path.Combine(input, "catalog.json"));
        if (catalog.Schema != "endogenous-population-inputs-v1" || catalog.Cases.Select(c => c.Id).Distinct().Count() != catalog.Cases.Length)
            throw new InvalidDataException("Invalid endogenous population catalog.");
        if (Files.Sha(catalog.ArticleCsv) != catalog.ArticleCsvSha256) throw new InvalidDataException("Changed article CSV.");
        return catalog;
    }

    public static async Task Run(Dictionary<string, string> args)
    {
        string[] allowed = ["input", "output", "cases", "selectors", "iterations", "epsilon", "bandwidth", "floor", "seed-mode", "initial-seed", "seed-archive", "report-outcomes", "validate-only", "archive-store", "reuse-cases-from"];
        if (args.Keys.Except(allowed, StringComparer.OrdinalIgnoreCase).Any()) throw new ArgumentException("Unknown endogenous population option.");
        string input = Path.GetFullPath(args["input"]), output = Path.GetFullPath(args["output"]);
        if (Directory.Exists(output) || Files.Nested(output, input) || Files.Nested(input, output)) throw new IOException("Use a fresh separate output directory.");
        var catalog = LoadCatalog(input);
        var selection = args.GetValueOrDefault("cases", "all");
        var cases = selection switch
        {
            "all" => catalog.Cases,
            "main" => catalog.Cases.Where(c => c.Family == "Baseline").ToArray(),
            "baseline" => catalog.Cases.Where(c => c.OptionSetName == "Risk Neutral CM 1 FSM 0 DM 1 LT 1 UPC 1E-05" || c.OptionSetName == "MODRA CM 1 FSM 0 DM 1 LT 1 UPC 1E-05").ToArray(),
            _ => selection.Split(',').Select(id => catalog.Cases.Single(c => c.Id == id)).ToArray()
        };
        if (cases.Length == 0 || cases.Select(c => c.Id).Distinct().Count() != cases.Length) throw new ArgumentException("Select distinct article cases.");
        string seedMode = args.GetValueOrDefault("seed-mode", "independent");
        if (seedMode is not ("independent" or "article")) throw new ArgumentException("Invalid seed mode.");
        int initialSeed = int.Parse(args.GetValueOrDefault("initial-seed", "1729"));
        var selectors = args.GetValueOrDefault("selectors", "gini,liu").Split(',');
        if (selectors.Length == 0 || selectors.Distinct().Count() != selectors.Length || selectors.Any(s => s is not ("gini" or "liu"))) throw new ArgumentException("Invalid selectors.");
        bool validationOnly = bool.Parse(args.GetValueOrDefault("validate-only", "false"));
        if (args.ContainsKey("seed-archive") && cases.Length != 1) throw new ArgumentException("A population archive belongs to one case.");
        if (!bool.Parse(args.GetValueOrDefault("report-outcomes", "true"))) throw new ArgumentException("The endogenous comparison requires economic outcomes.");
        var settings = new JpsroSettings(int.Parse(args.GetValueOrDefault("iterations", "150")),
            double.Parse(args.GetValueOrDefault("epsilon", "1e-7"), CultureInfo.InvariantCulture),
            Selection: new(ScaleQuadraticIncentives: true),
            Affinity: new(double.Parse(args.GetValueOrDefault("bandwidth", ".1"), CultureInfo.InvariantCulture),
                double.Parse(args.GetValueOrDefault("floor", "1e-6"), CultureInfo.InvariantCulture)));
        TabbedText.DisableOutput(); Directory.CreateDirectory(output);
        Files.Save(Path.Combine(output, "plan.json"), new { Schema = "endogenous-population-experiment-v1", Model = "endogenous", Cases = cases,
            Settings = settings, Selectors = selectors, SeedMode = seedMode, InitialSeed = initialSeed, InputDirectory = input, ReportOutcomes = true,
            CatalogSha256 = Files.Sha(Path.Combine(input, "catalog.json")), AssemblySha256 = Files.Sha(typeof(JpsroCoordinator).Assembly.Location),
            HarnessSha256 = Files.Sha(typeof(EndogenousPopulationExperiment).Assembly.Location),
            SeedArchiveSha256 = args.TryGetValue("seed-archive", out var seedArchive) ? Files.Sha(PopulationArchiveIO.PhysicalPath(seedArchive)) : null,
            ProbeSeeds = new[] { 1729, 1730 }, Features = new[] { "normalized plaintiff utility", "normalized defendant utility", "filing", "unconditional answering", "trial" },
            ProbeRule = "Fixed article current policy, uniform, and two random policies per player. Article policy is a probe, not an independent-start population member.",
            HistoricalPolicy = "CFR current policy in original InformationSets order, not sequence-form player-sorted coordinates. The article Exploitability column describes a different averaged policy.",
            SolutionConcept = "Normal-form approximate CCE over complete policies", RateDenominators = "Unconditional; separate ratios given a dispute. Ratios are formed after aggregation.",
            ValidationOnly = validationOnly });
        var results = new List<object>();
        string? reuse = args.GetValueOrDefault("reuse-cases-from");
        if (reuse != null && File.Exists(Path.Combine(reuse, "plan.json")))
        {
            var prior = Files.Object(Path.Combine(reuse, "plan.json"));
            var current = Files.Object(Path.Combine(output, "plan.json"));
            foreach (string key in new[] { "Model", "Cases", "Settings", "Selectors", "SeedMode", "InitialSeed", "CatalogSha256", "AssemblySha256", "HarnessSha256", "SeedArchiveSha256", "ValidationOnly" })
                if (!JsonNode.DeepEquals(prior[key], current[key])) throw new InvalidDataException("Changed reused case settings: " + key);
        }
        else reuse = null;
        int casesComplete = 0;
        foreach (var spec in cases)
        {
            if (new DriveInfo(Path.GetPathRoot(output)!).AvailableFreeSpace < 3L * 1024 * 1024 * 1024) throw new IOException("Less than 3 GiB free; preserve and resume completed cases after freeing storage.");
            WriteProgress(output, casesComplete, cases.Length, spec.Id);
            Console.WriteLine("Preparing " + spec.Id + " " + spec.OptionSetName);
            string folder = Path.Combine(output, spec.Id); Directory.CreateDirectory(folder);
            if (Files.Sha(spec.SavedProfile) != spec.SavedProfileSha256 || Files.Sha(spec.SourceLog) != spec.SourceLogSha256) throw new InvalidDataException("Changed historical policy or log.");
            if (reuse != null && File.Exists(Path.Combine(reuse, spec.Id, "case-completed.json")))
            {
                string previous = Path.Combine(reuse, spec.Id);
                var prior = Files.Object(Path.Combine(previous, "case-completed.json"));
                if (prior["Passed"]?.GetValue<bool>() != true) throw new InvalidDataException("Invalid reused case.");
                foreach (string source in Directory.EnumerateFiles(previous, "*", SearchOption.AllDirectories))
                {
                    string destination = Path.Combine(folder, Path.GetRelativePath(previous, source));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (source.EndsWith("population.json.gz", StringComparison.Ordinal))
                        PopulationArchiveIO.Save(destination[..^3], PopulationArchiveIO.Read(source), args.GetValueOrDefault("archive-store"));
                    else Files.CopyVerified(source, destination);
                }
                results.AddRange(prior["Results"]!.AsArray().Select(x => (object)x!.DeepClone()));
                casesComplete++; WriteProgress(output, casesComplete, cases.Length, null);
                Console.WriteLine("Reused completed case " + spec.Id); continue;
            }
            var options = OptionsFor(spec);
            ValidateLoggedParameters(options, File.ReadAllText(spec.SourceLog));
            var developer = await Initialize(spec);
            var tree = PolicyGameTree.Create(developer);
            var saved = FlatEquilibriumFiles.Read(spec.SavedProfile).Single();
            if (saved.Length != tree.Layout.Sum(s => s.Probabilities.Count)) throw new InvalidDataException("Historical CFR profile length differs.");
            ((GeneralizedVanilla)developer).SetInformationSetValues(saved);
            var primary = PolicyAccess.CaptureCurrent(developer, "article-current");
            var fingerprint = StrategicGameFingerprint.Capture(developer, roundChanceProbabilities: false);
            Files.Save(Path.Combine(folder, "game.json"), new { Case = spec, Fingerprint = fingerprint, SavedPrimarySha256 = spec.SavedProfileSha256,
                NumericConvention = "Unrounded CFR chance probabilities and terminal utilities; chance boundary roundoff outside [0,1] is clamped only within 1e-14.",
                tree.ChanceBoundaryCorrections, tree.MaximumChanceBoundaryCorrection,
                Grid = new { options.NumOffers, options.NumLiabilitySignals, options.NumLiabilityStrengthPoints } });
            var values = tree.Evaluate(primary.ForPlayer(0), primary.ForPlayer(1));
            var raw = Enumerable.Range(0, 2).Select(p => tree.BestResponse((byte)p, [primary.ForPlayer((byte)(1 - p))], [1]).Utility - values[p]).ToArray();
            var baseline = Evaluate(developer, tree, options, [primary.ForPlayer(0)], [primary.ForPlayer(1)], [1], values);
            var validation = ValidateBaseline(spec, baseline);
            Files.Save(Path.Combine(folder, "baseline.json"), new { Values = values, RawGains = raw,
                NormalizedGains = raw.Select((g, p) => Math.Max(0, g) / tree.UtilityScale[p]), Outcomes = baseline, Validation = validation,
                tree.UtilityMinimum, tree.UtilityScale, Historical = spec.Historical });
            var caseResults = validationOnly ? new List<object>() : PopulationExperiment.RunPrepared(spec.Id, folder, developer, tree, fingerprint.CompleteSha256,
                    primary, values, baseline, seedMode, initialSeed, selectors, settings, args.GetValueOrDefault("seed-archive")!,
                    (p, d, q, v) => Evaluate(developer, tree, options, p, d, q, v), args.GetValueOrDefault("archive-store"));
            Files.Save(Path.Combine(folder, "case-completed.json"), new { Passed = true, CaseId = spec.Id, Results = caseResults });
            results.AddRange(caseResults);
            casesComplete++; WriteProgress(output, casesComplete, cases.Length, null);
            Console.WriteLine("Completed case " + spec.Id);
        }
        Files.Save(Path.Combine(output, "completed.json"), new { Passed = true, Cases = cases.Length, Runs = results.Count, Results = results,
            Note = "Inspect individual statuses and deviations. Historical current-policy outcome replay is checked against printed article values." });
    }

    private static void WriteProgress(string output, int completed, int total, string? current)
    {
        string file = Path.Combine(output, "progress.json"), temporary = file + ".tmp";
        Files.Save(temporary, new { CasesComplete = completed, CasesTotal = total, CurrentCase = current, UpdatedUtc = DateTime.UtcNow }, replace: true);
        File.Move(temporary, file, overwrite: true);
    }

    public static Outcomes Evaluate(StrategiesDeveloperBase developer, PolicyGameTree tree, LitigGameOptions options,
        PlayerPolicy[] plaintiffs, PlayerPolicy[] defendants, double[] weights, double[] values)
    {
        var generator = (PrecautionNegligenceDisputeGenerator)options.LitigGameDisputeGenerator;
        double[] totals = new double[15]; double mass = 0;
        JointPolicyOutcomeReplay.Visit(developer, tree, plaintiffs, defendants, weights, (progress, w) =>
        {
            var p = (PrecautionNegligenceProgress)progress;
            double[] v = [p.PWelfare, p.DWelfare, p.PFiles ? 1 : 0, p.DAnswers ? 1 : 0, p.TrialOccurs ? 1 : 0,
                p.CaseSettles ? 1 : 0, p.DisputeArises ? 1 : 0, p.EngagesInActivity ? 1 : 0, p.RelativePrecautionLevel,
                p.OpportunityCost, p.HarmCost, p.TotalExpensesIncurred, p.AccidentWronglyCausallyAttributedToDefendant ? 1 : 0,
                p.RelativePrecautionLevel * generator.UnitPrecautionCost, p.EngagesInActivity ? 0 : generator.BenefitToDefendantOfActivity];
            for (int k = 0; k < totals.Length; k++) totals[k] += w * v[k];
            mass += w;
        });
        if (Math.Abs(mass - 1) > 1e-8 || Enumerable.Range(0, 2).Any(p => Math.Abs(totals[p] - values[p]) > Math.Max(1e-8, Math.Abs(values[p]) * 1e-10)))
            throw new ArithmeticException("Native endogenous replay disagrees with the policy evaluation.");
        var measures = new Dictionary<string, double>
        {
            ["DisputeProbability"] = totals[6], ["Engagement"] = totals[7], ["PrecautionLevel"] = totals[8],
            ["OpportunityCost"] = totals[9], ["HarmCost"] = totals[10], ["LitigationExpenditures"] = totals[11],
            ["WrongfulAttribution"] = totals[12], ["PrecautionCost"] = totals[13], ["ForegoneActivityCost"] = totals[14],
            ["SocialWelfareLoss"] = totals[9] + totals[10] + totals[11]
        };
        var conditional = new Dictionary<string, double?> { ["Filing"] = Divide(totals[2], totals[6]), ["Answering"] = Divide(totals[3], totals[6]),
            ["Trial"] = Divide(totals[4], totals[6]), ["Settlement"] = Divide(totals[5], totals[6]) };
        return new(values.Take(2).ToArray(), totals.Take(2).ToArray(), totals[2], totals[3], Divide(totals[3], totals[2]), totals[4], totals[5], measures, conditional, mass);
    }
    private static double? Divide(double x, double y) => y > 0 ? x / y : null;
    public static double PrintedTolerance(string value)
    {
        string[] parts = value.ToLowerInvariant().Split('e');
        int exponent = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        int dot = parts[0].IndexOf('.'), decimals = dot < 0 ? 0 : parts[0].Length - dot - 1;
        // Column widths can reduce the nominal six significant figures; use the
        // precision actually present in the CSV rather than assuming six digits.
        return Math.Max(1e-12, .51 * Math.Pow(10, exponent - decimals));
    }
    public static object ValidateBaseline(Case spec, Outcomes value)
    {
        var metrics = new Dictionary<string, double>
        {
            ["P Welfare"] = value.NativeUtilities[0], ["D Welfare"] = value.NativeUtilities[1], ["P Files"] = value.Filing,
            ["D Answers"] = value.AnsweringUnconditional, ["Trial"] = value.Trial, ["Settles"] = value.Settlement,
            ["Precaution Level"] = value.Measures["PrecautionLevel"], ["Opportunity Cost"] = value.Measures["OpportunityCost"],
            ["Harm Cost"] = value.Measures["HarmCost"], ["Expenditures"] = value.Measures["LitigationExpenditures"],
            ["Social Welfare Loss"] = value.Measures["SocialWelfareLoss"], ["Engages in Activity"] = value.Measures["Engagement"],
            ["Accident Occurs"] = value.Measures["DisputeProbability"], ["Wrongful Attribution"] = value.Measures["WrongfulAttribution"]
        };
        var differences = metrics.Select(x => new { Measure = x.Key, Replayed = x.Value,
            Article = double.Parse(spec.Historical[x.Key], CultureInfo.InvariantCulture), PrintedTolerance = PrintedTolerance(spec.Historical[x.Key]),
            Tolerance = Math.Max(PrintedTolerance(spec.Historical[x.Key]), Math.Max(1e-10,
                x.Key is "P Welfare" or "D Welfare" ? 0 : Math.Abs(double.Parse(spec.Historical[x.Key], CultureInfo.InvariantCulture)) * 1e-4)) }).ToArray();
        var failures = differences.Where(x => Math.Abs(x.Replayed - x.Article) > x.Tolerance).ToArray();
        if (failures.Length > 0) throw new ArithmeticException("Article replay differs for " + spec.OptionSetName + ": " + System.Text.Json.JsonSerializer.Serialize(failures));
        return new { Passed = true, Compared = differences, MaximumAbsoluteDifference = differences.Max(x => Math.Abs(x.Replayed - x.Article)),
            WithinPrintedPrecision = differences.All(x => Math.Abs(x.Replayed - x.Article) <= x.PrintedTolerance),
            Convention = "Keep the saved current policy unchanged. Check printed precision separately; permit bounded saved-profile/report discrepancies up to 0.01% or 1e-10 absolute. Published CSV values remain the paper comparison reference." };
    }
    private static void ValidateLoggedParameters(LitigGameOptions options, string log)
    {
        int checkedValues = 0;
        foreach (object obj in new object[] { options, options.LitigGameDisputeGenerator })
        {
            var members = obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => (f.Name, Value: f.GetValue(obj)))
                .Concat(obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(decimal))).Select(p => (p.Name, Value: p.GetValue(obj))));
            foreach (var (name, value) in members)
            {
                if (value == null || !(value.GetType().IsPrimitive || value is decimal)) continue;
                var match = Regex.Match(log, @"(?<![A-Za-z0-9_])" + Regex.Escape(name) + @"\s+(\S+)");
                if (!match.Success) continue;
                string recorded = match.Groups[1].Value;
                if (value is bool b)
                { if (!bool.TryParse(recorded, out bool actual)) continue; if (actual != b) throw new InvalidDataException("Historical boolean differs: " + name); }
                else
                { if (!double.TryParse(recorded, NumberStyles.Float, CultureInfo.InvariantCulture, out double actual)) continue;
                    if (Math.Abs(actual - Convert.ToDouble(value, CultureInfo.InvariantCulture)) > 1e-12) throw new InvalidDataException("Historical parameter differs: " + name); }
                checkedValues++;
            }
        }
        if (checkedValues < 25) throw new InvalidDataException("Insufficient historical model metadata.");
    }
}
