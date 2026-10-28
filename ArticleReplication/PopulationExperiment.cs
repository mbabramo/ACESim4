using ACESim;
using ACESimBase.GameSolvingSupport.Evaluation;
using ACESimBase.GameSolvingSupport.Policies;
using ACESimBase.GameSolvingSupport.Population;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.Util.Debugging;
using System.Globalization;
using System.Text.Json;

namespace ArticleReplication;

/// <summary>Opt-in population experiment. Uses the final article factory and saved primary
/// reference, writes a separate collection, and never changes the published replication route.</summary>
public static class PopulationExperiment
{
    public sealed record SavedPolicy(string Name, byte Player, double[][] Probabilities);
    public sealed record Archive(string Schema, string GameHash, string[] Keys, SavedPolicy[] Policies, SavedPolicy[] Probes);
    private static readonly string[] FeatureNames = ["normalized plaintiff utility", "normalized defendant utility", "filing", "unconditional answering", "trial"];

    public static async Task Run(Dictionary<string, string> arguments)
    {
        string[] allowed = ["input", "output", "cases", "selectors", "iterations", "epsilon", "bandwidth", "floor", "seed-mode", "initial-seed", "seed-archive", "report-outcomes"];
        if (arguments.Keys.Except(allowed, StringComparer.OrdinalIgnoreCase).Any()) throw new ArgumentException("Unknown population option.");
        string Get(string key) => arguments.TryGetValue(key, out var value) ? value : throw new ArgumentException("Missing --" + key);
        string input = Path.GetFullPath(Get("input")), output = Path.GetFullPath(Get("output"));
        if (Directory.Exists(output) || Files.Nested(output, input) || Files.Nested(input, output)) throw new IOException("Use a fresh output directory separate from saved inputs.");
        string seedMode = arguments.GetValueOrDefault("seed-mode", "independent");
        int initialSeed = int.Parse(arguments.GetValueOrDefault("initial-seed", "1729"));
        if (seedMode is not ("article" or "independent")) throw new ArgumentException("Seed mode must be article or independent.");
        var settings = new JpsroSettings(int.Parse(arguments.GetValueOrDefault("iterations", "100")),
            double.Parse(arguments.GetValueOrDefault("epsilon", "0.00001"), CultureInfo.InvariantCulture),
            Affinity: new(double.Parse(arguments.GetValueOrDefault("bandwidth", "0.1"), CultureInfo.InvariantCulture),
                double.Parse(arguments.GetValueOrDefault("floor", "0.000001"), CultureInfo.InvariantCulture)));
        bool reportOutcomes = bool.Parse(arguments.GetValueOrDefault("report-outcomes", "true"));
        string[] selectors = arguments.GetValueOrDefault("selectors", "gini,liu").Split(',');
        if (selectors.Length == 0 || selectors.Distinct().Count() != selectors.Length || selectors.Any(x => x is not ("gini" or "liu"))) throw new ArgumentException("Selectors: gini,liu.");
        var plan = ArticlePlan.Resolve(new(), ArticlePlan.PublishedCalibration);
        string selection = arguments.GetValueOrDefault("cases", "baseline");
        var cases = selection switch
        {
            "baseline" => plan.Cases.Where(c => c.Family == "baseline").ToArray(),
            "main" => plan.Cases.Where(c => c.Family is "baseline" or "cost-multiplier").ToArray(),
            "all" => plan.Cases,
            _ => selection.Split(',').Select(id => plan.Cases.Single(c => c.Id == id)).ToArray()
        };
        if (cases.Length == 0 || cases.Select(c => c.Id).Distinct().Count() != cases.Length) throw new ArgumentException("Choose distinct article cases.");
        if (arguments.ContainsKey("seed-archive") && cases.Length != 1) throw new ArgumentException("A restart archive belongs to one game.");
        Directory.CreateDirectory(output); TabbedText.DisableOutput();
        Files.Save(Path.Combine(output, "plan.json"), new { Schema = "article-population-experiment-v1", Cases = cases, Settings = settings, Selectors = selectors, SeedMode = seedMode,
            InitialSeed = initialSeed, ProbeSeeds = new[] { 1729, 1730 }, Features = FeatureNames, FeatureWeights = Enumerable.Repeat(.2, 5),
            ProbeRule = "Frozen published primary, uniform, and two random policies per player; equal probe weights. Never enlarged during response expansion.",
            RateDenominators = "Filing, answering, trial: unconditional. Conditional answering is reported separately.",
            SolutionConcept = "Normal-form CCE, not Nash or recommendation-conditional CE", ReferenceFloor = "Explicit adaptation: mix affinity optimum with equal mass per exact signature class.",
            InputDirectory = input, ReportOutcomes = reportOutcomes, AssemblySha256 = Files.Sha(typeof(JpsroCoordinator).Assembly.Location) });
        var results = new List<object>();
        foreach (var spec in cases)
        {
            Console.WriteLine($"Preparing {spec.Id}"); string caseOutput = Path.Combine(output, spec.Id); Directory.CreateDirectory(caseOutput);
            var options = FinalArticleCaseFactory.Create(spec);
            var developer = await ArticleWorkedPathExtraction.InitializeAsync(options);
            string primaryFile = SolveShortcut.PrimaryPath(input, spec.Id);
            var saved = SolveShortcut.Read(primaryFile, spec.Id, "ExactPrimary", 0, plan.Settings);
            ArticleWorkedPathExtraction.LoadProfile(developer, saved.Probabilities);
            var primary = PolicyAccess.CaptureCurrent(developer, "published-primary");
            var fingerprint = StrategicGameFingerprint.Capture(developer); Files.Save(Path.Combine(caseOutput, "game.json"), new { Case = spec, Fingerprint = fingerprint, SavedPrimarySha256 = Files.Sha(primaryFile) });
            var tree = PolicyGameTree.Create(developer);
            var baselineValues = tree.Evaluate(primary.ForPlayer(0), primary.ForPlayer(1));
            var baselineGains = Enumerable.Range(0, 2).Select(p => tree.BestResponse((byte)p, [primary.ForPlayer((byte)(1 - p))], [1]).Utility - baselineValues[p]).ToArray();
            if (baselineGains.Any(g => Math.Abs(g) > 1e-7)) throw new ArithmeticException("Published primary fails the new unrestricted response oracle.");
            var baselineOutcomes = reportOutcomes ? PopulationOutcomes.Evaluate(developer, tree, options, [primary.ForPlayer(0)], [primary.ForPlayer(1)], [1], baselineValues) : null;
            Files.Save(Path.Combine(caseOutput, "baseline.json"), new { Values = baselineValues, Gains = baselineGains, Outcomes = baselineOutcomes, UtilityMinimum = tree.UtilityMinimum, UtilityScale = tree.UtilityScale });
            var seeds = new[] { new List<PopulationEntry>(), new List<PopulationEntry>() };
            var probes = new PlayerPolicy[2][];
            for (byte p = 0; p < 2; p++)
            {
                seeds[p].Add(new("published-primary", primary.ForPlayer(p)));
                seeds[p].Add(new("uniform", tree.Uniform(p)));
                var fixedBank = new List<PlayerPolicy>(seeds[p].Select(x => x.Policy));
                PlayerPolicy RandomPolicy(int seed)
                {
                    var random = new Random(seed);
                    return tree.MakePolicy(p, i => { var v = Enumerable.Range(0, tree.Layout[i].Probabilities.Count).Select(_ => -Math.Log(Math.Max(1e-15, random.NextDouble()))).ToArray(); double sum = v.Sum(); return v.Select(x => x / sum).ToArray(); });
                }
                for (int k = 0; k < 2; k++)
                {
                    var policy = RandomPolicy(initialSeed + k + 100 * p);
                    seeds[p].Add(new("random-" + k, policy));
                    fixedBank.Add(initialSeed == 1729 ? policy : RandomPolicy(1729 + k + 100 * p));
                }
                probes[p] = fixedBank.ToArray();
                if (seedMode == "independent") seeds[p].RemoveAt(0);
            }
            if (arguments.TryGetValue("seed-archive", out var archivePath))
            {
                var archive = Files.Read<Archive>(archivePath);
                if (archive.Schema != "population-policy-archive-v1" || archive.GameHash != fingerprint.CompleteSha256 || !archive.Keys.SequenceEqual(tree.Layout.Select(x => x.Key))) throw new InvalidDataException("Population archive belongs to a different game/layout.");
                for (byte p = 0; p < 2; p++)
                {
                    seeds[p] = archive.Policies.Where(x => x.Player == p).Select(x => new PopulationEntry(x.Name, Restore(tree, x))).ToList();
                    probes[p] = archive.Probes.Where(x => x.Player == p).Select(x => Restore(tree, x)).ToArray();
                }
            }
            double[] Features(double[] v) => [(v[0] - tree.UtilityMinimum[0]) / tree.UtilityScale[0], (v[1] - tree.UtilityMinimum[1]) / tree.UtilityScale[1], v[2], v[3], v[4]];
            var finalPopulations = new List<PopulationEntry>[2] { new(), new() };
            foreach (string selector in selectors)
            {
                string directory = Path.Combine(caseOutput, selector); Directory.CreateDirectory(directory);
                var coordinator = new JpsroCoordinator(tree, seeds[0], seeds[1], probes, Features);
                var result = coordinator.Run(selector == "gini" ? JointSelectionObjective.MaximumGini : JointSelectionObjective.RelativeEntropy, settings, step =>
                {
                    File.AppendAllText(Path.Combine(directory, "iterations.jsonl"), JsonSerializer.Serialize(step) + Environment.NewLine);
                    if (step.Iteration % 10 == 0 || step.NormalizedGains.Max() <= settings.MaximumNormalizedGain)
                        Console.WriteLine($"{spec.Id} {selector} iteration {step.Iteration}: {step.PlaintiffPolicies}x{step.DefendantPolicies}, gain {step.NormalizedGains.Max():G5}");
                });
                var archive = SaveArchive(tree, fingerprint.CompleteSha256, result.Plaintiffs.Concat(result.Defendants), probes);
                Files.Save(Path.Combine(directory, "population.json"), archive);
                var oracleCheck = CheckArticleOracle(developer, tree, result);
                Files.Save(Path.Combine(directory, "independent-oracle-check.json"), oracleCheck);
                var outcomes = reportOutcomes ? PopulationOutcomes.Evaluate(developer, tree, options, result.Plaintiffs.Select(x => x.Policy).ToArray(), result.Defendants.Select(x => x.Policy).ToArray(), result.JointWeights, result.ExpectedValues) : null;
                var record = new { CaseId = spec.Id, Selector = selector, result.SolutionConcept, result.Status, PlaintiffPolicies = result.Plaintiffs.Length, DefendantPolicies = result.Defendants.Length,
                    result.JointWeights, result.ExpectedValues, result.RawGains, result.NormalizedGains, result.References, result.Selection, Outcomes = outcomes,
                    Baseline = baselineOutcomes, BaselineValues = baselineValues, Iterations = result.History.Length, Settings = settings };
                Files.Save(Path.Combine(directory, "result.json"), record); results.Add(record);
                finalPopulations[0].AddRange(result.Plaintiffs); finalPopulations[1].AddRange(result.Defendants);
                Console.WriteLine($"Completed {spec.Id} {selector}: {result.Status}");
            }
            // Archive the union for controlled re-selection / further expansion with the same probes.
            Files.Save(Path.Combine(caseOutput, "union-population.json"), SaveArchive(tree, fingerprint.CompleteSha256,
                finalPopulations.SelectMany(x => x).DistinctBy(x => string.Join("|", x.Policy.InformationSets.Values.SelectMany(s => s.Probabilities).Select(BitConverter.DoubleToInt64Bits)) + "/" + x.Policy.Player), probes));
        }
        Files.Save(Path.Combine(output, "completed.json"), new { Passed = true, Cases = cases.Length, Runs = results.Count,
            Note = "Run completion does not imply equilibrium: inspect each Status and full-game gains.", Results = results });
    }
    private static SavedPolicy Save(PolicyGameTree tree, string name, PlayerPolicy policy) => new(name, policy.Player,
        tree.Layout.Where(s => s.Player == policy.Player).Select(s => policy.InformationSets[s.Key].Probabilities.ToArray()).ToArray());
    private static Archive SaveArchive(PolicyGameTree tree, string hash, IEnumerable<PopulationEntry> population, PlayerPolicy[][] probes) =>
        new("population-policy-archive-v1", hash, tree.Layout.Select(s => s.Key).ToArray(), population.Select(p => Save(tree, p.Name, p.Policy)).ToArray(),
            probes.SelectMany((ps, p) => ps.Select((policy, i) => Save(tree, "probe-" + i, policy))).ToArray());
    internal static PlayerPolicy Restore(PolicyGameTree tree, SavedPolicy policy)
    {
        if (policy.Probabilities.Length != tree.Layout.Count(s => s.Player == policy.Player)) throw new InvalidDataException("Incomplete saved player policy.");
        int i = 0; return tree.MakePolicy(policy.Player, _ => policy.Probabilities[i++]);
    }

