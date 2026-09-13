using ACESim;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;

namespace ACESimBase.Games.LitigGame.ManualReports;

/// <summary>
/// Equilibrium-preserving coordinate mixing, not a global equilibrium finder.
/// Each block minimizes weighted sum(p^2) on simplexes with linear deviation cuts.
/// A full unrestricted best-response oracle separates missing constraints.
/// A player's same-decision information sets are mutually exclusive in the
/// supported one-round game, so their joint deviation gains are affine.
/// </summary>
public static class EquilibriumMixingSearch
{
    public sealed record Settings(int MaxSweeps = 6, int MaxCutsPerBlock = 60,
        double GainLimit = 1e-9, double ValidationTolerance = 1e-7,
        double TieTolerance = 1e-10, double ImprovementTolerance = 1e-7,
        double SourceReachThreshold = 1e-12, double SupportThreshold = 1e-6);
    public sealed record Cut(double[] Coefficients, double UpperBound, string Reason);
    public sealed record Check(double[] Gains, Cut[] Cuts);
    public sealed record BlockResult(double[] Probabilities, double[] Gains, int OracleCalls,
        int Cuts, string Status, int QpTermination);
    public sealed record PolicyChange(string Key, byte Player, string Decision,
        double Signal, int? ExitCommitment, double[] Before, double[] After);
    public sealed record Step(int Sweep, string Block, PolicyChange[] Changes,
        double ScoreBefore, double ScoreAfter, double[] Gains, int Cuts, int OracleCalls);
    public sealed record BlockAudit(int Sweep, string Key, int CandidateActions,
        string Status, int Cuts, int OracleCalls, int QpTermination);
    public sealed record Run(string Order, string Status, int Sweeps,
        double OriginalScore, double FinalScore, double[] FinalGains,
        Profile FinalProfile, Reference FinalReference, Step[] Steps, BlockAudit[] Blocks);

    public static void Validate(Settings s)
    {
        if (s.MaxSweeps < 1 || s.MaxCutsPerBlock < 1 ||
            new[] { s.GainLimit, s.ValidationTolerance, s.TieTolerance, s.ImprovementTolerance,
                s.SourceReachThreshold, s.SupportThreshold }.Any(v => !double.IsFinite(v) || v <= 0) ||
            s.GainLimit >= s.ValidationTolerance || s.TieTolerance > s.GainLimit ||
            s.SupportThreshold >= 1 || s.SourceReachThreshold >= 1)
            throw new ArgumentException("Invalid equilibrium-mixing settings.");
    }

    public static double Mixing(double[] p) => p.Length <= 1 ? 0 :
        (1 - p.Sum(x => x * x)) / (1 - 1.0 / p.Length);
    public static double Entropy(double[] p) => -p.Where(x => x > 0).Sum(x => x * Math.Log(x));
    public static double Score(Profile p, string[] eligible) => eligible.Length == 0 ? 0 :
        eligible.Average(key => Mixing(p.Strategies[key].Probabilities));
    public static Profile Replace(Profile profile, string key, double[] probabilities) => profile with
    {
        Strategies = profile.Strategies.ToDictionary(x => x.Key, x => x.Key == key
            ? x.Value with { Probabilities = probabilities.ToArray(), UniformFallback = false } : x.Value)
    };

