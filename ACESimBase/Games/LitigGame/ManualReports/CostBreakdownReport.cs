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
    public static class CostBreakdownReport
    {
        const double MaxStackUnits = 4.0;                    // top of the y-axis

        static readonly string[] RegFill =
        {
            "pattern=north east lines, pattern color=green",
            "pattern=north west lines, pattern color=yellow",
            "pattern=dots,            pattern color=blue!60!black",
            "pattern=vertical lines,  pattern color=blue!80!black",
            "pattern=crosshatch,      pattern color=blue!80!black",
            "pattern=grid,            pattern color=red!70!black"
        };
        static readonly string[] PresFill =
        {
            "fill=green!85","fill=yellow!85","fill=blue!75!black",
            "fill=blue!70","fill=blue!85","fill=red!75"
        };
        static readonly string[] Labels =
            { "Opportunity","Harm","File","Answer","Bargaining","Trial" };

        public static List<string> GenerateReport(
            List<(GameProgress theProgress, double weight)> gp) =>
            GenerateReport(gp.Select(g =>
                ((LitigGameProgress)g.theProgress, g.weight)));

        public static List<string> GenerateReport(
            IEnumerable<(LitigGameProgress p, double w)> raw,
            bool presentation = false)
        {
            double totW = raw.Sum(r => r.w);
            if (totW <= 0) throw new ArgumentException("weights");

            var slices = raw.Select(r => BuildSlice(r.p, r.w / totW)).ToList();
            slices = Merge(slices);
            slices = slices.OrderByDescending(s => s.Opportunity)
                           .ThenBy(s => s.Harm)
                           .ThenBy(s => s.Total).ToList();     // new ordering
            slices = Scale(slices).ToList();

            var opt = (LitigGameOptions)raw.First().p.GameDefinition.GameOptions;
            string ttl = $"Costs: {opt.CostsMultiplier}x; Fee Shift: {opt.LoserPaysMultiple}x";
            return new()
            {
                Csv(slices),
                Tikz(slices,false,null),              // print mode - no title
                Csv(slices),
                Tikz(slices,true,ttl)                 // presentation with title
            };
        }

        #region slice helpers
        sealed record Slice(
            double Width, double Opportunity, double Harm, double Filing,
            double Answer, double Bargain, double Try)
        {
            public double Total =>
                Opportunity + Harm + Filing + Answer + Bargain + Try;
        }

        static Slice BuildSlice(LitigGameProgress p, double w)
        {
            var o = (LitigGameOptions)p.GameDefinition.GameOptions;
            double m = o.CostsMultiplier;

            double opp = p.OpportunityCost, harm = p.HarmCost;
            double file = p.PFiles ? (o.PFilingCost -
                (!p.DAnswers ? o.PFilingCost_PortionSavedIfDDoesntAnswer : 0)) * m : 0;
            double ans = p.DAnswers ? o.DAnswerCost * m : 0;
            double bar = 2 * o.PerPartyCostsLeadingUpToBargainingRound *
                       p.BargainingRoundsComplete * m;
            double tri = p.TrialOccurs ? (o.PTrialCosts + o.DTrialCosts) * m : 0;

            return new(w, opp, harm, file, ans, bar, tri);
        }

        static List<Slice> Merge(IEnumerable<Slice> src)
        {
            var acc = new List<Slice>();
            foreach (var s in src)
            {
                var hit = acc.FirstOrDefault(a =>
                    Math.Abs(a.Opportunity - s.Opportunity) < 1e-7 &&
                    Math.Abs(a.Harm - s.Harm) < 1e-7 &&
                    Math.Abs(a.Filing - s.Filing) < 1e-7 &&
                    Math.Abs(a.Answer - s.Answer) < 1e-7 &&
                    Math.Abs(a.Bargain - s.Bargain) < 1e-7 &&
                    Math.Abs(a.Try - s.Try) < 1e-7);
                if (hit is null) acc.Add(s);
                else acc[acc.IndexOf(hit)] =
                        hit with { Width = hit.Width + s.Width };
            }
            return acc;
        }

        static IEnumerable<Slice> Scale(IEnumerable<Slice> src)
        {
            double peak = src.Max(s => s.Total);
            double k = peak > MaxStackUnits ? MaxStackUnits / peak : 1;
            return src.Select(s => s with
            {
                Opportunity = s.Opportunity * k,
                Harm = s.Harm * k,
                Filing = s.Filing * k,
                Answer = s.Answer * k,
                Bargain = s.Bargain * k,
                Try = s.Try * k
            });
        }
        #endregion

        #region csv
        static string Csv(IEnumerable<Slice> ss)
        {
            var sb = new StringBuilder("Width,Opportunity,Harm,Filing," +
                "Answering,Bargaining,Trying,Total\n");
            foreach (var s in ss)
                sb.AppendLine(string.Join(",",
                    s.Width.ToString("F6", CultureInfo.InvariantCulture),
                    s.Opportunity.ToString("G6", CultureInfo.InvariantCulture),
                    s.Harm.ToString("G6", CultureInfo.InvariantCulture),
                    s.Filing.ToString("G6", CultureInfo.InvariantCulture),
                    s.Answer.ToString("G6", CultureInfo.InvariantCulture),
                    s.Bargain.ToString("G6", CultureInfo.InvariantCulture),
                    s.Try.ToString("G6", CultureInfo.InvariantCulture),
                    s.Total.ToString("G6", CultureInfo.InvariantCulture)));
            return sb.ToString();
        }
        #endregion

        #region tikz
        static string Tikz(IList<Slice> s, bool pres, string title)
        {
            double W = pres ? 26.6666 : 15, H = pres ? 15 : 16;
            var outer = new TikzRectangle(0, 0, W, H);
            var panel = outer.ReducedByPadding(1.5, 2.5, 1.5, 1.5); // extra bottom space

            string pen = pres ? "white" : "black";
            string[] fills = pres ? PresFill : RegFill;
            double scaleY = panel.height / MaxStackUnits;

            var sb = new StringBuilder();
            if (pres) sb.AppendLine(outer.DrawCommand("fill=black"));

            if (pres && !string.IsNullOrEmpty(title))
            {
                var tArea = new TikzRectangle(
                    outer.left, panel.top, outer.right, outer.top);
                sb.AppendLine(tArea.DrawCommand($"draw=none,text={pen}",
                    $"\\huge {title}"));
            }

            // axes
            var yAxis = new TikzLine(new TikzPoint(panel.left, panel.bottom),
                                   new TikzPoint(panel.left, panel.top));
            var xAxis = new TikzLine(new TikzPoint(panel.left, panel.bottom),
                                   new TikzPoint(panel.right, panel.bottom));

            // y-ticks 0-4
            var yTicks = Enumerable.Range(0, 5)
                                 .Select(i => ((double)i / 4, i.ToString()))
                                 .ToList();
            sb.AppendLine(yAxis.DrawAxis(
                $"{pen},very thin",
                yTicks, $"font=\\small,text={pen}", "east",
                "Cost", "center", TikzHorizontalAlignment.Center,
                $"font=\\small,rotate=90,text={pen}", -0.65, 0));

            // x-axis (only 0 and 100)
            var xTicks = new List<(double, string)> { (0, "0\\%"), (1, "100\\%") };
            sb.AppendLine(xAxis.DrawAxis(
                $"{pen},very thin",
                xTicks, $"font=\\small,text={pen}", "north",
                "Proportion of Cases", "south", TikzHorizontalAlignment.Center,
                $"font=\\small,text={pen}", 0, -0.6)); // lowered

            // bars
            double totalW = s.Sum(a => a.Width);
            if (Math.Abs(totalW - 1) > 1e-9)
                s[^1] = s[^1] with { Width = s[^1].Width + (1 - totalW) };

            double x = panel.left;
            foreach (var sl in s)
            {
                double sw = sl.Width * panel.width;
                double y = panel.bottom;
                double[] seg ={sl.Opportunity,sl.Harm,sl.Filing,
                              sl.Answer,sl.Bargain,sl.Try};
                for (int i = 0; i < seg.Length; i++)
                {
                    if (seg[i] <= 1e-12) continue;
                    var r = new TikzRectangle(
                        x, y, x + sw, y + seg[i] * scaleY);
                    sb.AppendLine(r.DrawCommand($"{fills[i]},draw={pen},very thin"));
                    y += seg[i] * scaleY;
                }
                x += sw;
            }


            // legend
            sb.AppendLine(
                $@"\draw ({panel.left + panel.width / 2},{panel.bottom}) node (B) {{}};");
            sb.AppendLine(@"\begin{scope}[align=center]");
            sb.AppendLine($@"\matrix[scale=0.5,draw={pen},below=0.5cm of B,"
                         + @"nodes={draw},column sep=0.1cm]{");
            for (int i = 0; i < fills.Length; i++)
            {
                sb.Append(@$"\node[rectangle,draw,minimum width=0.5cm,"
                           + $"minimum height=0.5cm,{fills[i]}]{{}}; & "
                           + $@"\node[draw=none,font=\small,text={pen}]{{{Labels[i]}}};");
                sb.AppendLine(i == fills.Length - 1 ? @" \\" : " &");
            }
            sb.AppendLine("};\\end{scope}");

            string header = pres ? "\\usepackage[sfdefault]{ClearSans}" : null;
            return TikzHelper.GetStandaloneDocument(
                sb.ToString(), additionalHeaderInfo: header,
                additionalTikzLibraries: new() { "patterns", "positioning" });
        }
        #endregion
    }
}