    private static object CheckArticleOracle(StrategiesDeveloperBase developer, PolicyGameTree tree, JpsroResult result)
    {
        int n = result.Plaintiffs.Length, m = result.Defendants.Length;
        var marginal = new[] { new double[n], new double[m] };
        for (int i = 0; i < n; i++) for (int j = 0; j < m; j++)
        { marginal[0][i] += result.JointWeights[i * m + j]; marginal[1][j] += result.JointWeights[i * m + j]; }
        var mixed = BehavioralProfile.Combine("independent-marginal-check", developer.GameDefinition.OptionSetName,
            tree.Mix(result.Plaintiffs.Select(p => p.Policy).ToArray(), marginal[0]), tree.Mix(result.Defendants.Select(p => p.Policy).ToArray(), marginal[1]));
        var original = PolicyAccess.CaptureCurrent(developer, "restore");
        var old = new double[2];
        try
        {
            PolicyAccess.ApplyCurrent(developer, mixed);
            for (byte p = 0; p < 2; p++) old[p] = developer.CalculateBestResponse(p, ActionStrategies.CurrentProbability);
        }
        finally { PolicyAccess.ApplyCurrent(developer, original); }
        var ours = result.RawGains.Select((g, p) => g + result.ExpectedValues[p]).ToArray();
        var differences = old.Zip(ours, (a, b) => Math.Abs(a - b)).ToArray();
        if (differences.Max() > 1e-8) throw new ArithmeticException("Independent article and population response oracles disagree.");
        return new { Passed = true, ExistingArticleBestResponseUtilities = old, PopulationBestResponseUtilities = ours, Differences = differences,
            Explanation = "Existing GEBR evaluates each realization-equivalent opponent marginal; CCE gains subtract the joint-distribution utility, not product-marginal utility." };
    }
}
