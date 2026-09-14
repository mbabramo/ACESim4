using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
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

/// <summary>Publication tables from saved reports only. No equilibrium loading or solving.</summary>
public static class PublicationTables
{
    public sealed record Baseline(string Regime, string OptionSetName, string DetailedReport);
    public sealed record Contrast(string Label, string[] OptionSetNames);
    public sealed record Request(string NumericalResultsCsv, string EquilibriumRangesCsv,
        string EquilibriumOutcomesCsv, string ProductionManifest, string ModelRepository,
        string OutputDirectory, Baseline[] Baselines, Contrast[] Contrasts,
        Contrast[] ModelForms, Contrast[] OfferGrids);
    public sealed record Source(string Path, string Sha256);
    public sealed record SourceRow(string Id, string Path, int CsvRecordNumber, Dictionary<string, string> Values);
    public sealed record Evidence(string Row, string Column, string RawValue);
    public sealed record Cell(string Latex, double? Value = null, string Derivation = null, Evidence[] Inputs = null);
    public sealed record Row(Cell[] Cells);
    public sealed record Panel(string Title, string Columns, string[] Headings, Row[] Rows);
    public sealed record Table(string Stem, string Title, Panel[] Panels, string Notes, string Caption,
        Source[] Sources, SourceRow[] SourceRows, object ModelSettings = null);

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    public static readonly string[] MoneyColumns =
    [
        CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn,
        CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn,
        CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn,
        CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn, "Real Litigation Costs"
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            string requestPath = null;
            bool sourcesOnly = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--request" && i + 1 < args.Length) requestPath = args[++i];
                else if (args[i] == "--sources-only") sourcesOnly = true;
                else throw new ArgumentException("Usage: tables --request <publication-tables.json> [--sources-only]");
            }
            if (requestPath == null) throw new ArgumentException("A --request file is required.");
            requestPath = Path.GetFullPath(requestPath);
            var request = ReadRequest(requestPath);
            // Validate the complete selection before writing any artifacts.
            var tables = Generate(requestPath);
            string output = Path.GetFullPath(request.OutputDirectory, Path.GetDirectoryName(requestPath));
            Directory.CreateDirectory(output);
            foreach (var table in tables)
            {
                string stem = Path.Combine(output, table.Stem);
                string fragment = RenderFragment(table);
                File.WriteAllText(stem + ".tex", fragment, new UTF8Encoding(false));
                File.WriteAllText(stem + ".json", JsonSerializer.Serialize(table, JsonOptions));
                File.WriteAllText(stem + ".txt", table.Title + "\n\n" + table.Caption + "\n\n" +
                    "Source records, exact reported values, formulas and SHA-256 hashes: " + table.Stem + ".json\n");
                if (!sourcesOnly)
                {
                    // Wrapper is disposable; the delivered .tex remains an input-ready fragment.
                    var temp = Directory.CreateTempSubdirectory("acesim-table-");
                    bool success = false;
                    try
                    {
                        string source = Path.Combine(temp.FullName, table.Stem + ".tex");
                        File.WriteAllText(source, Standalone(table.Title, fragment), new UTF8Encoding(false));
                        await DiagramCompiler.CompileAsync(source, new ArticleDiagramCommand.Configuration());
                        File.Copy(Path.ChangeExtension(source, ".pdf"), stem + ".pdf", true);
                        File.Copy(Path.ChangeExtension(source, ".png"), stem + ".png", true);
                        success = true;
                    }
                    finally
                    {
                        if (success && temp.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar).Equals(
                            Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                            && !temp.Attributes.HasFlag(FileAttributes.ReparsePoint)) temp.Delete(true);
                        else if (!success) Console.Error.WriteLine("Table diagnostics retained in " + temp.FullName);
                    }
                }
                Console.WriteLine(table.Stem + (sourcesOnly ? " (sources)" : " (PDF, PNG, TeX, JSON, TXT)"));
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static Request ReadRequest(string path) => JsonSerializer.Deserialize<Request>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException("Empty table request.");

    public static Table[] Generate(string requestPath)
    {
        requestPath = Path.GetFullPath(requestPath);
        var request = ReadRequest(requestPath);
        if (request.Baselines == null || request.Baselines.Length != 2 ||
            !request.Baselines.Select(b => b.Regime).SequenceEqual(new[] { "American", "British" }))
            throw new InvalidDataException("The publication comparison requires American and British baselines, in that order.");
        return new Builder(request, requestPath).Build();
    }

    public static Dictionary<string, string> SelectSummary(IEnumerable<Dictionary<string, string>> rows, string name)
    {
        var matches = rows.Where(r => r.GetValueOrDefault("OptionSetName") == name &&
            r.GetValueOrDefault("Filter") == "All" && r.GetValueOrDefault("Equilibrium Type") == "Only Eq" &&
            string.IsNullOrEmpty(r.GetValueOrDefault("GroupName"))).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"Expected one All/Only Eq/ungrouped row for {name}; found {matches.Length}.");
        return matches[0];
    }

