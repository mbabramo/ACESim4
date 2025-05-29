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
    /// <summary>
    /// Generates a visual breakdown of litigation costs by case slice.
    ///
    /// Each simulation outcome is turned into a weighted "slice" with cost values
    /// for six categories:
    /// - Opportunity (precaution)
    /// - Harm
    /// - Filing
    /// - Answer
    /// - Bargaining
    /// - Trial
    ///
    /// The diagram may be rendered as:
    /// (1) a single stacked bar across 100% of cases; or
    /// (2) a split-panel chart: precaution-only outcomes on the left,
    ///     all others on the right.
    ///
    /// In split mode:
    /// - Left and right panels each span 50% of the x-axis.
    /// - Each slice is shown with a width proportional to its case probability
    ///   (normalized to the total on that side).
    /// - The height of each bar component is scaled so that its drawn area
    ///   remains proportional to cost × probability — this preserves relative
    ///   visual area comparisons even when the chart is distorted.
    ///
    /// The report can optionally use a reference diagram's area-per-unit cost
    /// to preserve visual comparability across diagrams.
    ///
    /// Outputs include:
    /// - CSV summary
    /// - TikZ code for embedding into LaTeX
    /// </summary>
    public static class CostBreakdownReport
    {
        #region Constants

        // ---------------------------------------------------------------------
        // Constants and styling settings
        // ---------------------------------------------------------------------

        /// <summary>
        /// The maximum stack height for legacy non-scaled mode.
        /// </summary>
        const double MaxStackUnits = 4.0;

        /// <summary>
        /// Fill patterns for print mode (default color/line patterns).
        /// </summary>
        static readonly string[] RegFill =
        {
            "pattern=north east lines, pattern color=green",
            "pattern=north west lines, pattern color=yellow",
            "pattern=dots,            pattern color=blue!30",
            "pattern=vertical lines,  pattern color=blue!60",
            "pattern=crosshatch,      pattern color=blue!90",
            "pattern=grid,            pattern color=red!70!black"
        };

        /// <summary>
        /// Fill patterns for presentation mode (solid fill colors).
        /// </summary>
        static readonly string[] PresFill =
        {
            "fill=green!85","fill=yellow!85","fill=blue!30",
            "fill=blue!60","fill=blue!90","fill=red!75"
        };

        /// <summary>
        /// Cost category labels, used in order of plotting and in the legend.
        /// </summary>
        static readonly string[] Labels =
            { "Opportunity","Harm","File","Answer","Bargaining","Trial" };

        #endregion

        #region Report generation

        // ---------------------------------------------------------------------
        // Public entrypoints — three overloads for different report contexts
        // ---------------------------------------------------------------------

        /// <summary>
        /// Generate a cost breakdown report using default axis scaling
        /// (right-axis top fixed at 4.0).
        /// </summary>
        /// <param name="raw">Raw data (progress, weight)</param>
        /// <param name="presentation">Whether to use presentation styling</param>
        /// <param name="splitRareHarmPanel">Whether to use the split-panel view</param>
        /// <returns>List of strings: CSV, TikZ, CSV</returns>
        public static List<string> GenerateReport(
            IEnumerable<(LitigGameProgress p, double w)> raw,
            bool presentation = false,
            bool splitRareHarmPanel = true)
        {
            var slices = ToNormalizedSlices(raw);
            var scale = splitRareHarmPanel
                ? ComputeScaling(slices, MaxStackUnits)
                : ComputeSinglePanelScaling(slices, MaxStackUnits);

            var options = raw.First().p == null ? null
                : (LitigGameOptions)raw.First().p.GameDefinition.GameOptions;

            return BuildOutputs(slices, scale, presentation, splitRareHarmPanel, "", options);
        }

        /// <summary>
        /// Generate a cost breakdown using a specified right-axis maximum.
        /// Useful when controlling the vertical scaling manually.
        /// </summary>
        /// <param name="raw">Raw data (progress, weight)</param>
        /// <param name="rightAxisTop">Numeric top value of right y-axis</param>
        /// <param name="presentation">Whether to use presentation styling</param>
        /// <param name="splitRareHarmPanel">Whether to use the split-panel view</param>
        /// <returns>List of strings: CSV, TikZ, CSV</returns>
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
                : (LitigGameOptions)raw.First().p.GameDefinition.GameOptions;

            return BuildOutputs(slices, scale, presentation, splitRareHarmPanel, "", options);
        }

        /// <summary>
        /// Generate a cost breakdown report using a reference diagram
        /// to enforce a shared area-per-unit scale.
        /// This ensures visual comparability across multiple charts.
        /// </summary>
        /// <param name="raw">Raw data for the current chart</param>
        /// <param name="reference">Reference data from an earlier chart</param>
        /// <param name="referenceRightAxisTop">Right y-axis max used in reference</param>
        /// <param name="presentation">Whether to use presentation styling</param>
        /// <param name="splitRareHarmPanel">Whether to use split panel layout</param>
        /// <returns>List of strings: CSV, TikZ, CSV</returns>
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
                : (LitigGameOptions)raw.First().p.GameDefinition.GameOptions;

            return BuildOutputs(slices, scale, presentation, splitRareHarmPanel, "", options);
        }

