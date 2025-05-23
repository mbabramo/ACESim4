using ACESim;
using ACESimBase.Games.LitigGame;
using ACESimBase.Util.Tikz;
using ACESimBase.Util.Mathematics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    /// <summary>
    /// Produces the mosaic stacked-bar diagram of litigation cost components:
    ///   Opportunity, Harm, Filing, Answering, Bargaining, Trying.
    /// Each slice width = probability; heights are rescaled so the tallest stack = 4.0.
    /// </summary>
    public static class CostBreakdownReport
    {
        // ---------------- constants / styling -----------------------------------------------

        private const double MaxStackHeight = 4.0;

        // hatch patterns (publication mode)
        private static readonly string[] RegularFill =
        {
            "pattern=north east lines, pattern color=blue",          // Opportunity
            "pattern=north west lines, pattern color=red",           // Harm
            "pattern=dots,            pattern color=green!60!black", // Filing
            "pattern=vertical lines,  pattern color=green!80!black", // Answering
            "pattern=crosshatch,      pattern color=orange!80!black",// Bargaining
            "pattern=grid,            pattern color=purple!70!black" // Trying
        };

        // solid colours (presentation mode)
        private static readonly string[] PresentationFill =
        {
            "fill=blue!85", "fill=red!85",    "fill=yellow!75!black",
            "fill=green!70","fill=orange!85", "fill=purple!75"
        };

        private static readonly string[] ComponentLabels =
            { "Opportunity", "Harm", "Filing", "Answering", "Bargaining", "Trying" };

        // ---------------- public entry points -----------------------------------------------

        /// <summary>Pipeline entry — signature matches StageCostReport.</summary>
        public static List<string> GenerateReport(
            List<(GameProgress theProgress, double weight)> gameProgresses)
            => GenerateReport(gameProgresses.Select(g =>
                    ((LitigGameProgress)g.theProgress, g.weight)));

        /// <summary>Lower-level overload for typed progress/weight pairs.</summary>
        public static List<string> GenerateReport(
            IEnumerable<(LitigGameProgress progress, double weight)> raw,
            bool presentationMode = false)
        {
            double totW = raw.Sum(r => r.weight);
            if (totW <= 0)
                throw new ArgumentException("Total weight must be > 0.");

            var initial = raw
                .Select(r => BuildSlice(r.progress, r.weight / totW))
                .ToList();

            var merged = MergeDuplicates(initial);
            var ordered = merged.OrderBy(s => s.TotalCost).ToList();
            var scaled = ScaleToFour(ordered);

            // Determine title (Costs and Fee Shift multipliers, plus risk aversion info)
            var options = (LitigGameOptions)raw.First().progress.GameDefinition.GameOptions;
            string title = $"Costs: {options.CostsMultiplier}x; Fee Shift: {options.LoserPaysMultiple}x";
            bool pRiskNeutral = options.PUtilityCalculator is RiskNeutralUtilityCalculator;
            bool dRiskNeutral = options.DUtilityCalculator is RiskNeutralUtilityCalculator;
            string supplementalTitle = (pRiskNeutral, dRiskNeutral) switch
            {
                (true, true) => "",
                (true, false) => "; D Risk Averse",
                (false, true) => "; P Risk Averse",
                (false, false) => "; Both Risk Averse"
            };
            title += supplementalTitle;

            // Return: CSV + TikZ (print) + CSV (dup) + TikZ (presentation) — mimics StageCostReport.
            return new()
            {
                BuildCsv(scaled),
                BuildTikz(scaled, false, title),
                BuildCsv(scaled),
                BuildTikz(scaled, true, title)
            };
        }

        // ---------------- slice representation ----------------------------------------------

        private sealed record Slice(
            double Width,
            double Opportunity,
            double Harm,
            double Filing,
            double Answering,
            double Bargaining,
            double Trying)
        {
            public double TotalCost =>
                Opportunity + Harm + Filing + Answering + Bargaining + Trying;

            public bool Matches(Slice o, double tol = 1e-7) =>
                Math.Abs(Opportunity - o.Opportunity) < tol &&
                Math.Abs(Harm - o.Harm) < tol &&
                Math.Abs(Filing - o.Filing) < tol &&
                Math.Abs(Answering - o.Answering) < tol &&
                Math.Abs(Bargaining - o.Bargaining) < tol &&
                Math.Abs(Trying - o.Trying) < tol;

            public Slice AddWidth(double extra) => this with { Width = Width + extra };
        }

        // ---------------- build a slice from progress ---------------------------------------

        private static Slice BuildSlice(LitigGameProgress p, double width)
        {
            var opt = (LitigGameOptions)p.GameDefinition.GameOptions;
            double m = opt.CostsMultiplier;                     // litigation-cost multiplier

            // Non-litigation parts (un-multiplied)
            double opportunity = p.OpportunityCost;
            double harm = p.HarmCost;

            // Filing
            double filing = p.PFiles
                ? (opt.PFilingCost -
                   (!p.DAnswers ? opt.PFilingCost_PortionSavedIfDDoesntAnswer : 0.0)) * m
                : 0.0;

            // Answering
            double answering = p.DAnswers ? opt.DAnswerCost * m : 0.0;

            // Bargaining – both parties pay per round
            int rounds = p.BargainingRoundsComplete;
            double bargaining = 2 * opt.PerPartyCostsLeadingUpToBargainingRound
                               * rounds * m;

            // Trying
            double trying = p.TrialOccurs
                ? (opt.PTrialCosts + opt.DTrialCosts) * m
                : 0.0;

            return new Slice(width,
                             opportunity,
                             harm,
                             filing,
                             answering,
                             bargaining,
                             trying);
        }

        // ---------------- helpers: merge, scale ---------------------------------------------

        private static List<Slice> MergeDuplicates(IEnumerable<Slice> slices)
        {
            var acc = new List<Slice>();
            foreach (var s in slices)
            {
                var hit = acc.FirstOrDefault(a => a.Matches(s));
                if (hit is null)
                    acc.Add(s);
                else
                    acc[acc.IndexOf(hit)] = hit.AddWidth(s.Width);
            }
            return acc;
        }

        private static List<Slice> ScaleToFour(IEnumerable<Slice> slices)
        {
            double peak = slices.Max(s => s.TotalCost);
            if (peak <= 0) return slices.ToList();

            double k = MaxStackHeight / peak;

            return slices.Select(s => s with
            {
                Opportunity = s.Opportunity * k,
                Harm = s.Harm * k,
                Filing = s.Filing * k,
                Answering = s.Answering * k,
                Bargaining = s.Bargaining * k,
                Trying = s.Trying * k
            }).ToList();
        }

        // ---------------- CSV ----------------------------------------------------------------

        private static string BuildCsv(IEnumerable<Slice> slices)
        {
            var sb = new StringBuilder(
                "Width,Opportunity,Harm,Filing,Answering,Bargaining,Trying,TotalCost\n");

            foreach (var s in slices)
                sb.AppendLine(string.Join(",",
                    s.Width.ToString("F6", CultureInfo.InvariantCulture),
                    s.Opportunity.ToString("G6", CultureInfo.InvariantCulture),
                    s.Harm.ToString("G6", CultureInfo.InvariantCulture),
                    s.Filing.ToString("G6", CultureInfo.InvariantCulture),
                    s.Answering.ToString("G6", CultureInfo.InvariantCulture),
                    s.Bargaining.ToString("G6", CultureInfo.InvariantCulture),
                    s.Trying.ToString("G6", CultureInfo.InvariantCulture),
                    s.TotalCost.ToString("G6", CultureInfo.InvariantCulture)));

            return sb.ToString();
        }

        // ---------------- TikZ builder -------------------------------------------------------

        private static string BuildTikz(IReadOnlyList<Slice> slices, bool presentation, string title)
        {
            // Aspect ratios: 20×16 cm (print) or 26.6666×15 cm (presentation)
            double W = presentation ? 26.6666 : 20.0;
            double H = presentation ? 15.0 : 16.0;
            double pad = 1.5;

            var outer = new TikzRectangle(0, 0, W, H);
            var panel = outer.ReducedByPadding(pad, pad + 1.0, pad, pad + 2.0);

            var yAxis = new TikzLine(
                new TikzPoint(panel.left, panel.bottom),
                new TikzPoint(panel.left, panel.top));

            var xAxis = new TikzLine(
                new TikzPoint(panel.left, panel.bottom),
                new TikzPoint(panel.right, panel.bottom));

            string[] fills = presentation ? PresentationFill : RegularFill;
            var code = new StringBuilder();

            string penColor = presentation ? "white" : "black";
            if (presentation)
                code.AppendLine(outer.DrawCommand("fill=black"));

            // Title at top (centered)
            var titleArea = new TikzRectangle(outer.left, panel.top, outer.right, outer.top);
            code.AppendLine(titleArea.DrawCommand($"draw=none, text={penColor}", $"\\huge {title}"));

            // Y-axis (0% to 100% with 10% intervals)
            var ticksY = Enumerable.Range(0, 11)
                                   .Select(i => (i * 0.1, (i * 10).ToString() + "\\%"))
                                   .ToList();
            code.AppendLine(yAxis.DrawAxis(
                $"{penColor}, very thin, dotted",
                ticksY,
                $"font=\\small, text={penColor}",
                "east",
                "Cost",
                "center",
                TikzHorizontalAlignment.Center,
                $"font=\\small, rotate=90, text={penColor}",
                -0.55, 0));

            // X-axis (include 0% origin)
            var ticksX = new List<(double, string)> { (0.0, "0\\%"), (1.0, "100\\%") };
            code.AppendLine(xAxis.DrawAxis(
                $"{penColor}, very thin",
                ticksX,
                $"font=\\small, text={penColor}",
                "north",
                "Proportion of Cases",
                "south",
                TikzHorizontalAlignment.Center,
                $"font=\\small, text={penColor}",
                0, -0.35));

            // Bars (stacked slices covering full width)
            var sliceList = slices.ToList();
            double totalWidth = sliceList.Sum(s => s.Width);
            if (Math.Abs(totalWidth - 1.0) > 1e-9)
            {
                int lastIndex = sliceList.Count - 1;
                sliceList[lastIndex] = sliceList[lastIndex] with
                {
                    Width = sliceList[lastIndex].Width + (1.0 - totalWidth)
                };
            }
            double xLeft = panel.left;
            foreach (var s in sliceList)
            {
                double sliceW = s.Width * panel.width;
                double yBase = panel.bottom;
                double[] seg = {
                    s.Opportunity, s.Harm, s.Filing,
                    s.Answering,   s.Bargaining, s.Trying
                };
                for (int i = 0; i < seg.Length; i++)
                {
                    if (seg[i] <= 1e-12) continue;
                    var r = new TikzRectangle(
                        xLeft, yBase,
                        xLeft + sliceW, yBase + seg[i]);
                    code.AppendLine(r.DrawCommand(
                        $"{fills[i]}, draw=black, very thin"));
                    yBase += seg[i];
                }
                xLeft += sliceW;
            }

            // Legend (scaled 0.5, centered below plot)
            code.AppendLine(
                $@"\draw ({panel.left + panel.width / 2},{panel.bottom}) node[draw=none] (legendbase) {{}};");
            code.AppendLine(@"\begin{scope}[align=center]");
            code.AppendLine($@"\matrix[scale=0.5, draw={penColor}, below=0.5cm of legendbase, nodes={{draw}}, column sep=0.1cm]{{");
            for (int i = 0; i < fills.Length; i++)
            {
                code.Append(
                    $@"\node[rectangle, draw, minimum width=0.5cm, minimum height=0.5cm, {fills[i]}]{{}}; & " +
                    $@"\node[draw=none, font=\small, text={penColor}]{{{ComponentLabels[i]}}};");
                code.AppendLine(i == fills.Length - 1 ? @" \\" : " &");
            }
            code.AppendLine("};\\end{scope}");

            string header = presentation ? "\\usepackage[sfdefault]{ClearSans}" : null;
            return TikzHelper.GetStandaloneDocument(
                code.ToString(),
                additionalHeaderInfo: header,
                additionalTikzLibraries: new() { "patterns", "positioning" });
        }
    }
}
