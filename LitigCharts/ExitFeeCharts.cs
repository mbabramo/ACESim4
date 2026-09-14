using ACESim;
using CsvHelper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Matched, unconditional comparisons of the exit-fee extension and archived controls.</summary>
public static class ExitFeeCharts
{
    public sealed record Request(string BaselineResultsCsv, string ExtensionResultsCsv,
        string BaselineIndividualDirectory, string ExtensionIndividualDirectory, string OutputDirectory,
        bool CompileIndividualResults = true);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly double[] Costs = [0.25, 0.5, 1, 2, 4];
    private static readonly string[] Specifications = ["Baseline", "ModerateRiskAversion"];
    private static readonly string[] Regimes = ["American", "Trial only", "Trial + exit"];
    private const string Suffix = "__ExitFees-AllUnilateralExits";
    private static string Name(string spec, double cost, string fee, bool exit = false) =>
        $"Specification-{spec}__Cost-{cost.ToString(CultureInfo.InvariantCulture)}__Fee-{fee}" + (exit ? Suffix : "");
    private static double Number(Dictionary<string, string> row, string key) => double.Parse(row[key], CultureInfo.InvariantCulture);
    private static string F(double number) => number.ToString("0.############", CultureInfo.InvariantCulture);
    private static object Fingerprint(string path) => new { Path = Path.GetFullPath(path), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };

