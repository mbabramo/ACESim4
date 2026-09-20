using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.EquilibriumChangeDecomposition;
using static LitigCharts.EquilibriumManuscriptTable;

namespace LitigCharts;

/// <summary>Compact risk-neutral and risk-averse selections; the complete packet remains online.</summary>
public static class SelectedEquilibriumManuscriptTable
{
    public const string Stem = "selected-strategy-mechanisms";
    public const string RiskAverseStem = "selected-risk-averse-strategy-mechanisms";
    public const string RiskAverseCaption = "Selected equilibrium strategy changes involving risk aversion at ordinary costs. " +
        "Panels A-C change both parties from risk neutrality to CARA risk aversion with coefficient 2, holding the fee rule fixed; " +
        "panel D changes Trial to Complete Fee-Shifting under risk aversion. " +
        "Direct changes preferences or the fee rule while holding the opponent's strategy fixed. " +
        "Opponent-entry, offer and exit contributions average over orders of replacing opponent strategy components. " +
        "Decision probabilities are percentages and their contributions percentage points; demand and offer amounts and contributions are fractions of damages. " +
        "Displayed histories are reached in both equilibria. The exit decision is a commitment to withdraw after bargaining failure; demands and offers condition on commitment to continue. " +
        "Remaining contributions are zero. The full analysis and diagnostic checks are in the online repository.";
    public const string Caption = "Selected risk-neutral equilibrium strategy changes at ordinary costs. " +
        "Direct changes the fee rule while holding the opponent's strategy fixed; " +
        "opponent-entry and opponent-offer contributions average over the orders of replacing opponent strategy components. " +
        "Filing and answering contributions are percentage points; the demand and its contributions are fractions of damages. " +
        "Opponent-exit and remaining contributions are zero in these rows. " +
        "These are illustrative accounts of selected equilibria, not unique causal effects. " +
        "The full analysis, methodology and diagnostic checks are in the online repository.";

    public static Panel[] Select(Panel[] full)
    {
        ChangeRow[] Pick(string comparison, string decision, params int[] signals)
        {
            var panel = full.Single(p => p.Comparison.Id == comparison);
            // Signal indices (one-based) identify the saved coordinates without comparing rounded labels.
            return signals.Select(signal => panel.Rows.Single(r => r.Decision == decision &&
                r.Signal == signal && r.Action == null && (decision != "P Offer" || r.ExitCommitment == 2))).ToArray();
        }
        const string trial = "american-to-trial-risk-neutral";
        const string complete = "trial-to-complete-risk-neutral";
        Panel Make(string id, ChangeRow[] rows) => new(full.Single(p => p.Comparison.Id == id).Comparison, rows);
        return new[] {
            Make(trial, Pick(trial, "P Offer", 4)),
            Make(complete, Pick(complete, "D Answers", 7, 8, 9, 10).Concat(Pick(complete, "P Files", 3, 4)).ToArray())
        };
    }

    public static Panel[] SelectRiskAverse(Panel[] full)
    {
        Panel Pick(string id, params (string Decision, int[] Signals)[] selections)
        {
            var panel = full.Single(p => p.Comparison.Id == id);
            var rows = selections.SelectMany(s => s.Signals.Select(signal => panel.Rows.Single(r =>
                r.Decision == s.Decision && r.Signal == signal && r.Action == null &&
                (s.Decision is not ("P Offer" or "D Offer") || r.ExitCommitment == 2)))).ToArray();
            return new(panel.Comparison, rows);
        }
        return new[] {
            Pick("risk-neutral-to-risk-averse-american", ("P Files", new[] { 1, 2, 4 }),
                ("P Offer", new[] { 4 }), ("D Offer", new[] { 1, 2, 7 })),
            Pick("risk-neutral-to-risk-averse-trial", ("P Abandons", new[] { 5 })),
            Pick("risk-neutral-to-risk-averse-complete", ("P Offer", new[] { 5, 6 })),
            Pick("trial-to-complete-risk-averse", ("D Answers", new[] { 8, 9, 10 }))
        };
    }

