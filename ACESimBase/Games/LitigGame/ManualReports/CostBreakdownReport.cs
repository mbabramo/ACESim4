using ACESim;
using ACESimBase.Games.LitigGame;
using ACESimBase.Util.Mathematics;
using ACESimBase.Util.Tikz;
using ACESimBase.Util.Reporting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    public static class CostBreakdownReport
    {
        // ---------------------------------------------------------------------
        // constant styling -----------------------------------------------------
        const double MaxStackUnits = 4.0;            // old fixed y-max (print mode)

        static readonly string[] RegFill =
        {
            "pattern=north east lines, pattern color=green",
            "pattern=north west lines, pattern color=yellow",
            "pattern=dots,            pattern color=blue!30",
            "pattern=vertical lines,  pattern color=blue!60",
            "pattern=crosshatch,      pattern color=blue!90",
            "pattern=grid,            pattern color=red!70!black"
        };
        static readonly string[] PresFill =
        {
            "fill=green!85","fill=yellow!85","fill=blue!30",
            "fill=blue!60","fill=blue!90","fill=red!75"
        };
        static readonly string[] Labels =
            { "Opportunity","Harm","File","Answer","Bargaining","Trial" };

        // ---------------------------------------------------------------------
        // public API – three overloads ----------------------------------------

        /// <summary>Original interface – retains legacy behaviour.</summary>
        public static List<string> GenerateReport(
            IEnumerable<(LitigGameProgress p, double w)> raw,
            bool presentation = false,
            bool splitRareHarmPanel = true)
        {
            // legacy: pick a 4-unit right axis, no cross-diagram scaling
            var slices = ToNormalizedSlices(raw);
            var scale = splitRareHarmPanel
                ? ComputeScaling(slices, MaxStackUnits)
                : ComputeSinglePanelScaling(slices, MaxStackUnits);
            var options = raw.First().p == null ? null
             : (LitigGameOptions)raw.First().p
                   .GameDefinition.GameOptions;
            return BuildOutputs(
                slices, scale, presentation, splitRareHarmPanel, "", options);
        }

        /// <summary>
        ///  Caller fixes the right-axis top; left axis and widths are chosen to
        ///  keep area parity within the diagram.
        /// </summary>
        public static List<string> GenerateReport(
            IEnumerable<(LitigGameProgress p, double w)> raw,
            double rightAxisTop,
            bool presentation = false,
            bool splitRareHarmPanel = true)
        {
            var slices = ToNormalizedSlices(raw);
            var scale = splitRareHarmPanel
                ? ComputeScaling(slices, rightAxisTop)
                : ComputeSinglePanelScaling(slices, rightAxisTop);

            var options = raw.First().p == null ? null
             : (LitigGameOptions)raw.First().p
                   .GameDefinition.GameOptions;
            return BuildOutputs(
                slices, scale, presentation, splitRareHarmPanel,
                $"", options);
        }

        /// <summary>
        ///  Match the area-per-unit scale of a reference diagram so that areas
        ///  are comparable across charts.
        /// </summary>
        public static List<string> GenerateReport(
            IEnumerable<(LitigGameProgress p, double w)> raw,
            IEnumerable<(LitigGameProgress p, double w)> reference,
            double referenceRightAxisTop,
            bool presentation = false,
            bool splitRareHarmPanel = true)
        {
            var slices = ToNormalizedSlices(raw);
            var referenceSlices = ToNormalizedSlices(reference);
            var scale = splitRareHarmPanel
                ? ComputeScalingFromReference(
                      slices, referenceSlices, referenceRightAxisTop)
                : ComputeSinglePanelScaling(slices, referenceRightAxisTop);


            var options = raw.First().p == null ? null
             : (LitigGameOptions)raw.First().p
                   .GameDefinition.GameOptions;

            return BuildOutputs(
                slices, scale, presentation, splitRareHarmPanel,
                $"", options);
        }

        // ---------------------------------------------------------------------
        // core domain model – Slice -------------------------------------------
        internal sealed record Slice(
            double Width, double Opportunity, double Harm, double Filing,
            double Answer, double Bargaining, double Trial)
        {
            public double Total =>
                Opportunity + Harm + Filing + Answer + Bargaining + Trial;
        }

        static Slice BuildSlice(LitigGameProgress p, double w)
        {
            var o = (LitigGameOptions)p.GameDefinition.GameOptions;
            double m = o.CostsMultiplier;

            double opp = p.OpportunityCost;
            double harm = p.HarmCost;
            double file = p.PFiles
                ? (o.PFilingCost - (!p.DAnswers
                       ? o.PFilingCost_PortionSavedIfDDoesntAnswer
                       : 0)) * m
                : 0;
            double ans = p.DAnswers ? o.DAnswerCost * m : 0;
            double bar = 2 * o.PerPartyCostsLeadingUpToBargainingRound *
                          p.BargainingRoundsComplete * m;
            double tri = p.TrialOccurs ? (o.PTrialCosts + o.DTrialCosts) * m : 0;

            return new(w, opp, harm, file, ans, bar, tri);
        }

        // ---------------------------------------------------------------------
        // helpers – merge identical slices, legacy max-4 scaling -------------
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
                    Math.Abs(a.Bargaining - s.Bargaining) < 1e-7 &&
                    Math.Abs(a.Trial - s.Trial) < 1e-7);
                if (hit is null)
                    acc.Add(s);
                else
                    acc[acc.IndexOf(hit)] =
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
                Bargaining = s.Bargaining * k,
                Trial = s.Trial * k
            });
        }

        // ---------------------------------------------------------------------
        // public CSV ----------------------------------------------------------
        static string Csv(IEnumerable<Slice> ss)
        {
            var sb = new StringBuilder(
                "Width,Opportunity,Harm,Filing," +
                "Answering,Bargaining,Trying,Total\n");
            foreach (var s in ss)
                sb.AppendLine(string.Join(",",
                    s.Width.ToString("F6", CultureInfo.InvariantCulture),
                    s.Opportunity.ToString("G6", CultureInfo.InvariantCulture),
                    s.Harm.ToString("G6", CultureInfo.InvariantCulture),
                    s.Filing.ToString("G6", CultureInfo.InvariantCulture),
                    s.Answer.ToString("G6", CultureInfo.InvariantCulture),
                    s.Bargaining.ToString("G6", CultureInfo.InvariantCulture),
                    s.Trial.ToString("G6", CultureInfo.InvariantCulture),
                    s.Total.ToString("G6", CultureInfo.InvariantCulture)));
            return sb.ToString();
        }

        public sealed record AxisScalingInfo(
            double YMaxLeft,
            double YMaxRight,
            double XScaleLeft,   // = 0.5 / pLeft
            double XScaleRight,  // = 0.5 / pRight
            double AreaPerUnit); // common cm² per unit cost

        internal static CostBreakdownReport.AxisScalingInfo ComputeSinglePanelScaling(
            List<Slice> slices,
            double yMax)
        {
            double xScale = 1.0;                  // no horizontal compression
            return new(yMax, yMax, xScale, xScale, xScale / yMax);
        }

        internal static AxisScalingInfo ComputeScaling(
    List<Slice> slices, double rightAxisTop)
        {
            SplitMasses(slices, out var pL, out var pR);
            var (maxL, maxR) = TallestStacks(slices);

            /* -----------------------------------------------
               Area-parity condition:
                  (0.5/pL) / yMaxL  ==  (0.5/pR) / yMaxR
                  ⇒ yMaxL = yMaxR * (pR / pL)
            ----------------------------------------------- */
            double yR = rightAxisTop;
            double yL = yR * (pR / pL);                 // ← ratio **inverted**

            // enlarge uniformly if any bar would overflow
            double k = Math.Max(maxL / yL, maxR / yR);
            if (k > 1.0) { yL *= k; yR *= k; }

            double xL = 0.5 / pL, xR = 0.5 / pR;
            return new(yL, yR, xL, xR, xR / yR);
        }

        internal static AxisScalingInfo ComputeScalingFromReference(
            List<Slice> slices,
            List<Slice> reference,
            double referenceRightAxisTop)
        {
            // ---------- reference factors ------------------------------------
            SplitMasses(reference, out var pL1, out var pR1);
            double areaRef = (0.5 / pR1) / referenceRightAxisTop;

            // ---------- local diagram ---------------------------------------
            SplitMasses(slices, out var pL2, out var pR2);
            var (maxL2, maxR2) = TallestStacks(slices);

            double yR = (0.5 / pR2) / areaRef;          // eq.(5)
            double yL = yR * (pR2 / pL2);               // ← ratio **inverted**

            // fit-check: enlarge both axes uniformly if needed
            double k = Math.Max(maxL2 / yL, maxR2 / yR);
            if (k > 1.0) { yL *= k; yR *= k; }

            double xL = 0.5 / pL2, xR = 0.5 / pR2;
            return new(yL, yR, xL, xR, areaRef / k);
        }

        // ---------------------------------------------------------------------
        //  rectangle list in unit coordinates ---------------------------------
        sealed record ComponentRect(
            int SliceIndex, int Category, bool IsLeft,
            double X0, double X1, double Y0, double Y1);

        static List<ComponentRect> GetComponentRects(
            List<Slice> slices,
            AxisScalingInfo sc,
            bool splitRareHarmPanel)
        {
            var rects = new List<ComponentRect>();

            if (!splitRareHarmPanel)
            {
                // --- single-panel: place slices in original order, full width -----
                double x = 0.0;
                for (int idx = 0; idx < slices.Count; idx++)
                {
                    double w = slices[idx].Width * sc.XScaleLeft;   // == 1.0
                    AddRects(rects, slices[idx], true, idx, x, x + w, sc.YMaxLeft);
                    x += w;
                }
                return rects;
            }

            // --- split-panel path (old behaviour) ---------------------------------
            var left = slices.Where(s => s.Harm + s.Filing + s.Answer +
                                          s.Bargaining + s.Trial == 0).ToList();
            var right = slices.Except(left).ToList();

            double xL = 0.0;
            for (int i = 0; i < left.Count; i++)
            {
                double w = left[i].Width * sc.XScaleLeft;          // 0.5/pLeft
                AddRects(rects, left[i], true, i, xL, xL + w, sc.YMaxLeft);
                xL += w;
            }

            double xR = 0.5;
            for (int j = 0; j < right.Count; j++)
            {
                double w = right[j].Width * sc.XScaleRight;        // 0.5/pRight
                AddRects(rects, right[j], false, j, xR, xR + w, sc.YMaxRight);
                xR += w;
            }
            return rects;
        }


        static void AddRects(ICollection<ComponentRect> bag, Slice s,
                             bool isLeft, int idx, double x0, double x1, double yMax)
        {
            double y = 0;
            double[] seg =
            { s.Opportunity, s.Harm, s.Filing, s.Answer, s.Bargaining, s.Trial };
            for (int cat = 0; cat < 6; cat++)
            {
                if (seg[cat] <= 1e-12) continue;
                double y1 = y + seg[cat] / yMax;
                bag.Add(new(idx, cat, isLeft, x0, x1, y, y1));
                y = y1;
            }
        }

        // ---------------------------------------------------------------------
        //  full TikZ renderer using the geometry list -------------------------
        internal static string TikzScaled(
            List<Slice> slices,
            AxisScalingInfo sc,
            bool pres,
            string title,
            bool splitRareHarmPanel)
        {
            double W = pres ? 26.6666 : 15, H = pres ? 15 : 16;
            var outer = new TikzRectangle(0, 0, W, H);
            var pane = outer.ReducedByPadding(1.5, 2.5, 1.5, 1.5);
            string pen = pres ? "white" : "black";
            string[] fills = pres ? PresFill : RegFill;

            var sb = new StringBuilder();
            if (pres) sb.AppendLine(outer.DrawCommand("fill=black"));
            if (pres && !string.IsNullOrEmpty(title))
            {
                var head = new TikzRectangle(
                    outer.left, pane.top, outer.right, outer.top);
                sb.AppendLine(head.DrawCommand($"draw=none,text={pen}",
                    $"\\huge {title}"));
            }

            // --- draw bars ----------------------------------------------------
            double sx = pane.width, sy = pane.height;
            foreach (var rc in GetComponentRects(slices, sc, splitRareHarmPanel))
            {
                var box = new TikzRectangle(
                    pane.left + rc.X0 * sx,
                    pane.bottom + rc.Y0 * sy,
                    pane.left + rc.X1 * sx,
                    pane.bottom + rc.Y1 * sy);
                sb.AppendLine(box.DrawCommand(
                    $"{fills[rc.Category]},draw={pen},very thin"));
            }

            // --- axes ---------------------------------------------------------
            var yL = new TikzLine(
                new(pane.left, pane.bottom), new(pane.left, pane.top));
            var xB = new TikzLine(
                new(pane.left, pane.bottom), new(pane.right, pane.bottom));
            // ---- LEFT y-axis ------------------------------------------------------------
            var ticksLeft = BuildTicks(sc.YMaxLeft);

            double shiftYL = BestLabelShiftY(ticksLeft, pane.height);

            sb.AppendLine(yL.DrawAxis($"{pen},very thin", ticksLeft,
                $"font=\\small,text={pen}",          // tick-label style
                "east",
                "Cost",                              // axis title
                "center",
                TikzHorizontalAlignment.Center,
                $"font=\\small,rotate=90,text={pen}",
                -0.40,                               // X-shift (leftwards)
                shiftYL));                           // Y-shift (between ticks)

            if (splitRareHarmPanel)
            {
                // dashed divider
                var mid = new TikzLine(
                    new(pane.left + 0.5 * sx, pane.bottom - 0.4),
                    new(pane.left + 0.5 * sx, pane.top));
                sb.AppendLine(mid.DrawCommand($"{pen},dashed,very thin"));

                // right-hand y-axis
                var yR = new TikzLine(
                    new(pane.right, pane.bottom), new(pane.right, pane.top));
                sb.AppendLine(yR.DrawAxis($"{pen},very thin",
                    BuildTicks(sc.YMaxRight),
                     $"font=\\small,text={pen}", "west",
                    null, null, TikzHorizontalAlignment.Center,
                    $"font=\\small,text={pen}", 0.65, 0));
            }

            // x-axis (0 % to 100 %)
            var xTicks = new List<(double, string)> { (0, "0\\%"), (1, "100\\%") };

            // ------------------------------------------------ draw the baseline
            sb.AppendLine(xB.DrawAxis($"{pen},very thin", xTicks,
                $"font=\\small,text={pen}", "north",
                splitRareHarmPanel ? null : "Proportion of Cases",   // no caption in split mode
                "south", TikzHorizontalAlignment.Center,
                $"font=\\small,text={pen}", 0, -0.6));

            // ------------------------------------------------------------------ custom labels when split
            if (splitRareHarmPanel)
            {
                // pLeft and pRight from the x-scales
                double pLeft = 0.5 / sc.XScaleLeft;
                double pRight = 0.5 / sc.XScaleRight;

                string leftLbl = $"No\\ Harm\\ ({Pct(pLeft)})";
                string rightLbl = $"Harm\\ ({Pct(pRight)})";

                double yLabel = pane.bottom - 0.6;        // same vertical offset as former caption

                // left label at 25 %, anchored SOUTH so its bottom sits on yLabel
                sb.AppendLine(TikzHelper.DrawText(
                    pane.left + 0.25 * sx,
                    yLabel,
                    leftLbl,
                    $"font=\\small,text={pen},anchor=south"));

                // right label at 75 %
                sb.AppendLine(TikzHelper.DrawText(
                    pane.left + 0.75 * sx,
                    yLabel,
                    rightLbl,
                    $"font=\\small,text={pen},anchor=south"));
            }


            // --- legend -------------------------------------------------------
            // figure out which categories are non-zero across all slices
            bool[] used = new bool[Labels.Length];
            foreach (var s in slices)
            {
                if (s.Opportunity > 1e-12) used[0] = true;
                if (s.Harm > 1e-12) used[1] = true;
                if (s.Filing > 1e-12) used[2] = true;
                if (s.Answer > 1e-12) used[3] = true;
                if (s.Bargaining > 1e-12) used[4] = true;
                if (s.Trial > 1e-12) used[5] = true;
            }
            var active = Enumerable.Range(0, Labels.Length)
                                    .Where(i => used[i]).ToList();
            if (active.Count > 0)
            {
                sb.AppendLine(
                    $@"\draw ({pane.left + pane.width / 2},{pane.bottom}) node (B) {{}};");
                sb.AppendLine(@"\begin{scope}[align=center]");
                sb.AppendLine(
                    $@"\matrix[scale=0.5,draw={pen},below=0.5cm of B,nodes={{draw}},column sep=0.1cm]{{");
                for (int k = 0; k < active.Count; k++)
                {
                    int i = active[k];
                    sb.Append(
                        $@"\node[rectangle,draw,minimum width=0.5cm,minimum height=0.5cm,{fills[i]}]{{}}; & ");
                    sb.Append(
                        $@"\node[draw=none,font=\small,text={pen}]{{{Labels[i]}}};");
                    sb.AppendLine(k == active.Count - 1 ? @" \\" : " &");
                }
                sb.AppendLine("};\\end{scope}");
            }

            return TikzHelper.GetStandaloneDocument(
                sb.ToString(),
                additionalHeaderInfo: pres ? "\\usepackage[sfdefault]{ClearSans}" : null,
                additionalTikzLibraries: new() { "patterns", "positioning" });

        }


        // ---------------------------------------------------------------------
        //  master builder -----------------------------------------------------
        static List<string> BuildOutputs(
            List<Slice> slices,
            AxisScalingInfo sc,
            bool pres,
            bool splitRareHarmPanel,
            string subtitle,
            LitigGameOptions options = null)
        {
            string title =
                options is null
                ? subtitle
                : $"Costs: {options.CostsMultiplier}x; " +
                  $"Fee Shift: {options.LoserPaysMultiple}x {subtitle}";

            return new()
            {
                Csv(slices),
                TikzScaled(slices, sc, pres, title, splitRareHarmPanel),
                Csv(slices)
            };
        }

        // ---------------------------------------------------------------------
        //  utility routines used in scaling -----------------------------------
        static void SplitMasses(IEnumerable<Slice> slices,
                                out double pL, out double pR)
        {
            double l = 0, r = 0;
            foreach (var s in slices)
            {
                bool left = s.Harm + s.Filing + s.Answer +
                            s.Bargaining + s.Trial == 0;
                if (left) l += s.Width; else r += s.Width;
            }
            if (l <= 0 || r <= 0)
                throw new InvalidOperationException("one panel empty");
            pL = l; pR = r;
        }

        static (double tallestLeft, double tallestRight) TallestStacks(
            List<Slice> slices)
        {
            double leftMax = 0, rightMax = 0;
            foreach (var s in slices)
            {
                double total = s.Total;
                bool left = s.Harm + s.Filing + s.Answer +
                            s.Bargaining + s.Trial == 0;
                if (left) leftMax = Math.Max(leftMax, total);
                else rightMax = Math.Max(rightMax, total);
            }
            return (leftMax, rightMax);
        }

        static List<Slice> ToNormalizedSlices(
            IEnumerable<(LitigGameProgress p, double w)> raw)
        {
            double tot = raw.Sum(t => t.w);
            if (tot <= 0) throw new ArgumentException("weights");
            return Scale(Merge(
                    raw.Select(t => BuildSlice(t.p, t.w / tot))))
                   .ToList();
        }

        /// <summary>
        ///  Smallest right-axis top that lets every diagram in <paramref name="allDiagrams"/>
        ///  share one common area-per-unit scale without any bar overflowing.
        /// </summary>
        public static double FindMinimalSharedRightAxisTop(
            IEnumerable<IEnumerable<(LitigGameProgress p, double w)>> allDiagrams)
        {
            if (allDiagrams is null)
                throw new ArgumentNullException(nameof(allDiagrams));

            // materialise each diagram’s slices
            List<List<Slice>> sliceSets = allDiagrams
                .Select(diagram =>
                {
                    double totW = diagram.Sum(t => t.w);
                    if (totW <= 0) throw new ArgumentException("weights");

                    return diagram
                        .Select(t => BuildSlice(t.p, t.w / totW))
                        .ToList();
                })
                .ToList();

            return FindMinimalSharedRightAxisTop(sliceSets);
        }

        // ---------------------------------------------------------------------------
        // private slice-level worker (was omitted by mistake)
        internal static double FindMinimalSharedRightAxisTop(
            List<List<Slice>> allSliceSets)
        {
            if (allSliceSets is null || allSliceSets.Count == 0)
                throw new ArgumentException(nameof(allSliceSets));

            double candidate = 0.0;

            foreach (var slices in allSliceSets)
            {
                SplitMasses(slices, out var pLeft, out var pRight);
                var (maxLeft, maxRight) = TallestStacks(slices);

                // need R ≥ max( tallestRight ,
                //               tallestLeft × (pRight/pLeft) )
                double need = Math.Max(
                    maxRight,
                    maxLeft * (pRight / pLeft));

                if (need > candidate)
                    candidate = need;
            }
            return candidate;
        }


        #region Formatting

        private static double RoundDown1SigFig(double value)
        {
            if (value <= 0) return 0;
            double order = Math.Pow(10, Math.Floor(Math.Log10(value)));
            int first = (int)Math.Floor(value / order);
            return first * order;
        }

        // Build tick list using the new rule
        private static List<(double proportion, string label)> BuildTicks(double yMax)
        {
            double top = RoundDown1SigFig(yMax);           // axis top (below or equal)
            if (top == 0) top = 1;                         // guard for degenerate

            // determine tick count
            int firstDigit = (int)(top / Math.Pow(10, Math.Floor(Math.Log10(top))));
            int ticks = (top == 10) ? 10 : firstDigit;     // special case “10”

            double step = top / ticks;

            var list = new List<(double proportion, string label)>();
            for (int i = 0; i <= ticks; i++)
            {
                double val = i * step;
                // one significant figure label
                string lab = val.RoundToSignificantFigures(1)
                                .ToString(System.Globalization.CultureInfo.InvariantCulture);
                list.Add((proportion: val / yMax, label: lab));
            }
            return list;
        }

        // -----------------------------------------------------------------------------
        //  pick the tick–interval whose midpoint is closest to 0.5 and return the
        //  required ∆y (in diagram units) to move the label from 0.5 to that midpoint
        private static double BestLabelShiftY(
            IReadOnlyList<(double proportion, string label)> ticks,
            double axisHeight /* pane.height */ )
        {
            if (ticks.Count < 2) return 0;                   // nothing to do

            double bestMid = 0.5;
            double bestDist = double.MaxValue;

            for (int i = 0; i < ticks.Count - 1; i++)
            {
                double mid = 0.5 * (ticks[i].proportion + ticks[i + 1].proportion);
                double dist = Math.Abs(mid - 0.5);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestMid = mid;
                }
            }
            // convert from proportion (0…1 along the axis) to coordinates
            return (bestMid - 0.5) * axisHeight;
        }

        static string F(double v) =>
            v.RoundToSignificantFigures(2).ToString();   // 2 sig-figs max

        // -----------------------------------------------------------------------------
        // Format probability p (0-1) as a percentage string.
        // • If p ≥ 0.99, show the same number of decimal places as the *complement*
        //   (1-p).  E.g. 0.9999 → "99.99%", 0.999  → "99.9%".
        // • Otherwise fall back to the original 1- or 2-sig-fig rule.
        private static string Pct(double p)
        {
            if (p >= 0.99)
            {
                double complement = (1.0 - p) * 100.0;

                int decimals = 0;
                if (complement >= 1.0)
                    decimals = 0;
                else if (complement >= 0.1)
                    decimals = 1;
                else if (complement >= 0.01)
                    decimals = 2;
                else if (complement >= 0.001)
                    decimals = 3;
                else
                    decimals = 4;

                double pPct = p * 100.0;

                // Use ToString("G") to avoid unnecessary trailing zeros
                string pStr = Math.Round(pPct, decimals)
                              .ToString("G", CultureInfo.InvariantCulture);

                return pStr + "\\%";
            }
            else
            {
                int sig = p <= 0.10 ? 1 : 2;
                return (p * 100)
                    .RoundToSignificantFigures(sig)
                    .ToString(CultureInfo.InvariantCulture) + "\\%";
            }
        }




        #endregion
    }
}
