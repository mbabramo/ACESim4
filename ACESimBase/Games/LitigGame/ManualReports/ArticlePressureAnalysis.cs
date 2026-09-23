using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;

namespace ACESimBase.Games.LitigGame.ManualReports;

public static class ArticlePressureAnalysis
{
    public sealed record Source(string Id, string OptionSetName, string EquilibriumFile,
        string ActionReportFile, int EquilibriumNumber = 1, string ProfileFile = null);
    public sealed record Contrast(string Id, string Label, string Source, string Target);
    public sealed record Request(string OutputDirectory, Source[] Sources, Contrast[] Contrasts,
        Tolerances Tolerances = null, bool CheckOffPathCompletions = true, bool CheckTieSensitivity = true,
        string PublicationDirectory = null);
    public sealed record Fingerprint(string Path, string Sha256);
    public sealed record Loaded(Source Selection, Fingerprint Equilibrium, Fingerprint ActionReport,
        int ValidatedRows, Profile Profile, Reference Reference, Result[] Controls, Fingerprint ProfileOverride = null);
    public sealed record Scenario(string Panel, string Component, Result Result);
    public sealed record ContrastResult(string Schema, Contrast Contrast, string SourceOptionSet,
        string TargetOptionSet, Tolerances Tolerances, Reference SourceEquilibrium,
        Reference TargetEquilibrium, Scenario[] Scenarios, string[] Interpretation,
        EquilibriumChangeDecomposition.ChangeRow[] Changes,
        EquilibriumChangeDecomposition.ExcludedHistory[] ExcludedHistories, double[] OfferValues);
    public sealed record Manifest(string Schema, DateTimeOffset CreatedUtc, Fingerprint Request,
        int MaxIntegralUtility, int RoundOffChanceDigits, Tolerances Tolerances,
        Loaded[] Sources, Contrast[] Contrasts, string[] OutputJsonFiles,
        Fingerprint[] OutputFingerprints, Fingerprint CoreAssembly);

    public static JsonSerializerOptions JsonOptions => ArticleWorkedPathExtraction.JsonOptions;
    public static Fingerprint Hash(string file) => new(Path.GetFullPath(file), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());
    public static Request ReadRequest(string requestFile) => JsonSerializer.Deserialize<Request>(File.ReadAllText(requestFile), JsonOptions)
        ?? throw new InvalidDataException("Empty pressure-analysis request.");