    public static double Number(IReadOnlyDictionary<string, string> row, string column)
    {
        if (!row.TryGetValue(column, out var raw) || !double.TryParse(raw, NumberStyles.Float, Invariant, out double value) || !double.IsFinite(value))
            throw new InvalidDataException("Missing or non-finite value: " + column);
        return value;
    }

    public static double? ConditionalRate(double joint, double conditioning)
    {
        if (!double.IsFinite(joint) || !double.IsFinite(conditioning) || joint < 0 || conditioning < 0 ||
            conditioning > 1 + 1e-5 || joint > conditioning + 1e-5)
            throw new InvalidDataException("Invalid conditional-rate inputs.");
        return conditioning == 0 ? null : joint / conditioning;
    }

    public static string Format(double value, string format)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("Non-finite table value.");
        double scaled = format == "percent" ? value * 100 : value;
        int digits = format == "percent" ? 2 : 3;
        if (Math.Abs(scaled) < .5 * Math.Pow(10, -digits)) scaled = 0; // Suppress display-only negative zero.
        return format switch
        {
            "integer" => value.ToString("0", Invariant),
            "scientific" => value == 0 ? "0" : "$" + value.ToString("0.00E+0", Invariant).Replace("E", @"\times10^{") + "}$",
            "delta" => scaled.ToString("+0.000;-0.000;0.000", Invariant),
            "percent" => scaled.ToString("0.00", Invariant),
            _ => scaled.ToString("0.000", Invariant)
        };
    }

    private static Cell Text(string tex) => new(tex);
    private static Row R(params string[] cells) => new(cells.Select(Text).ToArray());
    private static string Escape(string text) => text.Replace("&", @"\&").Replace("%", @"\%").Replace("_", @"\_").Replace("#", @"\#");

    private sealed class Builder(Request request, string requestPath)
    {
        private readonly Dictionary<string, Dictionary<string, string>[]> cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SourceRow> used = new();
        private readonly Dictionary<string, Source> sources = new(StringComparer.OrdinalIgnoreCase);
        private string Resolve(string path) => Path.GetFullPath(path, Path.GetDirectoryName(requestPath));
        private Source Fingerprint(string path)
        {
            path = Resolve(path);
            if (!sources.TryGetValue(path, out var source))
            {
                using var stream = File.OpenRead(path);
                source = new(Path.GetRelativePath(Path.GetDirectoryName(requestPath), path).Replace('\\', '/'),
                    Convert.ToHexString(SHA256.HashData(stream)));
                sources.Add(path, source);
            }
            return source;
        }
        private Dictionary<string, string>[] Read(string path)
        {
            path = Resolve(path);
            if (!cache.TryGetValue(path, out var data)) cache[path] = data = PublicationFigures.ReadCsv(path);
            return data;
        }
        private SourceRow Use(string path, Dictionary<string, string> values)
        {
            var all = Read(path);
            int record = Array.IndexOf(all, values) + 2; // Header is CSV record 1, not necessarily physical line 1.
            if (record < 2) throw new InvalidDataException("Row is not from the claimed source.");
            var file = Fingerprint(path);
            string id = file.Path + "#record=" + record;
            var row = new SourceRow(id, file.Path, record, values);
            used[id] = row;
            return row;
        }
        private SourceRow Summary(string name, string expectedRegime)
        {
            var row = Use(request.NumericalResultsCsv, SelectSummary(Read(request.NumericalResultsCsv), name));
            if (row.Values["Fee Regime"] != expectedRegime) throw new InvalidDataException("Wrong fee regime for " + name);
            PublicationFigures.ValidateDisposition(PublicationFigures.CategoryColumns.Select(c => Number(row.Values, c)).ToArray(), Number(row.Values, "Trial"));
            Close(Number(row.Values, "Net Outcome Fidelity Loss"), Number(row.Values, "Defendant Excess Net Monetary Burden") +
                Number(row.Values, "Plaintiff Net Recovery Shortfall"), 1e-5, "fidelity accounting");
            return row;
        }
        private SourceRow Detail(Baseline baseline, string filter)
        {
            var matches = Read(baseline.DetailedReport).Where(r => r.GetValueOrDefault("OptionSet") == baseline.OptionSetName &&
                r.GetValueOrDefault("Filter") == filter).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Missing/duplicate truth-conditioned report row.");
            return Use(baseline.DetailedReport, matches[0]);
        }
        private Cell Cell(SourceRow row, string column, string format = "money")
        {
            double value = Number(row.Values, column);
            return new(Format(value, format), value, "Reported value (display rounding only)", [Input(row, column)]);
        }
        private static Evidence Input(SourceRow row, string column) => new(row.Id, column, row.Values[column]);
        private Cell Ratio(SourceRow row, string numerator, string denominator)
        {
            double? value = ConditionalRate(Number(row.Values, numerator), Number(row.Values, denominator));
            return new(value.HasValue ? Format(value.Value, "percent") : "---", value,
                numerator + " / " + denominator + "; undefined when denominator is zero",
                [Input(row, numerator), Input(row, denominator)]);
        }
        private Cell Difference(SourceRow row, SourceRow baseline, string column)
        {
            double delta = Number(row.Values, column) - Number(baseline.Values, column);
            return new(Format(delta, "delta"), delta, "Scenario minus same-regime baseline, before display rounding",
                [Input(row, column), Input(baseline, column)]);
        }
        private Row Pair(string label, SourceRow[] rows, string column, string format = "money") =>
            new(new[] { Text(label) }.Concat(rows.Select(r => Cell(r, column, format))).ToArray());
        private Table Finish(string stem, string title, Panel[] panels, string notes, string caption, object settings = null)
        {
            Fingerprint(requestPath);
            Fingerprint(request.ProductionManifest);
            var result = new Table(stem, title, panels, notes, caption, sources.Values.ToArray(), used.Values.ToArray(), settings);
            sources.Clear(); used.Clear();
            return result;
        }

        public Table[] Build() => [Primitives(), BaselineOutcomes(), Comparisons(), Sensitivity()];

        private Table Primitives()
        {
            var baselines = request.Baselines.Select(b => Summary(b.OptionSetName, b.Regime)).ToArray();
            var options = request.Baselines.Select(b => ArticleWorkedPathExtraction.CreateOptions(b.OptionSetName)).ToArray();
            for (int i = 0; i < options.Length; i++)
            {
                var o = options[i];
                foreach (string key in new[] { "Party Signal Sigma", "Court Signal Sigma", "Number of Offers", "Number of Signals", "Number of Court Signals", "CARA Alpha", "Costs Multiplier", "Quadrature Order" })
                    Close(Number(baselines[i].Values, key), Convert.ToDouble(o.VariableSettings[key], Invariant), 1e-10, "saved/current option " + key);
                if (o.NumDamagesStrengthPoints != 1 || o.DamagesMax != 1 || o.PInitialWealth != 10 || o.DInitialWealth != 10 ||
                    o.PFilingCost != .15 || o.DAnswerCost != .15 || o.PTrialCosts != .15 || o.DTrialCosts != .15 ||
                    o.PerPartyCostsLeadingUpToBargainingRound != 0 || o.NumPotentialBargainingRounds != 1 ||
                    !o.BargainingRoundsSimultaneous || !o.PredeterminedAbandonAndDefaults || !o.AllowAbandonAndDefaults ||
                    o.SkipFileAndAnswerDecisions || o.IncludeEndpointsForOffers || o.LoserPaysAfterAbandonment)
                    throw new InvalidDataException("Essential model settings changed; revise the primitives table deliberately.");
            }
            string[] codePaths = ["ACESimBase/Games/LitigGame/LitigGameCorrelatedSignalsArticleLauncher.cs",
                "ACESimBase/Games/LitigGame/Options/LitigGameOptionsGenerator.cs", "ACESimBase/Games/LitigGame/LitigGameProgress.cs",
                "ACESimBase/Games/LitigGame/DisputeGeneration/LitigGameUniformQualityDisputeGenerator.cs"];
            foreach (string path in codePaths) Fingerprint(Path.Combine(Resolve(request.ModelRepository), path));
            var rows = new List<Row>
            {
                R("Common merits", "Shared, unobserved case quality", @"$Q\sim U[0,1]$"),
                R("True liability", "Normative liability benchmark", @"$T\mid Q\sim\mathrm{Bernoulli}(Q)$;\newline $\Pr(T=1)=0.5$"),
                R("Private information", "One noisy merits signal per party", "10 bins each"),
                R("Signal noise", "Party and court noise parameters", @"$\sigma_P=\sigma_D=\sigma_C=0.20$"),
                R("Adjudication", "Unobserved court signal determines trial winner", "2 court-signal bins"),
                R("Damages", "Fixed award if plaintiff wins", @"$d=1$"),
                R("Initial wealth", "Wealth before litigation", @"$w_P=w_D=10$"),
                R("Filing / answering", "Plaintiff filing and defendant answering costs", "0.15 per acting party"),
                R("Trial costs", "Additional cost if trial occurs", "0.15 per party"),
                R("Bargaining costs", "Additional cost of making demands/offers", "0"),
                R("Participation and exit", "Endogenous filing/answering; exit precommitted before offers, effective after bargaining fails", "P may abandon; D may default"),
                R("Bargaining", "One simultaneous demand/offer round; settlement at midpoint if demand does not exceed offer", @"10 actions: $0.05,0.15,\ldots,0.95$"),
                R("Preferences", "Expected monetary payoff", @"Risk neutral ($\alpha=0$)"),
                R("Fee regimes", "American: own costs. British: loser pays both parties' costs at trial", "No fee shifting on exit"),
                R("Integration", "Integrate over continuous merits; not a 64-state strategic game", "64-point Gauss--Legendre")
            };
            var settings = options.Select(o => new { o.Name, o.VariableSettings, o.PInitialWealth, o.DInitialWealth,
                o.DamagesMax, o.NumDamagesStrengthPoints, o.PFilingCost, o.DAnswerCost, o.PTrialCosts, o.DTrialCosts,
                o.CostsMultiplier, o.NumOffers, o.IncludeEndpointsForOffers, o.LoserPays, o.LoserPaysMultiple,
                o.LoserPaysAfterAbandonment, o.PredeterminedAbandonAndDefaults, o.NumPotentialBargainingRounds }).ToArray();
            return Finish("model-primitives", "Essential model primitives",
                [new("", @"@{}>{\raggedright\arraybackslash}p{3.1cm}X>{\raggedright\arraybackslash}p{4.5cm}@{}", ["Primitive", "Interpretation", "Baseline"], rows.ToArray())],
                @"Monetary amounts are in units of damages. All baseline costs use multiplier 1. Signals are conditionally independent given exact $Q$; parties do not observe $Q$, $T$, or the court signal before bargaining.",
                "Essential primitives for the cost-1, ten-offer baseline. Values were checked against saved result metadata and the model option constructors; additional settings are recorded in JSON. Detailed signal, information-set and accounting definitions belong in Appendix A. The baseline is not calibrated to a particular docket.", settings);
        }

        private Table BaselineOutcomes()
        {
            var baseline = request.Baselines.Select(b => Summary(b.OptionSetName, b.Regime)).ToArray();
            var all = request.Baselines.Select(b => Detail(b, "All")).ToArray();
            var liable = request.Baselines.Select(b => Detail(b, "Truly Liable")).ToArray();
            var nonliable = request.Baselines.Select(b => Detail(b, "Truly Not Liable")).ToArray();
            for (int i = 0; i < 2; i++)
            {
                foreach (string column in new[] { "P Files", "D Answers", "Trial" })
                    Close(Number(baseline[i].Values, column), Number(all[i].Values, column), 1e-5, "summary/detail " + column);
                double truthMass = Number(all[i].Values, "TrulyLiable");
                foreach (string column in new[] { "PFiles", "DAnswers" })
                    Close(Number(all[i].Values, column), truthMass * Number(liable[i].Values, column) +
                        (1 - truthMass) * Number(nonliable[i].Values, column), 2e-5, "truth-conditioned " + column);
                Close(Number(baseline[i].Values, MoneyColumns[0]), Number(liable[i].Values, "False-"), 1e-5, "P shortfall");
                Close(Number(baseline[i].Values, MoneyColumns[1]), Number(nonliable[i].Values, "False+"), 1e-5, "nonliable burden");
                Close(Number(baseline[i].Values, MoneyColumns[2]), Number(liable[i].Values, "False+"), 1e-5, "liable excess");
                Close(Number(baseline[i].Values, "Settlement Conditional on Reaching Bargaining"),
                    ConditionalRate(Number(baseline[i].Values, "Settles"), Number(baseline[i].Values, "Reaches Bargaining")).Value,
                    1e-5, "conditional settlement");
            }
            Row Conditional(string label, SourceRow[] data, string top, string bottom) => new(
                new[] { Text(label) }.Concat(data.Select(r => Ratio(r, top, bottom))).ToArray());
            var dispositions = new[]
            {
                Pair("Plaintiff files", baseline, "P Files", "percent"),
                Pair("Both participate / reach bargaining", baseline, "Reaches Bargaining", "percent"),
                Pair("Not filed", baseline, "No Suit", "percent"),
                Pair("Not answered", baseline, "No Answer", "percent"),
                Pair("Settled", baseline, "Settles", "percent"),
                Pair("Plaintiff abandons", baseline, "P Abandons", "percent"),
                Pair("Defendant defaults", baseline, "D Defaults", "percent"),
                Pair("Trial", baseline, "Trial", "percent"),
                Pair("Plaintiff wins at trial", baseline, "P Wins", "percent"),
                Pair("Defendant wins at trial", baseline, "P Loses", "percent")
            };
            var conditional = new[]
            {
                Conditional("Defendant answers, given filing", baseline, "D Answers", "P Files"),
                Pair("Settled, given reaching bargaining", baseline, "Settlement Conditional on Reaching Bargaining", "percent"),
                Pair(@"Plaintiff files, given $T=1$", liable, "PFiles", "percent"),
                Pair(@"Plaintiff files, given $T=0$", nonliable, "PFiles", "percent"),
                Conditional(@"Defendant answers, given filing and $T=1$", liable, "DAnswers", "PFiles"),
                Conditional(@"Defendant answers, given filing and $T=0$", nonliable, "DAnswers", "PFiles")
            };
            string[] names = [@"Meritorious-P net recovery shortfall ($T=1$)", @"Nonliable-D net burden ($T=0$)",
                @"Liable-D excess net burden above damages ($T=1$)", "Net Outcome Fidelity Loss (ex ante)", "Real litigation expenditures (ex ante)"];
            return Finish("baseline-outcomes", "Baseline outcomes",
                [new("A. Overall participation and dispositions (percent of potential disputes)", "@{}Xrr@{}", ["Outcome", "American", "British"], dispositions),
                 new("B. Conditional participation and settlement (percent)", "@{}Xrr@{}", ["Outcome and conditioning event", "American", "British"], conditional),
                 new("C. Truth-relative net outcomes and expenditures (damages units)", "@{}Xrr@{}", ["Measure", "American", "British"],
                    MoneyColumns.Select((c, i) => Pair(names[i], baseline, c)).ToArray())],
                @"Cost multiplier 1; 10 offers. $T=1$: truly liable; $T=0$: nonliable. Trial-win rows partition trials, not all disputes. Conditional answering divides joint filing-and-answering by filing within the indicated truth group. P and D denote plaintiff and defendant. Lower loss/burden measures indicate smaller shortfalls or excess burdens; they are not error probabilities.",
                "Cost-1, ten-offer American/British baseline. Panel A uses all potential disputes. The seven terminal dispositions are not filed, not answered, settled, P abandons, D defaults, P trial win and D trial win; they sum to one before display rounding. Trial is their trial-win subtotal; filing and reaching bargaining are intermediate events, not additional terminal categories. Panel B conditions exactly as labeled. Truth-conditioned participation comes from the saved individual report, not inferred from aggregate totals. Conditional answering is DAnswers/PFiles within each truth group. Panel C reports expected positive-part monetary losses conditional on truth for its first three rows and ex-ante quantities for its last two rows. Net Outcome Fidelity Loss includes costs and transfers relative to truth-dependent entitlements; it is a crude descriptive proxy, not a welfare measure or a probability of judicial error. The supplied notes for comparative-outcomes define its construction.");
        }

        private Table Comparisons()
        {
            if (request.Contrasts == null || request.Contrasts.Length != 4) throw new InvalidDataException("Expected the four selected contrasts.");
            var panels = new List<Panel>();
            for (int i = 0; i < 2; i++)
            {
                var baseline = Summary(request.Baselines[i].OptionSetName, request.Baselines[i].Regime);
                var rows = request.Contrasts.Select(c =>
                {
                    if (c.OptionSetNames.Length != 2) throw new InvalidDataException("Each contrast needs two fee regimes.");
                    var scenario = Summary(c.OptionSetNames[i], request.Baselines[i].Regime);
                    return new Row(new[] { Text(Escape(c.Label)) }.Concat(MoneyColumns.Select(m => Difference(scenario, baseline, m))).ToArray());
                }).ToArray();
                panels.Add(new(request.Baselines[i].Regime + " rule", "@{}Xrrrrr@{}",
                    ["Change from baseline", @"\shortstack{P shortfall\\$T=1$}", @"\shortstack{D burden\\$T=0$}",
                     @"\shortstack{D excess\\$T=1$}", @"\shortstack{Net fidelity\\loss}", @"\shortstack{Real\\costs}"], rows));
            }
            return Finish("comparative-outcomes", "Changes in net outcomes and expenditures", panels.ToArray(),
                @"Each cell is a change in damages units from the same fee rule's baseline (cost 1, risk neutral, noise 0.20). Higher costs: multiplier 2. Moderate risk aversion: CARA $\alpha=2$. Lower noise: party and court $\sigma=0.10$. All use 10 offers. Negative values mean reductions. First three columns condition on truth; final two are ex ante.",
                "Four contrasts matching the disposition figure, with differences calculated before display rounding. Let d=1, T denote true liability, and delta-w denote the net change in wealth including transfers and litigation costs. P shortfall is E[max(0,d-delta-w_P) | T=1]. Nonliable-D burden is E[max(0,-delta-w_D) | T=0]. Liable-D excess is E[max(0,-d-delta-w_D) | T=1]. Net Outcome Fidelity Loss is the sum of the saved ex-ante P shortfall and D excess-burden measures. With population liability probability pi, this is approximately pi times conditional P shortfall plus (1-pi) times conditional nonliable-D burden plus pi times conditional liable-D excess; small differences reflect numerical reporting/aggregation. Do not add the three conditional columns unweighted. Real litigation expenditures exclude transfers between parties. The loss index is one-sided and entitlement-dependent, incorporates costs, and can count a single misallocation from both parties' perspectives; it is neither social welfare nor judicial accuracy. It is reported alongside its components and costs rather than used to declare one scenario normatively best.");
        }

        private Table Sensitivity()
        {
            string[] metrics = ["Settles", "Trial", "Real Litigation Costs", "Net Outcome Fidelity Loss"];
            Panel ComparisonPanel(string title, Contrast[] cases)
            {
                if (cases == null || cases.Length != 2) throw new InvalidDataException("Expected baseline plus one sensitivity contrast.");
                var rows = cases.SelectMany(c =>
                {
                    if (c.OptionSetNames.Length != 2) throw new InvalidDataException("Sensitivity needs two regimes.");
                    return request.Baselines.Select((b, i) =>
                    {
                        var source = Summary(c.OptionSetNames[i], b.Regime);
                        return new Row(new[] { Text(Escape(c.Label)), Text(b.Regime) }
                            .Concat(metrics.Select((m, j) => Cell(source, m, j < 2 ? "percent" : "money"))).ToArray());
                    });
                }).ToArray();
                return new(title, "@{}Xlrrrr@{}", ["Specification", "Fee rule", @"\shortstack{Settled\\(\%)}", @"\shortstack{Trial\\(\%)}",
                    @"\shortstack{Real\\costs}", @"\shortstack{Net fidelity\\loss}"], rows);
            }
            var panels = new List<Panel>
            {
                ComparisonPanel("A. Model form (10 offers)", request.ModelForms),
                ComparisonPanel("B. Offer-grid sensitivity (continuous-merits baseline)", request.OfferGrids)
            };
            var rangeRows = request.Baselines.Select(b =>
            {
                var rows = Read(request.EquilibriumRangesCsv).Where(r => r["Fee Regime"] == b.Regime).ToArray();
                if (rows.Length != 1) throw new InvalidDataException("Missing/duplicate equilibrium range row.");
                return Use(request.EquilibriumRangesCsv, rows[0]);
            }).ToArray();
            foreach (var range in rangeRows)
            {
                var outcomes = Read(request.EquilibriumOutcomesCsv).Where(r => r["Fee Regime"] == range.Values["Fee Regime"]).ToArray();
                foreach (var outcome in outcomes) Use(request.EquilibriumOutcomesCsv, outcome);
                Close(outcomes.Length, Number(range.Values, "Distinct Reported Strategy Profiles"), 0, "distinct profiles");
                Close(outcomes.Select(o => Number(o, "Equilibrium Recovery Count")).Sum(), Number(range.Values, "Verified Recoveries"), 0, "recovery accounting");
                if (outcomes.Select(o => o["Equilibrium"]).Distinct().Count() != outcomes.Length)
                    throw new InvalidDataException("Duplicate equilibrium IDs.");
                foreach (string metric in new[] { "P Offer", "Trial", "Settles", "Net Outcome Fidelity Loss", "Exploitability" })
                {
                    Close(outcomes.Min(o => Number(o, metric)), Number(range.Values, "Minimum " + metric), 1e-12, "range minimum " + metric);
                    Close(outcomes.Max(o => Number(o, metric)), Number(range.Values, "Maximum " + metric), 1e-12, "range maximum " + metric);
                }
            }
            Row Range(string label, string metric, string format)
            {
                return new(new[] { Text(label) }.Concat(rangeRows.Select(row =>
                {
                    string min = "Minimum " + metric, max = "Maximum " + metric;
                    return new Cell(Format(Number(row.Values, min), format) + "--" + Format(Number(row.Values, max), format), null,
                        "Minimum--maximum across distinct retained profiles, not a confidence interval", [Input(row, min), Input(row, max)]);
                })).ToArray());
            }
            var startsRows = new List<Row>();
            foreach (string count in new[] { "Requested Priors", "Attempted Solves", "Verified Recoveries", "Distinct Reported Strategy Profiles" })
                startsRows.Add(Pair(count == "Distinct Reported Strategy Profiles" ? "Distinct retained strategy profiles" : count, rangeRows, count, "integer"));
            startsRows.Add(Range("Mean plaintiff demand: profile range", "P Offer", "money"));
            startsRows.Add(Range(@"Settlement rate: profile range (\%)", "Settles", "percent"));
            startsRows.Add(Range(@"Trial rate: profile range (\%)", "Trial", "percent"));
            startsRows.Add(Range("Net fidelity loss: profile range", "Net Outcome Fidelity Loss", "money"));
            startsRows.Add(Pair("Maximum reported exploitability", rangeRows, "Maximum Exploitability", "scientific"));
            panels.Add(new("C. Multiple-start sensitivity (10-offer baseline)", "@{}Xrr@{}", ["Diagnostic", "American", "British"], startsRows.ToArray()));
            return Finish("robustness-and-sensitivity", "Model-form and numerical sensitivity", panels.ToArray(),
                @"Cost multiplier 1; risk neutral; 10 party-signal bins throughout. Settlement and trial use all potential disputes. Costs/losses are in damages units. A changes the information structure, not only the solver. B changes offers, not signals. C ranges are descriptive across retained profiles, not confidence intervals; repeated endpoints indicate no dispersion at reported precision.",
                "Panel A compares continuous merits with directly truth-generated binary-state signals, using the saved calibrated noise parameters (continuous: party/court 0.20; direct binary: party 0.3498283040, court 0.3060453855). It is a model-form comparison, not a test of numerical convergence or identical signal noise. Panel B compares ten and fifteen offers holding ten party signals and the cost-1, risk-neutral baseline fixed. It does not establish convergence to a continuous-offer equilibrium. Panel C uses the saved original multiple-start exercise, not the separate 5x5 monotone-pure enumeration. Requested priors are initializations; attempted solves also include fallback attempts. Verified recoveries are not necessarily distinct profiles. Distinctness uses exact equality of normalized action-probability vectors. Mean plaintiff demand is an aggregate across reached bargaining histories for each profile, not a mixed strategy at one information set. Recovery counts are not equilibrium-selection probabilities, and zero reported outcome dispersion does not prove uniqueness or robustness beyond the tested design. Complete recovery accounting, model forms, and the risk-averse fifteen-offer checks remain designated for online appendices.");
        }
        private static void Close(double actual, double expected, double tolerance, string name)
        {
            if (!double.IsFinite(actual) || !double.IsFinite(expected) || Math.Abs(actual - expected) > tolerance)
                throw new InvalidDataException($"Validation failed for {name}: {actual:R} vs {expected:R}.");
        }
    }

    public static string RenderFragment(Table table)
    {
        var tex = new StringBuilder("% Generated table fragment; requires booktabs, tabularx, array.\n% Input inside a manuscript table environment; numbering/caption belong there.\n\\begingroup\n\\small\n\\setlength{\\tabcolsep}{5pt}\n\\renewcommand{\\arraystretch}{1.20}\n");
        foreach (var panel in table.Panels)
        {
            if (panel.Title.Length != 0) tex.AppendLine(@"\noindent\textbf{" + panel.Title + @"}\par\smallskip");
            tex.AppendLine(@"\noindent\begin{tabularx}{\linewidth}{" + panel.Columns.Replace("X", @">{\raggedright\arraybackslash}X") + "}");
            tex.AppendLine(@"\toprule");
            tex.AppendLine(string.Join(" & ", panel.Headings.Select(h => @"\textbf{" + h + "}")) + @" \\");
            tex.AppendLine(@"\midrule");
            foreach (var row in panel.Rows)
            {
                if (row.Cells.Length != panel.Headings.Length) throw new InvalidDataException("Table row/column mismatch.");
                tex.AppendLine(string.Join(" & ", row.Cells.Select(c => c.Latex)) + @" \\");
            }
            tex.AppendLine(@"\bottomrule\end{tabularx}\par\medskip");
        }
        if (!string.IsNullOrWhiteSpace(table.Notes))
            tex.AppendLine(@"{\footnotesize\noindent " + table.Notes + @"\par}");
        tex.AppendLine(@"\endgroup");
        return tex.ToString();
    }

    public static string Standalone(string title, string fragment) =>
        "\\documentclass[10pt,border=5pt,varwidth=17cm]{standalone}\n\\usepackage[T1]{fontenc}\n" +
        "\\usepackage{lmodern,booktabs,tabularx,array,amsmath}\n\\begin{document}\n" +
        (string.IsNullOrWhiteSpace(title) ? "" : "\\noindent{\\large\\bfseries " + Escape(title) + "}\\par\\medskip\n") + fragment + "\\end{document}\n";
}
