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
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;

namespace LitigCharts;

/// <summary>Request-driven diagnostic and publication tables; no production switches.</summary>
public static class InformationSetPressureTables
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public sealed record ReportHeading(string Title, string HeldFixed, string Cost,
        string Original, string Target, string OriginalColumn, string TargetColumn);

    // Derive direction from the selected game definitions, never from an ambiguous
    // display label or filename. This also works if a future request reverses direction.
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
                Console.WriteLine("pressure --request <json> [--calculate-only | --render-only]");
                return 0;
            }
            string requestFile = null; bool calculateOnly = false, renderOnly = false;
            for (int i = 0; i < args.Length; i++)
                switch (args[i])
                {
                    case "--request": requestFile = Path.GetFullPath(args[++i]); break;
                    case "--calculate-only": calculateOnly = true; break;
                    case "--render-only": renderOnly = true; break;
                    default: throw new ArgumentException("Unknown pressure argument: " + args[i]);
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
        string output = Path.GetFullPath(request.OutputDirectory, Path.GetDirectoryName(Path.GetFullPath(requestFile)));
        string manifestFile = Path.Combine(output, "pressure-analysis-manifest.json");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestFile), JsonOptions)
            ?? throw new InvalidDataException("Missing completed diagnostic manifest.");
        if (manifest.Request.Sha256 != Hash(requestFile).Sha256)
            throw new InvalidDataException("Request changed since the calculation. Recalculate before rendering.");
        foreach (var file in manifest.OutputFingerprints)
            if (Hash(file.Path).Sha256 != file.Sha256)
                throw new InvalidDataException("Calculation output differs from the completed manifest: " + file.Path);
        var summaries = new List<string>();
        summaries.Add(Csv("Contrast", "Panel", "Component", "Player", "ReferenceUtility", "ResponseUtility", "BestResponseUtility",
            "Gain", "MaxActionValueError", "TerminalProbability", "NewlyReachedOpponentSets", "ExposedOpponentSets", "NearTieInformationSets", "ResponseName"));
        var index = new StringBuilder("# Information-set pressure analysis\n\n");
        index.AppendLine("Each PDF has four pages: first-response dynamics (P, D), then actual-equilibrium decomposition (P, D). Headings state the direction of change, what is held fixed, and the original and target equilibria. The two endpoint columns show levels; the five middle columns emphasize changes from the original equilibrium. They are full best responses, not additional equilibria.\n");
        index.AppendLine("| Intervention | Tables | Exact data |\n|---|---|---|");
        foreach (var selection in request.Contrasts)
        {
            string json = Path.Combine(output, selection.Id + ".json");
            var data = JsonSerializer.Deserialize<ContrastResult>(File.ReadAllText(json), JsonOptions)
                ?? throw new InvalidDataException("Missing contrast data.");
            var heading = Heading(data.SourceOptionSet, data.TargetOptionSet);
            string stem = Path.Combine(output, selection.Id);
            await File.WriteAllTextAsync(stem + ".tex", RenderLatex(data));
            await File.WriteAllTextAsync(stem + ".txt", $"{heading.Title}\nOriginal equilibrium: {heading.Original}\nTarget equilibrium: {heading.Target}\n{heading.HeldFixed}; costs unchanged at multiplier {heading.Cost}.\n\n"
                + PresentationNotes + "\n\n" + string.Join("\n", data.Interpretation) + "\n");
            await File.WriteAllTextAsync(stem + "-actions.csv", ActionCsv(data));
            await File.WriteAllTextAsync(stem + "-beliefs.csv", BeliefsCsv(data));
            await File.WriteAllTextAsync(stem + "-sensitivity.csv", SensitivityCsv(data));
            foreach (var s in data.Scenarios)
                summaries.Add(Csv(selection.Id, s.Panel, s.Component, s.Result.Player == 0 ? "P" : "D",
                    s.Result.ReferenceUtility, s.Result.ResponseUtility, s.Result.BestResponseUtility, s.Result.Gain,
                    s.Result.MaxActionValueError, s.Result.TerminalProbability, s.Result.NewlyReachedOpponentSets.Length, s.Result.ExposedOpponentSets.Length,
                    s.Result.InformationSets.Count(i => !i.ActualOffPath && i.Actions.Count(a => a.NearBest == true) > 1), s.Result.Name));
            // DiagramCompiler isolates LaTeX scratch files. Its PNG is page one; render
            // every page separately below so validation covers the entire table packet.
            await DiagramCompiler.CompileAsync(stem + ".tex", new ArticleDiagramCommand.Configuration());
            await RenderPagePreviews(stem, output);
            index.AppendLine($"| {heading.Title}; {heading.HeldFixed.Replace("Both players", "both players")}; cost {heading.Cost} | [{selection.Id}.pdf]({selection.Id}.pdf) | [Actions]({selection.Id}-actions.csv), [beliefs]({selection.Id}-beliefs.csv), [sensitivity]({selection.Id}-sensitivity.csv), [JSON]({selection.Id}.json) |");
            Console.WriteLine("Rendered " + heading.Title + " (cost " + heading.Cost + ")");
        }
        await File.WriteAllTextAsync(Path.Combine(output, "pressure-analysis-summary.csv"), string.Join("\n", summaries) + "\n");
        index.AppendLine("\n## How to read the tables\n");
        index.AppendLine(PresentationNotes + "\n");
        index.AppendLine("- Direct changes the game but holds the opponent's original complete strategy fixed. Opponent entry/offers/exit replace only that component; All replaces all three. They are not cumulative. Panel A uses the opponent's simultaneous first-round best response; Panel B uses its actual target-equilibrium strategy.\n- A dagger marks a reached response information set with multiple actions within the reported near-tie tolerance; exact utility gaps are in the action CSV. A selected pure response does not imply uniqueness.\n- Low/high completion checks are illustrative changes to donor-unvisited policies, not exhaustive bounds or equilibria.\n- The action CSV distinguishes actual conditional utilities from counterfactual values conditional on the responding player reaching that history. The latter can exist when actual reach is zero; neither values nor beliefs are invented at zero opponent-and-chance reach.\n- All utility gains compare policies within the same utility function and hybrid opponent profile. Do not compare raw utility levels across risk preferences.\n- These are selected-profile mechanism diagnostics, not additive causal shares, an observed adjustment path, or a proof of convergence.\n");
        index.AppendLine("A star on a PDF column flags exposure to donor-unvisited opponent policies, including policies reachable through a focal deviation but not played by the selected response. It indicates a completion-sensitivity check, not necessarily a material change. The JSON records both these potential exposures and actually newly reached sets.\n");
        index.AppendLine("## Reproduction\n\nRun the C# `LitigCharts pressure --request <information-set-pressure.request.json>` command. `--calculate-only` skips table assembly; `--render-only` reuses the completed calculation. The manifest fingerprints the request and both production input files for every source. No production solve or saved-equilibrium write occurs.\n");
        index.AppendLine("The summary CSV contains root payoffs and verification errors for every response. Individual JSON files contain complete behavioral response policies, all action values, posterior opponent-signal distributions, and donor-completion warnings. `pressure-analysis-manifest.json` contains source validation and original/target exploitability controls.");
        await File.WriteAllTextAsync(Path.Combine(output, "README.md"), index.ToString());
    }

    private static async Task RenderPagePreviews(string stem, string output)
    {
        var temp = Directory.CreateTempSubdirectory("acesim-pressure-preview-");
        try
        {
            string prefix = Path.GetFileName(stem) + "-page-";
            await DiagramCompiler.RunProcessAsync("pdftoppm", output, 180, "-png", "-r", "120",
                stem + ".pdf", Path.Combine(temp.FullName, Path.GetFileName(stem) + "-page"));
            var fresh = Directory.GetFiles(temp.FullName, "*.png");
            if (fresh.Length == 0) throw new IOException("No pressure-table page previews produced.");
            var names = fresh.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string file in fresh) File.Copy(file, Path.Combine(output, Path.GetFileName(file)), overwrite: true);
            // Only obsolete, numbered page previews belonging to this exact report.
            // A shorter layout must not leave old page 5 etc. beside a four-page PDF.
            foreach (string file in Directory.GetFiles(output, prefix + "*.png"))
            {
                string name = Path.GetFileName(file);
                if (!names.Contains(name) && int.TryParse(Path.GetFileNameWithoutExtension(name)[prefix.Length..], out _))
                    File.Delete(file);
            }
        }
        finally
        {
            if (temp.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && !temp.Attributes.HasFlag(FileAttributes.ReparsePoint)) temp.Delete(recursive: true);
        }
    }

    public static string RenderLatex(ContrastResult data)
    {
        var heading = Heading(data.SourceOptionSet, data.TargetOptionSet);
        double[] offerValues = PublicationFigures.GridForOptionSet(data.TargetOptionSet).Offers;
        var b = new StringBuilder("""
            \documentclass[10pt]{article}
            \usepackage[a4paper,margin=12mm]{geometry}
            \usepackage[T1]{fontenc}
            \usepackage{lmodern,booktabs,tabularx,array}
            \usepackage{microtype}
            \setlength{\parindent}{0pt}
            \setlength{\tabcolsep}{2pt}
            \newcolumntype{Y}{>{\centering\arraybackslash}X}
            \begin{document}
            """);
        bool first = true;
        foreach (string panel in new[] { "dynamics", "equilibrium" })
            foreach (byte player in new byte[] { 0, 1 })
            {
                if (!first) b.AppendLine(@"\newpage"); first = false;
                b.AppendLine(@"{\large\bfseries " + Escape(heading.Title) + @"}\par\smallskip");
                b.AppendLine(@"{\small " + Escape(heading.HeldFixed) + "; costs unchanged at multiplier " + Escape(heading.Cost) + @".}\par\smallskip");
                b.AppendLine(@"{\bfseries " + (panel == "dynamics" ? "A. Two-step best-response dynamics" : "B. Actual-equilibrium decomposition")
                    + " --- " + (player == 0 ? "Plaintiff" : "Defendant") + @"}\par\smallskip");
                b.AppendLine(@"{\small\bfseries Middle columns: changes from original equilibrium. End columns: levels.}\par");
                var columns = Columns(data, panel, player);
                var responseColumns = new[] { data.Scenarios.Single(s => s.Panel == "direct" && s.Result.Player == player) }
                    .Concat(new[] { "participation", "offers", "exit", "all" }.Select(c =>
                        data.Scenarios.Single(s => s.Panel == panel && s.Component == c && s.Result.Player == player))).ToArray();
                var completionFlags = new[] { false }.Concat(responseColumns.Select(s => s.Result.ExposedOpponentSets.Length > 0)).Append(false).ToArray();
                AddTable(b, player == 0 ? "Filing probability" : "Answering probability (if sued)",
                    player == 0 ? "P Files" : "D Answers", columns, false, completionFlags, heading, offerValues);
                AddTable(b, player == 0 ? "Abandonment commitment (if bargaining fails)" : "Default commitment (if bargaining fails)",
                    player == 0 ? "P Abandons" : "D Defaults", columns, false, completionFlags, heading, offerValues);
                AddTable(b, player == 0 ? "Demand by signal and prior exit commitment" : "Offer by signal and prior exit commitment",
                    player == 0 ? "P Offer" : "D Offer", columns, true, completionFlags, heading, offerValues);
                b.AppendLine(@"\medskip{\footnotesize Direct: target game with opponent unchanged. Entry/offers/exit: replace only that opponent component; All: replace all three. "
                    + (panel == "dynamics" ? "Replacements come from the opponent's first best response." : "Replacements come from the opponent's target equilibrium.")
                    + @" Middle columns are best responses, not equilibria.}\par");
                b.AppendLine(@"\smallskip{\footnotesize ---: unchanged; blank: unreached history; new: level at a newly reached history; level: actual offer(s) where mixing prevents a single offer delta. "
                    + @"Parentheses give mixing probabilities. Abandon/default and continue are prior private commitments, applied only if settlement fails; both branches still make an offer. "
                    + @"$\dagger$: alternative action within " + Scientific(data.Tolerances.NearTie) + @" utility units. "
                    + @"$^*$: column exposes donor-unvisited policies; see completion sensitivity.}\par");
            }
        return b.AppendLine(@"\end{document}").ToString();
    }

    private static InformationSet[][] Columns(ContrastResult data, string panel, byte player) =>
        new[] { data.SourceEquilibrium.InformationSets.Where(x => x.Player == player).ToArray(),
            data.Scenarios.Single(s => s.Panel == "direct" && s.Result.Player == player).Result.InformationSets }
        .Concat(new[] { "participation", "offers", "exit", "all" }.Select(c =>
            data.Scenarios.Single(s => s.Panel == panel && s.Component == c && s.Result.Player == player).Result.InformationSets))
        .Append(data.TargetEquilibrium.InformationSets.Where(x => x.Player == player).ToArray()).ToArray();

    private static void AddTable(StringBuilder b, string title, string decision, InformationSet[][] columns,
        bool offers, bool[] completionFlags, ReportHeading heading, double[] offerValues)
    {
        b.AppendLine(@"\smallskip{\bfseries " + Escape(title) + @"}\quad{\footnotesize "
            + (offers ? "Levels and changes: fraction of damages" : @"Levels: \%; changes: percentage points") + @"}\par\smallskip");
        b.AppendLine(@"{\fontsize{9}{10.4}\selectfont\renewcommand{\arraystretch}{1.12}");
        b.AppendLine(@"\begin{tabularx}{\textwidth}{@{}" + (offers ? "rc" : "r") + @"YYYYYYY@{}}\toprule");
        b.AppendLine("Signal & " + (offers ? @"\shortstack{If no\\agreement} & " : "") + string.Join(" & ",
            new[] { @"\shortstack{Original\\" + heading.OriginalColumn + "}", @"\shortstack{Direct\\$\Delta$}",
                @"\shortstack{Opponent\\entry $\Delta$}", @"\shortstack{Opponent\\offers $\Delta$}",
                @"\shortstack{Opponent\\exit $\Delta$}", @"\shortstack{Opponent\\all $\Delta$}",
                @"\shortstack{Target\\" + heading.TargetColumn + "}" }
            .Select((label, i) => label + (completionFlags[i] ? @"$^*$" : ""))) + @" \\\midrule");
        var rows = columns.SelectMany(c => c).Where(i => i.Decision == decision).GroupBy(i => i.Key).Select(g => g.First())
            .OrderBy(i => i.Signal).ThenBy(i => i.ExitCommitment).ToArray();
        foreach (var info in rows)
        {
            // Keep both exit histories if either is reached in any comparison.
            var cells = columns.Select(c => c.Single(i => i.Key == info.Key)).ToArray();
            if (offers && cells.All(i => i.ActualOffPath)) continue;
            b.Append(info.SignalValue.ToString("0.00", Invariant));
            if (offers) b.Append(" & " + ExitLabel(info));
            for (int c = 0; c < cells.Length; c++) b.Append(" & " + (c is > 0 and < 6
                ? DeltaCell(cells[c], cells[0], offers, offerValues) : Cell(cells[c], offers, false)));
            b.AppendLine(@" \\");
        }
        b.AppendLine(@"\bottomrule\end{tabularx}}\par");
    }

    public static string ExitLabel(InformationSet info) => info.ExitCommitment switch
    {
        1 => info.Player == 0 ? "Abandon" : "Default",
        2 => "Continue",
        _ => throw new InvalidDataException("Offer history has no recognized prior exit commitment.")
    };

    public static string DeltaCell(InformationSet info, InformationSet original, bool offers, double[] offerValues = null)
    {
        if (info.Key != original.Key || !info.Actions.Select(a => a.Action).SequenceEqual(original.Actions.Select(a => a.Action)))
            throw new InvalidDataException("Cannot compare different information sets or action menus.");
        if (info.ActualOffPath) return "";
        if (original.ActualOffPath) return @"\textit{new}~" + Cell(info, offers, false) + TieMark(info);
        if (info.Actions.Zip(original.Actions, (a, o) => Math.Abs(a.Probability - o.Probability)).Max() < 1e-9)
            return "---" + TieMark(info);
        if (!offers)
            return SignedDelta(100 * (info.Actions.Single(a => a.Label == "Yes").Probability
                - original.Actions.Single(a => a.Label == "Yes").Probability), "0.#") + TieMark(info);
        var from = original.Actions.Where(a => a.Probability > 1e-12).ToArray();
        var to = info.Actions.Where(a => a.Probability > 1e-12).ToArray();
        // A mixed policy is not an average action: retain actual support instead of
        // manufacturing a single demand or hiding a redistribution of probabilities.
        if (from.Length != 1 || to.Length != 1)
            return @"\textit{level}~" + Cell(info, true, false) + TieMark(info);
        if (offerValues == null || offerValues.Length != info.Actions.Length)
            throw new InvalidDataException("Exact production offer grid is required for offer deltas.");
        return SignedDelta(offerValues[to[0].Action - 1] - offerValues[from[0].Action - 1], "0.00") + TieMark(info);
    }

    private static string TieMark(InformationSet info) => info.Actions.Count(a => a.NearBest == true) > 1 ? @"$\dagger$" : "";
    private static string SignedDelta(double value, string format)
    {
        string magnitude = Math.Abs(value).ToString(format, Invariant);
        if (double.Parse(magnitude, Invariant) == 0 && value != 0)
            magnitude = Math.Abs(value).ToString("0.###E+0", Invariant);
        return (value > 0 ? "+" : value < 0 ? "-" : "") + magnitude;
    }

    public const string PresentationNotes =
        "Original and Target show equilibrium levels, including actual mixing. The five middle columns show changes from Original, never from the preceding column. Probability changes are percentage points; pure-offer changes are fractions of damages. A dash means unchanged; a blank means that history is unreached under that column's profile. 'new' reports a level where the original history was unreached; 'level' reports the actual offer policy where mixing prevents a single offer delta. Mixed offers are never replaced by an average.\n\n"
        + "The offer-history labels name the player's own prior private commitment if no agreement is reached: Abandon for P, Default for D, or Continue to trial. Both branches still make an offer; quitting is implemented only if bargaining fails. The same signal may therefore have separate rows for different own commitments. Do not pool those histories.\n\n"
        + "The exact action/reach/belief CSVs and calculation JSON retain levels and full policies; this is a presentation-only change, with no new best-response calculation.";

    public static string Cell(InformationSet info, bool offers, bool markNearTie)
    {
        if (info.ActualOffPath) return "";
        string suffix = markNearTie && info.Actions.Count(a => a.NearBest == true) > 1 ? @"$\dagger$" : "";
        if (!offers) return (100 * info.Actions.Single(a => a.Label == "Yes").Probability).ToString("0.#", Invariant) + @"\%" + suffix;
        var active = info.Actions.Where(a => a.Probability > 1e-12).ToArray();
        if (active.Length == 1) return Escape(active[0].Label) + suffix;
        return @"\shortstack{" + string.Join(@"\\", active.Select(a => Escape(a.Label) + " (" + (100 * a.Probability).ToString("0.#", Invariant) + @"\%)")) + "}" + suffix;
    }

    public static string ActionCsv(ContrastResult data)
    {
        var lines = new List<string> { Csv("Contrast", "Panel", "Component", "Player", "Key", "Decision", "Signal", "ExitCommitment",
            "ActualReach", "CounterfactualReach", "ActualOffPath", "CounterfactuallyUnreachable", "Action", "Label", "Probability",
            "ActualConditionalUtility", "CounterfactualConditionalActionUtility", "LossFromBest", "BestWithinTieTolerance", "NearBest", "ReferenceActionLoss", "ActionGap") };
        foreach (var (panel, component, player, infos) in AllInformation(data))
            foreach (var i in infos)
                foreach (var a in i.Actions)
                    lines.Add(Csv(data.Contrast.Id, panel, component, player, i.Key, i.Decision, i.SignalValue, i.ExitCommitment,
                        i.ActualReach, i.CounterfactualReach, i.ActualOffPath, i.CounterfactuallyUnreachable, a.Action, a.Label, a.Probability,
                        i.ConditionalUtility, a.CounterfactualConditionalUtility, a.Loss, a.Best, a.NearBest, i.ReferenceActionLoss, i.ActionGap));
        return string.Join("\n", lines) + "\n";
    }

    private static IEnumerable<(string Panel, string Component, byte Player, InformationSet[] Infos)> AllInformation(ContrastResult data)
    {
        foreach (byte p in new byte[] { 0, 1 })
        {
            yield return ("original-equilibrium", "reference", p, data.SourceEquilibrium.InformationSets.Where(i => i.Player == p).ToArray());
            yield return ("target-equilibrium", "reference", p, data.TargetEquilibrium.InformationSets.Where(i => i.Player == p).ToArray());
        }
        foreach (var s in data.Scenarios) yield return (s.Panel, s.Component, s.Result.Player, s.Result.InformationSets);
    }

    private static string BeliefsCsv(ContrastResult data)
    {
        var b = new StringBuilder(Csv("Contrast", "Panel", "Component", "Player", "Key", "Decision", "OwnSignal", "ExitCommitment",
            "ActualReach", "CounterfactualReach", "OpponentSignal", "ConditionalProbability") + "\n");
        foreach (var (panel, component, player, infos) in AllInformation(data))
            foreach (var i in infos.Where(i => i.OpponentSignalBeliefs != null))
                for (int s = 0; s < i.OpponentSignalBeliefs.Length; s++)
                    b.AppendLine(Csv(data.Contrast.Id, panel, component, player, i.Key, i.Decision, i.SignalValue, i.ExitCommitment,
                        i.ActualReach, i.CounterfactualReach, (s + .5) / i.OpponentSignalBeliefs.Length, i.OpponentSignalBeliefs[s]));
        return b.ToString();
    }

    private static string SensitivityCsv(ContrastResult data)
    {
        var b = new StringBuilder(Csv("Contrast", "Check", "Component", "Player", "RootUtilityChange", "ChangedResponseInformationSets",
            "ChangedReachedInformationSets", "MaxActionProbabilityChange", "BaselineResponse", "AlternativeResponse") + "\n");
        foreach (var s in data.Scenarios.Where(s => s.Panel.Contains("-tie-check") || s.Panel.Contains("-completion-")))
        {
            string panel = s.Panel.StartsWith("first-response") ? "direct" : s.Panel.Split("-tie-check")[0].Split("-completion-")[0];
            var original = data.Scenarios.Single(x => x.Panel == panel && x.Component == s.Component && x.Result.Player == s.Result.Player);
            var differences = original.Result.InformationSets.Select(i =>
            {
                var other = s.Result.InformationSets.Single(j => j.Key == i.Key);
                double delta = i.Actions.Zip(other.Actions, (a, c) => Math.Abs(a.Probability - c.Probability)).Max();
                return (Delta: delta, Reached: !i.ActualOffPath || !other.ActualOffPath);
            }).ToArray();
            b.AppendLine(Csv(data.Contrast.Id, s.Panel, s.Component, s.Result.Player,
                s.Result.ResponseUtility - original.Result.ResponseUtility, differences.Count(d => d.Delta > 1e-9),
                differences.Count(d => d.Delta > 1e-9 && d.Reached), differences.Max(d => d.Delta), original.Result.Name, s.Result.Name));
        }
        return b.ToString();
    }

    private static string Scientific(double x) => "$" + x.ToString("0.#E+0", Invariant).Replace("E", @"\times10^{") + "}$";
    public static string Escape(string s) => s.Replace("\\", @"\textbackslash{}").Replace("&", @"\&").Replace("%", @"\%").Replace("_", @"\_").Replace("#", @"\#");
    private static string Csv(params object[] fields) => string.Join(",", fields.Select(v => "\"" + (v switch
    {
        null => "", double d => d.ToString("G17", Invariant), bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, Invariant), _ => v.ToString()
    }).Replace("\"", "\"\"") + "\""));
}