    public static List<Dictionary<string, string>> SelectComparisons(string baselineFile, string extensionFile)
    {
        var baseline = PublicationFigures.ReadCsv(baselineFile);
        var extension = PublicationFigures.ReadCsv(extensionFile);
        var rows = new List<Dictionary<string, string>>();
        foreach (string spec in Specifications)
        foreach (double cost in Costs)
        for (int regime = 0; regime < 3; regime++)
        {
            bool exit = regime == 2;
            string name = Name(spec, cost, regime == 0 ? "American" : "British", exit);
            var selected = (exit ? extension : baseline).Where(row => row["OptionSetName"] == name &&
                row["Filter"] == "All" && row["Equilibrium Type"] == "Only Eq").ToArray();
            if (selected.Length != 1) throw new InvalidDataException($"Expected exactly one comparison row for {name}.");
            var row = new Dictionary<string, string>(selected[0], StringComparer.OrdinalIgnoreCase);
            if (row["Number of Offers"] != "10" || Number(row, "Costs Multiplier") != cost ||
                row["Fee Regime"] != (regime == 0 ? "American" : "British") ||
                row["Filing and Answering"] != "Endogenous" || row["Allow Abandon and Defaults"] != "true" ||
                row["Signal Structure"] != "Continuous merits" || row["Number of Signals"] != "10" ||
                row["Number of Court Signals"] != "2" || Number(row, "Party Signal Sigma") != 0.2 ||
                Number(row, "Court Signal Sigma") != 0.2 || Number(row, "Probability Truly Liable") != 0.5 ||
                Number(row, "Proportion of Costs at Beginning") != 0.5 ||
                Number(row, "CARA Alpha") != (spec == "Baseline" ? 0 : 2) ||
                Number(row, "Fee Shifting Multiplier") != (regime == 0 ? 0 : 1))
                throw new InvalidDataException("Comparison primitives disagree: " + name);
            if (exit && (row["Fee Shifting Trigger"] != LitigGameCorrelatedSignalsArticleLauncher.ExitFeeTriggerLabel ||
                row["Fees After Nonanswer"] != "true"))
                throw new InvalidDataException("Missing exit-fee treatment: " + name);
            PublicationFigures.ValidateDisposition(PublicationFigures.CategoryColumns.Select(column => Number(row, column)).ToArray(), Number(row, "Trial"));
            row["Comparison Regime"] = Regimes[regime];
            row["Source Plan"] = exit ? "CS006EF" : "CS004";
            double prior = Number(row, "Probability Truly Liable");
            row["Plaintiff shortfall contribution"] = F(prior * Number(row, CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn));
            row["Nonliable defendant contribution"] = F((1 - prior) * Number(row, CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn));
            row["Liable defendant contribution"] = F(prior * Number(row, CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn));
            double componentSum = new[] { "Plaintiff shortfall contribution", "Nonliable defendant contribution", "Liable defendant contribution" }.Sum(column => Number(row, column));
            if (Math.Abs(componentSum - Number(row, CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn)) > 1E-4)
                throw new InvalidDataException("Population-weighted monetary accounting failed: " + name);
            rows.Add(row);
        }
        return rows;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length is < 2 or > 3 || args[0] != "--request" || args.Length == 3 && args[2] != "--sources-only")
                throw new ArgumentException("Usage: LitigCharts exit-fees --request <request.json> [--sources-only]");
            bool sourcesOnly = args.Length == 3;
            string requestFile = Path.GetFullPath(args[1]);
            var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestFile), Json) ?? throw new InvalidDataException("Empty request.");
            string Resolve(string path) => Path.GetFullPath(path, Path.GetDirectoryName(requestFile));
            string baselineFile = Resolve(request.BaselineResultsCsv), extensionFile = Resolve(request.ExtensionResultsCsv);
            string output = Resolve(request.OutputDirectory);
            Directory.CreateDirectory(output);
            var rows = SelectComparisons(baselineFile, extensionFile);
            string comparisonFile = Path.Combine(output, "matched-comparisons.csv");
            WriteCsv(comparisonFile, rows);
            var sources = new List<string>();
            var dataSources = new List<string> { requestFile, baselineFile, extensionFile };
            for (int preference = 0; preference < Specifications.Length; preference++)
            {
                string spec = Specifications[preference];
                string stem = preference == 0 ? "risk-neutral" : "moderate-risk-aversion";
                string label = preference == 0 ? "Risk neutrality" : "Moderate risk aversion";
                string control = Name(spec, 1, "British"), treatment = Name(spec, 1, "British", true);
                string oldActions = Path.Combine(Resolve(request.BaselineIndividualDirectory), "CS004 " + control + " -InformationSetActions.csv");
                string newActions = Path.Combine(Resolve(request.ExtensionIndividualDirectory), "CS006EF " + treatment + " -InformationSetActions.csv");
                dataSources.AddRange([oldActions, newActions]);
                var cases = new[] {
                    new PublicationFigures.StrategyCase("Trial only", control, oldActions),
                    new PublicationFigures.StrategyCase("Trial + exit", treatment, newActions)
                };
                var groups = Costs.Select(cost => new PublicationFigures.DispositionGroup("Costs " + F(cost),
                    [Name(spec, cost, "British"), Name(spec, cost, "British", true)])).ToArray();
                string figureRequest = Path.Combine(output, stem + ".request.json");
                File.WriteAllText(figureRequest, JsonSerializer.Serialize(new PublicationFigures.Request(cases, "matched-comparisons.csv", groups,
                    label + ": trial-contingent fee shifting versus reimbursement on trial and unilateral exit, including initial nonanswer. " +
                    "Filing, answering, and later exit remain voluntary; ten offers. Every disposition uses all potential disputes. " +
                    "Archived CS004 controls and new CS006EF cases are distinct source batches.", "Comparison Regime", SeparateExitHistories: true), Json));
                foreach (string target in new[] { "dispositions", "selection-offers" })
                {
                    var figure = PublicationFigures.Generate(figureRequest, target);
                    string path = Path.Combine(output, stem + "-" + target);
                    File.WriteAllText(path + ".tex", figure.Latex);
                    File.WriteAllText(path + ".txt", label + ". " + figure.Caption);
                    File.WriteAllText(path + ".json", JsonSerializer.Serialize(figure.Data, Json));
                    sources.Add(path + ".tex");
                }
                var selected = rows.Where(row => row["OptionSetName"].StartsWith("Specification-" + spec + "__", StringComparison.Ordinal)).ToArray();
                string outcomeStem = Path.Combine(output, stem + "-monetary-outcomes");
                File.WriteAllText(outcomeStem + ".tex", RenderMonetaryOutcomes(label, selected));
                File.WriteAllText(outcomeStem + ".json", JsonSerializer.Serialize(new { Source = Fingerprint(comparisonFile), Rows = selected }, Json));
                File.WriteAllText(outcomeStem + ".txt", label + ". Each series uses five cost levels. All outcomes are expected damages units per potential dispute. " +
                    "The three truth-relative contributions are weighted by their truth-state probabilities. Net Outcome Fidelity Loss is their sum, not welfare. " +
                    "Fees are transfers, separate from real litigation expenditure. American and trial-only results are archived controls; trial-plus-exit results are new. Lines connect computed specifications and do not establish behavior at intermediate costs.");
                sources.Add(outcomeStem + ".tex");
            }
            string ordinaryRequest = Path.Combine(output, "ordinary-cost-dispositions.request.json");
            var ordinaryCases = new[] {
                new PublicationFigures.StrategyCase("Trial only", Name("Baseline", 1, "British"), "unused-by-dispositions.csv"),
                new PublicationFigures.StrategyCase("Trial + exit", Name("Baseline", 1, "British", true), "unused-by-dispositions.csv")
            };
            var ordinaryGroups = Specifications.Select((spec, index) => new PublicationFigures.DispositionGroup(
                index == 0 ? "Risk neutral" : "Risk averse",
                [Name(spec, 1, "British"), Name(spec, 1, "British", true)])).ToArray();
            File.WriteAllText(ordinaryRequest, JsonSerializer.Serialize(new PublicationFigures.Request(ordinaryCases,
                "matched-comparisons.csv", ordinaryGroups,
                "Ordinary costs (multiplier 1), with risk neutrality and symmetric CARA alpha 2. Trial + exit includes " +
                "fees on initial nonanswer and later unilateral exit. All bars use all potential disputes. " +
                "Filing, answering and later exit remain voluntary. Trial-only controls are archived CS004 cases; " +
                "the expanded trigger uses new CS006EF cases.", "Comparison Regime"), Json));
            var ordinaryFigure = PublicationFigures.Generate(ordinaryRequest, "dispositions");
            string ordinaryStem = Path.Combine(output, "ordinary-cost-dispositions");
            File.WriteAllText(ordinaryStem + ".tex", ordinaryFigure.Latex);
            File.WriteAllText(ordinaryStem + ".txt", ordinaryFigure.Caption);
            File.WriteAllText(ordinaryStem + ".json", JsonSerializer.Serialize(ordinaryFigure.Data, Json));
            sources.Add(ordinaryStem + ".tex");
            if (request.CompileIndividualResults)
            {
                string directory = Resolve(request.ExtensionIndividualDirectory);
                var individual = Directory.GetFiles(directory, "CS006EF *.tex", SearchOption.TopDirectoryOnly);
                if (individual.Length != 60) throw new InvalidDataException($"Expected 60 individual extension diagrams, found {individual.Length}.");
                sources.AddRange(individual);
            }
            if (!sourcesOnly)
                await DiagramCompiler.CompileAllAsync(sources.ToArray(), new ArticleDiagramCommand.Configuration(), 4);
            var inventory = sources.SelectMany(source => new[] { source, Path.ChangeExtension(source, ".pdf"), Path.ChangeExtension(source, ".png") })
                .Where(File.Exists).Select(Fingerprint).ToArray();
            File.WriteAllText(Path.Combine(output, "chart-inventory.json"), JsonSerializer.Serialize(new {
                Schema = "exit-fee-comparison-v1", GeneratedUtc = DateTime.UtcNow,
                Compiled = !sourcesOnly, SelectedRows = rows.Count, NewCases = 10, ArchivedControlCases = 20,
                Inputs = dataSources.Distinct().Select(Fingerprint).ToArray(), Outputs = inventory
            }, Json));
            Console.WriteLine($"Validated 10 extension cases and 20 archived controls; generated {sources.Count} chart sources in {output}.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
    }

    private static void WriteCsv(string path, List<Dictionary<string, string>> rows)
    {
        string[] columns = rows.SelectMany(row => row.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using var writer = new StreamWriter(path);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        foreach (string column in columns) csv.WriteField(column);
        csv.NextRecord();
        foreach (var row in rows)
        {
            foreach (string column in columns) csv.WriteField(row.GetValueOrDefault(column, ""));
            csv.NextRecord();
        }
    }

    public static string RenderMonetaryOutcomes(string label, Dictionary<string, string>[] rows)
    {
        var measures = new[] {
            ("Real Litigation Costs", "Real litigation expenditures"),
            ("Plaintiff shortfall contribution", "Meritorious plaintiff shortfall"),
            ("Nonliable defendant contribution", "Nonliable defendant burden"),
            ("Liable defendant contribution", "Liable defendant excess burden"),
            (CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn, "Net Outcome Fidelity Loss"),
            ("Total Net Transfer to Plaintiff", "Net transfer to plaintiff")
        };
        var tex = new StringBuilder(@"\documentclass[border=8pt]{standalone}
\usepackage{pgfplots}\usetikzlibrary{calc}\usepgfplotslibrary{groupplots}\pgfplotsset{compat=1.18}
\begin{document}\begin{tikzpicture}
\begin{groupplot}[group style={group size=2 by 3,horizontal sep=1.5cm,vertical sep=1.85cm},
width=7.5cm,height=5.4cm,xmode=log,log basis x=2,xtick={0.25,0.5,1,2,4},
xticklabels={0.25,0.5,1,2,4},xlabel={Cost multiplier},tick label style={font=\small},
label style={font=\small},title style={font=\small},ymajorgrids=true,grid style={gray!20},
scaled y ticks=false,legend style={font=\scriptsize,draw=none,fill=white},legend pos=north west]
");
        string[] styles = ["black,densely dotted,mark=triangle*", "black,dashed,mark=square*", "black,solid,mark=* "];
        for (int panel = 0; panel < measures.Length; panel++)
        {
            tex.AppendLine("\\nextgroupplot[title={" + measures[panel].Item2 + "}]");
            for (int regime = 0; regime < Regimes.Length; regime++)
            {
                tex.Append("\\addplot+[" + styles[regime] + ",mark size=1.6pt,mark options={solid,draw=black,fill=white}] coordinates {");
                foreach (double cost in Costs)
                {
                    var row = rows.Single(row => row["Comparison Regime"] == Regimes[regime] && Number(row, "Costs Multiplier") == cost);
                    tex.Append("(" + F(cost) + "," + F(Number(row, measures[panel].Item1)) + ")");
                }
                tex.AppendLine("};");
                if (panel == 0) tex.AppendLine("\\addlegendentry{" + Regimes[regime] + "}");
            }
        }
        tex.AppendLine(@"\end{groupplot}");
        tex.AppendLine("\\node[anchor=south,font=\\bfseries] at ($(group c1r1.north west)!0.5!(group c2r1.north east)$) [yshift=32pt] {" + label + "};");
        tex.AppendLine(@"\node[anchor=north,font=\small] at ($(group c1r3.south west)!0.5!(group c2r3.south east)$) [yshift=-35pt] {Expected damages units per potential dispute};
\end{tikzpicture}\end{document}");
        return tex.ToString();
    }
}