#endregion

        #region Slices

        // ---------------------------------------------------------------------
        // Core domain type — Slice
        // ---------------------------------------------------------------------

        /// <summary>
        /// One aggregate outcome type, normalized to a weight (probability)
        /// and six cost components.
        /// </summary>
        internal sealed record Slice(
            double Width, double Opportunity, double Harm, double Filing,
            double Answer, double Bargaining, double Trial)
        {
            /// <summary>
            /// The total cost in this slice (sum of components).
            /// </summary>
            public double Total =>
                Opportunity + Harm + Filing + Answer + Bargaining + Trial;
        }

        /// <summary>
        /// Constructs a Slice from simulation data, applying cost multipliers
        /// and converting binary actions to conditional costs.
        /// </summary>
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
        // Slice consolidation and rescaling (used outside split mode)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Converts raw simulation outcomes into normalized, merged, and scaled slices.
        ///
        /// Steps:
        /// 1. Normalizes weights so that the total mass equals 1.0.
        /// 2. Converts each progress instance to a <see cref="Slice"/> using cost rules.
        /// 3. Merges duplicate slices (i.e., identical costs) to simplify output.
        /// 4. Applies vertical scaling if needed to keep bars within the fixed max height
        ///    used in non-split legacy diagrams.
        /// </summary>
        /// <param name="raw">Sequence of progress objects and their associated weights.</param>
        /// <returns>A list of distinct, normalized, scaled <see cref="Slice"/> objects.</returns>

        static List<Slice> ToNormalizedSlices(
            IEnumerable<(LitigGameProgress p, double w)> raw)
        {
            double tot = raw.Sum(t => t.w);
            if (tot <= 0) throw new ArgumentException("weights");
            return SinglePanelScale(Merge(
                    raw.Select(t => BuildSlice(t.p, t.w / tot))))
                   .ToList();
        }

        /// <summary>
        /// Merges slices that are identical in all cost components,
        /// summing their weights.
        /// This avoids clutter in the final report and prevents overplotting.
        /// </summary>
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

        /// <summary>
        /// Uniformly rescales all cost values so that the tallest total-cost bar
        /// fits within the legacy y-axis range MaxStackUnits.
        /// Used only in older one-panel diagrams.
        /// </summary>
        static IEnumerable<Slice> SinglePanelScale(IEnumerable<Slice> src)
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

