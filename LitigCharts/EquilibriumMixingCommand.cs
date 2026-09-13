using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.EquilibriumMixingSearch;
using Strategy = ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis.Strategy;

namespace LitigCharts;

public static class EquilibriumMixingCommand
{
    public sealed record Request(string OutputDirectory, ArticlePressureAnalysis.Source[] Sources,
        Settings Settings = null, string[] Orders = null);
    public sealed record Report(string Schema, DateTimeOffset CreatedUtc,
        ArticlePressureAnalysis.Source Source, Settings Settings, ArticlePressureAnalysis.Fingerprint Request,
        ArticlePressureAnalysis.Fingerprint Equilibrium, ArticlePressureAnalysis.Fingerprint ActionReport,
        ArticlePressureAnalysis.Fingerprint CoreAssembly, int ValidatedRows,
        Profile OriginalProfile, Reference OriginalReference, string SelectedOrder, Run[] Runs, Outcome[] Outcomes,
        string[] Interpretation);
    public sealed record Outcome(string Profile, double NotFiled, double NotAnswered,
        double Settlement, double Trial, double ExitAfterFailedBargaining);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("equilibrium-mixing --request <json> (diagnostic only; no source profile writes)");
                return 0;
            }
            if (args.Length != 2 || args[0] != "--request") throw new ArgumentException("Supply --request <json>.");
            await ExecuteAsync(Path.GetFullPath(args[1]), Console.WriteLine);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    public static async Task ExecuteAsync(string requestFile, Action<string> progress)
    {
        var json = ArticleWorkedPathExtraction.JsonOptions;
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestFile), json)
            ?? throw new InvalidDataException("Empty mixing request.");
        var settings = request.Settings ?? new();
        Validate(settings);
        var orders = request.Orders ?? new[] { "forward", "reverse" };
        if (orders.Length == 0 || orders.Distinct().Count() != orders.Length || orders.Any(o => o is not ("forward" or "reverse")))
            throw new InvalidDataException("Use distinct forward/reverse search orders.");
        if (request.Sources == null || request.Sources.Length == 0 || request.Sources.Any(s =>
            string.IsNullOrWhiteSpace(s.Id) || s.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || s.EquilibriumNumber < 1 || s.ProfileFile != null) ||
            request.Sources.Select(s => s.Id).Distinct().Count() != request.Sources.Length)
            throw new InvalidDataException("Supply uniquely identified saved sources without profile overrides.");
        string Resolve(string p) => Path.GetFullPath(p, Path.GetDirectoryName(Path.GetFullPath(requestFile)));
        string output = Resolve(request.OutputDirectory);
        foreach (var source in request.Sources)
            foreach (string input in new[] { Resolve(source.EquilibriumFile), Resolve(source.ActionReportFile) })
            {
                string inputDirectory = Path.GetDirectoryName(input).TrimEnd(Path.DirectorySeparatorChar);
                if (output.Equals(inputDirectory, StringComparison.OrdinalIgnoreCase) ||
                    output.StartsWith(inputDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Mixing reports must be outside the source-results directory.");
            }
        Directory.CreateDirectory(output);
        var summary = new StringBuilder("# Equilibrium-preserving mixing exploration\n\n" + string.Join("\n\n", Interpretation) + "\n\n");
        foreach (var source in request.Sources)
        {
            var eqHash = ArticlePressureAnalysis.Hash(Resolve(source.EquilibriumFile));
            var actionHash = ArticlePressureAnalysis.Hash(Resolve(source.ActionReportFile));
            progress?.Invoke("Initialize and validate " + source.Id);
            var options = ArticleWorkedPathExtraction.CreateOptions(source.OptionSetName);
            ArticlePressureAnalysis.RequireProtocol(options);
            var developer = await ArticleWorkedPathExtraction.InitializeAsync(options);
            string line = File.ReadLines(eqHash.Path).Skip(source.EquilibriumNumber - 1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("Missing saved equilibrium.");
            var fallback = ArticleWorkedPathExtraction.LoadProfile(developer, line.Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray());
            int validatedRows = ArticleWorkedPathExtraction.ValidateActionReport(developer, source.EquilibriumNumber, actionHash.Path);
            var original = Capture(developer, source.Id, fallback);
            var originalReference = Describe(developer, original);
            original = WithObservedReach(original, originalReference);
            var runs = new List<Run>();
            foreach (string order in orders)
            {
                var run = Explore(developer, original, originalReference, settings, order, progress);
                runs.Add(run);
                // Checkpoint each completed run separately; never overwrite production files.
                await File.WriteAllTextAsync(Path.Combine(output, source.Id + "-" + order + ".json"), JsonSerializer.Serialize(run, json) + "\n");
                await File.WriteAllTextAsync(Path.Combine(output, source.Id + "-" + order + "-profile.json"), JsonSerializer.Serialize(run.FinalProfile, json) + "\n");
            }
            var selected = runs.OrderByDescending(r => r.FinalScore).ThenBy(r => r.FinalGains.Max()).First();
            if (ArticlePressureAnalysis.Hash(eqHash.Path).Sha256 != eqHash.Sha256 ||
                ArticlePressureAnalysis.Hash(actionHash.Path).Sha256 != actionHash.Sha256)
                throw new IOException("Source results changed during mixing exploration.");
            var report = new Report("1", DateTimeOffset.UtcNow, source, settings,
                ArticlePressureAnalysis.Hash(requestFile), eqHash, actionHash,
                ArticlePressureAnalysis.Hash(typeof(EquilibriumMixingSearch).Assembly.Location), validatedRows,
                original, originalReference, selected.Order, runs.ToArray(),
                new[] { Outcomes(developer, original, "original") }.Concat(runs.Select(r => Outcomes(developer, r.FinalProfile, r.Order))).ToArray(), Interpretation);
            string stem = Path.Combine(output, source.Id);
            await File.WriteAllTextAsync(stem + ".json", JsonSerializer.Serialize(report, json) + "\n");
            await File.WriteAllTextAsync(stem + "-profile.json", JsonSerializer.Serialize(selected.FinalProfile, json) + "\n");
            await File.WriteAllTextAsync(stem + ".md", Markdown(report));
            summary.AppendLine($"- [{source.Id}]({source.Id}.md): mixing score {selected.OriginalScore:F6} -> {selected.FinalScore:F6}; selected {selected.Order}; maximum unilateral gain {selected.FinalGains.Max():G6}.");
            progress?.Invoke($"Completed {source.Id}: {selected.Order}; score {selected.FinalScore:F6}; maximum gain {selected.FinalGains.Max():G6}");
        }
        await File.WriteAllTextAsync(Path.Combine(output, "README.md"), summary.ToString());
    }

    public static string Markdown(Report report)
    {
        var run = report.Runs.Single(r => r.Order == report.SelectedOrder);
        var b = new StringBuilder("# " + report.Source.Id + ": equilibrium-preserving mixing\n\n");
        b.AppendLine("Source specification: `" + report.Source.OptionSetName + "`. Original saved equilibrium retained unchanged.\n");
        b.AppendLine("Mixing score is the equally weighted mean of (1 - sum of squared probabilities)/(1 - 1/action count) over source-reached information sets. Zero means pure; one means uniform over every action. This is a quadratic/Gini mixing measure, not Shannon entropy or a predicted randomization rule.\n");
        b.AppendLine("| Search order | Initial score | Final score | Accepted changes | Maximum gain | Status |\n|---|---:|---:|---:|---:|---|");
        foreach (var r in report.Runs)
            b.AppendLine($"| {r.Order}{(r.Order == run.Order ? " (selected)" : "")} | {r.OriginalScore:F6} | {r.FinalScore:F6} | {r.Steps.Length} | {r.FinalGains.Max():G6} | {r.Status} |");
        b.AppendLine($"\nSelected profile's unrestricted best-response gains: P **{run.FinalGains[0]:G8}**, D **{run.FinalGains[1]:G8}** (reported utility units). Acceptance limit: {report.Settings.GainLimit:G3}. This checks Nash deviations at the root, not an equilibrium refinement.\n");
        b.AppendLine("## Aggregate outcomes\n\nUnconditional percentages, partitioning all potential disputes.\n\n| Profile | Not filed | Not answered | Settled | Trial | Exit after failed bargaining |\n|---|---:|---:|---:|---:|---:|");
        foreach (var o in report.Outcomes)
            b.AppendLine($"| {o.Profile} | {100 * o.NotFiled:F6} | {100 * o.NotAnswered:F6} | {100 * o.Settlement:F6} | {100 * o.Trial:F6} | {100 * o.ExitAfterFailedBargaining:F6} |");
        b.AppendLine("## Strategy changes\n\nOnly changed, source-reached information sets are listed. Unvisited source policies remain frozen, including uniform loader fallbacks. Offer branches retain the player's private exit commitment.\n");
        b.AppendLine("| Player | Decision | Signal | Branch | Original distribution | Selected distribution | Effective actions, original -> selected |\n|---|---|---:|---|---|---|---:|");
        foreach (var info in report.OriginalReference.InformationSets)
        {
            var old = report.OriginalProfile.Strategies[info.Key];
            var end = run.FinalProfile.Strategies[info.Key];
            if (old.Probabilities.Zip(end.Probabilities, (a, c) => Math.Abs(a - c)).Max() <= report.Settings.SupportThreshold) continue;
            string branch = info.ExitCommitment == 2 ? "continue" : info.ExitCommitment == 1 ? "exit" : "";
            b.AppendLine($"| {(info.Player == 0 ? "P" : "D")} | {info.Decision} | {info.SignalValue:F2} | {branch} | {Distribution(old, report.Settings.SupportThreshold)} | {Distribution(end, report.Settings.SupportThreshold)} | {Math.Exp(Entropy(old.Probabilities)):F3} -> {Math.Exp(Entropy(end.Probabilities)):F3} |");
        }
        b.AppendLine("\nEffective actions = exp(Shannon entropy), a supplementary measure. Displayed shares omit probabilities below the support threshold; the JSON retains all probabilities.\n");
        b.AppendLine("## Interpretation\n");
        foreach (string note in report.Interpretation) b.AppendLine("- " + note);
        b.AppendLine("\nFull profiles, action utilities and reaches, best-response gains, every accepted block, solver statuses and input fingerprints are in the JSON files. No existing decomposition or publication figure has been replaced.\n");
        return b.ToString().TrimEnd() + "\n";
    }

    private static string Distribution(Strategy strategy, double threshold) => string.Join("; ",
        strategy.Probabilities.Select((p, a) => (p, a)).Where(x => x.p > threshold)
            .Select(x => strategy.ActionLabels[x.a] + ": " + (100 * x.p).ToString("0.###", CultureInfo.InvariantCulture) + "%"));

    public static Outcome Outcomes(StrategiesDeveloperBase developer, Profile profile, string name)
    {
        var restore = Capture(developer, "restore");
        try
        {
            Apply(developer, profile);
            var recorder = new RecordGamePathsProcessor();
            developer.TreeWalk_Tree(recorder);
            var counts = new double[5];
            foreach (var path in recorder.Paths)
            {
                if (path.Probability == 0) continue;
                var actions = path.Steps.ToDictionary(x => (LitigGameDecisions)((IAnyNode)x.FromNode).Decision.DecisionByteCode, x => x.ActionIndex);
                int bucket;
                if (actions[LitigGameDecisions.PFile] == 2) bucket = 0;
                else if (actions[LitigGameDecisions.DAnswer] == 2) bucket = 1;
                else if (actions[LitigGameDecisions.DOffer] >= actions[LitigGameDecisions.POffer]) bucket = 2;
                else if (actions[LitigGameDecisions.PAbandon] == 2 && actions[LitigGameDecisions.DDefault] == 2) bucket = 3;
                else bucket = 4;
                counts[bucket] += path.Probability;
            }
            Near(1, counts.Sum(), 1e-9, "Mixing outcome partition");
            return new(name, counts[0], counts[1], counts[2], counts[3], counts[4]);
        }
        finally { Apply(developer, restore); }
    }

    public static readonly string[] Interpretation =
    {
        "This explores local equilibrium-preserving mixing around the original equilibrium. It does not find or certify a globally maximally mixed equilibrium, enumerate all equilibria, or impose quantal response.",
        "A player's same-decision information sets change jointly across signals and reached exit branches. They are mutually exclusive on any path in the supported one-round game, making their joint deviation comparisons affine. Candidate actions include current support and actions tied in conditional utility under current continuation play. A quadratic program spreads probability subject to full-strategy deviation constraints, added by the unrestricted best-response oracle for BOTH players.",
        "Each accepted complete profile passes the stated root-payoff deviation limit. A local utility tie alone is never treated as permission to change the equilibrium. Numerical tolerances are absolute reported utility units, not percentages of damages or a 2% equilibrium tolerance.",
        "Only information sets reached in the original equilibrium enter the objective or can change. Source-unvisited policies stay fixed. Sets becoming unvisited are also skipped; this prevents meaningless off-path mixing from inflating the score.",
        "Forward and reverse decision-block orders restart independently from the saved original. Their difference measures search-order sensitivity, not exhaustive bounds. Blocks can be constrained optima while the joint search remains order-dependent and only locally converged. Multiple changes in an accepted block must be applied together; intermediate partial application is not verified.",
        "Adding or mixing actions can change opponent incentives, beliefs and outcomes. Every accepted full profile is rechecked, and final action values/reaches are retained. An evenly spread representative is a diagnostic convention, not an additional behavioral prediction."
    };
}