    /// <summary>Projection of uniform onto the supplied linear-constraint polytope.</summary>
    public static (double[] Probabilities, int Termination) Project(double[] initial, IReadOnlyList<Cut> cuts,
        int[] groups = null, double[] objectiveWeights = null)
    {
        int n = initial.Length;
        groups ??= new[] { n };
        objectiveWeights ??= Enumerable.Repeat(1.0, n).ToArray();
        if (n < 2 || initial.Any(v => !double.IsFinite(v) || v < 0 || v > 1) ||
            groups.Length == 0 || groups.Any(g => g < 2) || groups.Sum() != n ||
            objectiveWeights.Length != n || objectiveWeights.Any(w => !double.IsFinite(w) || w <= 0) ||
            cuts.Any(c => c.Coefficients.Length != n ||
                c.Coefficients.Any(v => !double.IsFinite(v)) || !double.IsFinite(c.UpperBound)))
            throw new ArgumentException("Invalid mixing projection.");
        int offset = 0;
        foreach (int size in groups)
        {
            if (Math.Abs(initial.Skip(offset).Take(size).Sum() - 1) > 1e-9)
                throw new ArgumentException("Every information-set distribution must sum to one.");
            offset += size;
        }
        alglib.minqpcreate(n, out var state);
        var quadratic = new double[n, n];
        for (int i = 0; i < n; i++) quadratic[i, i] = 2 * objectiveWeights[i];
        alglib.minqpsetquadraticterm(state, quadratic);
        alglib.minqpsetlinearterm(state, new double[n]);
        alglib.minqpsetbc(state, new double[n], Enumerable.Repeat(1.0, n).ToArray());
        alglib.minqpsetscale(state, Enumerable.Repeat(1.0, n).ToArray());
        alglib.minqpsetstartingpoint(state, initial);
        var matrix = new double[cuts.Count + groups.Length, n + 1];
        var types = new int[cuts.Count + groups.Length];
        offset = 0;
        for (int g = 0; g < groups.Length; g++)
        {
            for (int i = offset; i < offset + groups[g]; i++) matrix[g, i] = 1;
            matrix[g, n] = 1;
            offset += groups[g];
        }
        for (int row = 0; row < cuts.Count; row++)
        {
            var c = cuts[row];
            // Center EACH simplex independently using its sum(p)=1 equality.
            var centered = c.Coefficients.ToArray();
            double bound = c.UpperBound;
            offset = 0;
            foreach (int size in groups)
            {
                double center = c.Coefficients.Skip(offset).Take(size).Average();
                for (int i = offset; i < offset + size; i++) centered[i] -= center;
                bound -= center;
                offset += size;
            }
            double scale = Math.Max(1e-12, Math.Max(Math.Abs(bound), centered.Max(Math.Abs)));
            for (int i = 0; i < n; i++) matrix[row + groups.Length, i] = centered[i] / scale;
            matrix[row + groups.Length, n] = bound / scale;
            types[row + groups.Length] = -1;
        }
        alglib.minqpsetlc(state, matrix, types);
        alglib.minqpsetalgobleic(state, 1e-11, 0, 1e-12, 10000);
        alglib.minqpoptimize(state);
        alglib.minqpresults(state, out var answer, out var report);
        if (report.terminationtype <= 0 || report.terminationtype == 5 ||
            answer.Any(x => !double.IsFinite(x) || x < -1e-8 || x > 1 + 1e-8))
            return (initial.ToArray(), report.terminationtype <= 0 ? report.terminationtype : report.terminationtype == 5 ? -5 : -99);
        // Cleanup is always followed by the full game oracle, never assumed safe.
        answer = answer.Select(x => x < 1e-12 ? 0 : Math.Min(1, x)).ToArray();
        offset = 0;
        foreach (int size in groups)
        {
            double mass = answer.Skip(offset).Take(size).Sum();
            if (mass <= 0) return (initial.ToArray(), -99);
            for (int i = offset; i < offset + size; i++) answer[i] /= mass;
            offset += size;
        }
        return (answer, report.terminationtype);
    }

    public static BlockResult OptimizeBlock(double[] initial, Func<double[], Check> oracle, Settings settings,
        int[] groups = null, double[] objectiveWeights = null)
    {
        Validate(settings);
        var cuts = new List<Cut>();
        double[] lastGains = null;
        int qpTermination = 0;
        for (int iteration = 0; iteration < settings.MaxCutsPerBlock; iteration++)
        {
            var projected = Project(initial, cuts, groups, objectiveWeights);
            qpTermination = projected.Termination;
            if (qpTermination <= 0) return new(initial, lastGains, iteration, cuts.Count, "QP failure; unchanged", qpTermination);
            var check = oracle(projected.Probabilities);
            if (check.Gains.Length != 2 || check.Gains.Any(x => !double.IsFinite(x)))
                throw new InvalidDataException("Invalid equilibrium verification.");
            lastGains = check.Gains;
            if (check.Gains.All(x => x <= settings.GainLimit))
                return new(projected.Probabilities, check.Gains, iteration + 1, cuts.Count, "Verified block optimum", qpTermination);
            if (check.Cuts.Length == 0)
                throw new InvalidDataException("An infeasible mixture needs a separating deviation constraint.");
            foreach (var cut in check.Cuts)
            {
                double violation = cut.Coefficients.Zip(projected.Probabilities, (a, b) => a * b).Sum() - cut.UpperBound;
                if (violation <= 0) throw new InvalidDataException("Oracle supplied a nonseparating cut.");
                cuts.Add(cut);
            }
        }
        return new(initial, lastGains, settings.MaxCutsPerBlock, cuts.Count, "Cut limit; unchanged", qpTermination);
    }

