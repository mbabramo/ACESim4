using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.EquilibriumChangeFocus;

namespace LitigCharts;

/// <summary>Experimental, compact Markdown/JSON reports; never writes animation or publication paths.</summary>
public static class EquilibriumChangeFocusCommand
{
    public sealed record Comparison(Contrast Contrast, Row[] OriginalRows, Row[] MixedRows,
        EquilibriumChangeDecomposition.ExcludedHistory[] ExcludedHistories);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length != 4 || args[0] != "--request" || args[2] != "--original")
                throw new ArgumentException("equilibrium-change-focus --request <mixed decomposition request> --original <saved decomposition request>");
            string requestFile = Path.GetFullPath(args[1]), originalFile = Path.GetFullPath(args[3]);
            var request = ReadRequest(requestFile);
            if (request.Sources.Any(s => s.ProfileFile == null)) throw new InvalidDataException("Focus comparison requires explicit diagnostic profiles.");
            string output = Path.GetFullPath(request.OutputDirectory, Path.GetDirectoryName(requestFile));
            var current = EquilibriumChangeTables.LoadCalculated(requestFile);
            var original = EquilibriumChangeTables.LoadCalculated(originalFile);
            if (current.Manifest.OutputJsonFiles.Intersect(original.Manifest.OutputJsonFiles, StringComparer.OrdinalIgnoreCase).Any())
                throw new InvalidDataException("Experimental calculations must be separate from original calculations.");
            double tolerance = (request.Tolerances ?? new()).NearTie;
            var comparisons = current.Results.Select(r => new Comparison(r.Contrast,
                Build(original.Results.Single(o => o.Contrast.Id == r.Contrast.Id), tolerance), Build(r, tolerance), r.ExcludedHistories)).ToArray();
            var document = new StringBuilder("# Strategy changes after equilibrium-preserving mixing\n\n");
            document.AppendLine("Experimental comparison only. Original equilibria, solution-path animations and existing paper tables are unchanged. The mixing search chooses local diagnostic representatives, not globally or uniquely maximally mixed equilibria.\n");
            document.AppendLine($"**Focus rule:** retain a changed, commonly reached information set if more than 0.0001% of the original policy uses actions worse than the best local action by more than {tolerance:G3} target-utility units, against the target opponent and with the player's subsequent decisions reoptimized. This allows overlapping supports. A change between tied actions is shown separately, not declared irrelevant: it may be needed to sustain the opponent's incentives.\n");
            document.AppendLine("One row per information set. The decomposed quantity is probability assigned to the set of actions that gain probability (percentage points), not the mean offer. Thus mixed distributions do not explode into one row per action. Full action-level decompositions remain in the calculation JSON.\n");
            document.AppendLine("| Intervention (ordinary costs) | Original changed sets | Original focus | After mixing | Focus retained | Disjoint supports after mixing | Focus with Remaining |\n|---|---:|---:|---:|---:|---:|---:|");
            foreach (var c in comparisons)
                document.AppendLine($"| {c.Contrast.Label} | {c.OriginalRows.Length} | {c.OriginalRows.Count(r => r.Focus)} | {c.MixedRows.Length} | {c.MixedRows.Count(r => r.Focus)} | {c.MixedRows.Count(r => r.SupportsDisjoint)} | {c.MixedRows.Count(r => r.Focus && r.Allocation != null && Math.Abs(r.Allocation.SelectionResidual) > 0.0001)} |");
            document.AppendLine("\nThe same focus rule can be applied without mixing (Original focus column). Greater mixing does not necessarily reduce the number of differences or resolve selection dependence.\n");
            document.AppendLine("\n**Reading the allocation:** Direct changes the rule/preferences first. Entry, offers and exit average the incremental opponent-policy replacements over all six orders. Remaining is the target share minus the selected best-response share with all opponent policies replaced; it is not assigned to a mechanism. This is diagnostic accounting, not an identified causal explanation.\n");
            document.AppendLine("Flags: `*` nonzero Remaining; `T` tie-selection sensitivity; `C` donor-off-path completion sensitivity; `H` some hybrid response does not reach this information set (conditional values remain defined); `U` zero counterfactual reach prevents allocation. Rounding can make tiny effects display as zero.\n");
            foreach (var c in comparisons)
            {
                document.AppendLine("## " + c.Contrast.Label + "\n");
                document.AppendLine("### Focused changes\n");
                AppendAllocations(document, c.MixedRows.Where(r => r.Focus).ToArray());
                document.AppendLine("\n### Endpoint strategies for focused changes\n\n| Decision | Signal | Original diagnostic distribution | Target diagnostic distribution | Old-policy local loss at target |\n|---|---:|---|---|---:|");
                foreach (var r in c.MixedRows.Where(r => r.Focus))
                    document.AppendLine($"| {Decision(r)} | {r.SignalValue:F2} | {Distribution(r.ActionLabels, r.OriginalPolicy)} | {Distribution(r.ActionLabels, r.TargetPolicy)} | {r.OriginalPolicyLossAtTarget:G6} |");
                document.AppendLine("\nLocal loss uses the target utility scale and optimized own continuation against the target opponent. It does not compare welfare across risk regimes or measure the loss of reverting the entire strategy. Fixed-target-continuation losses are also retained in the JSON.\n");
                document.AppendLine("### Other changed sets (retained for audit)\n");
                AppendAllocations(document, c.MixedRows.Where(r => !r.Focus).ToArray());
                document.AppendLine($"\n{c.ExcludedHistories.Length} histories change reach and are excluded from the two-endpoint strategy comparison. They remain listed in the JSON.\n");
            }
            string stem = Path.Combine(output, "Focused strategy changes");
            await File.WriteAllTextAsync(stem + ".md", document.ToString().TrimEnd() + "\n");
            await File.WriteAllTextAsync(stem + ".json", JsonSerializer.Serialize(new
            {
                Schema = "1", CreatedUtc = DateTimeOffset.UtcNow, UtilityTolerance = tolerance,
                ProbabilityTolerance = EquilibriumChangeDecomposition.PolicyTolerance,
                Request = Hash(requestFile), OriginalRequest = Hash(originalFile),
                Calculation = Hash(Path.Combine(output, "equilibrium-changes-manifest.json")),
                OriginalCalculation = Hash(Path.Combine(Path.GetFullPath(ReadRequest(originalFile).OutputDirectory,
                    Path.GetDirectoryName(originalFile)), "equilibrium-changes-manifest.json")),
                CoreAssembly = Hash(typeof(EquilibriumChangeFocus).Assembly.Location),
                ReportAssembly = Hash(typeof(EquilibriumChangeFocusCommand).Assembly.Location), Comparisons = comparisons
            }, JsonOptions) + "\n");
            Console.WriteLine(stem + ".md");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static string Decision(Row row) => row.Decision + (row.ExitCommitment == 1 ? " / exit" : row.ExitCommitment == 2 ? " / continue" : "");
    private static string Distribution(string[] labels, double[] p) => string.Join("; ", p.Select((v, a) => (v, a))
        .Where(x => x.v > EquilibriumChangeDecomposition.PolicyTolerance)
        .Select(x => labels[x.a] + ": " + (100 * x.v).ToString("0.##", CultureInfo.InvariantCulture) + "%"));

    private static void AppendAllocations(StringBuilder document, Row[] rows)
    {
        if (rows.Length == 0) { document.AppendLine("None.\n"); return; }
        document.AppendLine("| Decision | Signal | Actions gaining probability | Shift (pp) | Direct | Opp. entry | Opp. offers | Opp. exit | Remaining | Flags |\n|---|---:|---|---:|---:|---:|---:|---:|---:|---|");
        foreach (var r in rows)
        {
            string Num(double? v) => v?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—";
            string flags = (Math.Abs(r.Allocation?.SelectionResidual ?? 0) > 0.0001 ? "*" : "") +
                (r.TieSensitive ? "T" : "") + (r.CompletionSensitive ? "C" : "") + (r.UnreachedCoalitions.Length > 0 ? "H" : "") + (r.CounterfactualUndefined ? "U" : "");
            string gaining = string.Join(", ", r.GainingActions.Select(a => r.ActionLabels[a - 1]));
            double change = 100 * r.GainingActions.Sum(a => r.TargetPolicy[a - 1] - r.OriginalPolicy[a - 1]);
            document.AppendLine($"| {Decision(r)} | {r.SignalValue:F2} | {gaining} | {Num(change)} | {Num(r.Allocation?.Direct)} | {Num(r.Allocation?.Entry)} | {Num(r.Allocation?.Offers)} | {Num(r.Allocation?.Exit)} | {Num(r.Allocation?.SelectionResidual)} | {flags} |");
        }
    }
}
