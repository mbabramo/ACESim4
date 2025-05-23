using ACESim;
using ACESimBase.Games.LitigGame;
using ACESimBase.Util.Tikz;
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
            if (totW <= 0) throw new ArgumentException("Total weight must be > 0.");

            var initial = raw
                .Select(r => BuildSlice(r.progress, r.weight / totW))
                .ToList();

            var merged = MergeDuplicates(initial);
            var ordered = merged.OrderBy(s => s.TotalCost).ToList();
            var scaled = ScaleToFour(ordered);

            // Return: CSV + TikZ (print) + CSV (dup) + TikZ (presentation) — mimics StageCostReport.
            return new()
            {
                BuildCsv(scaled),
                BuildTikz(scaled, false),
                BuildCsv(scaled),
                BuildTikz(scaled, true)
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
                if (hit is null) acc.Add(s); else acc[acc.IndexOf(hit)] = hit.AddWidth(s.Width);
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

        private static string BuildTikz(IReadOnlyList<Slice> slices, bool presentation)
        {
            // Aspect ratios: 20×16 (print) or 26.666×15 (presentation)
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
            if (presentation) code.AppendLine(outer.DrawCommand("fill=black"));

            // Y-axis
            code.AppendLine(yAxis.DrawAxis(
                "black, very thin",
                new() { (1.0, MaxStackHeight.ToString("0")) },
                "font=\\small",
                "east",
                "Cost",
                "center",
                TikzHorizontalAlignment.Center,
                "font=\\small, rotate=90",
                -0.55, 0));

            // X-axis
            code.AppendLine(xAxis.DrawAxis(
                "black, very thin",
                new() { (1.0, "100\\%") },
                "font=\\small",
                "north",
                "Proportion of Cases",
                "south",
                TikzHorizontalAlignment.Center,
                "font=\\small",
                0, -0.35));

            // Bars
            double xLeft = panel.left;
            foreach (var s in slices)
            {
                double sliceW = s.Width * panel.width;
                double yBase = panel.bottom;

                double[] seg = {
                    s.Opportunity, s.Harm, s.Filing,
                    s.Answering,   s.Bargaining, s.Trying
                };

                for (int i = 0; i < 6; i++)
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

            // Legend
            code.AppendLine(
                $@"\draw ({panel.left + panel.width / 2},{outer.bottom}) node (legend) {{}};");
            code.AppendLine(@"\begin{scope}[below=0.6cm of legend]");
            code.AppendLine(@"\matrix[draw, column sep=0.15cm]{");

            for (int i = 0; i < 6; i++)
            {
                code.Append(
                    $@"\node[minimum width=0.5cm, minimum height=0.5cm, {fills[i]}]{{}}; & " +
                    $@"\node[draw=none]{{\small {ComponentLabels[i]}}};");
                code.AppendLine(i == 5 ? @" \\" : " &");
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