    public static Run Explore(StrategiesDeveloperBase developer, Profile source, Reference sourceReference,
        Settings settings, string order = "forward", Action<string> progress = null)
    {
        Validate(settings);
        ArticlePressureAnalysis.RequireProtocol(((LitigGameDefinition)developer.GameDefinition).Options);
        if (order is not ("forward" or "reverse")) throw new ArgumentException("Use forward or reverse block order.");
        var tolerance = new Tolerances(settings.ValidationTolerance, settings.TieTolerance, 1e-6);
        var eligible = sourceReference.InformationSets.Where(i => i.ActualReach > settings.SourceReachThreshold)
            .OrderBy(i => i.Player).ThenBy(i => i.Number).Select(i => i.Key).ToArray();
        if (order == "reverse") Array.Reverse(eligible);
        var sourceControls = Verify(developer, source, tolerance);
        if (sourceControls.Any(r => r.Gain > settings.GainLimit))
            throw new InvalidDataException("Source exceeds the search gain limit; do not repair it silently.");
        var current = source;
        var currentReference = sourceReference;
        var steps = new List<Step>();
        var blocks = new List<BlockAudit>();
        string status = "Sweep limit reached";
        int sweeps = 0;
        try
        {
            for (int sweep = 1; sweep <= settings.MaxSweeps; sweep++)
            {
                sweeps = sweep;
                int accepted = 0;
                foreach (var decisionGroup in eligible.GroupBy(key => source.Strategies[key].Player + "/" + source.Strategies[key].Decision))
                {
                    // Source-unvisited sets stay fixed. A newly unvisited set is not
                    // optimized for a cosmetic increase in the mixing score either.
                    var candidates = decisionGroup.Select(key => currentReference.InformationSets.Single(i => i.Key == key))
                        .Where(i => i.ActualReach > settings.SourceReachThreshold && !i.CounterfactuallyUnreachable &&
                            i.Actions.All(a => a.CounterfactualConditionalUtility.HasValue)).Select(i =>
                        {
                            var old = current.Strategies[i.Key].Probabilities;
                            double best = i.Actions.Max(a => a.CounterfactualConditionalUtility.Value);
                            var actions = Enumerable.Range(0, old.Length).Where(a => old[a] > 0 ||
                                best - i.Actions[a].CounterfactualConditionalUtility.Value <= settings.TieTolerance).ToArray();
                            return (Info: i, Original: old, Allowed: actions);
                        }).Where(c => c.Allowed.Length >= 2).ToArray();
                    if (candidates.Length == 0) continue;
                    var info = candidates[0].Info;
                    string key = decisionGroup.Key;
                    var groups = candidates.Select(c => c.Allowed.Length).ToArray();
                    var initial = candidates.SelectMany(c => c.Allowed.Select(a => c.Original[a])).ToArray();
                    var objectiveWeights = candidates.SelectMany(c => c.Allowed.Select(_ => 1 / (1 - 1.0 / c.Original.Length))).ToArray();
                    if (candidates.All(c => c.Allowed.Sum(a => c.Original[a] * c.Original[a]) - 1.0 / c.Allowed.Length <= settings.ImprovementTolerance)) continue;
                    var fixedProfile = current;
                    Profile Candidate(double[] p)
                    {
                        int offset = 0;
                        var entries = fixedProfile.Strategies.ToDictionary(x => x.Key, x => x.Value);
                        foreach (var c in candidates)
                        {
                            var expanded = new double[c.Original.Length];
                            for (int a = 0; a < c.Allowed.Length; a++) expanded[c.Allowed[a]] = p[offset++];
                            entries[c.Info.Key] = entries[c.Info.Key] with { Probabilities = expanded, UniformFallback = false };
                        }
                        return fixedProfile with { Strategies = entries };
                    }
                    double[] Vertex(int index)
                    {
                        var pure = initial.ToArray();
                        int offset = 0;
                        foreach (int size in groups)
                        {
                            if (index < offset + size)
                            {
                                for (int a = offset; a < offset + size; a++) pure[a] = a == index ? 1 : 0;
                                return pure;
                            }
                            offset += size;
                        }
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }
                    double[][] vertexUtilities = null;
                    Check Oracle(double[] p)
                    {
                        var candidate = Candidate(p);
                        var responses = Verify(developer, candidate, tolerance);
                        var violations = responses.Where(r => r.Gain > settings.GainLimit).ToArray();
                        if (violations.Length == 0) return new(responses.Select(r => r.Gain).ToArray(), Array.Empty<Cut>());
                        // Single-simplex vertex evaluations recover the affine gain of the
                        // discovered COMPLETE deviation, not just a local offer deviation.
                        var baseUtilities = Utilities(developer, fixedProfile);
                        vertexUtilities ??= Enumerable.Range(0, initial.Length).Select(a =>
                        {
                            var utility = Utilities(developer, Candidate(Vertex(a)));
                            return utility.Select((u, player) => u - baseUtilities[player] + baseUtilities[player] / groups.Length).ToArray();
                        }).ToArray();
                        var result = new List<Cut>();
                        foreach (var response in violations)
                        {
                            Profile Deviate(Profile vertex) => vertex with { Strategies = vertex.Strategies.ToDictionary(x => x.Key,
                                x => x.Value.Player == response.Player ? response.Response.Strategies[x.Key] : x.Value) };
                            double baseDeviation = response.Player == info.Player ? 0 : Utilities(developer, Deviate(fixedProfile))[response.Player];
                            var coefficients = new double[initial.Length];
                            for (int a = 0; a < initial.Length; a++)
                            {
                                double deviatingUtility;
                                if (response.Player == info.Player)
                                    // The deviator replaces the variable information set too.
                                    deviatingUtility = response.BestResponseUtility / groups.Length;
                                else
                                {
                                    deviatingUtility = Utilities(developer, Deviate(Candidate(Vertex(a))))[response.Player]
                                        - baseDeviation + baseDeviation / groups.Length;
                                }
                                coefficients[a] = deviatingUtility - vertexUtilities[a][response.Player];
                            }
                            double predicted = coefficients.Zip(p, (a, b) => a * b).Sum();
                            Near(response.Gain, predicted, settings.ValidationTolerance, "Affine deviation replay");
                            // Preserve feasibility of the already verified current profile.
                            // A fixed tighter RHS can make later blocks infeasible merely
                            // because another accepted block consumed part of the tolerance.
                            double currentDeviation = coefficients.Zip(initial, (a, b) => a * b).Sum();
                            double bound = Math.Min(settings.GainLimit,
                                Math.Max(settings.GainLimit * .25, currentDeviation + settings.GainLimit * .001));
                            result.Add(new(coefficients, bound, "Player " + response.Player + " unrestricted deviation"));
                        }
                        return new(responses.Select(r => r.Gain).ToArray(), result.ToArray());
                    }
                    var optimized = OptimizeBlock(initial, Oracle, settings, groups, objectiveWeights);
                    blocks.Add(new(sweep, key, initial.Length, optimized.Status, optimized.Cuts, optimized.OracleCalls, optimized.QpTermination));
                    var proposal = Candidate(optimized.Probabilities);
                    double before = Score(current, eligible), after = Score(proposal, eligible);
                    if (optimized.Status == "Verified block optimum" && after > before + settings.ImprovementTolerance)
                    {
                        current = proposal;
                        currentReference = Describe(developer, current);
                        var changes = candidates.Where(c => c.Original.Zip(current.Strategies[c.Info.Key].Probabilities, (a, b) => Math.Abs(a - b)).Max() > 1e-12)
                            .Select(c => new PolicyChange(c.Info.Key, c.Info.Player, c.Info.Decision, c.Info.SignalValue, c.Info.ExitCommitment,
                                c.Original.ToArray(), current.Strategies[c.Info.Key].Probabilities.ToArray())).ToArray();
                        steps.Add(new(sweep, key, changes, before, after,
                            optimized.Gains, optimized.Cuts, optimized.OracleCalls));
                        accepted++;
                        progress?.Invoke($"  {order} sweep {sweep}: {info.Decision}, {changes.Length} information sets jointly; score {after:F6}; max gain {optimized.Gains.Max():G3}");
                    }
                    else if (optimized.Status != "Verified block optimum")
                        progress?.Invoke($"  {order} sweep {sweep}: {key}: {optimized.Status}");
                }
                progress?.Invoke($"{order}: sweep {sweep} accepted {accepted} blocks; score {Score(current, eligible):F6}");
                if (accepted == 0)
                {
                    status = blocks.Where(b => b.Sweep == sweep).Any(b => b.Status != "Verified block optimum")
                        ? "No improvement; some block optimizations unresolved" : "Coordinate search converged within improvement tolerance";
                    break;
                }
            }
            var finalControls = Verify(developer, current, tolerance);
            if (finalControls.Any(r => r.Gain > settings.GainLimit)) throw new InvalidDataException("Final profile failed equilibrium validation.");
            foreach (string key in source.Strategies.Keys.Except(eligible))
                if (!source.Strategies[key].Probabilities.SequenceEqual(current.Strategies[key].Probabilities))
                    throw new InvalidDataException("A source-unvisited policy changed.");
            return new(order, status, sweeps, Score(source, eligible), Score(current, eligible),
                finalControls.Select(r => r.Gain).ToArray(), current, Describe(developer, current), steps.ToArray(), blocks.ToArray());
        }
        finally { Apply(developer, source); }
    }

    public static Result[] Verify(StrategiesDeveloperBase developer, Profile profile, Tolerances tolerances) =>
        new[] { Respond(developer, profile, 0, "mixing-check-P", tolerances), Respond(developer, profile, 1, "mixing-check-D", tolerances) };
    private static double[] Utilities(StrategiesDeveloperBase developer, Profile profile)
    {
        Apply(developer, profile);
        return Walk(developer, 0).RootUtility;
    }
}
