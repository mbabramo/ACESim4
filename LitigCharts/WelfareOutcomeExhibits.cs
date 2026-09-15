using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using CsvHelper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Welfare tables and separate disposition figures for every saved specification and cost; never solves a game.</summary>
public static class WelfareOutcomeExhibits
{
    public sealed record Input(string NumericalResultsCsv, string IndividualDirectory, string ReportPrefix);
    public sealed record Request(Input[] Inputs, string OutputDirectory);
    public sealed record Exhibit(string Family, double Cost, string Kind, string TexFile, string[] OptionSets);
    public sealed record Generation(string OutputDirectory, Dictionary<string, string> Files, Exhibit[] Exhibits,
        PublicationTables.Source[] Inputs, int Cases);
    public sealed record PaymentAudit(double SettlementProbability, double? SettlementMean,
        double Nonanswer, double Default, double PlaintiffTrialWin, double MutualGiveUp,
        bool AddedMutualAllocation, double MeanPayment);
    public sealed record ErrorAudit(double Prior, PaymentAudit Liable, PaymentAudit Nonliable,
        double MeritoriousUnderpayment, double NonliablePayment, double Error);
    public sealed record CaseAudit(string OptionSet, string DetailedReport, string Sha256,
        Dictionary<string, string> All, Dictionary<string, string> Liable,
        Dictionary<string, string> Nonliable, ErrorAudit Calculation, double AggregatePaymentResidual);
    public const string ErrorColumn = "Outcome Error Before Legal Costs and Fee Transfers";
    private static readonly string[] Measures = ["Plaintiff shortfall contribution", "Nonliable defendant contribution",
        "Liable defendant contribution", ErrorColumn, "Real Litigation Costs"];
    public static readonly string[] Regimes = ["American", "Trial Fee-Shifting", "Complete Fee-Shifting"];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private static double N(IReadOnlyDictionary<string, string> row, string key) => PublicationTables.Number(row, key);
    private static string F(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);
    private static void WriteText(string path, string content) => File.WriteAllText(path, content.Replace("\r\n", "\n"), new UTF8Encoding(false));
    private static PublicationTables.Source Source(string path) => new(Path.GetFullPath(path),
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    // With fixed damages d=1 and every base payment R in [0,1], absolute error is
    // linear within each truth state: E[|R-T|] = pi(1-E[R|T=1])+(1-pi)E[R|T=0].
    // The restriction is checked against the selected game options before this is used.
    public static ErrorAudit OutcomeError(double prior, PaymentAudit liable, PaymentAudit nonliable)
    {
        if (!double.IsFinite(prior) || prior < 0 || prior > 1 ||
            !double.IsFinite(liable.MeanPayment) || !double.IsFinite(nonliable.MeanPayment) ||
            liable.MeanPayment < 0 || liable.MeanPayment > 1 || nonliable.MeanPayment < 0 || nonliable.MeanPayment > 1)
            throw new InvalidDataException("Outcome error requires a valid truth prior and payments in [0,1].");
        double underpayment = prior * (1 - liable.MeanPayment);
        double overpayment = (1 - prior) * nonliable.MeanPayment;
        return new(prior, liable, nonliable, underpayment, overpayment, underpayment + overpayment);
    }

    public static PaymentAudit GrossPayment(IReadOnlyDictionary<string, string> row)
    {
        double settles = N(row, "Settles"), noAnswer = N(row, "DDoesntAnswer"),
            defaults = N(row, "DDefaults"), abandons = N(row, "PAbandons"),
            mutual = N(row, "BothReadyToGiveUp"), trial = N(row, "Trial"),
            answer = N(row, "DAnswers"), win = N(row, "P Wins");
        foreach (double probability in new[] { settles, noAnswer, defaults, abandons, mutual, trial, answer, win })
            if (!double.IsFinite(probability) || probability < 0 || probability > 1 + 1e-5)
                throw new InvalidDataException("Invalid saved disposition probability.");
        bool added = false;
        double residual = answer - settles - defaults - abandons - trial;
        if (Math.Abs(residual) > 1e-5)
        {
            if (Math.Abs(residual - mutual) > 1e-5)
                throw new InvalidDataException("Cannot reconcile mutual give-up allocation.");
            defaults += .5 * mutual;
            added = true;
        }
        // A missing mean is appropriate only if the settlement event has zero probability.
        double? mean = settles == 0 ? null : N(row, "ValIfSettled");
        if (mean is < 0 or > 1 || win > trial + 1e-5)
            throw new InvalidDataException("Expected fixed damages and settlements in [0,1].");
        double payment = settles * mean.GetValueOrDefault() + noAnswer + defaults + win;
        if (payment < 0 || payment > 1 + 1e-5)
            throw new InvalidDataException("Gross payment outside the model's support.");
        return new(settles, mean, noAnswer, defaults, win, mutual, added, payment);
    }

    private static Dictionary<string, string> Detail(Dictionary<string, string>[] rows, string name, string filter)
    {
        var selected = rows.Where(row => row.GetValueOrDefault("OptionSet") == name && row.GetValueOrDefault("Filter") == filter).ToArray();
        if (selected.Length != 1) throw new InvalidDataException($"Expected one {filter} detail row for {name}.");
        return selected[0];
    }
    private static void Close(double x, double y, double tolerance, string label)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x - y) > tolerance)
            throw new InvalidDataException($"{label}: {x:R} versus {y:R}.");
    }

    public static string FeeLabel(IReadOnlyDictionary<string, string> row)
    {
        double multiplier = N(row, "Fee Shifting Multiplier");
        if (multiplier == 0) return Regimes[0];
        if (multiplier != 1 || row["Fee Regime"] != "British")
            throw new InvalidDataException("This comparison requires American or full loser-pays fee shifting.");
        bool complete = row.GetValueOrDefault("Fees After Nonanswer") == "true";
        string trigger = row.GetValueOrDefault("Fee Shifting Trigger", "");
        if (complete && trigger == LitigGameCorrelatedSignalsArticleLauncher.ExitFeeTriggerLabel) return Regimes[2];
        if (complete || trigger != "" && trigger != "Trial only")
            throw new InvalidDataException("Unrecognized fee trigger; do not label a partial exit rule as complete.");
        return Regimes[1];
    }

    // Pair the existing RN/RA specifications while keeping other extensions distinct.
    // Additional families also require an explicit display name in ArticleResultsLayout.
    public static string Family(IReadOnlyDictionary<string, string> row)
    {
        string spec = row["OptionSetName"].Split("__")[0];
        if (!spec.StartsWith("Specification-", StringComparison.Ordinal))
            throw new InvalidDataException("Missing specification identity.");
        spec = spec["Specification-".Length..];
        spec = spec switch { "ModerateRiskAversion" => "Baseline", "LowNoiseModerateRiskAversion" => "LowNoise", _ => spec };
        string slug = Regex.Replace(spec, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant();
        if (!Regex.IsMatch(slug, "^[a-z0-9-]+$")) throw new InvalidDataException("Unsafe specification filename.");
        int offers = checked((int)N(row, "Number of Offers"));
        return slug + (offers == 10 ? "" : "-offers-" + offers.ToString(CultureInfo.InvariantCulture));
    }

    public static void ValidateGroup(Dictionary<string, string>[] rows)
    {
        // Every displayed row must be a different preference/rule treatment of the same design.
        var identity = rows.Select(r => (N(r, "CARA Alpha"), FeeLabel(r))).ToArray();
        if (identity.Distinct().Count() != rows.Length)
            throw new InvalidDataException("Duplicate preference/fee-rule case in a welfare comparison.");
        string[] invariantColumns = ["Costs Multiplier", "Number of Offers", "Number of Signals",
            "Number of Court Signals", "Probability Truly Liable", "Party Signal Sigma", "Court Signal Sigma",
            "Proportion of Costs at Beginning", "Allow Abandon and Defaults", "Filing and Answering",
            "Signal Structure", "Quality Distribution", "Quality-Truth Link", "Relative Costs"];
        foreach (string column in invariantColumns)
        {
            string Normalize(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? F(n) : value;
            if (rows.Select(r => Normalize(r.GetValueOrDefault(column, ""))).Distinct().Count() != 1)
                throw new InvalidDataException("Mismatched comparison primitive: " + column);
        }
    }

    private static Request ReadRequest(string path) => JsonSerializer.Deserialize<Request>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException("Empty welfare-exhibits request.");

    public static string OutputDirectory(string requestPath) => Path.GetFullPath(ReadRequest(requestPath).OutputDirectory,
        Path.GetDirectoryName(Path.GetFullPath(requestPath)));

    public static Generation Prepare(string requestPath, string outputOverride = null)
    {
        requestPath = Path.GetFullPath(requestPath);
        var request = ReadRequest(requestPath);
        if (request.Inputs == null || request.Inputs.Length == 0) throw new InvalidDataException("Supply saved result sources.");
        string Resolve(string path) => Path.GetFullPath(path, Path.GetDirectoryName(requestPath));
        string output = Path.GetFullPath(outputOverride ?? Resolve(request.OutputDirectory));
        var inputs = new List<string> { requestPath };
        var rows = new List<Dictionary<string, string>>();
        var audits = new List<CaseAudit>();
        foreach (var input in request.Inputs)
        {
            string numerical = Resolve(input.NumericalResultsCsv), directory = Resolve(input.IndividualDirectory);
            inputs.Add(numerical);
            var saved = PublicationFigures.ReadCsv(numerical);
            if (saved.Any(r => r.GetValueOrDefault("Filter") == "All" && r.GetValueOrDefault("Equilibrium Type") != "Only Eq"))
                throw new InvalidDataException("Multiple-equilibrium summaries need an explicit profile selection.");
            foreach (var original in saved.Where(r => r.GetValueOrDefault("Filter") == "All"))
            {
                var row = new Dictionary<string, string>(original, StringComparer.OrdinalIgnoreCase);
                string name = row["OptionSetName"];
                if (Path.GetFileName(name) != name) throw new InvalidDataException("Unsafe option-set filename.");
                string detailPath = Path.Combine(directory, input.ReportPrefix + " " + name + ".csv");
                var detail = PublicationFigures.ReadCsv(detailPath);
                var all = Detail(detail, name, "All");
                var liable = Detail(detail, name, "Truly Liable");
                var nonliable = Detail(detail, name, "Truly Not Liable");
                var options = ArticleWorkedPathExtraction.CreateOptions(name);
                var grid = PublicationFigures.GridForOptionSet(name);
                if (options.NumDamagesStrengthPoints != 1 || options.DamagesMax != 1 || options.DamagesMultiplier != 1 ||
                    options.NumPotentialBargainingRounds != 1 || !options.BargainingRoundsSimultaneous ||
                    grid.Offers.Any(offer => offer < 0 || offer > 1))
                    throw new InvalidDataException("Gross error requires fixed unit damages and payments in [0,1].");
                Close(N(liable, "TrulyLiable"), 1, 1e-10, "liable filter");
                Close(N(nonliable, "TrulyLiable"), 0, 1e-10, "nonliable filter");
                foreach (string key in new[] { "P Files", "D Answers", "Trial" })
                    Close(N(all, key), N(row, key), 1e-5, "summary/detail " + key);
                Close(N(liable, "False-"), N(row, CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn), 1e-5, "P burden");
                Close(N(nonliable, "False+"), N(row, CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn), 1e-5, "nonliable D burden");
                Close(N(liable, "False+"), N(row, CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn), 1e-5, "liable D burden");
                var calculation = OutcomeError(N(row, "Probability Truly Liable"), GrossPayment(liable), GrossPayment(nonliable));
                var aggregate = GrossPayment(all);
                double weighted = calculation.Prior * calculation.Liable.MeanPayment + (1 - calculation.Prior) * calculation.Nonliable.MeanPayment;
                Close(aggregate.MeanPayment, N(row, "Liability Transfer to Plaintiff"), 1e-5, "gross transfer from dispositions");
                Close(aggregate.MeanPayment, weighted, 3e-5, "truth-weighted gross transfer (rounded reports)");
                // Read stage costs from each extension's options, rather than imposing baseline timing.
                if (options.PerPartyCostsLeadingUpToBargainingRound != 0 || options.RoundSpecificBargainingCosts != null ||
                    options.PFilingCost_PortionSavedIfDDoesntAnswer != 0)
                    throw new InvalidDataException("Stage expenditure check needs extension for additional bargaining/saved filing costs.");
                Close(N(row, "Real Litigation Costs"), options.CostsMultiplier *
                    (options.PFilingCost * N(row, "P Files") + options.DAnswerCost * N(row, "D Answers") +
                     (options.PTrialCosts + options.DTrialCosts) * N(row, "Trial")), 1e-5, "stage costs");
                row[Measures[0]] = F(calculation.Prior * N(liable, "False-"));
                row[Measures[1]] = F((1 - calculation.Prior) * N(nonliable, "False+"));
                row[Measures[2]] = F(calculation.Prior * N(liable, "False+"));
                Close(Measures.Take(3).Sum(m => N(row, m)), N(row, CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn), 1e-4, "net monetary components");
                row[ErrorColumn] = F(calculation.Error);
                row["Gross error meritorious underpayment"] = F(calculation.MeritoriousUnderpayment);
                row["Gross error nonliable payment"] = F(calculation.NonliablePayment);
                row["Comparison Fee Rule"] = FeeLabel(row);
                row["Comparison Family"] = Family(row);
                inputs.Add(detailPath);
                rows.Add(row);
                audits.Add(new(name, detailPath, Source(detailPath).Sha256, all, liable, nonliable, calculation, aggregate.MeanPayment - weighted));
            }
        }
        if (rows.Count == 0 || rows.Select(r => r["OptionSetName"]).Distinct().Count() != rows.Count)
            throw new InvalidDataException("No results, or duplicate saved option sets across inputs.");
        rows = rows.OrderBy(r => r["Comparison Family"]).ThenBy(r => N(r, "Costs Multiplier"))
            .ThenBy(r => N(r, "CARA Alpha")).ThenBy(r => Array.IndexOf(Regimes, r["Comparison Fee Rule"])).ToList();
        var sourceRecords = inputs.Distinct().Select(Source).ToArray();
        string csvText = Csv(rows);
        var csvSource = new PublicationFigures.Source(ArticleResultsLayout.Source(output, "welfare-outcomes", ".csv"),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csvText))));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var exhibits = new List<Exhibit>();
        var coverage = new StringBuilder("| Family | Cost | Cases | Available fee rules |\n|---|---:|---:|---|\n");
        foreach (var group in rows.GroupBy(r => (Family: r["Comparison Family"], Cost: N(r, "Costs Multiplier"))))
        {
            var selected = group.ToArray();
            ValidateGroup(selected);
            var riskGroups = selected.GroupBy(r => N(r, "CARA Alpha")).OrderBy(g => g.Key).ToArray();
            var views = riskGroups.Select(g => (Risk: ArticleResultsLayout.Risk(g.Key), Rows: g.ToArray())).ToList();
            if (riskGroups.Length > 1) views.Add((ArticleResultsLayout.RiskComparison, selected));
            foreach (var view in views)
            {
            selected = view.Rows;
            string directory = ArticleResultsLayout.Aggregate(output, group.Key.Family, view.Risk);
            string[] names = selected.Select(r => r["OptionSetName"]).ToArray();
            var table = BuildTable(group.Key.Cost, selected, sourceRecords, audits.Where(a => names.Contains(a.OptionSet)).ToArray());
            string tableFile = ArticleResultsLayout.Source(directory, table.Stem, ".tex");
            files.Add(tableFile, PublicationTables.Standalone("", PublicationTables.RenderFragment(table)));
            files.Add(Path.ChangeExtension(tableFile, ".json"), JsonSerializer.Serialize(table, Json));
            exhibits.Add(new(group.Key.Family, group.Key.Cost, "welfare-outcomes", tableFile, names));
            var bars = selected.Select(r => new PublicationFigures.DispositionBar(Preference(r), r["Comparison Fee Rule"], r["OptionSetName"],
                PublicationFigures.CategoryColumns.Select(c => N(r, c)).ToArray(), PublicationFigures.CategoryColumns.Sum(c => N(r, c)),
                N(r, CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn), N(r, "Trial"))).ToArray();
            foreach (var bar in bars) PublicationFigures.ValidateDisposition(bar.Values, bar.Trial);
            var requestSource = Source(requestPath);
            var data = new PublicationFigures.DispositionData(new(requestSource.Path, requestSource.Sha256),
                csvSource, PublicationFigures.CategoryLabels, bars);
            string chartFile = ArticleResultsLayout.Source(directory, ArticleResultsLayout.Cost(group.Key.Cost) + "-dispositions", ".tex");
            files.Add(chartFile, PublicationFigures.RenderDispositions(data, groupHeadingsAbove: true));
            files.Add(Path.ChangeExtension(chartFile, ".json"), JsonSerializer.Serialize(new { Data = data, Sources = sourceRecords, Rows = selected }, Json));
            exhibits.Add(new(group.Key.Family, group.Key.Cost, "dispositions", chartFile, names));
            }
            coverage.AppendLine($"| {group.Key.Family} | {F(group.Key.Cost)} | {selected.Length} | {string.Join(", ", selected.Select(r => r["Comparison Fee Rule"]).Distinct())} |");
        }
        files.Add(ArticleResultsLayout.Source(output, "welfare-outcomes", ".csv"), csvText);
        files.Add(ArticleResultsLayout.Source(output, "calculation-audit", ".json"), JsonSerializer.Serialize(new { Description = ErrorDescription, Cases = audits }, Json));
        files.Add(Path.Combine(output, "README.md"), Readme(coverage.ToString(), rows.Count, exhibits.Count));
        return new(output, files.ToDictionary(f => f.Key, f => f.Value.Replace("\r\n", "\n"), StringComparer.OrdinalIgnoreCase),
            exhibits.ToArray(), sourceRecords, rows.Count);
    }

    private static string Preference(IReadOnlyDictionary<string, string> row) => N(row, "CARA Alpha") == 0
        ? "Risk neutral" : "Risk averse" + (N(row, "CARA Alpha") == 2 ? "" : " (alpha " + F(N(row, "CARA Alpha")) + ")");

    public static PublicationTables.Table BuildTable(double cost, Dictionary<string, string>[] rows,
        PublicationTables.Source[] sources, CaseAudit[] audits)
    {
        var panels = rows.GroupBy(r => N(r, "CARA Alpha")).OrderBy(g => g.Key).Select(group =>
            new PublicationTables.Panel(Preference(group.First()), "@{}Xccccc@{}",
                ["Fee rule", @"\shortstack{Meritorious-P\\shortfall}", @"\shortstack{Nonliable-D\\burden}",
                 @"\shortstack{Liable-D\\excess burden}", @"\shortstack{Gross outcome\\error}", @"\shortstack{Total\\expenditures}"],
                group.Select(row => new PublicationTables.Row(new[] { new PublicationTables.Cell(row["Comparison Fee Rule"]) }
                    .Concat(Measures.Select(key => new PublicationTables.Cell(N(row, key).ToString("0.000", CultureInfo.InvariantCulture), N(row, key), key))).ToArray())).ToArray())).ToArray();
        return new(ArticleResultsLayout.Cost(cost) + "-welfare-outcomes", "Welfare outcomes", panels, "", "",
            sources, [], new { CostMultiplier = cost, Rows = rows, Audits = audits });
    }

    public const string ErrorDescription = "Gross outcome error is E[|R-T|], averaged over all potential disputes. R is the base payment before legal costs and separately awarded fee transfers; T is true liability and damages equal one. Since payments lie in [0,1], error equals pi(1-E[R|T=1])+(1-pi)E[R|T=0]. Conditional means are intermediate calculations, not conditional headline outcomes. The configured truth prior is also used for the three net-burden contributions. Source reports are rounded. Fee rules still affect error through equilibrium behavior. The five measures are distinct, not additive welfare components.";

    private static string Csv(List<Dictionary<string, string>> rows)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture) { NewLine = "\n" });
        string[] columns = rows.SelectMany(r => r.Keys).Distinct().ToArray();
        foreach (string c in columns) csv.WriteField(c);
        csv.NextRecord();
        foreach (var r in rows) { foreach (string c in columns) csv.WriteField(r.GetValueOrDefault(c, "")); csv.NextRecord(); }
        csv.Flush();
        return writer.ToString();
    }

    private static string Readme(string coverage, int cases, int exhibits) => $"""
        # Welfare outcomes and dispositions

        {cases} saved cases; {exhibits / 2} specification/cost/risk views, each with a separate welfare-outcomes table and disposition chart.
        Baseline contains the three fee rules crossed with risk neutrality/risk aversion. Each available risk has its own folder; Risk Comparison combines preferences. Figures and tables share these folders, one pair per cost. Sources contains editable TeX and exact data; PDF/PNG sit directly in the risk folder.
        Extensions receive exactly the same formats for their available cases. Absence of complete fee-shifting results
        in an extension is not imputed; the inventory below records coverage. Generating a saved extension does not
        select it for the manuscript (excluded participation restrictions are not part of routine production).

        Each PDF contains only its table or chart. Cost multipliers and extension names are in paths/filenames, not in the artwork.
        Table headings and numeric cells are centered; fee labels are left aligned. There are no titles, captions, notes or combined packets.
        PDF and PNG are insertion-ready; TeX is standalone and can be compiled directly. JSON and CSV retain the numerical data.
        Captions and explanatory text belong in the manuscript or here.

        ## Definitions

        American: each side bears its own legal expenses. Trial Fee-Shifting: loser-pays after trial.
        Complete Fee-Shifting: loser-pays after trial, initial nonanswer, and later unilateral exit.
        Risk averse denotes symmetric CARA alpha 2 unless a different alpha is explicitly identified.
        All displayed measures and disposition shares average over all potential disputes, including unfiled disputes.
        The three net monetary measures are prior-weighted meritorious-plaintiff recovery shortfall,
        nonliable-defendant burden, and liable-defendant excess burden above deserved damages. They include legal costs and fee transfers.
        Total expenditures count real resource costs and exclude transfers. Values are in damages units and shown to three decimals.

        {ErrorDescription}

        Settlements use their truth-specific probabilities and means; an undefined zero-probability mean remains null in the audit.
        Mutual give-up is allocated once, half to abandonment and half to default. Disposition bars preserve saved probabilities
        without renormalization. Trial wins/losses concern court findings, not true liability.
        The checks reconcile saved summary/detail records, truth-weighted transfers, monetary components, stage costs, and disposition totals.

        ## Regeneration

        After equilibrium determination and report generation, run the normal LitigCharts diagram command with the article configuration:

        ```text
        dotnet run --project LitigCharts -c Release -- diagrams results --config <article>/article-diagrams.json
        ```

        The same exhibits are included in `diagrams all`, `diagrams publication`, and `diagrams dispositions`.
        Use `diagrams welfare-outcomes` to regenerate just these tables and charts. `--list` writes nothing;
        `--sources-only` writes TeX/data; `--compile-only` recompiles the inventoried TeX without reading reports.
        `--output-root` and `--jobs` work as for other diagrams. No command determines equilibria or changes reports.
        The request's Inputs list points to numerical summaries and matching individual reports. Every saved specification
        and cost in those sources is discovered automatically. New specification families also require a display-name mapping in ArticleResultsLayout.
        A new report batch needs one Inputs entry; it does not need separate figure or table selections.
        Changing any primitive beyond preferences/fee rule within a group is rejected rather than silently pooled.

        ## Available comparisons

        {coverage}
        """;

    public static void Write(Generation generation)
    {
        foreach (var file in generation.Files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.Key));
            WriteText(file.Key, file.Value);
        }
    }

    public static void WriteInventory(Generation generation, bool compiled)
    {
        var artifacts = generation.Exhibits.SelectMany(e => compiled
            ? new[] { e.TexFile, ArticleResultsLayout.RenderedArtifact(e.TexFile, ".pdf"), ArticleResultsLayout.RenderedArtifact(e.TexFile, ".png") }
            : new[] { e.TexFile });
        WriteText(ArticleResultsLayout.Source(generation.OutputDirectory, "exhibit-inventory", ".json"), JsonSerializer.Serialize(new
        {
            Schema = "welfare-outcomes-v2", GeneratedUtc = DateTime.UtcNow, Compiled = compiled, generation.Cases,
            generation.Inputs, generation.Exhibits, GeneratorAssembly = Source(typeof(WelfareOutcomeExhibits).Assembly.Location),
            Outputs = generation.Files.Keys.Concat(artifacts).Distinct().Select(Source).ToArray()
        }, Json));
    }

    public static string[] ExistingSources(string requestPath)
    {
        string output = OutputDirectory(requestPath);
        using var inventory = JsonDocument.Parse(File.ReadAllText(ArticleResultsLayout.Source(output, "exhibit-inventory", ".json")));
        return inventory.RootElement.GetProperty("Exhibits").EnumerateArray()
            .Select(e => e.GetProperty("TexFile").GetString()).ToArray();
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length is < 2 or > 3 || args[0] != "--request" || args.Length == 3 && args[2] != "--sources-only")
                throw new ArgumentException("Usage: welfare-outcomes --request <request.json> [--sources-only]");
            bool sourcesOnly = args.Length == 3;
            var generation = Prepare(args[1]);
            Write(generation);
            if (!sourcesOnly) await DiagramCompiler.CompileAllAsync(generation.Exhibits.Select(e => e.TexFile).ToArray(), new ArticleDiagramCommand.Configuration(), 4);
            WriteInventory(generation, !sourcesOnly);
            Console.WriteLine($"Generated {generation.Exhibits.Length} separate exhibits for {generation.Cases} saved cases.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
