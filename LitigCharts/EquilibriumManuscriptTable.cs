using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.EquilibriumChangeDecomposition;

namespace LitigCharts;

/// <summary>Assemble the complete comparison packet and the article's illustrative selection.</summary>
public static class EquilibriumManuscriptTable
{
    public const string Stem = "manuscript-strategy-mechanisms";
    public const string ArticleStem = "Table 2 - Strategy mechanisms";
    public const string RiskAverseArticleStem = "Table 3 - Risk-averse strategy changes";
    public sealed record Comparison(string Id, string Label);
    public sealed record Panel(Comparison Comparison, ChangeRow[] Rows);

    public static Comparison[] Comparisons() => JsonSerializer.Deserialize<Comparison[]>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ArticleStrategyComparisons.json")), JsonOptions)
        ?? throw new InvalidDataException("Missing manuscript comparison list.");

    public static string Latex(Panel[] panels)
    {
        if (panels.Length == 0 || panels.Select(p => p.Comparison.Id).Distinct().Count() != panels.Length)
            throw new InvalidDataException("Supply unique ordered manuscript panels.");
        var b = new StringBuilder("""
            \documentclass[10pt,letterpaper]{article}
            \usepackage[T1]{fontenc}
            \usepackage{lmodern,booktabs,array,tabularx,microtype}
            \usepackage[margin=.5in]{geometry}
            \pagestyle{empty}
            \setlength{\parindent}{0pt}
            \setlength{\tabcolsep}{3.5pt}
            \renewcommand{\arraystretch}{1.22}
            \begin{document}
            """);
        for (int i = 0; i < panels.Length; i++)
        {
            // Keep a comparison together; page breaks occur between full panels.
            b.AppendLine(@"\noindent\begin{minipage}{\linewidth}");
            b.AppendLine(@"\textbf{" + (char)('A' + i) + ". " +
                EquilibriumChangeTables.Escape(panels[i].Comparison.Label) + @"}\par\smallskip");
            b.Append(EquilibriumPublicationTables.LatexBody(panels[i].Rows));
            b.AppendLine(@"\end{minipage}\par\vspace{14pt}");
        }
        return b.AppendLine(@"\end{document}").ToString().Replace("\r\n", "\n");
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i += 2)
                if (i + 1 == args.Length || args[i] is not ("--input" or "--article") ||
                    !options.TryAdd(args[i], Path.GetFullPath(args[i + 1])))
                    throw new ArgumentException("equilibrium-manuscript --input <strategy-change directory> [--article <article repository>]");
            if (!options.TryGetValue("--input", out string input))
                throw new ArgumentException("Supply the strategy-change directory.");
            var panels = new List<Panel>();
            var sources = new List<object>();
            bool hasDocumentedSmallGap = false;
            foreach (var comparison in Comparisons())
            {
                string id = comparison.Id + "-cost-1";
                string request = Path.Combine(input, "Data", "cost-1", id, "equilibrium-changes.request.json");
                var calculated = EquilibriumChangeTables.LoadCalculated(request);
                var result = calculated.Results.Single(r => r.Contrast.Id == id);
                if (EquilibriumChangeTables.Heading(result.SourceOptionSet, result.TargetOptionSet).Cost != "1")
                    throw new InvalidDataException("Main strategy comparisons require ordinary costs.");
                // The same selection function as the individual tables; no row allowlist.
                var rows = EquilibriumPublicationTables.Select(result);
                if (comparison.Id == "american-to-trial-risk-neutral")
                {
                    var filing = rows.SingleOrDefault(r => r.Decision == "P Files" && Math.Abs(r.SignalValue - .25) < 1e-8);
                    if (filing != null && filing.Allocation.Direct > PolicyTolerance)
                    {
                        var direct = result.Scenarios.Single(s => s.Panel == "coalition" && s.Component == "0" && s.Result.Player == 0)
                            .Result.InformationSets.Single(i => i.Key == filing.Key);
                        double? gap = direct.Actions[0].CounterfactualConditionalUtility - direct.Actions[1].CounterfactualConditionalUtility;
                        // Qualify the documented small-gap attribution while it remains in the inputs.
                        // This is not an additional row-selection rule or a general numerical audit.
                        hasDocumentedSmallGap = gap > 0 && gap <= 1e-5;
                    }
                }
                panels.Add(new(comparison, rows));
                sources.Add(new { Comparison = comparison, result.Contrast, result.SourceOptionSet,
                    result.TargetOptionSet, SelectedRows = rows,
                    DisplayedRows = rows.GroupBy(EquilibriumPublicationTables.Unchanged)
                        .Sum(g => EquilibriumPublicationTables.GroupRows(g.ToArray()).Length),
                    Inputs = new[] { Hash(request), Hash(Path.Combine(Path.GetDirectoryName(request),
                        "equilibrium-changes-manifest.json")), Hash(Path.Combine(Path.GetDirectoryName(request), id + ".json")) } });
            }
            string texDirectory = Path.Combine(input, "Sources", "Tex"),
                jsonDirectory = Path.Combine(input, "Sources", "Json"), tables = Path.Combine(input, "Tables");
            foreach (string directory in new[] { texDirectory, jsonDirectory, tables }) Directory.CreateDirectory(directory);
            string tex = Path.Combine(texDirectory, Stem + ".tex");
            await File.WriteAllTextAsync(tex, Latex(panels.ToArray()));
            string caption = "Equilibrium strategy comparisons at cost multiplier 1. Panels follow the seven specified fee/preference changes. " +
                "Every coordinate satisfying the shared strict action-loss or offsetting-effect criterion is included; there is no further illustrative-row selection. " +
                "Adjacent signals are grouped only when their policies, allocations, definedness and intermediate reach patterns match. " +
                "Binary decisions and individual mixed-offer action shares have percentage endpoints and percentage-point contributions. " +
                "Pure demand and offer amounts and their contributions are fractions of damages. Offer-action probabilities identify the action in the Decision column. " +
                "An asterisk marks an unreached intermediate response with a defined conditional comparison. Remaining retains endpoint-selection residuals. " +
                "Sensitive describes the recorded tie and off-path completion checks, not payoff-grid robustness. These counterfactual accounts are not causal shares or adjustment paths.";
            if (hasDocumentedSmallGap)
                caption += " Numerical review remains pending: in panel A, P filing at signal 0.25 has a positive Direct entry triggered by a utility gap below 0.00001, matching the documented payoff-rounding issue. " +
                    "That entry must not be interpreted as an established positive economic incentive from trial fee shifting. The recorded values are retained pending that audit.";
            await File.WriteAllTextAsync(Path.Combine(jsonDirectory, Stem + ".json"), JsonSerializer.Serialize(new {
                Schema = "1", Caption = caption, Cost = 1, Selection = "All qualifying rows within each listed comparison",
                Panels = sources, Generator = Hash(typeof(EquilibriumManuscriptTable).Assembly.Location),
                ComparisonList = Hash(Path.Combine(AppContext.BaseDirectory, "ArticleStrategyComparisons.json")) }, JsonOptions).Replace("\r\n", "\n") + "\n");
            // Caption belongs outside the diagrams and Sources/Tex and Sources/Json.
            await File.WriteAllTextAsync(Path.Combine(input, "Sources", Stem + "-caption.txt"), caption + "\n");
            await DiagramCompiler.CompileAsync(tex, new(), renderedDirectory: tables, allPages: true);
            await SelectedEquilibriumManuscriptTable.WriteAsync(input, panels.ToArray());
            if (options.TryGetValue("--article", out string article))
            {
                PublishArticle(input, article, SelectedEquilibriumManuscriptTable.Stem, ArticleStem);
                PublishArticle(input, article, SelectedEquilibriumManuscriptTable.RiskAverseStem, RiskAverseArticleStem);
            }
            Console.WriteLine($"Retained {panels.Count} full panels, {panels.Sum(p => p.Rows.Length)} coordinates; published three risk-neutral and eleven risk-averse illustrative rows; no equilibrium was rerun.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void PublishArticle(string input, string article, string selectedStem, string articleStem)
    {
        string tables = Path.Combine(article, "Tables"), sources = Path.Combine(tables, "Sources");
        Directory.CreateDirectory(sources);
        var mappings = new List<(string Source, string Output)> {
            (Path.Combine(input, "Tables", selectedStem + ".pdf"), Path.Combine(tables, articleStem + ".pdf")),
            (Path.Combine(input, "Tables", selectedStem + ".png"), Path.Combine(tables, articleStem + ".png")),
            (Path.Combine(input, "Sources", "Tex", selectedStem + ".tex"), Path.Combine(sources, articleStem + ".tex")),
            (Path.Combine(input, "Sources", "Json", selectedStem + ".json"), Path.Combine(sources, articleStem + ".json")),
            (Path.Combine(input, "Sources", selectedStem + "-caption.txt"), Path.Combine(sources, articleStem + ".txt")) };
        foreach (string page in Directory.GetFiles(Path.Combine(input, "Tables"), selectedStem + "-page-*.png"))
            mappings.Add((page, Path.Combine(tables, articleStem + Path.GetFileName(page)[selectedStem.Length..])));
        foreach (var file in mappings) File.Copy(file.Source, file.Output, overwrite: true);
        // Remove only obsolete page previews of this exact exhibit after successful rendering.
        var kept = mappings.Select(m => m.Output).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string page in Directory.GetFiles(tables, articleStem + "-page-*.png"))
            if (!kept.Contains(page)) File.Delete(page);
        string readmePath = Path.Combine(tables, "README.md");
        if (File.Exists(readmePath))
        {
            var lines = File.ReadAllLines(readmePath).Select(line =>
                line.StartsWith("| [Table 2 - Strategy mechanisms]")
                    ? "| [Table 2 - Strategy mechanisms](<Table 2 - Strategy mechanisms.pdf>) | [selected-strategy-mechanisms](<../Supplemental materials/Equilibrium strategy changes/Tables/selected-strategy-mechanisms.pdf>) |"
                    : line.StartsWith("Regenerate with `python scripts/assemble_manuscript_exhibits.py`") || line.StartsWith("Regenerate Table 2 from ACESim4")
                    ? "Regenerate Tables 2 and 3 from ACESim4 with `LitigCharts equilibrium-manuscript --input <strategy-change directory> --article <article repository>`. The article uses three risk-neutral and eleven risk-averse illustrative rows, without sensitivity markers. The full seven-panel analysis and diagnostic checks remain in `Supplemental materials/Equilibrium strategy changes/Tables/manuscript-strategy-mechanisms.pdf` and its sources. Remaining contributions are zero in both selections; opponent-exit contributions are displayed in Table 3. `manuscript-exhibits.json` records the numbered files and their source/output hashes."
                    : line);
            File.WriteAllText(readmePath, string.Join("\n", lines) + "\n");
        }
        string manifestFile = Path.Combine(article, "manuscript-exhibits.json");
        if (!File.Exists(manifestFile)) return;
        var manifest = JsonNode.Parse(File.ReadAllText(manifestFile));
        var exhibits = manifest["Exhibits"].AsArray();
        for (int i = exhibits.Count - 1; i >= 0; i--)
            if (exhibits[i]["Exhibit"]?.GetValue<string>() == articleStem) exhibits.RemoveAt(i);
        foreach (var file in mappings)
            exhibits.Add(new JsonObject { ["Exhibit"] = articleStem, ["Source"] = Path.GetRelativePath(article, file.Source),
                ["Output"] = Path.GetRelativePath(article, file.Output), ["Sha256"] = Hash(file.Output).Sha256 });
        File.WriteAllText(manifestFile, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n");
    }
}
