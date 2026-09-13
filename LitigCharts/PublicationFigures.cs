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

namespace LitigCharts;

/// <summary>Reusable publication layouts. Data selection lives in a request, never in renderer switches.</summary>
public static class PublicationFigures
{
    public sealed record StrategyCase(string Label, string OptionSetName, string ActionReport, int EquilibriumNumber = 1);
    public sealed record DispositionGroup(string Label, string[] OptionSetNames);
    public sealed record Request(StrategyCase[] StrategyCases, string NumericalResultsCsv, DispositionGroup[] DispositionGroups,
        string DispositionIntroduction = null, string RegimeColumn = "Fee Regime");
    public sealed record Source(string Path, string Sha256);
    public sealed record ActionValue(int Action, double Value, double Probability);
    public sealed record StrategyPoint(int Signal, double SignalValue, int[] InformationSets,
        double Reach, bool OffPath, ActionValue[] Actions);
    public sealed record StrategyPanel(string Decision, string Label, StrategyPoint[][] Series);
    public sealed record StrategyData(string[] Labels, string[] OptionSets, int[] Equilibria, Source RequestSource, Source[] Sources, StrategyPanel[] Panels);
    public sealed record DispositionBar(string Group, string Regime, string OptionSetName,
        double[] Values, double Sum, double MutualGiveUpAudit, double Trial);
    public sealed record DispositionData(Source RequestSource, Source Source, string[] Categories, DispositionBar[] Bars);
    public sealed record Figure(string Stem, string Latex, string Caption, object Data);