#endregion

        #region Scaling

        // ---------------------------------------------------------------------
        // AxisScalingInfo – results of scaling computation
        // ---------------------------------------------------------------------

        /// <summary>
        /// Encapsulates axis maxima and width scalings computed for a diagram.
        /// Used by TikZ generation and layout logic.
        /// </summary>
        public sealed record AxisScalingInfo(
            double YMaxLeft,     // top value on the left y-axis
            double YMaxRight,    // top value on the right y-axis
            double XScaleLeft,   // cm per unit of pLeft; = 0.5 / pLeft
            double XScaleRight,  // cm per unit of pRight; = 0.5 / pRight
            double AreaPerUnit   // final width/height ratio (cm² per cost unit)
        );

        /// <summary>
        /// Computes a one-panel layout: full width, common y-axis, single scale.
        /// Used for legacy or non-split diagrams.
        /// </summary>
        internal static AxisScalingInfo ComputeSinglePanelScaling(
            List<Slice> slices,
            double yMax)
        {
            double xScale = 1.0;  // entire width
            return new(yMax, yMax, xScale, xScale, xScale / yMax);
        }

        /// <summary>
        /// Computes left/right y-axis maxima and width scaling for a split-panel
        /// diagram, ensuring that area remains proportional for all components.
        /// 
        /// Formula:
        ///     yMaxLeft = yMaxRight × (pRight / pLeft)
        /// </summary>
        internal static AxisScalingInfo ComputeScaling(
            List<Slice> slices, double rightAxisTop)
        {
            SplitMasses(slices, out var pL, out var pR);
            var (maxL, maxR) = TallestStacks(slices);

            double yR = rightAxisTop;
            double yL = yR * (pR / pL);  // ensure area parity

            // If any bar is taller than axis allows, scale both axes up
            double k = Math.Max(maxL / yL, maxR / yR);
            if (k > 1.0) { yL *= k; yR *= k; }

            double xL = 0.5 / pL, xR = 0.5 / pR;
            return new(yL, yR, xL, xR, xR / yR);
        }

        /// <summary>
        /// Computes axis maxima for a new diagram based on the scale used in a
        /// reference diagram. Ensures cross-diagram area comparability.
        /// 
        /// Uses the reference area-per-unit (Aref) and then adjusts local axes:
        ///     yMaxRight = (0.5 / pRight) / Aref
        ///     yMaxLeft  = yMaxRight × (pRight / pLeft)
        /// </summary>
        internal static AxisScalingInfo ComputeScalingFromReference(
            List<Slice> slices,
            List<Slice> reference,
            double referenceRightAxisTop)
        {
            SplitMasses(reference, out var pL1, out var pR1);
            double areaRef = (0.5 / pR1) / referenceRightAxisTop;

            SplitMasses(slices, out var pL2, out var pR2);
            var (maxL2, maxR2) = TallestStacks(slices);

            double yR = (0.5 / pR2) / areaRef;
            double yL = yR * (pR2 / pL2);  // ensure area parity

            double k = Math.Max(maxL2 / yL, maxR2 / yR);
            if (k > 1.0) { yL *= k; yR *= k; }

            double xL = 0.5 / pL2, xR = 0.5 / pR2;
            return new(yL, yR, xL, xR, areaRef / k);
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

#endregion

        #region Geometry in unit coordinates

        // ---------------------------------------------------------------------
        // Geometry for layout – bar segments in unit coordinates
        // ---------------------------------------------------------------------

        /// <summary>
        /// Represents one visual rectangle (bar segment) in unit coordinates [0,1] × [0,1].
        /// Each rectangle corresponds to one cost component of one slice.
        /// </summary>
        sealed record ComponentRect(
            int SliceIndex,      // index within its panel (left or right)
            int Category,        // index into Labels (0=Opportunity … 5=Trial)
            bool IsLeft,         // whether this slice was assigned to the left panel
            double X0, double X1,// horizontal start/end (unit coordinates)
            double Y0, double Y1 // vertical start/end (unit coordinates)
        );

        /// <summary>
        /// Computes the list of rectangle components needed to draw the diagram.
        /// Each slice may produce multiple stacked rectangles.
        /// 
        /// In split-panel mode:
        ///   - Slices are split into left/right panels.
        ///   - Horizontal scaling is done independently for each panel.
        /// 
        /// In single-panel mode:
        ///   - Slices are placed sequentially left-to-right across full width.
        /// </summary>
        static List<ComponentRect> GetComponentRects(
            List<Slice> slices,
            AxisScalingInfo sc,
            bool splitRareHarmPanel)
        {
            var rects = new List<ComponentRect>();

            if (!splitRareHarmPanel)
            {
                // --- Single panel: map all slices across full width ------------
                double x = 0.0;
                for (int idx = 0; idx < slices.Count; idx++)
                {
                    double w = slices[idx].Width * sc.XScaleLeft;   // XScaleLeft == 1.0 in this mode
                    AddRects(rects, slices[idx], true, idx, x, x + w, sc.YMaxLeft);
                    x += w;
                }
                return rects;
            }

            // --- Split panel: divide into left/right by content ----------------
            var left = slices.Where(s => s.Harm + s.Filing + s.Answer +
                                          s.Bargaining + s.Trial == 0).ToList();
            var right = slices.Except(left).ToList();

            double xL = 0.0;
            for (int i = 0; i < left.Count; i++)
            {
                double w = left[i].Width * sc.XScaleLeft;          // 0.5 / pLeft
                AddRects(rects, left[i], true, i, xL, xL + w, sc.YMaxLeft);
                xL += w;
            }

            double xR = 0.5;
            for (int j = 0; j < right.Count; j++)
            {
                double w = right[j].Width * sc.XScaleRight;        // 0.5 / pRight
                AddRects(rects, right[j], false, j, xR, xR + w, sc.YMaxRight);
                xR += w;
            }

            return rects;
        }

        /// <summary>
        /// Decomposes one slice into its stack of rectangles.
        /// Adds a bar segment for each non-zero cost component.
        /// </summary>
        static void AddRects(ICollection<ComponentRect> bag, Slice s,
                             bool isLeft, int idx, double x0, double x1, double yMax)
        {
            double y = 0;
            double[] seg =
            { s.Opportunity, s.Harm, s.Filing, s.Answer, s.Bargaining, s.Trial };
            for (int cat = 0; cat < 6; cat++)
            {
                if (seg[cat] <= 1e-12) continue;
                double y1 = y + seg[cat] / yMax; // height scaled to y-axis max
                bag.Add(new(idx, cat, isLeft, x0, x1, y, y1));
                y = y1;
            }
        }

#endregion

        #region Tikz

        // ---------------------------------------------------------------------
        // TikZ diagram renderer
        // ---------------------------------------------------------------------

        /// <summary>
        /// Renders the full cost breakdown diagram in TikZ.
        /// Draws all rectangles, axes, labels, legend, and optional split logic.
        /// </summary>
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

            // Background fill (black in presentation mode)
            if (pres)
                sb.AppendLine(outer.DrawCommand("fill=black"));

            // Title
            if (pres && !string.IsNullOrEmpty(title))
            {
                var head = new TikzRectangle(
                    outer.left, pane.top, outer.right, outer.top);
                sb.AppendLine(head.DrawCommand($"draw=none,text={pen}",
                    $"\\huge {title}"));
            }

            // --- Draw cost rectangles -----------------------------------------
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

            // --- Draw y-axis on the left --------------------------------------
            var yL = new TikzLine(
                new(pane.left, pane.bottom), new(pane.left, pane.top));
            var ticksLeft = BuildTicks(sc.YMaxLeft);
            double shiftYL = BestLabelShiftY(ticksLeft, pane.height);
            sb.AppendLine(yL.DrawAxis($"{pen},very thin", ticksLeft,
                $"font=\\small,text={pen}", "east",
                "Cost", "center", TikzHorizontalAlignment.Center,
                $"font=\\small,rotate=90,text={pen}",
                -0.40, shiftYL));

            // --- Optional split mode logic -------------------------------------
            if (splitRareHarmPanel)
            {
                // Dashed divider between left/right panels
                var mid = new TikzLine(
                    new(pane.left + 0.5 * sx, pane.bottom - 0.4),
                    new(pane.left + 0.5 * sx, pane.top));
                sb.AppendLine(mid.DrawCommand($"{pen},dashed,very thin"));

                // Right-side y-axis
                var yR = new TikzLine(
                    new(pane.right, pane.bottom), new(pane.right, pane.top));
                sb.AppendLine(yR.DrawAxis($"{pen},very thin",
                    BuildTicks(sc.YMaxRight),
                    $"font=\\small,text={pen}", "west",
                    null, null, TikzHorizontalAlignment.Center,
                    $"font=\\small,text={pen}", 0.65, 0));
            }

            // --- Draw x-axis ---------------------------------------------------
            var xB = new TikzLine(
                new(pane.left, pane.bottom), new(pane.right, pane.bottom));
            var xTicks = new List<(double, string)> { (0, "0\\%"), (1, "100\\%") };

            sb.AppendLine(xB.DrawAxis($"{pen},very thin", xTicks,
                $"font=\\small,text={pen}", "north",
                splitRareHarmPanel ? null : "Proportion of Cases",
                "south", TikzHorizontalAlignment.Center,
                $"font=\\small,text={pen}", 0, -0.6));

            // --- Panel label captions in split mode ----------------------------
            if (splitRareHarmPanel)
            {
                double pLeft = 0.5 / sc.XScaleLeft;
                double pRight = 0.5 / sc.XScaleRight;

                string leftLbl = $"No\\ Harm\\ ({Pct(pLeft)})";
                string rightLbl = $"Harm\\ ({Pct(pRight)})";
                double yLabel = pane.bottom - 0.6;

                sb.AppendLine(TikzHelper.DrawText(
                    pane.left + 0.25 * sx,
                    yLabel,
                    leftLbl,
                    $"font=\\small,text={pen},anchor=south"));

                sb.AppendLine(TikzHelper.DrawText(
                    pane.left + 0.75 * sx,
                    yLabel,
                    rightLbl,
                    $"font=\\small,text={pen},anchor=south"));
            }

            // --- Legend (only includes used categories) ------------------------
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

            // Wrap everything in a LaTeX standalone document
            return TikzHelper.GetStandaloneDocument(
                sb.ToString(),
                additionalHeaderInfo: pres ? "\\usepackage[sfdefault]{ClearSans}" : null,
                additionalTikzLibraries: new() { "patterns", "positioning" });
        }

        /// <summary>
        /// Generates CSV and TikZ strings given slices and layout parameters.
        /// Used by all report overloads.
        /// </summary>
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