    public static string Latex(Panel[] panels, bool includeExit = false, bool requireIntermediateReach = true)
    {
        foreach (var panel in panels)
        {
            EquilibriumPublicationTables.ValidateRows(panel.Rows);
            // The abbreviated presentation must not silently hide a newly nonzero or undefined contribution.
            if (panel.Rows.Any(r => r.CounterfactualUndefined || (requireIntermediateReach && r.UnreachedCoalitions.Length != 0) ||
                (!includeExit && Math.Abs(r.Allocation.Exit) > PolicyTolerance) || Math.Abs(r.Allocation.SelectionResidual) > PolicyTolerance))
                throw new InvalidDataException("Revisit the short table: omitted columns or reach qualifications are needed.");
        }
        if (panels.Sum(p => EquilibriumPublicationTables.GroupRows(p.Rows).Length) != (includeExit ? 9 : 3))
            throw new InvalidDataException("Unexpected selected row count; revisit changed grouping.");
        var b = new StringBuilder("""
            \documentclass[10pt,border=5pt,varwidth=6.4in]{standalone}
            \usepackage[T1]{fontenc}
            \usepackage{lmodern,booktabs,array,tabularx,microtype}
            \setlength{\tabcolsep}{4pt}
            \renewcommand{\arraystretch}{1.18}
            \begin{document}
            \begin{minipage}{6.4in}
            \begin{tabularx}{\linewidth}{@{}>{\raggedright\arraybackslash}p{1.35in}>{\centering\arraybackslash}p{.75in}>{\centering\arraybackslash}p{1.12in}*{3}{>{\centering\arraybackslash}X}@{}}
            \toprule
            Decision & Signal & Original $\to$ Target & Direct & \shortstack{Opponent\\entry} & \shortstack{Opponent\\offers} \\
            """);
        if (includeExit)
            b.Replace("p{1.35in}", "p{1.18in}").Replace("p{.75in}", "p{.70in}")
                .Replace("p{1.12in}", "p{1.05in}").Replace("*{3}", "*{4}")
                .Replace(@"\shortstack{Opponent\\offers} \\", @"\shortstack{Opponent\\offers} & \shortstack{Opponent\\exit} \\");
        for (int i = 0; i < panels.Length; i++)
        {
            b.AppendLine(@"\midrule");
            b.AppendLine(@"\multicolumn{" + (includeExit ? 7 : 6) + @"}{@{}l}{\textit{" + (char)('A' + i) + ". " +
                EquilibriumChangeTables.Escape(panels[i].Comparison.Label) + @"}}\\[3pt]");
            foreach (var group in EquilibriumPublicationTables.GroupRows(panels[i].Rows))
            {
                var row = group.First;
                bool amount = row.Metric == "offer amount";
                string Num(double value, bool signed)
                {
                    if (Math.Abs(value) < (amount ? .0005 : .05)) value = 0;
                    return (signed && value > 0 ? "+" : "") +
                        value.ToString(amount ? "0.00#" : "0.#", CultureInfo.InvariantCulture);
                }
                string label = row.Decision switch {
                    "P Files" => "P files", "D Answers" => "D answers",
                    "P Abandons" => "P commits to exit",
                    "P Offer" => "P demand, continue", "D Offer" => "D offer, continue",
                    _ => throw new InvalidDataException("Unexpected selected decision.") };
                string signal = row.SignalValue.ToString("0.00", CultureInfo.InvariantCulture) +
                    (group.Rows.Length > 1 ? "--" + group.Last.SignalValue.ToString("0.00", CultureInfo.InvariantCulture) : "");
                var a = row.Allocation;
                string endpoint = "$" + Num(a.Original, false) + (amount ? "" : @"\%") +
                    @"\to " + Num(a.Target, false) + (amount ? "" : @"\%") + "$";
                b.Append(label + " & " + signal + " & " + endpoint);
                foreach (double effect in includeExit ? new[] { a.Direct, a.Entry, a.Offers, a.Exit } : new[] { a.Direct, a.Entry, a.Offers })
                    b.Append(" & $" + Num(effect, true) + "$");
                b.AppendLine(@"\\");
            }
        }
        return b.AppendLine(@"\bottomrule\end{tabularx}\end{minipage}\end{document}").ToString().Replace("\r\n", "\n");
    }

    public static async Task WriteAsync(string input, Panel[] full)
    {
        await WriteSelectedAsync(input, Select(full), Stem, Caption, false);
        await WriteSelectedAsync(input, SelectRiskAverse(full), RiskAverseStem, RiskAverseCaption, true);
    }

    private static async Task WriteSelectedAsync(string input, Panel[] panels, string stem, string caption, bool includeExit)
    {
        string tex = Path.Combine(input, "Sources", "Tex", stem + ".tex");
        // The risk-averse display includes conditional offer comparisons at histories reached
        // in both endpoint equilibria; intermediate own best responses may avoid them.
        // Definedness and the complete reach diagnostics remain in the saved rows below.
        await File.WriteAllTextAsync(tex, Latex(panels, includeExit, requireIntermediateReach: !includeExit));
        await File.WriteAllTextAsync(Path.Combine(input, "Sources", "Json", stem + ".json"),
            JsonSerializer.Serialize(new {
                Schema = "1", Caption = caption, Cost = 1,
                Selection = includeExit ? "Nine illustrative rows involving risk aversion; complete comparison packet retained online" :
                    "Three risk-neutral illustrative rows selected for manuscript discussion; complete comparison packet retained online",
                OmittedZeroColumns = includeExit ? new[] { "Remaining" } : new[] { "Opponent exit", "Remaining" },
                Panels = panels.Select(p => new { p.Comparison, SelectedRows = p.Rows,
                    DisplayedRows = EquilibriumPublicationTables.GroupRows(p.Rows).Length }),
                FullAnalysis = Hash(Path.Combine(input, "Sources", "Json", EquilibriumManuscriptTable.Stem + ".json")),
                Methodology = EquilibriumPublicationTables.MethodologyFile,
                Generator = Hash(typeof(SelectedEquilibriumManuscriptTable).Assembly.Location)
            }, JsonOptions).Replace("\r\n", "\n") + "\n");
        await File.WriteAllTextAsync(Path.Combine(input, "Sources", stem + "-caption.txt"), caption + "\n");
        await DiagramCompiler.CompileAsync(tex, new(), renderedDirectory: Path.Combine(input, "Tables"), allPages: true);
    }
}