    public static async Task<Manifest> RunAsync(string requestFile, Action<string> progress = null)
    {
        requestFile = Path.GetFullPath(requestFile);
        string directory = Path.GetDirectoryName(requestFile);
        var request = ReadRequest(requestFile);
        var tolerance = request.Tolerances ?? new();
        ValidateTolerances(tolerance);
        if (request.Sources == null || request.Contrasts == null || request.Contrasts.Length == 0 ||
            request.Sources.Select(x => x.Id).Distinct().Count() != request.Sources.Length ||
            request.Contrasts.Select(x => x.Id).Distinct().Count() != request.Contrasts.Length ||
            request.Contrasts.Any(x => !SafeId(x.Id)) || request.Sources.Any(x => !SafeId(x.Id)))
            throw new InvalidDataException("Supply nonempty, uniquely identified contrasts and sources.");
        string Resolve(string p) => Path.GetFullPath(p, directory);
        string output = Resolve(request.OutputDirectory);
        foreach (var source in request.Sources)
            foreach (string input in new[] { source.EquilibriumFile, source.ActionReportFile, source.ProfileFile }
                .Where(p => p != null).Select(Resolve))
                if (Inside(output, Path.GetDirectoryName(input)))
                    throw new InvalidDataException("Diagnostic outputs must be outside production-input directories.");
        Directory.CreateDirectory(output);
        var loaded = new Dictionary<string, Loaded>();
        foreach (var source in request.Sources)
        {
            progress?.Invoke("Validate source " + source.Id);
            var options = ArticleWorkedPathExtraction.CreateOptions(source.OptionSetName);
            RequireProtocol(options);
            var developer = await ArticleWorkedPathExtraction.InitializeAsync(options);
            string file = Resolve(source.EquilibriumFile);
            string report = Resolve(source.ActionReportFile);
            var equilibriumHash = Hash(file);
            var actionHash = Hash(report);
            var profileHash = source.ProfileFile == null ? null : Hash(Resolve(source.ProfileFile));
            if (source.EquilibriumNumber < 1) throw new InvalidDataException("Equilibrium number must be positive.");
            string line = File.ReadLines(file).Skip(source.EquilibriumNumber - 1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("Missing equilibrium line in " + file);
            var values = line.Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray();
            var fallbacks = ArticleWorkedPathExtraction.LoadProfile(developer, values);
            int rows = ArticleWorkedPathExtraction.ValidateActionReport(developer, source.EquilibriumNumber, report);
            var profile = Capture(developer, source.Id, fallbacks);
            // The action report verifies the SAVED profile, not a diagnostic replacement.
            // Replacements have their own fingerprint, fresh utilities/reaches, and full BR checks.
            if (profileHash != null)
                profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(profileHash.Path), JsonOptions)
                    ?? throw new InvalidDataException("Empty diagnostic profile.");
            var inspected = InspectEquilibrium(developer, profile with { Name = source.Id }, source.OptionSetName, tolerance);
            profile = inspected.Profile;
            var reference = inspected.Reference;
            var controls = inspected.Controls;
            loaded[source.Id] = new(source, equilibriumHash, actionHash, rows, profile, reference, controls, profileHash);
            progress?.Invoke($"  {rows} action rows verified; max gain {controls.Max(c => c.Gain):G4}");
        }
        var outputs = new List<string>();
        foreach (var contrast in request.Contrasts)
        {
            if (!loaded.TryGetValue(contrast.Source, out var source) || !loaded.TryGetValue(contrast.Target, out var target))
                throw new InvalidDataException("Contrast references an unknown source.");
            var sourceOptions = ArticleWorkedPathExtraction.CreateOptions(source.Selection.OptionSetName);
            var targetOptions = ArticleWorkedPathExtraction.CreateOptions(target.Selection.OptionSetName);
            ValidateMatchedOptions(sourceOptions, targetOptions);
            progress?.Invoke("Analyze " + contrast.Label);
            var developer = await ArticleWorkedPathExtraction.InitializeAsync(targetOptions);
            var scenarios = new List<Scenario>();
            int coalitionCount = EquilibriumChangeDecomposition.CoalitionCount(source.Profile);
            foreach (byte player in new byte[] { 0, 1 })
            {
                byte opponent = (byte)(1 - player);
                for (int mask = 0; mask < coalitionCount; mask++)
                {
                    string name = contrast.Id + "-coalition-" + mask + "-" + player;
                    var hybrid = EquilibriumChangeDecomposition.Coalition(source.Profile, target.Profile, player, mask);
                    var response = Respond(developer, hybrid, player, name, tolerance, tieReference: source.Profile);
                    scenarios.Add(new("coalition", mask.ToString(), response));
                    if (mask == coalitionCount - 1)
                        Near(target.Reference.Utilities[player], response.BestResponseUtility,
                            tolerance.Numerical, "Full target-opponent substitution");
                    if (request.CheckTieSensitivity)
                    {
                        scenarios.Add(new("tie-low", mask.ToString(), Respond(developer, hybrid, player, name + "-tie-low", tolerance)));
                        scenarios.Add(new("tie-high", mask.ToString(), Respond(developer, hybrid, player, name + "-tie-high", tolerance, highTie: true)));
                    }
                    if (request.CheckOffPathCompletions && response.ExposedOpponentSets.Length > 0)
                        foreach (bool highest in new[] { false, true })
                        {
                            var completed = ReplaceUnvisitedOpponentPolicies(hybrid, opponent, highest);
                            // Each stress replaces one coalition at a time in the allocation.
                            string check = "completion-" + (highest ? "high-" : "low-") + mask;
                            scenarios.Add(new(check, mask.ToString(), Respond(developer, completed, player,
                                name + "-" + check, tolerance, tieReference: source.Profile)));
                        }
                }
                progress?.Invoke("  " + (player == 0 ? "Plaintiff" : "Defendant") + $": {coalitionCount} coalitions and sensitivity checks complete");
            }
            double[] offerValues = Enumerable.Range(1, targetOptions.NumOffers).Select(a =>
                Game.ConvertActionToUniformDistributionDraw((byte)a, targetOptions.NumOffers, targetOptions.IncludeEndpointsForOffers)).ToArray();
            var (changes, excluded) = EquilibriumChangeDecomposition.BuildRows(source.Reference, target.Reference, scenarios.ToArray(), offerValues);
            var result = new ContrastResult(coalitionCount == 16 ? "3" : "2", contrast, source.Selection.OptionSetName, target.Selection.OptionSetName,
                tolerance, source.Reference, target.Reference, scenarios.ToArray(), InterpretationFor(coalitionCount == 16), changes, excluded, offerValues);
            string path = Path.Combine(output, contrast.Id + ".json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, JsonOptions) + "\n");
            outputs.Add(path);
            progress?.Invoke($"  {changes.Length} changed rows; {changes.Count(r => r.EndpointSelection)} endpoint-selection rows; {scenarios.Count} diagnostic responses");
        }
        foreach (var source in loaded.Values)
        {
            if (SourceFingerprints(source).Any(f => Hash(f.Path).Sha256 != f.Sha256))
                throw new IOException("A production input changed during the diagnostic run.");
        }
        var manifest = new Manifest(loaded.Values.Any(x => EquilibriumChangeDecomposition.CoalitionCount(x.Profile) == 16) ? "3" : "2", DateTimeOffset.UtcNow, Hash(requestFile), EvolutionSettings.MaxIntegralUtility,
            EvolutionSettings.RoundOffChanceDigits, tolerance, loaded.Values.ToArray(), request.Contrasts, outputs.ToArray(),
            outputs.Select(Hash).ToArray(), Hash(typeof(ArticlePressureAnalysis).Assembly.Location));
        await File.WriteAllTextAsync(Path.Combine(output, "equilibrium-changes-manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions) + "\n");
        return manifest;
    }

    public static IEnumerable<Fingerprint> SourceFingerprints(Loaded source) =>
        new[] { source.Equilibrium, source.ActionReport, source.ProfileOverride }.Where(f => f != null);

    public static (Profile Profile, Reference Reference, Result[] Controls) InspectEquilibrium(
        StrategiesDeveloperBase developer, Profile profile, string expectedOptionSet, Tolerances tolerance)
    {
        ValidateTolerances(tolerance);
        if (profile.OptionSet != expectedOptionSet || developer.GameDefinition.OptionSetName != expectedOptionSet)
            throw new InvalidDataException("Diagnostic profile belongs to a different option set.");
        var restore = Capture(developer, "restore");
        try
        {
            var reference = Describe(developer, profile); // Apply validates all semantic policies before mutation.
            profile = WithObservedReach(profile, reference); // Never trust cached donor reaches in an overlay.
            var controls = new[] { Respond(developer, profile, 0, profile.Name + "-control-P", tolerance),
                Respond(developer, profile, 1, profile.Name + "-control-D", tolerance) };
            if (controls.Any(c => c.Gain > tolerance.Numerical))
                throw new InvalidDataException($"Source equilibrium {profile.Name} exploitability {controls.Max(c => c.Gain):G17} exceeds {tolerance.Numerical:G17}.");
            return (profile, reference, controls);
        }
        finally { Apply(developer, restore); }
    }

    public static Profile ReplaceUnvisitedOpponentPolicies(Profile input, byte opponent, bool highest) => input with
    {
        Strategies = input.Strategies.ToDictionary(x => x.Key, x =>
        {
            var strategy = x.Value;
            if (strategy.Player != opponent || strategy.DonorReach > ReachTolerance) return strategy;
            var p = new double[strategy.Probabilities.Length]; p[highest ? p.Length - 1 : 0] = 1;
            return strategy with { Probabilities = p, Donor = strategy.Donor + (highest ? "/unvisited-high" : "/unvisited-low"), UniformFallback = false };
        })
    };

    public static void RequireProtocol(LitigGameOptions o)
    {
        if (o.NumPotentialBargainingRounds != 1 || !o.BargainingRoundsSimultaneous ||
            !o.PredeterminedAbandonAndDefaults || !o.AllowAbandonAndDefaults || o.SkipFileAndAnswerDecisions ||
            o.NumLiabilitySignals < 2 || o.NumOffers < 2 || !o.CollapseChanceDecisions || !o.CollapseAlternativeEndings)
            throw new NotSupportedException("The pressure diagnostic requires the article's one-round, precommitted-exit game with at least two signals/offers.");
    }

    public static void ValidateMatchedOptions(LitigGameOptions source, LitigGameOptions target)
    {
        RequireProtocol(source); RequireProtocol(target);
        // VariableSettings is the production request's human-readable primitive manifest.
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Fee Regime", "Fee Shifting Multiplier", "Risk Aversion", "CARA Alpha", "Specification",
            "Fee Shifting Trigger", "Fees After Nonanswer" };
        var left = source.VariableSettings.ToDictionary(x => x.Key, x => Convert.ToString(x.Value, System.Globalization.CultureInfo.InvariantCulture));
        var right = target.VariableSettings.ToDictionary(x => x.Key, x => Convert.ToString(x.Value, System.Globalization.CultureInfo.InvariantCulture));
        // A legal fee-rule intervention may change both the reimbursement amount
        // and its trigger (American <-> Complete Fee-Shifting).
        bool feeChanged = source.LoserPays != target.LoserPays || source.LoserPaysMultiple != target.LoserPaysMultiple ||
            source.LoserPaysAfterAbandonment != target.LoserPaysAfterAbandonment ||
            source.LoserPaysAfterNonAnswer != target.LoserPaysAfterNonAnswer;
        bool riskChanged = left.GetValueOrDefault("Risk Aversion") != right.GetValueOrDefault("Risk Aversion") ||
            left.GetValueOrDefault("CARA Alpha") != right.GetValueOrDefault("CARA Alpha");
        if (feeChanged == riskChanged)
            throw new InvalidDataException("Change exactly one intervention: the fee rule (amount and/or trigger), OR risk preferences.");
        foreach (string key in left.Keys.Union(right.Keys))
            if (!allowed.Contains(key) && left.GetValueOrDefault(key) != right.GetValueOrDefault(key))
                throw new InvalidDataException("Unmatched intervention primitive: " + key);
        if (source.NumOffers != target.NumOffers || source.NumLiabilitySignals != target.NumLiabilitySignals)
            throw new InvalidDataException("Signal/offer grids must match.");
    }

    public static bool SafeId(string value) => !string.IsNullOrWhiteSpace(value) &&
        char.IsAsciiLetterOrDigit(value[0]) && char.IsAsciiLetterOrDigit(value[^1]) &&
        !value.Contains("..", StringComparison.Ordinal) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.');
    private static bool Inside(string path, string directory) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)
        .StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static string[] InterpretationFor(bool agreement) => Interpretation.Select(text => agreement
        ? text.Replace("Eight coalitions", "Sixteen coalitions")
            .Replace("entry, offer, and exit policies", "entry, offer, exit, and agreement policies")
            .Replace("all six orders", "all twenty-four orders")
            .Replace("four mechanisms", "five contributions")
            .Replace("four-way accounting", "five-contribution accounting")
        : text).ToArray();

    public static readonly string[] Interpretation =
    {
        "Each response optimizes ALL focal-player continuation decisions against a fixed, complete opponent strategy. No monotonicity restriction is imposed.",
        "Only actual strategy changes at information sets reached in BOTH endpoint equilibria enter the change table. Reach changes are recorded separately; no undefined endpoint action is imputed.",
        "The direct contribution changes the rule or preference primitive first, holding the opponent's original complete strategy. Eight coalitions then replace every subset of the opponent's actual target-equilibrium entry, offer, and exit policies.",
        "Opponent contributions average incremental replacements over all six orders, conditional on the new rules. Their sum is the all-opponent response minus the direct response. Direct plus opponent contributions plus the explicitly retained selection residual equals the observed change. This convention assigns interactions with the primitive change to the opponent-adjustment portion; it is not an order-free causal identification.",
        "Information sets are mapped using player, decision code, and labeled observed decision/action history, not numerical node identifiers.",
        "Counterfactual reach omits ONLY the responding player's own prior action probabilities. Its normalized action values assume optimized continuation and its beliefs include changed opponent selection. These are not the saved equilibrium-continuation Q values.",
        "ActualReach and actual conditional utility concern the reported response profile; actual conditional utility is null when actual reach is zero. Counterfactual conditional values can remain defined if the player could reach the set by changing an earlier action; values/beliefs are null when opponent-and-chance reach is zero.",
        "Offer rows retain own exit commitments separately. The commitments are private and operate only after failed settlement. This does not test a different bargaining/exit protocol.",
        "Primary responses retain and renormalize original probability on optimal actions within the tie tolerance; when none remains they choose the first optimum. Each resulting complete policy is independently replayed against the optimal root utility. Low/high action alternatives test selection sensitivity. These are illustrative checks, not exhaustive bounds.",
        "If the selected all-target-opponent response differs from the observed target policy, its residual is never silently attributed to the four mechanisms. The publication table retains the explicit remaining contribution and sensitivity flags. Mixed offers are decomposed as action shares, never as mean offers.",
        "Intermediate coalitions may not actually reach a commonly reached endpoint history. Conditional continuation policies are usable only with positive opponent-and-chance reach and are explicitly flagged. Zero-counterfactual-reach coalitions make the row undefined for attribution.",
        "Opponent completions are audited both when actually newly reached and when reachable through a focal-player deviation: an arbitrary continuation can deter entry without being played. FocalDeviationReachWeight at opponent sets is only an exposure weight, not a normalized probability. Low/high completion tests change only donor-unvisited sets and are illustrative stress tests, not exhaustive bounds or alternative equilibria.",
        "Utility differences are within the intervention utility function. Risk-neutral and CARA utilities are not cross-regime welfare-comparable. Positive effect sizes do not by themselves establish a policy ranking.",
        "All-target-opponent validation compares optimal response utility to actual target-equilibrium utility, allowing payoff-equivalent strategies and the numerical tolerance. Gain elsewhere is against the old focal strategy under the same hybrid opponent.",
        "Saved profile and report hashes are checked before and after the run; no equilibrium solve, saved-profile write, or production-manifest mutation is performed."
    };
}
