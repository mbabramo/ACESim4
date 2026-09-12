using ACESim;
using ACESimBase.Games.LitigGame;
using ACESimBase.GameSolvingAlgorithms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LitigCharts
{
    public sealed record PureTerminalMetrics(double[] Outcomes, int[] ReachedActions);
    public sealed record PureMetricCache(string Fingerprint, string[] OutcomeNames, int StrategyFeatureCount, PureTerminalMetrics[] Terminals);
    public sealed record PureRetainedProfile(PureProfileScore Score, double[] Outcomes, double[] StrategyFeatures);
    public sealed record PureCluster(int Id, int RepresentativeRow, int RepresentativeColumn, int Count);
    public sealed record PureClustering(PureCluster[] Clusters, int[] Assignments);

    /// <summary>Postprocessing of exhaustive matrices. Filtering always precedes clustering.</summary>
    public static class EnumeratedPureReport
    {
        public static readonly string[] OutcomeNames = { "Filed", "Answered", "Settlement", "Abandonment", "Default", "Trial",
            "Expenditures", "PlaintiffNetMoney", "DefendantNetMoney", "DefendantExcessNetBurden", "PlaintiffRecoveryShortfall", "NetOutcomeFidelityLoss",
            "PlaintiffNetLitigationExpense", "DefendantNetLitigationExpense" };

        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0 || args[0] is not ("run" or "report"))
                throw new ArgumentException("Usage: pure run --output <new directory> [--signals 5 --offers 5 --fee both --workers 1 --verify 12 --verify-all]; pure report --input <run directory> --output <new report directory> [--tolerances 0,0.001,0.005,0.01,0.025].");
            var parameters = LitigGameEnumeratedPureLauncher.ParseArguments(args.Skip(1).ToArray());
            var originalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                GameProgressLogger.LoggingOn = false; GameProgressLogger.DetailedLogging = false;
                if (args[0] == "run")
                {
                    string repository = parameters.GetValueOrDefault("repository", Directory.GetCurrentDirectory());
                    string source = LitigGameEnumeratedPureLauncher.CaptureSourceState(repository);
                    int n = LitigGameEnumeratedPureLauncher.Integer(parameters, "signals", 5), m = LitigGameEnumeratedPureLauncher.Integer(parameters, "offers", 5);
                    int workers = LitigGameEnumeratedPureLauncher.Integer(parameters, "workers", 1);
                    string root = Path.GetFullPath(LitigGameEnumeratedPureLauncher.Required(parameters, "output"));
                    string fee = parameters.GetValueOrDefault("fee", "both");
                    if (fee is not ("both" or "American" or "British")) throw new ArgumentException("Invalid fee regime.");
                    foreach (bool british in fee == "both" ? new[] { false, true } : new[] { fee == "British" })
                    {
                        var timer = Stopwatch.StartNew();
                        var options = LitigGameEnumeratedPureLauncher.CreateOptions(n, m, british);
                        string directory = Path.Combine(root, options.Name);
                        var solver = await LitigGameEnumeratedPureLauncher.InitializeAsync(options, directory, source, workers, parameters.ContainsKey("resume"));
                        try
                        {
                            await solver.RunAlgorithm(options.Name);
                            Console.WriteLine($"{options.Name}: matrix ready after {timer.Elapsed.TotalSeconds:F3}s.");
                            await GenerateAsync(solver.Store, Path.Combine(directory, "reports-" + Guid.NewGuid().ToString("N")[..8]), parameters, solver);
                            Console.WriteLine($"{options.Name}: complete after {timer.Elapsed.TotalSeconds:F3}s. {directory}");
                        }
                        finally { solver.Store?.Dispose(); }
                    }
                }
                else
                {
                    using var store = PureRunStore.Read(LitigGameEnumeratedPureLauncher.Required(parameters, "input"));
                    await GenerateAsync(store, Path.GetFullPath(LitigGameEnumeratedPureLauncher.Required(parameters, "output")), parameters);
                }
                return 0;
            }
            finally { CultureInfo.CurrentCulture = originalCulture; }
        }

        public static async Task GenerateAsync(PureRunStore store, string output, Dictionary<string, string> parameters,
            EnumeratedPureStrategies solver = null)
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Report output directory must be empty.");
            var timer = Stopwatch.StartNew();
            double[] tolerances = parameters.GetValueOrDefault("tolerances", "0,0.001,0.005,0.01,0.025").Split(',')
                .Select(x => double.Parse(x, CultureInfo.InvariantCulture)).Distinct().OrderBy(x => x).ToArray();
            if (tolerances.Length == 0 || tolerances.Any(t => !double.IsFinite(t) || t < 0)) throw new ArgumentException("Tolerances must be finite and nonnegative.");
            double outcomeRadius = double.Parse(parameters.GetValueOrDefault("outcome-radius", "0.025"), CultureInfo.InvariantCulture);
            double strategyRadius = double.Parse(parameters.GetValueOrDefault("strategy-radius", "0.01"), CultureInfo.InvariantCulture);
            int verify = LitigGameEnumeratedPureLauncher.Integer(parameters, "verify", solver == null ? 0 : 12);
            if (verify < 0 || outcomeRadius < 0 || strategyRadius < 0 || !double.IsFinite(outcomeRadius) || !double.IsFinite(strategyRadius)) throw new ArgumentException("Invalid report settings.");
            bool verifyAll = parameters.ContainsKey("verify-all");
            var matrix = solver?.Matrix ?? store.LoadMatrix();
            var scores = solver?.Scores ?? matrix.Score(store.Manifest.NumericalTolerance);
            var terms = solver?.Terminals ?? store.ReadTerminals();
            var strongest = scores.OrderedProfiles().Take(Math.Max(1, verify)).ToArray();
            var eligible = scores.Profiles().Where(p => p.Epsilon <= tolerances.Max() + store.Manifest.NumericalTolerance)
                .OrderBy(p => p.Epsilon).ThenBy(p => p.Row).ThenBy(p => p.Column).ToArray();
            Console.WriteLine($"Retained {eligible.Length:N0} / {matrix.Rows * matrix.Columns:N0} profiles at tolerance {tolerances.Max():G}; minimum restricted gain {strongest[0].Epsilon:G17}.");
            using var configuration = JsonDocument.Parse(store.Manifest.ConfigurationJson);
            var optionsJson = configuration.RootElement.GetProperty("Options");
            int n = optionsJson.GetProperty("NumLiabilitySignals").GetInt32(), m = optionsJson.GetProperty("NumOffers").GetInt32();
            bool british = optionsJson.GetProperty("LoserPaysMultiple").GetDouble() == 1;
            var catalog = new MonotonePureStrategyCatalog(n, m);
            if (catalog.DescriptionJson != store.Manifest.CatalogJson) throw new InvalidDataException("Catalog differs from the saved run.");

            bool ownsSolver = false;
            string metricPath = Path.Combine(store.DirectoryPath, "outcome-cache.json.gz");
            if (solver == null && (!File.Exists(metricPath) || verify > 0 || verifyAll))
            {
                string source = LitigGameEnumeratedPureLauncher.CaptureSourceState(parameters.GetValueOrDefault("repository", Directory.GetCurrentDirectory()));
                if (source != store.Manifest.SourceStateJson) throw new InvalidDataException("New verification or outcome replay requires the original source state. Existing outcome caches can be reported without reinitializing the game.");
                solver = await LitigGameEnumeratedPureLauncher.InitializeAsync(LitigGameEnumeratedPureLauncher.CreateOptions(n, m, british), store.DirectoryPath, source, 1, true);
                solver.CompileTerminals();
                solver.Store = PureRunStore.Open(solver.EvolutionSettings.EnumeratedPure, solver.Space, solver.Terminals);
                solver.Matrix = matrix; solver.Scores = scores;
                ownsSolver = true;
            }
            try
            {
                PureMetricCache metrics;
                if (File.Exists(metricPath))
                {
                    using var file = new MemoryStream(PureRunStore.ReadChecked(metricPath));
                    using var gzip = new GZipStream(file, CompressionMode.Decompress);
                    metrics = JsonSerializer.Deserialize<PureMetricCache>(gzip, PureRunStore.Json);
                    if (metrics.Fingerprint != store.Fingerprint || metrics.Terminals.Length != terms.Count) throw new InvalidDataException("Outcome cache identity mismatch.");
                }
                else
                {
                    metrics = BuildMetricCache(solver, n, m, british);
                    using var buffer = new MemoryStream();
                    using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, true)) JsonSerializer.Serialize(gzip, metrics, PureRunStore.Json);
                    store.WriteOnce("outcome-cache.json.gz", buffer.ToArray());
                }
                var retained = Aggregate(eligible, terms, metrics, m + 4);
                var analyses = tolerances.Select(t =>
                {
                    var filtered = retained.Where(p => p.Score.Epsilon <= t + store.Manifest.NumericalTolerance).ToArray();
                    return (Tolerance: t, Profiles: filtered,
                        Outcome: Cluster(filtered, p => OutcomeFeatures(p.Outcomes), outcomeRadius),
                        Strategy: Cluster(filtered, p => p.StrategyFeatures, strategyRadius));
                }).ToArray();
                var selected = strongest.Take(verify).Select(p => (p.Row, p.Column)).ToHashSet();
                if (verify > 0 || verifyAll)
                    foreach (var analysis in analyses) foreach (var c in analysis.Outcome.Clusters.Concat(analysis.Strategy.Clusters))
                        selected.Add((c.RepresentativeRow, c.RepresentativeColumn));
                var pResponses = new Dictionary<int, PureBestResponse>();
                var dResponses = new Dictionary<int, PureBestResponse>();
                for (int c = 0; c < matrix.Columns; c++) { var br = store.LoadBestResponse(0, c); if (br != null) pResponses[c] = br; }
                for (int r = 0; r < matrix.Rows; r++) { var br = store.LoadBestResponse(1, r); if (br != null) dResponses[r] = br; }
                var verifyTimer = Stopwatch.StartNew();
                if (solver != null && (verify > 0 || verifyAll))
                {
                    foreach (var profile in selected.OrderBy(x => scores.At(x.Row, x.Column).Epsilon).ThenBy(x => x.Row).ThenBy(x => x.Column))
                    {
                        pResponses[profile.Column] = solver.VerifyOpponent(0, profile.Column);
                        dResponses[profile.Row] = solver.VerifyOpponent(1, profile.Row);
                    }
                    int initialCount = pResponses.Count + dResponses.Count;
                    double elapsed = verifyTimer.Elapsed.TotalSeconds;
                    Console.WriteLine($"Candidate/representative verification: {initialCount} cached opponent responses, {elapsed:F3}s this pass. Estimated all-opponent time {(initialCount == 0 ? 0 : (matrix.Rows + matrix.Columns) * pResponses.Values.Concat(dResponses.Values).Average(x => x.Seconds)):F1}s.");
                    if (verifyAll)
                    {
                        var lastUpdate = Stopwatch.StartNew();
                        for (int c = 0; c < matrix.Columns; c++)
                        {
                            pResponses[c] = solver.VerifyOpponent(0, c);
                            if (lastUpdate.Elapsed.TotalSeconds > 15) { Console.WriteLine($"P unrestricted responses: {c + 1}/{matrix.Columns}"); lastUpdate.Restart(); }
                        }
                        for (int r = 0; r < matrix.Rows; r++)
                        {
                            dResponses[r] = solver.VerifyOpponent(1, r);
                            if (lastUpdate.Elapsed.TotalSeconds > 15) { Console.WriteLine($"D unrestricted responses: {r + 1}/{matrix.Rows}"); lastUpdate.Restart(); }
                        }
                    }
                }
                double verificationSeconds = verifyTimer.Elapsed.TotalSeconds;
                double? Unrestricted(PureProfileScore p)
                {
                    if (!pResponses.TryGetValue(p.Column, out var pb) || !dResponses.TryGetValue(p.Row, out var db)) return null;
                    int index = p.Row * matrix.Columns + p.Column;
                    double pGain = pb.Utility - matrix.Plaintiff[index], dGain = db.Utility - matrix.Defendant[index];
                    if (pGain + store.Manifest.NumericalTolerance < p.PlaintiffGain || dGain + store.Manifest.NumericalTolerance < p.DefendantGain)
                        throw new InvalidDataException("Unrestricted gain fell below restricted gain.");
                    return Math.Max(0, Math.Max(pGain, dGain));
                }
                // Evaluate the dominance invariant over EVERY profile with both cached opponent responses.
                var allVerified = scores.Profiles().Where(p => pResponses.ContainsKey(p.Column) && dResponses.ContainsKey(p.Row));
                int verifiedCount = 0, unrestrictedExact = 0; double? minimumUnrestricted = null;
                foreach (var p in allVerified)
                {
                    double value = Unrestricted(p).Value; verifiedCount++;
                    if (value <= store.Manifest.NumericalTolerance) unrestrictedExact++;
                    minimumUnrestricted = minimumUnrestricted == null ? value : Math.Min(minimumUnrestricted.Value, value);
                }
                Directory.CreateDirectory(output);
                var sensitivity = new List<object>();
                foreach (var analysis in analyses)
                {
                    string stem = "tolerance-" + analysis.Tolerance.ToString("G", CultureInfo.InvariantCulture);
                    WriteProfiles(Path.Combine(output, stem + ".csv"), analysis.Profiles, analysis.Outcome, analysis.Strategy,
                        matrix, catalog, pResponses, dResponses, analysis.Tolerance, store.Manifest.NumericalTolerance);
                    File.WriteAllText(Path.Combine(output, stem + "-clusters.json"), JsonSerializer.Serialize(new
                    { analysis.Tolerance, OutcomeClusters = analysis.Outcome.Clusters, StrategyClusters = analysis.Strategy.Clusters }, PureRunStore.Json));
                    File.WriteAllText(Path.Combine(output, stem + ".svg"), Plot(analysis.Profiles, analysis.Outcome, analysis.Tolerance));
                    sensitivity.Add(new
                    {
                        analysis.Tolerance, RetainedProfiles = analysis.Profiles.Length,
                        RestrictedNumericalEquilibria = analysis.Profiles.Count(p => p.Score.Epsilon <= store.Manifest.NumericalTolerance),
                        UnrestrictedVerifiedProfiles = analysis.Profiles.Count(p => Unrestricted(p.Score).HasValue),
                        UnrestrictedVerifiedWithinTolerance = analysis.Profiles.Count(p => Unrestricted(p.Score) is double u && u <= analysis.Tolerance + store.Manifest.NumericalTolerance),
                        OutcomeClusters = analysis.Outcome.Clusters.Length, StrategyClusters = analysis.Strategy.Clusters.Length
                    });
                }
                var strongestRetained = Aggregate(strongest, terms, metrics, m + 4);
                WriteProfiles(Path.Combine(output, "strongest-candidates.csv"), strongestRetained,
                    Cluster(strongestRetained, p => OutcomeFeatures(p.Outcomes), outcomeRadius),
                    Cluster(strongestRetained, p => p.StrategyFeatures, strategyRadius), matrix, catalog, pResponses, dResponses, tolerances.Max(), store.Manifest.NumericalTolerance);
                // Existing information-set/action reporting is useful for representatives. Its legacy
                // Equilibrium Number column is renamed because a candidate need not be an equilibrium.
                if (solver != null)
                {
                    var profileDirectory = Path.Combine(output, "representative-profiles"); Directory.CreateDirectory(profileDirectory);
                    var actionReportProfiles = strongest.Take(verify).Select(p => (p.Row, p.Column))
                        .Concat(analyses.SelectMany(a => a.Outcome.Clusters).Select(c => (Row: c.RepresentativeRow, Column: c.RepresentativeColumn)))
                        .Distinct().OrderBy(p => p.Row).ThenBy(p => p.Column);
                    foreach (var profile in actionReportProfiles)
                    {
                        var vector = solver.Profile(profile.Row, profile.Column);
                        solver.SetInformationSetsToEquilibrium(vector);
                        string stem = $"P{profile.Row:D4}-D{profile.Column:D4}";
                        File.WriteAllText(Path.Combine(profileDirectory, stem + "-profile.csv"), string.Join(",", vector.Select(x => x.ToString("R", CultureInfo.InvariantCulture))) + "\n");
                        File.WriteAllText(Path.Combine(profileDirectory, stem + "-actions.csv"), InformationSetActionReport.BuildCsv(solver, 1).Replace("Equilibrium Number", "Candidate Number").Replace("Equilibrium Reach Probability", "Profile Reach Probability").Replace("Equilibrium Action Probability", "Profile Action Probability"));
                    }
                }
                File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
                {
                    store.Fingerprint, OptionSet = configuration.RootElement.GetProperty("Name").GetString(),
                    ReportSchema = 2,
                    StrategyDistance = "Reached action mass; precommitted exit actions receive weight only on unsuccessful bargaining paths.",
                    matrix.Rows, matrix.Columns, Profiles = matrix.Rows * matrix.Columns,
                    MinimumRestrictedGain = strongest[0].Epsilon, store.Manifest.NumericalTolerance,
                    RestrictedZeroGainProfiles = scores.Profiles().Count(p => p.Epsilon == 0),
                    RestrictedNumericalEquilibria = scores.Profiles().Count(p => p.Epsilon <= store.Manifest.NumericalTolerance),
                    UnrestrictedPlaintiffOpponentStrategies = pResponses.Count, UnrestrictedDefendantOpponentStrategies = dResponses.Count,
                    UnrestrictedVerifiedProfiles = verifiedCount, UnrestrictedNumericalEquilibria = unrestrictedExact,
                    MinimumUnrestrictedGainAmongVerified = minimumUnrestricted,
                    VerificationComplete = verifiedCount == matrix.Rows * matrix.Columns,
                    verificationSeconds, ReportSeconds = timer.Elapsed.TotalSeconds,
                    BestResponseCalculationSeconds = pResponses.Values.Concat(dResponses.Values).Sum(x => x.Seconds),
                    outcomeRadius, strategyRadius, Sensitivity = sensitivity,
                    ClusteringComplete = true
                }, PureRunStore.Json));
                File.WriteAllText(Path.Combine(output, "README.md"), Documentation(store, tolerances, outcomeRadius, strategyRadius));
                Console.WriteLine($"Reports: {output}; unrestricted verification covers {verifiedCount:N0} profiles ({pResponses.Count + dResponses.Count} opponent responses).");
            }
            finally { if (ownsSolver) solver.Store.Dispose(); }
        }

        public static PureMetricCache BuildMetricCache(EnumeratedPureStrategies solver, int n, int m, bool british)
        {
            var options = LitigGameEnumeratedPureLauncher.CreateOptions(n, m, british);
            var definition = new LitigGameDefinition(); definition.Setup(options);
            var player = new GamePlayer(Strategy.GetStarterStrategies(definition), false, definition, true);
            int actionCount = 2 + 2 + m;
            var result = new PureTerminalMetrics[solver.Terminals.Count];
            for (int t = 0; t < result.Length; t++)
            {
                var term = solver.Terminals[t]; int cursor = 0;
                var progress = (LitigGameProgress)player.PlayUsingActionOverride((decision, _) =>
                {
                    if (cursor >= term.Path.Length) throw new InvalidDataException("Replay exhausted the terminal path.");
                    var step = term.Path[cursor++];
                    var expected = definition.DecisionsExecutionOrder[step.DecisionIndex];
                    if (expected.DecisionByteCode != decision.DecisionByteCode) throw new InvalidDataException("Replay decision mismatch.");
                    return step.Action;
                });
                if (cursor != term.Path.Length || !progress.GameComplete) throw new InvalidDataException("Incomplete terminal replay.");
                var values = new double[OutcomeNames.Length];
                double total = 0;
                foreach (var ending in progress.BayesianCalculations_GenerateAllConsistentGameProgresses(1))
                {
                    var p = (LitigGameProgress)ending.progress;
                    p.ResetPostGameInfo();
                    double transfer = !p.PFiles ? 0 : p.CaseSettles ? p.SettlementValue.Value : p.PAbandons ? 0 :
                        !p.DAnswers || p.DDefaults ? options.DamagesMax * options.DamagesMultiplier : p.PWinsAtTrial ? p.DamagesAwarded : 0;
                    // These are the existing game's truth-relative, net-wealth measures. The truth
                    // expansion uses its posterior integration; court findings are not substituted for truth.
                    double[] v = { p.PFiles ? 1 : 0, p.DAnswers ? 1 : 0, p.CaseSettles ? 1 : 0,
                        p.PFiles && p.DAnswers && !p.CaseSettles && p.PAbandons ? 1 : 0,
                        p.PFiles && p.DAnswers && !p.CaseSettles && !p.PAbandons && p.DDefaults ? 1 : 0,
                        p.TrialOccurs ? 1 : 0, p.TotalExpensesIncurred, p.PChangeWealth, p.DChangeWealth,
                        p.FalsePositiveExpenditures, p.FalseNegativeShortfall,
                        p.FalsePositiveExpenditures + p.FalseNegativeShortfall,
                        transfer - p.PChangeWealth, -transfer - p.DChangeWealth };
                    for (int i = 0; i < values.Length; i++) values[i] += ending.weight * v[i];
                    total += ending.weight;
                }
                if (Math.Abs(total - 1) > 1E-10) throw new InvalidDataException("Outcome expansion probability mismatch.");
                int pSignal = 0, dSignal = 0;
                var reached = new List<int>();
                foreach (var step in term.Path)
                {
                    var decision = (LitigGameDecisions)definition.DecisionsExecutionOrder[step.DecisionIndex].DecisionByteCode;
                    if (decision == LitigGameDecisions.PLiabilitySignal) pSignal = step.Action - 1;
                    if (decision == LitigGameDecisions.DLiabilitySignal) dSignal = step.Action - 1;
                    int party = decision is LitigGameDecisions.PFile or LitigGameDecisions.PAbandon or LitigGameDecisions.POffer ? 0 :
                        decision is LitigGameDecisions.DAnswer or LitigGameDecisions.DDefault or LitigGameDecisions.DOffer ? 1 : -1;
                    if (party < 0) continue;
                    int offset = decision is LitigGameDecisions.PFile or LitigGameDecisions.DAnswer ? 0 :
                        decision is LitigGameDecisions.PAbandon or LitigGameDecisions.DDefault ? 2 : 4;
                    reached.Add((party * n + (party == 0 ? pSignal : dSignal)) * actionCount + offset + step.Action - 1);
                }
                result[t] = new(values, reached.ToArray());
            }
            return new(solver.Store.Fingerprint, OutcomeNames, 2 * n * actionCount, result);
        }

        public static PureRetainedProfile[] Aggregate(PureProfileScore[] profiles, List<PureTerminal> terms, PureMetricCache metrics, int actionsPerSignal)
        {
            if (actionsPerSignal < 5 || metrics.StrategyFeatureCount % actionsPerSignal != 0) throw new ArgumentException("Invalid strategy feature dimensions.");
            var result = profiles.Select(p => new PureRetainedProfile(p, new double[OutcomeNames.Length], new double[metrics.StrategyFeatureCount])).ToArray();
            var rows = result.GroupBy(p => p.Score.Row).ToDictionary(g => g.Key, g => g.ToArray());
            for (int t = 0; t < terms.Count; t++)
            {
                var term = terms[t]; var metric = metrics.Terminals[t];
                foreach (int r in term.Rows)
                {
                    if (!rows.TryGetValue(r, out var candidates)) continue;
                    foreach (var p in candidates)
                    {
                        if (Array.BinarySearch(term.Columns, p.Score.Column) < 0) continue;
                        for (int i = 0; i < p.Outcomes.Length; i++) p.Outcomes[i] += term.ChanceProbability * metric.Outcomes[i];
                        foreach (int feature in metric.ReachedActions)
                        {
                            int actionOffset = feature % actionsPerSignal;
                            // Exit is committed before offers, but the continuation is economically
                            // unreachable on settled paths. Counting the announcement there would
                            // create clusters from an ineffective continuation (including the two
                            // baseline numerical equilibria). Offers are still counted on those paths.
                            double effectiveReach = actionOffset is 2 or 3 ? 1 - metric.Outcomes[2] : 1;
                            p.StrategyFeatures[feature] += term.ChanceProbability * effectiveReach;
                        }
                    }
                }
            }
            return result;
        }

        private static double[] OutcomeFeatures(double[] values) => new[] { values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[9], values[10] };
        public static PureClustering Cluster(PureRetainedProfile[] profiles, Func<PureRetainedProfile, double[]> features, double radius)
        {
            if (!double.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            var representatives = new List<(int Profile, double[] Features)>();
            var assignments = new int[profiles.Length];
            var counts = new List<int>();
            // Input is ordered by epsilon, then catalog row/column. Greedy leader clustering,
            // L-infinity radius, first matching representative. No transitive chaining or random seed.
            for (int i = 0; i < profiles.Length; i++)
            {
                double[] values = features(profiles[i]); int cluster = -1;
                for (int c = 0; c < representatives.Count; c++)
                {
                    double[] other = representatives[c].Features;
                    if (values.Length != other.Length) throw new ArgumentException("Feature dimensions differ.");
                    bool matches = true;
                    for (int j = 0; j < values.Length; j++) if (Math.Abs(values[j] - other[j]) > radius + 1E-12) { matches = false; break; }
                    if (matches) { cluster = c; break; }
                }
                if (cluster < 0) { cluster = representatives.Count; representatives.Add((i, values)); counts.Add(0); }
                assignments[i] = cluster; counts[cluster]++;
            }
            return new(representatives.Select((r, c) => new PureCluster(c, profiles[r.Profile].Score.Row, profiles[r.Profile].Score.Column, counts[c])).ToArray(), assignments);
        }

        private static void WriteProfiles(string path, PureRetainedProfile[] profiles, PureClustering outcomes, PureClustering strategies,
            PurePayoffMatrix matrix, MonotonePureStrategyCatalog catalog, Dictionary<int, PureBestResponse> pResponses,
            Dictionary<int, PureBestResponse> dResponses, double tolerance, double numericalTolerance)
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            writer.WriteLine("Row,Column,PStrategyId,DStrategyId,UP,UD,rP,rD,EpsilonRestricted,rPUnrestricted,rDUnrestricted,EpsilonUnrestricted,PBestResponseId,DBestResponseId,Classification,OutcomeCluster,StrategyCluster," + string.Join(",", OutcomeNames));
            foreach (var item in profiles.Select((p, i) => (p, i)))
            {
                var p = item.p.Score; int index = p.Row * matrix.Columns + p.Column;
                bool pv = pResponses.TryGetValue(p.Column, out var pb), dv = dResponses.TryGetValue(p.Row, out var db);
                double? pg = pv ? Math.Max(0, pb.Utility - matrix.Plaintiff[index]) : null, dg = dv ? Math.Max(0, db.Utility - matrix.Defendant[index]) : null;
                double? epsilon = pg.HasValue && dg.HasValue ? Math.Max(pg.Value, dg.Value) : null;
                string label = p.Epsilon <= numericalTolerance ? "Restricted pure numerical equilibrium" : p.Epsilon <= tolerance + numericalTolerance ? "Restricted near-equilibrium" : "Candidate outside tolerance";
                if (epsilon.HasValue) label += epsilon <= numericalTolerance ? "; Unrestricted-verified numerical equilibrium" : epsilon <= tolerance + numericalTolerance ? "; Unrestricted-verified near-equilibrium" : "; Unrestricted verification fails tolerance";
                string F(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
                writer.WriteLine(string.Join(",", new[] { p.Row.ToString(), p.Column.ToString(), catalog.Plaintiff[p.Row].Id, catalog.Defendant[p.Column].Id,
                    F(matrix.Plaintiff[index]), F(matrix.Defendant[index]), F(p.PlaintiffGain), F(p.DefendantGain), F(p.Epsilon), F(pg), F(dg), F(epsilon),
                    pv ? $"br-0-{p.Column:D6}" : "", dv ? $"br-1-{p.Row:D6}" : "", label,
                    outcomes.Assignments[item.i].ToString(), strategies.Assignments[item.i].ToString() }.Concat(item.p.Outcomes.Select(v => F(v)))));
            }
        }

        public static string Plot(PureRetainedProfile[] profiles, PureClustering clusters, double tolerance)
        {
            var b = new StringBuilder();
            b.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1000\" height=\"690\" viewBox=\"0 0 1000 690\"><rect width=\"1000\" height=\"690\" fill=\"#fafafa\"/><g font-family=\"Arial,sans-serif\" fill=\"#142b39\">");
            b.AppendLine(FormattableString.Invariant($"<text x=\"75\" y=\"36\" font-size=\"23\">Settlement and trial among retained pure profiles</text><text x=\"75\" y=\"62\" font-size=\"14\">Restricted maximum deviation gain ≤ {tolerance:G} (+ numerical tolerance); {profiles.Length:N0} profiles</text>"));
            for (int tick = 0; tick <= 10; tick++)
            {
                double x = 80 + tick * 70, y = 590 - tick * 48;
                b.AppendLine(FormattableString.Invariant($"<path d=\"M{x} 110V590 M80 {y}H780\" stroke=\"#ddd\" stroke-width=\"1\"/><text x=\"{x}\" y=\"613\" text-anchor=\"middle\" font-size=\"12\">{tick / 10.0:F1}</text><text x=\"65\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"12\">{tick / 10.0:F1}</text>"));
            }
            double max = profiles.Length == 0 ? 1 : Math.Max(1E-12, profiles.Max(p => p.Score.Epsilon));
            foreach (var profile in profiles.Reverse())
            {
                double x = 80 + 700 * profile.Outcomes[2], y = 590 - 480 * profile.Outcomes[5];
                double z = profile.Score.Epsilon / max; int red = (int)(30 + 205 * z), blue = (int)(175 - 120 * z);
                b.AppendLine(FormattableString.Invariant($"<circle cx=\"{x:F3}\" cy=\"{y:F3}\" r=\"3.5\" fill=\"rgb({red},100,{blue})\" opacity=\"0.6\"><title>P{profile.Score.Row}, D{profile.Score.Column}; rP={profile.Score.PlaintiffGain:G5}, rD={profile.Score.DefendantGain:G5}; settlement={profile.Outcomes[2]:G5}, trial={profile.Outcomes[5]:G5}</title></circle>"));
            }
            foreach (var cluster in clusters.Clusters.OrderByDescending(c => c.Count).ThenBy(c => c.Id).Take(20))
            {
                var p = profiles.First(p => p.Score.Row == cluster.RepresentativeRow && p.Score.Column == cluster.RepresentativeColumn);
                double x = 80 + 700 * p.Outcomes[2], y = 590 - 480 * p.Outcomes[5];
                b.AppendLine(FormattableString.Invariant($"<circle cx=\"{x:F3}\" cy=\"{y:F3}\" r=\"7\" fill=\"none\" stroke=\"#132d3d\" stroke-width=\"1.5\"/><text x=\"{x + 9:F3}\" y=\"{y - 7:F3}\" font-size=\"12\">C{cluster.Id}</text>"));
            }
            b.AppendLine("<text x=\"430\" y=\"649\" text-anchor=\"middle\" font-size=\"16\">Settlement probability</text><text transform=\"translate(24,355) rotate(-90)\" text-anchor=\"middle\" font-size=\"16\">Trial probability</text><text x=\"810\" y=\"145\" font-size=\"14\">Deviation gain</text>");
            for (int i = 0; i <= 40; i++)
            {
                double z = i / 40.0;
                b.AppendLine(FormattableString.Invariant($"<rect x=\"810\" y=\"{170 + i * 5}\" width=\"22\" height=\"6\" fill=\"rgb({(int)(30 + 205 * z)},100,{(int)(175 - 120 * z)})\"/>"));
            }
            b.AppendLine(FormattableString.Invariant($"<text x=\"843\" y=\"179\" font-size=\"12\">0</text><text x=\"843\" y=\"375\" font-size=\"12\">{max:G4}</text><text x=\"805\" y=\"422\" font-size=\"12\">C: outcome cluster</text><text x=\"805\" y=\"442\" font-size=\"12\">representative</text><text x=\"75\" y=\"677\" font-size=\"12\">Cluster sizes count catalog profiles; they are not equilibrium-selection probabilities.</text></g></svg>"));
            return b.ToString();
        }

        private static string Documentation(PureRunStore store, double[] tolerances, double outcomeRadius, double strategyRadius) => $"""
            # Enumerated pure-strategy report

            Run fingerprint: `{store.Fingerprint}`. Matrix rows are plaintiff strategies; columns are defendant strategies. IDs and zero-based threshold boundaries are in `catalog.json` in the run directory. Both individual deviation gains are retained; epsilon is their maximum, in unnormalized expected-utility units.

            Tolerances: {string.Join(", ", tolerances)}. A zero requested tolerance uses numerical tolerance {store.Manifest.NumericalTolerance:R}. “Numerical equilibrium” means no gain above this numerical tolerance in the rounded finite game, not symbolic exactness or an assertion about the undiscretized game. Near-equilibrium rows are explicitly labeled. Verification means an entire information-set-respecting best response, including later own choices. Its expected utility bounds mixed deviations as well. No mixed equilibrium profiles are sought.

            ## Filtering and clustering

            Each tolerance filters on restricted epsilon BEFORE either clustering. Profiles are ordered by epsilon, then row, then column. Greedy leader clustering assigns a profile to the first existing representative within the L-infinity radius; otherwise it becomes a new representative. No randomness or transitive chaining. Representatives are the strongest encountered member of each cluster.

            Outcome similarity uses unconditional filing, answering, settlement, abandonment, default, trial, total expenditures, and the two existing truth-relative shortfall/expenditure measures. Radius: {outcomeRadius:R}. Monetary features are in the article baseline's damage units (damages = 1); probabilities are on [0,1].

            Strategy similarity uses probability mass on each reached party/signal/decision/action, aggregated over chance-integrated terminal paths. Exit commitments receive weight only on unsuccessful bargaining paths: when settlement occurs, the exit continuation never takes effect. Offers still receive weight on those paths. Radius: {strategyRadius:R}. This compares realized play and gives zero weight to omitted own continuations and other off-path actions; it does not compare arbitrary full-profile completions. The catalog still retains distinctions that can matter for unilateral deviations. Cluster sizes are counts of retained catalog profiles, never equilibrium-selection probabilities.

            ## Accounting and numerical representation

            Full tree chance integration is used. Continuous merits remain integrated with the article's 64-point Gauss–Legendre rule; truth remains Bernoulli(Q), and the court remains binary. Utilities use SequenceForm's terminal rounding; uneven chance nodes use its rationalization on the MaxIntegralUtility grid, with the final action adjusted to sum to one. RoundOffChanceDigits is also recorded but is not the denominator of this rationalization. Matrix accumulation is binary64 in fixed tree order; all worker counts use that order. Ordinary tree evaluation and GEBR verification read the very same rounded nodes. Collapsed terminal lotteries retain the underlying integration before terminal utility rounding.

            Outcome reporting replays existing game logic and expands its terminal lotteries and posterior truth distribution. External chance weights match the matrix; monetary accounting and posterior/lottery values within collapsed leaves are unrounded. Therefore reported net money need not equal rounded utility minus initial wealth. Abandonment/default columns count implemented exits after failed bargaining; no answer is separately identifiable as Filed minus Answered. Net litigation expenses include fee transfers. DefendantExcessNetBurden and PlaintiffRecoveryShortfall are the article-facing names of the model's FalsePositiveExpenditures and FalseNegativeShortfall. NetOutcomeFidelityLoss is their sum, as in the existing focused article report.

            ## Reuse and outputs

            `payoffs-*.bin` are checksummed row blocks (row-major, interleaved P and D binary64 utilities). `profile-gains.bin` stores row-major individual restricted gains. Binary files begin with BinaryWriter string headers and dimensions, and end with a 32-byte SHA-256 checksum. `restricted-best-responses.json` preserves all numerical ties. Each `verification/br-player-opponent.json` caches one unrestricted response and its actions in information-set order, keyed by opponent catalog index. `complete.json` certifies matrix/scores completion; report summary records separate verification and clustering completion. A fresh report directory permits different tolerances without payoff recomputation; existing payoff files are immutable. Source/configuration/tree identity and checksums gate checkpoint reuse. Interrupted `.partial` files are ignored, never accepted as finished blocks.

            SVGs show settlement versus trial, color by restricted gain, and the 20 largest outcome-cluster representatives (all are in the cluster JSON). CSVs contain individual restricted/unrestricted gains, both cluster assignments, outcomes and classifications. For new game runs, full action reports for strongest candidates and outcome representatives use the existing InformationSetActionReport with profile/candidate headings; its single-action utility losses are descriptive and are NOT the unrestricted verification statistic. Strategy representatives are also verified and are identified in cluster JSON and the catalog.
            """;
    }
}
