using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.EquilibriumChangeDecomposition;

namespace LitigCharts;

/// <summary>Original equilibrium coordinates, selected by the common focus checks, without table notes.</summary>
public static class EquilibriumPublicationTables
{
    public sealed record Group(ChangeRow[] Rows)
    {
        public ChangeRow First => Rows[0];
        public ChangeRow Last => Rows[^1];
        public string Sensitivity => SensitivityLabel(Rows);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var arguments = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 == args.Length || !new[] { "--original", "--mixed", "--check", "--output", "--previews" }.Contains(args[i]) ||
                    !arguments.TryAdd(args[i], Path.GetFullPath(args[i + 1])))
                    throw new ArgumentException("equilibrium-publication --original <request> --mixed <request> --check <request> --output <directory> --previews <QA directory>");
            }
            if (arguments.Count != 5) throw new ArgumentException("Supply all five publication arguments.");
            string output = arguments["--output"], previews = arguments["--previews"];
            if (output.Equals(previews, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Keep QA previews outside the publication directory.");
            var original = EquilibriumChangeTables.LoadCalculated(arguments["--original"]);
            var mixed = EquilibriumChangeTables.LoadCalculated(arguments["--mixed"]);
            var check = EquilibriumChangeTables.LoadCalculated(arguments["--check"]);
            var publications = mixed.Results.Select(m =>
            {
                var o = original.Results.Single(r => r.Contrast.Id == m.Contrast.Id);
                var c = check.Results.Single(r => r.Contrast.Id == m.Contrast.Id);
                var selected = Select(o, m, c);
                return (Original: o, Rows: selected, Name: FileName(o));
            }).ToArray();
            if (publications.Length != 4 || publications.Select(p => p.Name).Distinct().Count() != 4)
                throw new InvalidDataException("This publication packet requires the four ordinary-cost interventions.");
            foreach (var manifest in new[] { original.Manifest, mixed.Manifest, check.Manifest })
                if (manifest.OutputJsonFiles.Any(p => Path.GetDirectoryName(p).Equals(output, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Do not write publication files in a calculation directory.");
            Directory.CreateDirectory(output);
            var sources = new List<string>();
            foreach (var p in publications)
            {
                string path = Path.Combine(output, p.Name + ".tex");
                string provenance = string.Join("\n", new[] { "--original", "--mixed", "--check" }
                    .Select(k => "% Input request SHA-256: " + Hash(arguments[k]).Sha256));
                await File.WriteAllTextAsync(path, provenance + "\n" + Latex(p.Original, p.Rows));
                sources.Add(path);
                Console.WriteLine($"{p.Name}: {p.Rows.Length} information sets, {GroupRows(p.Rows).Length} displayed rows.");
            }
            await DiagramCompiler.CompileAllAsync(sources.ToArray(), new(), 2, previewDirectory: previews);
            Console.WriteLine("Publication directory: " + output);
            // Methodology.tex is deliberately NOT generated or overwritten: it belongs to the author.
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    public static ChangeRow[] Select(ContrastResult original, ContrastResult mixed, ContrastResult check)
    {
        if (new[] { mixed, check }.Any(r => r.Contrast.Id != original.Contrast.Id ||
            r.SourceOptionSet != original.SourceOptionSet || r.TargetOptionSet != original.TargetOptionSet))
            throw new InvalidDataException("Compare the same directed intervention in all three representations.");
        double tolerance = original.Tolerances.NearTie;
        HashSet<string> Focus(ContrastResult r) => EquilibriumChangeFocus.Build(r, tolerance)
            .Where(i => i.Focus).Select(i => i.Key).ToHashSet();
        var keys = Focus(original);
        keys.IntersectWith(Focus(mixed));
        keys.IntersectWith(Focus(check));
        var offsets = OffsettingEffects(original);
        var offsetKeys = OffsettingEffects(mixed).Select(r => r.Key).ToHashSet();
        offsetKeys.IntersectWith(OffsettingEffects(check).Select(r => r.Key));
        var selected = original.Changes.Where(r => keys.Contains(r.Key))
            .Concat(offsets.Where(r => offsetKeys.Contains(r.Key))).ToArray();
        ValidateRows(selected);
        return selected;
    }

    public static bool Unchanged(ChangeRow row) =>
        row.OriginalPolicy.Zip(row.TargetPolicy, (a, b) => Math.Abs(a - b)).Max() <= PolicyTolerance;

    /// <summary>
    /// An unchanged endpoint policy is strictly suboptimal after the intervention
    /// alone, but restored by the all-target-opponent best response. Uses cached
    /// unrestricted responses, never a fresh equilibrium or an imposed causal path.
    /// </summary>
    public static ChangeRow[] OffsettingEffects(ContrastResult result)
    {
        bool Qualifies(ChangeRow row)
        {
            if (!Unchanged(row) || row.CounterfactualUndefined ||
                Math.Abs(row.Allocation.Direct) <= PolicyTolerance ||
                Math.Abs(row.Allocation.SelectionResidual) > PolicyTolerance) return false;
            var direct = result.Scenarios.Single(s => s.Panel == "coalition" && s.Component == "0" && s.Result.Player == row.Player)
                .Result.InformationSets.Single(s => s.Key == row.Key);
            var all = result.Scenarios.Single(s => s.Panel == "coalition" && s.Component == "7" && s.Result.Player == row.Player)
                .Result.InformationSets.Single(s => s.Key == row.Key);
            if (direct.CounterfactuallyUnreachable || direct.Actions.Any(a =>
                !a.CounterfactualConditionalUtility.HasValue || !double.IsFinite(a.CounterfactualConditionalUtility.Value)) ||
                all.Actions.Zip(row.TargetPolicy, (a, p) => Math.Abs(a.Probability - p)).Max() > PolicyTolerance) return false;
            double best = direct.Actions.Max(a => a.CounterfactualConditionalUtility.Value);
            double inferiorMass = direct.Actions.Select((a, i) =>
                best - a.CounterfactualConditionalUtility.Value > result.Tolerances.NearTie ? row.OriginalPolicy[i] : 0).Sum();
            return inferiorMass > PolicyTolerance;
        }
        return BuildRows(result.SourceEquilibrium, result.TargetEquilibrium, result.Scenarios,
            result.OfferValues, includeUnchanged: true).Rows.Where(Qualifies).ToArray();
    }

    public static void ValidateRows(ChangeRow[] rows)
    {
        if (rows.Length == 0 || rows.Select(r => r.Key).Distinct().Count() != rows.Length ||
            rows.Any(r => r.CounterfactualUndefined || Math.Abs(r.Allocation.SelectionResidual) > PolicyTolerance ||
                r.Metric is not ("decision probability" or "offer amount")))
            throw new InvalidDataException("The compact layout requires defined, zero-remainder decisions/pure offer amounts. Add explicit columns for other cases; do not hide them.");
        foreach (var r in rows)
            InformationSetPressureAnalysis.Near(r.Allocation.Change, r.Allocation.Explained, PolicyTolerance, "Publication accounting");
    }

    public static Group[] GroupRows(ChangeRow[] rows)
    {
        var groups = new List<Group>();
        foreach (var row in rows)
        {
            if (groups.Count > 0 && CanCombine(groups[^1].Last, row))
                groups[^1] = new(groups[^1].Rows.Append(row).ToArray());
            else groups.Add(new(new[] { row }));
        }
        return groups.ToArray();
    }

    private static bool CanCombine(ChangeRow a, ChangeRow b)
    {
        bool Same(double[] x, double[] y) => x.Length == y.Length && x.Zip(y, (u, v) => Math.Abs(u - v)).All(d => d <= PolicyTolerance);
        double[] Values(ChangeRow r) => new[] { r.Allocation.Original, r.Allocation.Target, r.Allocation.Direct,
            r.Allocation.Entry, r.Allocation.Offers, r.Allocation.Exit, r.Allocation.SelectionResidual };
        return a.Player == b.Player && a.Decision == b.Decision && a.Signal + 1 == b.Signal &&
            a.ExitCommitment == b.ExitCommitment && a.Metric == b.Metric && a.Action == b.Action &&
            Same(a.OriginalPolicy, b.OriginalPolicy) && Same(a.TargetPolicy, b.TargetPolicy) && Same(Values(a), Values(b));
        // Unlike the audit packet, combine equal numerical rows with different flags.
        // The displayed group explicitly distinguishes sensitivity at all/some/no signals.
    }

    public static string SensitivityLabel(ChangeRow[] rows)
    {
        int count = rows.Count(r => r.TieSensitive || r.CompletionSensitive);
        return count == 0 ? "No" : count == rows.Length ? "Yes" : "At some signals";
    }

    public static string FileName(ContrastResult r)
    {
        var h = EquilibriumChangeTables.Heading(r.SourceOptionSet, r.TargetOptionSet);
        if (h.Cost != "1") throw new InvalidDataException("Only ordinary costs belong in this packet.");
        bool fees = r.Contrast.Id.StartsWith("fee-shifting-");
        return fees ? "American to British - " + (r.SourceOptionSet.Contains("ModerateRiskAversion") ? "moderate risk aversion" : "risk neutral")
            : "Risk neutral to risk averse - " + (r.SourceOptionSet.EndsWith("American") ? "American rule" : "British rule");
    }

    public static string Latex(ContrastResult result, ChangeRow[] rows)
    {
        ValidateRows(rows);
        var h = EquilibriumChangeTables.Heading(result.SourceOptionSet, result.TargetOptionSet);
        var b = new StringBuilder("""
            \documentclass[10pt,border=8pt,varwidth=7.5in]{standalone}
            \usepackage[T1]{fontenc}
            \usepackage{lmodern,booktabs,array,tabularx,microtype}
            \setlength{\parindent}{0pt}
            \setlength{\tabcolsep}{3.5pt}
            \renewcommand{\arraystretch}{1.22}
            \begin{document}
            \begin{minipage}{7.5in}
            """);
        b.AppendLine("\n{\\large\\bfseries " + EquilibriumChangeTables.Escape(h.Title) + @"}\par");
        b.AppendLine(EquilibriumChangeTables.Escape(h.HeldFixed) + @"; ordinary costs\par\medskip");
        b.AppendLine(@"{\fontsize{9.5}{11.5}\selectfont\begin{tabularx}{\linewidth}{@{}>{\raggedright\arraybackslash}p{1.38in}>{\centering\arraybackslash}p{.54in}>{\centering\arraybackslash}p{1.07in}*{4}{>{\raggedleft\arraybackslash}p{.61in}}>{\centering\arraybackslash}X@{}}");
        b.AppendLine(@"\toprule & & & \multicolumn{4}{c}{Contributions to change} & \\");
        b.AppendLine(@"\cmidrule(lr){4-7} Decision & Own signal & Original $\to$ Target & Direct & \shortstack{Opponent\\entry} & \shortstack{Opponent\\offers} & \shortstack{Opponent\\exit} & \shortstack{Tie/off-path\\sensitivity} \\\midrule");
        bool hasOffsets = rows.Any(Unchanged);
        foreach (var section in rows.GroupBy(Unchanged).OrderBy(g => g.Key))
        {
            if (hasOffsets)
                b.AppendLine((section.Key ? @"\midrule " : "") + @"\multicolumn{8}{l}{\textit{" +
                    (section.Key ? "Unchanged actions with offsetting effects" : "Changed actions") + @"}}\\[2pt]");
            foreach (var group in GroupRows(section.ToArray()))
            {
                var row = group.First; var a = row.Allocation;
                bool amount = row.Metric == "offer amount";
                string Num(double v, bool signed)
                {
                    if (Math.Abs(v) < (amount ? .0005 : .05)) v = 0;
                    return (signed && v > 0 ? "+" : "") + v.ToString(amount ? "0.00#" : "0.#", CultureInfo.InvariantCulture);
                }
                string endpoint = "$" + Num(a.Original, false) + (amount ? "" : @"\%") + @"\to " + Num(a.Target, false) + (amount ? "" : @"\%") + "$";
                string label = row.Decision switch
                {
                    "P Files" => "P files",
                    "D Answers" => "D answers",
                    "P Abandons" => "P commits to abandon",
                    "D Defaults" => "D commits to default",
                    _ => (row.Player == 0 ? "P demand" : "D offer") + (row.ExitCommitment == 2 ? ", continue" : ", exit")
                };
                string signal = row.SignalValue.ToString("0.00", CultureInfo.InvariantCulture) +
                    (group.Rows.Length > 1 ? "--" + group.Last.SignalValue.ToString("0.00", CultureInfo.InvariantCulture) : "");
                if (group.Rows.Any(r => r.UnreachedCoalitions.Length > 0)) label += "$^{*}$";
                b.Append(label + " & " + signal + " & " + endpoint);
                foreach (double effect in new[] { a.Direct, a.Entry, a.Offers, a.Exit })
                    b.Append(" & $" + Num(effect, true) + (!amount && Math.Abs(effect) >= .05 ? @"\,\mathrm{pp}" : "") + "$");
                b.AppendLine(" & " + group.Sensitivity + @"\\");
            }
        }
        return b.AppendLine(@"\bottomrule\end{tabularx}}\end{minipage}\end{document}").ToString();
    }
}