#endregion

        #region Cross diagram axis normalization

        // ---------------------------------------------------------------------
        // Cross-diagram axis normalization utility
        // ---------------------------------------------------------------------

        /// <summary>
        /// Computes the smallest right-axis maximum that will allow all diagrams
        /// in a given batch to adopt a shared visual scale without clipping.
        ///
        /// For each diagram, we determine:
        ///     requiredRight = max(maxRightCost,
        ///                         maxLeftCost × (pRight / pLeft))
        ///
        /// This ensures that if the caller uses this R-value in each call to
        /// ComputeScaling(), the entire figure will fit without distortion.
        /// </summary>
        public static double FindMinimalSharedRightAxisTop(
            IEnumerable<IEnumerable<(LitigGameProgress p, double w)>> allDiagrams)
        {
            if (allDiagrams is null)
                throw new ArgumentNullException(nameof(allDiagrams));

            // Convert each dataset to its slice list
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

        /// <summary>
        /// Slice-level implementation of FindMinimalSharedRightAxisTop().
        /// </summary>
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

                // Required R-axis top to fit everything with proper area parity
                double need = Math.Max(
                    maxRight,
                    maxLeft * (pRight / pLeft));

                if (need > candidate)
                    candidate = need;
            }

            return candidate;
        }

#endregion

        #region Formatting

        // ---------------------------------------------------------------------
        // Formatting and tick-building utilities
        // ---------------------------------------------------------------------

        /// <summary>
        /// Rounds a value down to the next lower 1-significant-figure number.
        /// E.g., 4.6 → 4.0, 7652 → 7000.
        /// </summary>
        private static double RoundDown1SigFig(double value)
        {
            if (value <= 0) return 0;
            double order = Math.Pow(10, Math.Floor(Math.Log10(value)));
            int first = (int)Math.Floor(value / order);
            return first * order;
        }

        /// <summary>
        /// Builds tick marks for a y-axis with:
        /// - top value rounded down to one significant figure;
        /// - number of ticks = first digit of top (or 10 if top == 10);
        /// - labels printed with one significant figure;
        /// - each tick label positioned by its proportion of axis height.
        /// </summary>
        private static List<(double proportion, string label)> BuildTicks(double yMax)
        {
            double top = RoundDown1SigFig(yMax);
            if (top == 0) top = 1;

            int firstDigit = (int)(top / Math.Pow(10, Math.Floor(Math.Log10(top))));
            int ticks = (top == 10) ? 10 : firstDigit;

            double step = top / ticks;

            var list = new List<(double proportion, string label)>();
            for (int i = 0; i <= ticks; i++)
            {
                double val = i * step;
                string lab = val.RoundToSignificantFigures(1)
                                .ToString(CultureInfo.InvariantCulture);
                list.Add((proportion: val / yMax, label: lab));
            }

            return list;
        }

        /// <summary>
        /// Computes the vertical offset needed to position the "Cost" label
        /// between two tick marks that straddle the vertical midpoint (0.5).
        /// This avoids overlapping tick numbers and centers the label.
        /// </summary>
        private static double BestLabelShiftY(
            IReadOnlyList<(double proportion, string label)> ticks,
            double axisHeight)
        {
            if (ticks.Count < 2) return 0;

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

            return (bestMid - 0.5) * axisHeight;
        }

        /// <summary>
        /// Formats a numeric value (0–1) as a percentage string suitable for TikZ.
        ///
        /// - If p ≥ 0.99, it avoids rounding to 100%. Instead it computes the complement (1-p)
        ///   and uses the same number of decimals as required to express that difference.
        ///   E.g. p = 0.9999 → "99.99%", p = 0.999 → "99.9%", etc.
        ///
        /// - Otherwise, uses 1 or 2 significant figures:
        ///     - 1 sig fig if ≤ 10%
        ///     - 2 sig fig if > 10%
        /// </summary>
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

        // ---------------------------------------------------------------------
        // CSV export (used in text-based reports)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Converts the slice list to a comma-separated string with labeled columns.
        /// </summary>
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

        #endregion


    }
}