    public static readonly string[] CategoryColumns = ["No Suit", "No Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins"];
    public static readonly string[] CategoryLabels = ["Not filed", "Not answered", "Settled", "P abandons", "D defaults", "P loses trial", "P wins trial"];
    private sealed record DispositionStyle(string Fill, string Pattern = null, string PatternColor = "black")
    {
        // A TikZ pattern replaces the fill; paint an opaque base first (essential for white lines on black).
        public string ShapeStyle => Pattern == null ? $"fill={Fill}"
            : $"preaction={{fill={Fill},draw=none}},pattern={{{Pattern}}},pattern color={PatternColor}";
        public string LabelStyle => $"text={(Fill == "black" ? "white" : "black")},fill={(Pattern == null ? "none" : Fill)}";
    }
    private static readonly DispositionStyle[] CategoryStyles =
    [
        new("white", "Dots[distance=4pt,radius=.35pt]"),
        new("black", "Dots[distance=2.8pt,radius=.35pt]", "white"),
        new("black!50"),
        new("white", "north east lines"),
        new("black", "north east lines", "white"),
        new("white"),
        new("black")
    ];
    public static string Stem(string target) => target switch
    {
        "selection-offers" => "participation-and-offers",
        "dispositions" => "disposition",
        _ => throw new ArgumentException("Unknown publication target: " + target)
    };
    public static Figure Generate(string requestFile, string target)
    {
        requestFile = Path.GetFullPath(requestFile);
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestFile), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Empty publication request.");
        if (request.StrategyCases == null || request.StrategyCases.Length != 2 ||
            request.StrategyCases.Select(c => c.Label).Distinct().Count() != 2)
            throw new InvalidDataException("This comparison layout requires two distinctly labeled regimes.");
        string Resolve(string p) => Path.GetFullPath(p, Path.GetDirectoryName(requestFile));
        if (target == "selection-offers")
        {
            var files = request.StrategyCases.Select(c => Resolve(c.ActionReport)).ToArray();
            var rows = files.Select(ReadCsv).ToArray();
            var grids = request.StrategyCases.Select(c => GridForOptionSet(c.OptionSetName)).ToArray();
            string[] decisions = ["P Files", "D Answers", "P Offer", "D Offer"];
            string[] labels = ["Filing", "Answering", "Plaintiff demand", "Defendant offer"];
            var panels = decisions.Select((decision, i) => new StrategyPanel(decision, labels[i],
                request.StrategyCases.Select((c, j) => BuildStrategySeries(rows[j], c, decision,
                    grids[j].SignalCount, grids[j].Offers)).ToArray())).ToArray();
            if (panels.SelectMany(p => p.Series).Select(s => s.Length).Distinct().Count() != 1)
                throw new InvalidDataException("All strategy panels must use the same signal grid.");
            var data = new StrategyData(request.StrategyCases.Select(c => c.Label).ToArray(),
                request.StrategyCases.Select(c => c.OptionSetName).ToArray(),
                request.StrategyCases.Select(c => c.EquilibriumNumber).ToArray(), Fingerprint(requestFile), files.Select(Fingerprint).ToArray(), panels);
            return new Figure(Stem(target), RenderStrategies(data), StrategyCaption, data);
        }
        if (target != "dispositions") throw new ArgumentException("Unknown target: " + target);
        string numericalFile = Resolve(request.NumericalResultsCsv);
        var numericalRows = ReadCsv(numericalFile);
        if (request.DispositionGroups == null || request.DispositionGroups.Length == 0)
            throw new InvalidDataException("No disposition groups requested.");
        if (request.DispositionGroups.Select(g => g.Label).Distinct().Count() != request.DispositionGroups.Length)
            throw new InvalidDataException("Disposition group labels must be distinct.");
        var bars = new List<DispositionBar>();
        foreach (var group in request.DispositionGroups)
        {
            if (group.OptionSetNames.Length != request.StrategyCases.Length)
                throw new InvalidDataException("Each disposition group needs one option set per regime.");
            for (int i = 0; i < group.OptionSetNames.Length; i++)
            {
                string name = group.OptionSetNames[i];
                var matches = numericalRows.Where(r => r["OptionSetName"] == name && r["Filter"] == "All" && r["Equilibrium Type"] == "Only Eq").ToArray();
                if (matches.Length != 1) throw new InvalidDataException($"Expected exactly one All/Only Eq row for {name}; found {matches.Length}.");
                var row = matches[0];
                if (!row.TryGetValue(request.RegimeColumn, out string regime) || regime != request.StrategyCases[i].Label)
                    throw new InvalidDataException("Comparison regime and requested label disagree: " + name);
                double[] values = CategoryColumns.Select(c => Number(row, c)).ToArray();
                ValidateDisposition(values, Number(row, "Trial"));
                bars.Add(new(group.Label, request.StrategyCases[i].Label, name, values, values.Sum(),
                    Number(row, "Mutual Give-Up Probability Before 50/50 Allocation"), Number(row, "Trial")));
            }
        }
        if (bars.Select(b => b.OptionSetName).Distinct().Count() != bars.Count)
            throw new InvalidDataException("Duplicate disposition option sets.");
        var dispositionData = new DispositionData(Fingerprint(requestFile), Fingerprint(numericalFile), CategoryLabels, bars.ToArray());
        string caption = string.IsNullOrWhiteSpace(request.DispositionIntroduction) ? DispositionCaption
            : request.DispositionIntroduction.Trim() + "\n\n" + DispositionCaption;
        return new Figure(Stem(target), RenderDispositions(dispositionData), caption, dispositionData);
    }

    public static Dictionary<string, string>[] ReadCsv(string file)
    {
        using var reader = new StreamReader(file);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        if (!csv.Read()) throw new InvalidDataException("Empty source CSV: " + file);
        csv.ReadHeader();
        string[] headers = csv.HeaderRecord;
        if (headers.Distinct().Count() != headers.Length) throw new InvalidDataException("Duplicate CSV headers.");
        var rows = new List<Dictionary<string, string>>();
        while (csv.Read()) rows.Add(headers.ToDictionary(h => h, h => csv.GetField(h)));
        return rows.ToArray();
    }

    public static (int SignalCount, double[] Offers) GridForOptionSet(string optionSetName)
    {
        // Construct options only; no game-tree initialization, profile loading, or solver call.
        var options = ArticleWorkedPathExtraction.CreateOptions(optionSetName);
        if (options.NumPotentialBargainingRounds != 1)
            throw new InvalidDataException("The four-panel layout requires a one-round game; split later-round histories explicitly.");
        return (options.NumLiabilitySignals, Enumerable.Range(1, options.NumOffers)
            .Select(a => Game.ConvertActionToUniformDistributionDraw((byte)a, options.NumOffers, options.IncludeEndpointsForOffers)).ToArray());
    }

    public static StrategyPoint[] BuildStrategySeries(Dictionary<string, string>[] rows, StrategyCase selection, string decision,
        int? expectedSignalCount = null, double[] offerValues = null)
    {
        var selected = rows.Where(r => r["OptionSetName"] == selection.OptionSetName &&
            int.Parse(r["Equilibrium Number"], CultureInfo.InvariantCulture) == selection.EquilibriumNumber && r["Decision"] == decision).ToArray();
        if (selected.Length == 0) throw new InvalidDataException("Missing decision: " + decision);
        var canonicalActions = selected.GroupBy(r => r["Information Set Number"]).First()
            .OrderBy(r => int.Parse(r["Action"], CultureInfo.InvariantCulture))
            .Select(r => (r["Action"], r["Action Label"])).ToArray();
        string signalName = decision.StartsWith("P", StringComparison.Ordinal) ? "P Liability Signal" : "D Liability Signal";
        int Signal(Dictionary<string, string> row)
        {
            var match = Regex.Match(row["Information Set Labels"], @"(?:^|;)" + signalName + @": (\d+)(?:;|$)");
            if (!match.Success) throw new InvalidDataException("Missing own signal label.");
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        var signalGroups = selected.GroupBy(Signal).OrderBy(g => g.Key).ToArray();
        int count = signalGroups.Length;
        if (expectedSignalCount.HasValue && count != expectedSignalCount ||
            !signalGroups.Select(g => g.Key).SequenceEqual(Enumerable.Range(1, count)))
            throw new InvalidDataException("Signal grid is missing a bin.");
        bool offer = decision.EndsWith("Offer", StringComparison.Ordinal);
        if (offer && offerValues != null && canonicalActions.Length != offerValues.Length)
            throw new InvalidDataException("Action count disagrees with the model's offer grid.");
        var points = new List<StrategyPoint>();
        foreach (var signal in signalGroups)
        {
            var informationSets = signal.GroupBy(r => r["Information Set Number"]).ToArray();
            foreach (var set in informationSets)
            {
                var first = set.First();
                double reach = Number(first, "Equilibrium Reach Probability");
                bool offPath = bool.Parse(first["Off Path"]);
                if (reach < 0 || reach > 1 || (reach <= InformationSetActionReport.OffPathTolerance) != offPath ||
                    set.Any(r => Number(r, "Equilibrium Reach Probability") != reach || bool.Parse(r["Off Path"]) != offPath))
                    throw new InvalidDataException("Inconsistent information-set reach.");
                double sum = set.Sum(r => Number(r, "Equilibrium Action Probability"));
                if (Math.Abs(sum - 1) > 1e-9 || set.Any(r => Number(r, "Equilibrium Action Probability") < 0 || Number(r, "Equilibrium Action Probability") > 1))
                    throw new InvalidDataException("Invalid action probabilities.");
                if (set.Select(r => r["Action"]).Distinct().Count() != set.Count())
                    throw new InvalidDataException("Duplicate action row.");
                var actionIds = set.Select(r => int.Parse(r["Action"], CultureInfo.InvariantCulture)).OrderBy(a => a).ToArray();
                if (!actionIds.SequenceEqual(Enumerable.Range(1, actionIds.Length)))
                    throw new InvalidDataException("Incomplete action set.");
                if (!set.OrderBy(r => int.Parse(r["Action"], CultureInfo.InvariantCulture))
                    .Select(r => (r["Action"], r["Action Label"])).SequenceEqual(canonicalActions))
                    throw new InvalidDataException("Action menu differs across information sets for " + decision);
                if (offer && offerValues != null && set.Any(r =>
                    Math.Abs(Number(r, "Action Label") - offerValues[int.Parse(r["Action"], CultureInfo.InvariantCulture) - 1]) > .005000001))
                    throw new InvalidDataException("Rounded offer labels disagree with the model's exact offer grid.");
            }
            var active = informationSets.Where(s => !bool.Parse(s.First()["Off Path"])).ToArray();
            if (active.Length > 1)
                throw new InvalidDataException($"Multiple on-path histories at {decision}, signal {signal.Key}. Plot these histories separately; do not pool their strategies.");
            int[] infoIds = informationSets.Select(s => int.Parse(s.Key, CultureInfo.InvariantCulture)).ToArray();
            if (active.Length == 0)
            {
                points.Add(new(signal.Key, (signal.Key - .5) / count, infoIds,
                    informationSets.Sum(s => Number(s.First(), "Equilibrium Reach Probability")), true, []));
                continue;
            }
            var source = active[0].ToArray();
            if (!offer && (source.Length != 2 || source.Count(r => r["Action Label"] == "Yes") != 1 || source.Count(r => r["Action Label"] == "No") != 1))
                throw new InvalidDataException("Participation must have exactly Yes and No actions.");
            var actions = source.Select(r => new ActionValue(
                int.Parse(r["Action"], CultureInfo.InvariantCulture),
                offer ? offerValues == null ? Number(r, "Action Label") : offerValues[int.Parse(r["Action"], CultureInfo.InvariantCulture) - 1]
                    : r["Action Label"] == "Yes" ? 1 : 0,
                Number(r, "Equilibrium Action Probability"))).ToArray();
            if (actions.Any(a => a.Value < 0 || a.Value > 1)) throw new InvalidDataException("Action axis requires values in [0,1].");
            points.Add(new(signal.Key, (signal.Key - .5) / count,
                [int.Parse(source[0]["Information Set Number"], CultureInfo.InvariantCulture)],
                Number(source[0], "Equilibrium Reach Probability"), false, actions));
        }
        return points.ToArray();
    }

    public static void ValidateDisposition(double[] values, double trial)
    {
        // The aggregate CSV retains rounded report values. Never renormalize silently.
        if (values.Length != 7 || values.Any(v => !double.IsFinite(v) || v < 0 || v > 1) ||
            !double.IsFinite(trial) || trial < 0 || trial > 1 || Math.Abs(values.Sum() - 1) > 1e-5 ||
            Math.Abs(values[5] + values[6] - trial) > 1e-5)
            throw new InvalidDataException("Disposition totals do not reconcile to all disputes / trial.");
    }
    private static double Number(Dictionary<string, string> row, string key)
    {
        if (!row.TryGetValue(key, out string text) || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new InvalidDataException("Missing/non-numeric value in " + key);
        return value;
    }
    private static Source Fingerprint(string path) => new(Path.GetFullPath(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    private static string N(double number) => number.ToString("0.######", CultureInfo.InvariantCulture);
    private static string Label(string text) => string.Concat(text.Select(c => c switch
    {
        '&' => @"\&", '%' => @"\%", '_' => @"\_", '#' => @"\#", '$' => @"\$",
        '{' => @"\{", '}' => @"\}", '\\' => @"\textbackslash{}", '^' => @"\textasciicircum{}", '~' => @"\textasciitilde{}",
        _ => c.ToString()
    }));
    private static StringBuilder Start() => new("""
        % Generated by LitigCharts.PublicationFigures. Captions and provenance are sidecars.
        \documentclass[10pt,tikz,border=5pt]{standalone}
        \usepackage{lmodern}
        \usetikzlibrary{patterns,patterns.meta}
        \begin{document}
        \begin{tikzpicture}[font=\small]
        """);
    private static string Finish(StringBuilder b) => b.AppendLine("\\end{tikzpicture}\n\\end{document}").ToString();
    private static string LineStyle(int series) => series == 0 ? "black,solid,line width=.65pt" : "black,dashed,line width=.65pt";
    // Data retain actual action probabilities; only participation's plotting coordinate is derived.
    private static ActionValue[] PlottedSupport(StrategyPoint point, bool offers) => offers
        ? point.Actions.Where(a => a.Probability > 0).ToArray()
        : [new ActionValue(1, point.Actions.Single(a => a.Value == 1).Probability, 1)];
    private static void Marker(StringBuilder b, int series, double x, double y)
    {
        if (series == 0)
            b.AppendLine($@"\fill[black] ({N(x)},{N(y)}) circle[radius=2.1pt];");
        else
            b.AppendLine($@"\node[draw=black,fill=white,rectangle,inner sep=0pt,minimum size=5.8pt,line width=.6pt] at ({N(x)},{N(y)}) {{}};");
    }
    public static string RenderStrategies(StrategyData data)
    {
        var b = Start();
        // Global series key; explanation and conditioning belong in the caption.
        for (int s = 0; s < data.Labels.Length; s++)
        {
            double x = 4 + s * 5.5;
            b.AppendLine($@"\draw[{LineStyle(s)}] ({N(x)},12) -- ({N(x + .7)},12);");
            Marker(b, s, x + .35, 12);
            b.AppendLine($@"\node[anchor=west] at ({N(x + .85)},12) {{{Label(data.Labels[s])}}};");
        }
        for (int k = 0; k < data.Panels.Length; k++)
        {
            var panel = data.Panels[k];
            bool offers = panel.Decision.EndsWith("Offer", StringComparison.Ordinal);
            double x0 = (k % 2) * 8.6 + 1.0, y0 = k < 2 ? 6.6 : 0.4;
            b.AppendLine($@"\begin{{scope}}[shift={{({N(x0)},{N(y0)})}},x=6.4cm,y=4.1cm]");
            b.AppendLine($@"\node[anchor=west,font=\bfseries] at (0,1.14) {{({(char)('a' + k)}) {Label(panel.Label)}}};");
            for (int tick = 0; tick <= 4; tick++)
            {
                double y = tick / 4.0;
                b.AppendLine($@"\draw[black!12,line width=.2pt] (0,{N(y)}) -- (1,{N(y)});");
                b.AppendLine($@"\node[anchor=east] at (-.025,{N(y)}) {{{y.ToString("0.##", CultureInfo.InvariantCulture)}}};");
            }
            b.AppendLine(@"\draw[black,line width=.4pt] (0,1.03) -- (0,0) -- (1,0);");
            foreach (var point in panel.Series[0])
            {
                b.AppendLine($@"\draw ( {N(point.SignalValue)},0) -- ({N(point.SignalValue)},-.016);");
                b.AppendLine($@"\node[anchor=north,font=\footnotesize] at ({N(point.SignalValue)},-.035) {{{point.SignalValue.ToString("0.00", CultureInfo.InvariantCulture)}}};");
            }
            b.AppendLine($@"\node[rotate=90] at (-.15,.5) {{{(offers ? "Offer" : "Probability")}}};");
            b.AppendLine($@"\node at (.5,-.22) {{{(panel.Decision.StartsWith("P") ? "Plaintiff" : "Defendant")} signal}};");
            // Draw squares first, then circles so coincident regimes remain visible.
            for (int s = panel.Series.Length - 1; s >= 0; s--)
            {
                var points = panel.Series[s];
                for (int i = 0; i < points.Length; i++)
                {
                    var point = points[i];
                    if (point.OffPath)
                        continue;
                    var support = PlottedSupport(point, offers);
                    if (support.Length == 1)
                    {
                        double halfBin = .5 / points.Length;
                        b.AppendLine($@"\draw[{LineStyle(s)}] ({N(point.SignalValue - halfBin)},{N(support[0].Value)}) -- ({N(point.SignalValue + halfBin)},{N(support[0].Value)});");
                        if (i > 0 && !points[i - 1].OffPath)
                        {
                            var previous = PlottedSupport(points[i - 1], offers);
                            if (previous.Length == 1)
                                b.AppendLine($@"\draw[{LineStyle(s)}] ({N(point.SignalValue - halfBin)},{N(previous[0].Value)}) -- ({N(point.SignalValue - halfBin)},{N(support[0].Value)});");
                        }
                    }
                    foreach (var action in support)
                    {
                        Marker(b, s, point.SignalValue, action.Value);
                        if (support.Length > 1)
                            b.AppendLine($@"\node[anchor={(s == 0 ? "east" : "west")},font=\scriptsize,fill=white,inner sep=1pt] at ({N(point.SignalValue + (s == 0 ? -.012 : .012))},{N(action.Value + .035)}) {{$p={action.Probability.ToString("0.###", CultureInfo.InvariantCulture)}$}};");
                    }
                }
            }
            b.AppendLine(@"\end{scope}");
        }
        return Finish(b);
    }
    public static string RenderDispositions(DispositionData data)
    {
        var b = Start();
        var groups = data.Bars.GroupBy(bar => bar.Group).ToArray();
        double y = groups.Sum(g => g.Count() * .72 + .48) + .4;
        for (int tick = 0; tick <= 5; tick++)
        {
            double x = 5.1 + tick * 2.2;
            b.AppendLine($@"\draw[black!15,line width=.2pt] ({N(x)},.65) -- ({N(x)},{N(y + .4)});");
            b.AppendLine($@"\node[anchor=north] at ({N(x)},.55) {{{tick * 20}\%}};");
        }
        foreach (var group in groups)
        {
            double center = y - (group.Count() - 1) * .36;
            // Allow wrapping between words, but never split short scenario labels with hyphens.
            string groupLabel = string.Join(" ", group.Key.Split(' ').Select(word => $@"\mbox{{{Label(word)}}}"));
            b.AppendLine($@"\node[anchor=west,text width=3cm,align=left] at (0,{N(center)}) {{{groupLabel}}};");
            foreach (var bar in group)
            {
                b.AppendLine($@"\node[anchor=east] at (4.95,{N(y)}) {{{Label(bar.Regime)}}};");
                double start = 0;
                for (int c = 0; c < bar.Values.Length; c++)
                {
                    double value = bar.Values[c], left = 5.1 + 11 * start, right = left + 11 * value;
                    start += value;
                    if (value == 0) continue;
                    b.AppendLine($@"\path[{CategoryStyles[c].ShapeStyle},draw=black,line width=.25pt] ({N(left)},{N(y - .26)}) rectangle ({N(right)},{N(y + .26)});");
                    if (value >= .06)
                        b.AppendLine($@"\node[{CategoryStyles[c].LabelStyle},font=\footnotesize,inner sep=1pt] at ({N((left + right) / 2)},{N(y)}) {{{(value * 100).ToString("0.0", CultureInfo.InvariantCulture)}}};");
                }
                y -= .72;
            }
            y -= .48;
        }
        b.AppendLine(@"\node at (10.6,-.08) {Potential disputes};");
        for (int c = 0; c < CategoryLabels.Length; c++)
        {
            // Keep the nonparticipation/settlement family above the paired exit/trial outcomes.
            double x = .2 + (c < 3 ? c : c - 3) * 4.2, legendY = c < 3 ? -.85 : -1.5;
            b.AppendLine($@"\path[{CategoryStyles[c].ShapeStyle},draw=black,line width=.25pt] ({N(x)},{N(legendY - .13)}) rectangle ({N(x + .45)},{N(legendY + .13)});");
            b.AppendLine($@"\node[anchor=west] at ({N(x + .56)},{N(legendY)}) {{{CategoryLabels[c]}}};");
        }
        return Finish(b);
    }
    public const string StrategyCaption = """
        Figure 3. Participation and offer strategies by own signal, comparing the two requested equilibria.
        Filing probabilities condition on the plaintiff's own signal. Answering probabilities additionally
        condition on observed filing. Offer strategies condition on own signal and the complete on-path
        information set after participation and the party's exit commitment. A circle denotes the first
        requested regime and an open square the second. Symbols at the same coordinate may overlap.
        Steps span discrete signal bins and do not assert a continuous-signal limit. Every positive-probability
        offer is shown; mixed offer support receives short p labels. No mean-offer substitution is made.
        Offer coordinates use the exact production option-set grid, checked against the rounded report
        labels; the plotted unit is the game's normalized offer scale. All signal/action bins must be present.
        Blank offer regions have no reached information set; they are not zero offers or missing source data.
        No off-path labels or markers are drawn, and lines do not bridge these gaps.
        The action-report off-path tolerance is 1e-15; negligible reach is retained in the JSON, not treated
        as a measurable offer. Arbitrary actions at unreachable information sets are not plotted. The generator rejects multiple
        on-path histories for the same decision and signal rather than silently pooling their strategies.
        The companion JSON gives exact reach, action probabilities, information-set identifiers, selected
        option sets, equilibrium numbers and request/source SHA-256 hashes. For participation, JSON action
        values 1 and 0 mean Yes and No, and probabilities retain the actual mixture. The figure is a selected-equilibrium
        illustration, not a claim that all recovered equilibria share the same offer strategies.
        """;
    public const string DispositionCaption = """
        Dispositions as shares of all potential disputes, with fee regimes paired within specification.
        The accompanying request specifies the selected specifications and fee-regime pairs; the JSON
        records each exact source row, including its cost multiplier and offer-grid selection.
        Categories are mutually exclusive: no filing, no answer, settlement, plaintiff abandonment, defendant
        default, plaintiff loss at trial, and plaintiff win at trial. Court results are findings, not true liability.
        Plaintiff trial wins are black and defendant trial wins white. Defendant default uses white lines
        on black; plaintiff abandonment uses black lines on white. Settlement is middle gray. Nonfiling
        uses small black dots on white; nonanswering uses small white dots on black. This styling
        groups related procedural outcomes; it does not encode true liability or outcome fidelity.
        The saved P Abandons and D Defaults columns already contain the 50/50 mutual-give-up allocation;
        the audit column is retained in JSON and is not added a second time. Each bar reconciles to one,
        and the two trial segments reconcile to Trial, within 1e-5 for source-report rounding. Values are
        not renormalized. Labels inside sufficiently wide segments are percentages rounded to one decimal;
        narrow segments remain drawn without an interior label. Conditional settlement rates are not
        represented by these bars. The JSON preserves source values, row identifiers and the source hash.
        """;
}
