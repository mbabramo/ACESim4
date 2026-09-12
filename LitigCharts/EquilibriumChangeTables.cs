using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.EquilibriumChangeDecomposition;

namespace LitigCharts;

/// <summary>Change-only publication tables; replaces the former seven-column pressure packets.</summary>
public static class EquilibriumChangeTables
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public sealed record ReportHeading(string Title, string HeldFixed, string Cost,
        string Original, string Target, string OriginalColumn, string TargetColumn);
    public sealed record RowGroup(ChangeRow First, ChangeRow Last, int Count);

    public static ReportHeading Heading(string sourceOptionSet, string targetOptionSet)
    {
        var source = ArticleWorkedPathExtraction.CreateOptions(sourceOptionSet);
        var target = ArticleWorkedPathExtraction.CreateOptions(targetOptionSet);
        ValidateMatchedOptions(source, target);
        string From(string key) => Convert.ToString(source.VariableSettings[key], Invariant);
        string To(string key) => Convert.ToString(target.VariableSettings[key], Invariant);
        string fromFee = From("Fee Regime"), toFee = To("Fee Regime");
        string fromRisk = From("Risk Aversion").ToLowerInvariant(), toRisk = To("Risk Aversion").ToLowerInvariant();
        bool fees = fromFee != toFee;
        string ShortRisk(string risk) => risk == "moderately risk averse" ? "risk averse" : risk;
        return new(fees ? $"Fee shifting: {fromFee} to {toFee}" : $"Preferences: {fromRisk} to {toRisk}",
            fees ? $"Both players remain {fromRisk}" : $"{fromFee} rule remains in force",
            From("Costs Multiplier"), $"{fromFee} rule, {fromRisk}", $"{toFee} rule, {toRisk}",
            fees ? fromFee : ShortRisk(fromRisk), fees ? toFee : ShortRisk(toRisk));
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("equilibrium-changes --request <json> [--calculate-only | --render-only]");
                return 0;
            }
            string requestFile = null; bool calculateOnly = false, renderOnly = false;
            for (int i = 0; i < args.Length; i++)
                switch (args[i])
                {
                    case "--request" when i + 1 < args.Length: requestFile = Path.GetFullPath(args[++i]); break;
                    case "--calculate-only": calculateOnly = true; break;
                    case "--render-only": renderOnly = true; break;
                    default: throw new ArgumentException("Unknown or incomplete argument: " + args[i]);
                }
            if (requestFile == null || calculateOnly && renderOnly) throw new ArgumentException("Supply one request and at most one mode.");
            if (!renderOnly) await ArticlePressureAnalysis.RunAsync(requestFile, Console.WriteLine);
            if (!calculateOnly) await RenderAsync(requestFile);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    public static async Task RenderAsync(string requestFile)
    {
        var request = ReadRequest(requestFile);
        string Resolve(string p) => Path.GetFullPath(p, Path.GetDirectoryName(Path.GetFullPath(requestFile)));
        string output = Resolve(request.OutputDirectory);
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(output, "equilibrium-changes-manifest.json")), JsonOptions)
            ?? throw new InvalidDataException("Missing completed manifest.");
        if (manifest.Schema != "2" || manifest.Request.Sha256 != Hash(requestFile).Sha256)
            throw new InvalidDataException("Recalculate: incompatible schema or changed request.");
        foreach (var fingerprint in manifest.OutputFingerprints.Concat(manifest.Sources.SelectMany(s => new[] { s.Equilibrium, s.ActionReport })))
            if (Hash(fingerprint.Path).Sha256 != fingerprint.Sha256)
                throw new InvalidDataException("Input or calculation changed: " + fingerprint.Path);
        var results = manifest.OutputJsonFiles.Select(path => JsonSerializer.Deserialize<ContrastResult>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException(path)).ToArray();
        if (results.Any(r => r.Schema != "2" || r.Changes == null))
            throw new InvalidDataException("Old pressure data cannot be rendered as additive decompositions.");
        var sources = new List<string>();
        foreach (var result in results)
        {
            string stem = Path.Combine(output, result.Contrast.Id);
            await WriteReport(stem, new[] { result });
            sources.Add(stem + ".tex");
        }
        foreach (var group in results.GroupBy(r => Heading(r.SourceOptionSet, r.TargetOptionSet).Cost))
        {
            bool ordinary = group.Key == "1";
            string directory = ordinary && request.PublicationDirectory != null ? Resolve(request.PublicationDirectory) : output;
            Directory.CreateDirectory(directory);
            string stem = Path.Combine(directory, ordinary ? "Equilibrium strategy changes" : "High-cost equilibrium strategy changes");
            await WriteReport(stem, group.ToArray());
            await File.WriteAllTextAsync(stem + ".json", JsonSerializer.Serialize(group.Select(r => new
            {
                r.Contrast, r.SourceOptionSet, r.TargetOptionSet, r.Changes, r.ExcludedHistories
            }), JsonOptions) + "\n");
            sources.Add(stem + ".tex");
        }
        await DiagramCompiler.CompileAllAsync(sources.ToArray(), new ArticleDiagramCommand.Configuration(), 2, passes: 2);
        var readme = new StringBuilder("# Equilibrium strategy changes\n\n");
        readme.AppendLine(Notes);
        readme.AppendLine("\n## Reports\n");
        foreach (var result in results)
        {
            var h = Heading(result.SourceOptionSet, result.TargetOptionSet);
            readme.AppendLine($"- [{h.Title}; {h.HeldFixed}; costs {h.Cost}]({result.Contrast.Id}.pdf): " +
                $"{GroupRows(result.Changes).Length} displayed rows ({result.Changes.Length} ungrouped changed rows), " +
                $"{result.Changes.Count(r => r.EndpointSelection)} selection-residual rows.");
        }
        readme.AppendLine("\nFull source profiles, all eight coalition best responses, action values, conditional beliefs, " +
            "off-path exposure checks, tie/completion stresses, selection residuals, and excluded histories are retained in the calculation JSON and manifest.");
        readme.AppendLine("\nThe earlier Information-set pressure directory is historical: its independent columns were not additive contributions. " +
            "The former table generator has been removed; the pressure command is an alias for the new workflow.");
        await File.WriteAllTextAsync(Path.Combine(output, "README.md"), readme.ToString());
    }

    private static async Task WriteReport(string stem, ContrastResult[] results)
    {
        await File.WriteAllTextAsync(stem + ".tex", Latex(results));
        var notes = new StringBuilder(Notes + "\n\n");
        foreach (var result in results)
        {
            var heading = Heading(result.SourceOptionSet, result.TargetOptionSet);
            notes.AppendLine(heading.Title + "; " + heading.HeldFixed + "; costs " + heading.Cost);
            foreach (var row in GroupRows(result.Changes))
            {
                var r = row.First;
                notes.AppendLine($"{(r.Player == 0 ? "P" : "D")} {DecisionLabel(r)}; signal {SignalLabel(row)}: " +
                    $"{r.Allocation.Original:G6} -> {r.Allocation.Target:G6}; " +
                    $"direct={r.Allocation.Direct:G6}, entry={r.Allocation.Entry:G6}, offers={r.Allocation.Offers:G6}, exit={r.Allocation.Exit:G6}; " +
                    $"selection residual={r.Allocation.SelectionResidual:G6}; " +
                    $"tie-sensitive={r.TieSensitive}, completion-sensitive={r.CompletionSensitive}, undefined={r.CounterfactualUndefined}.");
            }
            notes.AppendLine("Histories omitted because reach changes:");
            foreach (var x in result.ExcludedHistories)
                notes.AppendLine($"  {(x.Player == 0 ? "P" : "D")} {x.Decision}, signal {x.SignalValue:0.00}, exit {x.ExitCommitment}: {x.Reason}");
            notes.AppendLine();
        }
        await File.WriteAllTextAsync(stem + ".txt", notes.ToString());
    }

    public static RowGroup[] GroupRows(IEnumerable<ChangeRow> rows)
    {
        int DecisionOrder(string d) => d is "P Files" or "D Answers" ? 0 : d is "P Abandons" or "D Defaults" ? 1 : 2;
        var ordered = rows.OrderBy(r => r.Player).ThenBy(r => DecisionOrder(r.Decision))
            .ThenBy(r => r.ExitCommitment).ThenBy(r => r.Action).ThenBy(r => r.Signal).ToArray();
        var groups = new List<RowGroup>();
        foreach (var row in ordered)
        {
            if (groups.Count > 0 && CanGroup(groups[^1].Last, row))
                groups[^1] = groups[^1] with { Last = row, Count = groups[^1].Count + 1 };
            else groups.Add(new(row, row, 1));
        }
        return groups.ToArray();
    }

    public static bool CanGroup(ChangeRow a, ChangeRow b)
    {
        double[] Values(ChangeRow r) => new[] { r.Allocation.Original, r.Allocation.Target, r.Allocation.Direct,
            r.Allocation.Entry, r.Allocation.Offers, r.Allocation.Exit, r.Allocation.SelectionResidual };
        return a.Player == b.Player && a.Decision == b.Decision && a.Signal + 1 == b.Signal &&
            a.ExitCommitment == b.ExitCommitment && a.Metric == b.Metric && a.Action == b.Action &&
            a.EndpointSelection == b.EndpointSelection && a.TieSensitive == b.TieSensitive &&
            a.CompletionSensitive == b.CompletionSensitive && a.CounterfactualUndefined == b.CounterfactualUndefined &&
            a.UnreachedCoalitions.SequenceEqual(b.UnreachedCoalitions) &&
            a.OriginalPolicy.Zip(b.OriginalPolicy, (x, y) => Math.Abs(x - y)).Max() < PolicyTolerance &&
            a.TargetPolicy.Zip(b.TargetPolicy, (x, y) => Math.Abs(x - y)).Max() < PolicyTolerance &&
            Values(a).Zip(Values(b), (x, y) => Math.Abs(x - y)).Max() < PolicyTolerance;
    }

    public static string DecisionLabel(ChangeRow r)
    {
        if (r.Decision == "P Files") return "File (%)";
        if (r.Decision == "D Answers") return "Answer (%)";
        if (r.Decision == "P Abandons") return "Commit to abandon (%)";
        if (r.Decision == "D Defaults") return "Commit to default (%)";
        string offer = r.Player == 0 ? "Demand" : "Offer";
        string exit = r.ExitCommitment == 2 ? "continue" : r.Player == 0 ? "abandon" : "default";
        return r.Action.HasValue ? $"{offer} {r.ActionLabels[r.Action.Value - 1]} (%) / {exit}"
            : $"{offer} / {exit}";
    }

    private static string SignalLabel(RowGroup group) => group.First.SignalValue.ToString("0.00", Invariant) +
        (group.Count > 1 ? "-" + group.Last.SignalValue.ToString("0.00", Invariant) : "");
    public static string Number(double value, bool amount, bool signed = false)
    {
        if (Math.Abs(value) < 0.5 * (amount ? .001 : .01)) value = 0;
        return (signed && value > 0 ? "+" : "") + value.ToString(amount ? "0.00#" : "0.##", Invariant);
    }

    public static string Latex(ContrastResult[] results)
    {
        var b = new StringBuilder("""
            \documentclass[10pt]{article}
            \usepackage[letterpaper,margin=0.5in]{geometry}
            \usepackage[T1]{fontenc}
            \usepackage{lmodern,booktabs,longtable,array,microtype}
            \setlength{\parindent}{0pt}
            \setlength{\tabcolsep}{5pt}
            \setlength{\LTpre}{5pt}
            \setlength{\LTpost}{4pt}
            \begin{document}
            """);
        bool first = true;
        foreach (var result in results)
        {
            if (!first) b.AppendLine(@"\clearpage");
            first = false;
            var h = Heading(result.SourceOptionSet, result.TargetOptionSet);
            b.AppendLine(@"{\large\bfseries " + Escape(h.Title) + @"}\par");
            b.AppendLine(Escape(h.HeldFixed) + "; costs unchanged at multiplier " + Escape(h.Cost) + @".\par");
            b.AppendLine(@"{\small Changed decisions only. Original: " + Escape(h.OriginalColumn) + "; target: " + Escape(h.TargetColumn) + @".}\par");
            b.AppendLine(@"\begin{longtable}{@{}p{1.9in}rrrrrrrr@{}}");
            b.AppendLine(@"\toprule Decision & Signal & Original & Target & Change & Direct & Entry & Offers & Exit \\\midrule\endfirsthead");
            b.AppendLine(@"\toprule Decision & Signal & Original & Target & Change & Direct & Entry & Offers & Exit \\\midrule\endhead");
            b.AppendLine(@"\bottomrule\endfoot");
            foreach (byte player in new byte[] { 0, 1 })
            {
                var rows = GroupRows(result.Changes.Where(r => r.Player == player));
                if (rows.Length == 0) continue;
                b.AppendLine(@"\multicolumn{9}{@{}l}{\textbf{" + (player == 0 ? "Plaintiff" : "Defendant") + @"}}\\");
                foreach (var group in rows)
                {
                    var r = group.First; var a = r.Allocation;
                    bool amount = r.Metric == "offer amount";
                    string marks = (r.TieSensitive ? @"\dagger" : "") + (r.CompletionSensitive ? "*" : "") +
                        (r.UnreachedCoalitions.Length > 0 ? @"\ddagger" : "");
                    b.Append(Escape(DecisionLabel(r)) + (marks.Length > 0 ? "$^{" + marks + "}$" : "") +
                        " & " + SignalLabel(group) + " & " + Number(a.Original, amount) + " & " + Number(a.Target, amount) +
                        " & " + Number(a.Change, amount, true));
                    if (r.CounterfactualUndefined)
                        b.Append(@" & \multicolumn{4}{c}{Undefined counterfactual}");
                    else if (r.EndpointSelection)
                        b.Append(@" & \multicolumn{4}{c}{Selection-dependent}");
                    else
                        foreach (double effect in new[] { a.Direct, a.Entry, a.Offers, a.Exit })
                            b.Append(" & " + Number(effect, amount, true));
                    b.AppendLine(@"\\");
                }
            }
            if (result.Changes.Length == 0)
                b.AppendLine(@"\multicolumn{9}{l}{No changed decisions reached in both equilibria.}\\");
            b.AppendLine(@"\end{longtable}");
            b.AppendLine(@"{\footnotesize Direct: rule/preferences first. Entry, Offers, Exit: opponent contributions, averaged over six replacement orders. Four contributions sum to Change, before rounding.\par");
            b.AppendLine(@"Probability changes are percentage points; pure demand/offer changes are fractions of damages. Mixed offers are shown as action probabilities, not averages.\par");
            b.AppendLine(@"$\dagger$: tie-sensitive allocation. $*$: sensitive to an unvisited opponent policy. $\ddagger$: a conditional policy at an intermediate history not reached in that hybrid.\par");
            b.AppendLine(@"Selection-dependent: the original-preserving optimal response does not reproduce the target action/mix; no four-way allocation is claimed. Undefined counterfactual: no opponent-and-chance reach.\par");
            b.AppendLine(result.ExcludedHistories.Count(x => x.Reason.StartsWith("no longer")) +
                " histories no longer reached; " + result.ExcludedHistories.Count(x => x.Reason.StartsWith("newly")) +
                @" newly reached. These have no comparable endpoint action and are omitted. Detailed diagnostics accompany the table.}\par");
        }
        return b.AppendLine(@"\end{document}").ToString();
    }

    public static string Escape(string value) => value.Replace("&", @"\&").Replace("%", @"\%")
        .Replace("_", @"\_").Replace("#", @"\#");

    public const string Notes =
        "Only changed decisions reached in both actual endpoint equilibria are displayed. Newly/no-longer reached histories are recorded in JSON, never assigned a fictitious endpoint strategy. " +
        "Adjacent signal rows are combined only when their endpoint policies, allocations, flags, and intermediate reach patterns agree within 1e-6. " +
        "Direct changes the fee rule or risk preferences first, holding the opponent's original strategy. Entry, offers, and exit then average marginal replacements across all six orders using all eight subsets. " +
        "Each hybrid reoptimizes the entire focal-player strategy; this is not the fixed-current-strategy misalignment measure and not an observed adjustment process. " +
        "Primary tie selection preserves original probability on optimal actions. Low/high tie alternatives and unvisited-opponent completion stresses identify sensitivity, not exhaustive bounds. " +
        "When the all-target-opponent selected best response does not reproduce the actual target action or mix, the residual is retained in JSON and the publication row is marked Selection-dependent. " +
        "No arbitrary residual is forced into the four categories. Defined, reconciled rows add exactly before display rounding. " +
        "Intermediate histories with positive opponent-and-chance reach have conditional continuation policies even if the response would not reach them; they carry a double dagger. " +
        "Probability contributions use percentage points. Only wholly pure offer comparisons use monetary grid values; mixed comparisons use separate action probabilities. " +
        "This direct-first, tie-convention-conditional accounting is a selected-equilibrium diagnostic, not uniquely identified causal shares.";
}
