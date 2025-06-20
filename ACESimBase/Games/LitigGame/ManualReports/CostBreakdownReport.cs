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

        public const double LightPanelWidth = 15.0;      // W when pres == false
        public const double LightPanelHeight = 16.0;      // H when pres == false
        public const double PresPanelWidth = 26.6666;   // W when pres == true
        public const double PresPanelHeight = 15.0;      // H when pres == true
        private const double MinPadLeftForLabels   = 0.45; // ≥ |yAxisLabelOffsetLeft|
        private const double MinPadBottomForLabels = 0.75; // ≥ xAxisLabelOffsetDown + tick height
        private const double MinPadTopForLegend    = 0.60; // room for legend title line

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
        // Public entry-points
        // ---------------------------------------------------------------------

        public static List<string> GenerateReports(
            IEnumerable<(LitigGameProgress p, double w)> raw,
            double rightAxisTop,
            bool splitRareHarmPanel = true)
        {
            var slices = ToNormalizedSlices(raw);

            bool useSplit = splitRareHarmPanel && HasTwoPanels(slices);

            if (!useSplit) // one-panel diagrams
                slices = SinglePanelScale(slices, rightAxisTop).ToList();

            var scale = useSplit
                ? ComputeScaling(slices, rightAxisTop)
                : ComputeSinglePanelScaling(slices, rightAxisTop);

            var options = raw.First().p == null ? null
                : (LitigGameOptions)raw.First().p.GameDefinition.GameOptions;

            return BuildOutputs(slices, scale, useSplit, "", options);
        }

        /// <inheritdoc cref="GenerateReports(IEnumerable{(LitigGameProgress,double)},double,bool)"/>
        public static List<string> GenerateReports(
            IEnumerable<(LitigGameProgress p, double w)> raw,
            IEnumerable<(LitigGameProgress p, double w)> reference,
            double referenceRightAxisTop,
            bool splitRareHarmPanel = true)
        {
            var slices = ToNormalizedSlices(raw);
            var referenceSlices = ToNormalizedSlices(reference);

            bool useSplit = splitRareHarmPanel
                            && HasTwoPanels(slices)
                            && HasTwoPanels(referenceSlices);

            if (!useSplit) // legacy one-panel diagrams
                slices = SinglePanelScale(slices, referenceRightAxisTop).ToList();

            var scale = useSplit
                ? ComputeScalingFromReference(
                      slices, referenceSlices, referenceRightAxisTop)
                : ComputeSinglePanelScaling(slices, referenceRightAxisTop);

            var options = raw.First().p == null ? null
                : (LitigGameOptions)raw.First().p.GameDefinition.GameOptions;

            return BuildOutputs(slices, scale, useSplit, "", options);
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
        public sealed record Slice(
            double width, double opportunity, double harm, double filing,
            double answer, double bargaining, double trial)
        {
            /// <summary>
            /// The total cost in this slice (sum of components).
            /// </summary>
            public double Total =>
                opportunity + harm + filing + answer + bargaining + trial;
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
            double totalWeight = raw.Sum(t => t.w);
            if (totalWeight <= 0)
                throw new ArgumentException(nameof(raw));

            // BuildSlice applies cost rules; Merge collapses identical slices.
            var tentativeResult = Merge(raw.Select(t => BuildSlice(t.p, t.w / totalWeight))).ToList();
            tentativeResult = RemoveTriviallySmallAreaSlices(tentativeResult);

            // Now normalize again
            totalWeight = tentativeResult.Sum(t => t.width);
            var result = tentativeResult.Select(s => s with
            {
                width = s.width / totalWeight, // normalize to total mass = 1.0
            }).ToList();

            return result;
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
                    Math.Abs(a.opportunity - s.opportunity) < 1e-7 &&
                    Math.Abs(a.harm - s.harm) < 1e-7 &&
                    Math.Abs(a.filing - s.filing) < 1e-7 &&
                    Math.Abs(a.answer - s.answer) < 1e-7 &&
                    Math.Abs(a.bargaining - s.bargaining) < 1e-7 &&
                    Math.Abs(a.trial - s.trial) < 1e-7);
                if (hit is null)
                    acc.Add(s);
                else
                    acc[acc.IndexOf(hit)] =
                        hit with { width = hit.width + s.width };
            }
            return acc;
        }

        /// <summary>
        /// Uniformly rescales all cost values so that the tallest total-cost bar
        /// fits within the legacy y-axis range MaxStackUnits.
        /// Used only in older one-panel diagrams.
        /// </summary>
        static IEnumerable<Slice> SinglePanelScale(IEnumerable<Slice> src, double yAxisTop)
        {
            double peak = src.Max(s => s.Total);
            double k = peak > yAxisTop ? yAxisTop / peak : 1;
            return src.Select(s => s with
            {
                opportunity = s.opportunity * k,
                harm = s.harm * k,
                filing = s.filing * k,
                answer = s.answer * k,
                bargaining = s.bargaining * k,
                trial = s.trial * k
            });
        }

        #endregion

        #region Scaling

        // ---------------------------------------------------------------------
        // AxisScalingInfo – results of scaling computation
        // ---------------------------------------------------------------------

        /// <summary>
        /// True only if the slice set contains at least one “left-panel”
        /// slice (no dispute costs) *and* at least one “right-panel” slice
        /// (has dispute costs).  When this is false we fall back to the
        /// simple one-panel layout.
        /// </summary>
        internal static bool HasTwoPanels(List<Slice> slices)
        {
            bool hasLeft = false;
            bool hasRight = false;

            foreach (var s in slices)
            {
                bool left = s.harm + s.filing + s.answer +
                            s.bargaining + s.trial == 0;
                if (left) hasLeft = true;
                else hasRight = true;

                if (hasLeft && hasRight)
                    return true;
            }
            return false;
        }

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
            slices = RemoveTriviallySmallAreaSlices(slices);

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

        internal static List<AxisScalingInfo> ComputeScaling(
            List<List<Slice>> sliceSets,
            double peakProportion)
        {
            sliceSets = sliceSets.Select(x => RemoveTriviallySmallAreaSlices(x)).ToList();

            if (sliceSets is null || sliceSets.Count == 0)
                throw new ArgumentException(nameof(sliceSets));
            if (peakProportion <= 0.0 || peakProportion > 1.0)
                throw new ArgumentOutOfRangeException(nameof(peakProportion));

            sliceSets = sliceSets.Select(x => x.Where(y => y.width > 1E-10).ToList()).ToList();

            StringBuilder DEBUG = new StringBuilder();
            foreach (var sliceSet in sliceSets)
            {
                foreach (var slice in sliceSet)
                {
                    DEBUG.AppendLine(slice.ToString());
                }
            }

            // ---------------------------------------------------------------------
            // Determine the largest area-per-unit value A that satisfies:
            //
            //   tallestRight ≤ peakProportion · (0.5 / pRight) / A
            //   tallestLeft  ≤ peakProportion · (0.5 / pLeft ) / A   (split only)
            //   tallestTotal ≤ peakProportion · 1.0          / A     (single panel)
            //
            // across every diagram.  The shared A is the tightest upper bound.
            // ---------------------------------------------------------------------
            double sharedAreaPerUnit = double.MaxValue;

            foreach (var slices in sliceSets)
            {
                bool isSplit = HasTwoPanels(slices);

                if (isSplit)
                {
                    SplitMasses(slices, out var pLeft, out var pRight);
                    var (tallestLeft, tallestRight) = TallestStacks(slices);

                    double limitRight = peakProportion * (0.5 / pRight) / tallestRight;
                    double limitLeft  = peakProportion * (0.5 / pLeft)  / tallestLeft;

                    double diagramLimit = Math.Min(limitLeft, limitRight);
                    if (diagramLimit < sharedAreaPerUnit)
                        sharedAreaPerUnit = diagramLimit;
                }
                else
                {
                    double tallestTotal = slices.Max(s => s.Total);
                    double diagramLimit = peakProportion / tallestTotal;
                    if (diagramLimit < sharedAreaPerUnit)
                        sharedAreaPerUnit = diagramLimit;
                }
            }

            // ---------------------------------------------------------------------
            // Build per-diagram AxisScalingInfo objects with the shared A.
            // ---------------------------------------------------------------------
            var infos = new List<AxisScalingInfo>(sliceSets.Count);

            foreach (var slices in sliceSets)
            {
                bool isSplit = HasTwoPanels(slices);

                if (isSplit)
                {
                    SplitMasses(slices, out var pLeft, out var pRight);

                    double yMaxRight = (0.5 / pRight) / sharedAreaPerUnit;
                    double yMaxLeft  = (0.5 / pLeft)  / sharedAreaPerUnit;

                    infos.Add(new AxisScalingInfo(
                        yMaxLeft,
                        yMaxRight,
                        0.5 / pLeft,
                        0.5 / pRight,
                        sharedAreaPerUnit));
                }
                else
                {
                    double yMax = 1.0 / sharedAreaPerUnit;

                    infos.Add(new AxisScalingInfo(
                        yMax,
                        yMax,
                        1.0,
                        1.0,
                        sharedAreaPerUnit));
                }
            }

            return infos;
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
            slices = RemoveTriviallySmallAreaSlices(slices);
            reference = RemoveTriviallySmallAreaSlices(reference);

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

        static List<Slice> RemoveTriviallySmallAreaSlices(
            List<Slice> source,
            double minAreaProportion = 1e-4)
        {
            if (source is null || source.Count == 0) return source;
            double totalArea = source.Sum(s => s.width * s.Total);
            if (totalArea <= 0) return source;
            var retained = source.Where(s => (s.width * s.Total) / totalArea >= minAreaProportion).ToList();
            return retained.Count == 0 ? source : retained;
        }

        static void SplitMasses(IEnumerable<Slice> slices,
                                out double pL, out double pR)
        {
            double l = 0, r = 0;
            foreach (var s in slices)
            {
                bool left = s.harm + s.filing + s.answer +
                            s.bargaining + s.trial == 0;
                if (left) 
                    l += s.width; 
                else 
                    r += s.width;
            }
            if (l <= 0 || r <= 0)
                throw new InvalidOperationException("one panel empty");
            pL = l; pR = r;
        }

        static (double tallestLeft, double tallestRight) TallestStacks(
            List<Slice> slices)
        {
            double leftMax = 0, rightMax = 0;
            foreach (var s in slices.Where(x => x.width > 1E-10))
            {
                double total = s.Total;
                bool left = s.harm + s.filing + s.answer +
                            s.bargaining + s.trial == 0;
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
                    double w = slices[idx].width * sc.XScaleLeft;   // XScaleLeft == 1.0 in this mode
                    AddRects(rects, slices[idx], true, idx, x, x + w, sc.YMaxLeft);
                    x += w;
                }
                return rects;
            }

            // --- Split panel: divide into left/right by content ----------------
            // Split into left/right and apply ordering within each panel
            var left = slices
                .Where(s => s.harm + s.filing + s.answer + s.bargaining + s.trial == 0)
                .OrderBy(s => s.opportunity)
                .ThenBy(s => s.Total)
                .ToList();

            var right = slices
                .Where(s => s.harm + s.filing + s.answer + s.bargaining + s.trial > 0)
                .OrderBy(s => s.harm)
                .ThenBy(s => s.Total)
                .ToList();

            double xL = 0.0;
            for (int i = 0; i < left.Count; i++)
            {
                double w = left[i].width * sc.XScaleLeft;          // 0.5 / pLeft
                AddRects(rects, left[i], true, i, xL, xL + w, sc.YMaxLeft);
                xL += w;
            }

            double xR = 0.5;
            for (int j = 0; j < right.Count; j++)
            {
                double w = right[j].width * sc.XScaleRight;        // 0.5 / pRight
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
            { s.opportunity, s.harm, s.filing, s.answer, s.bargaining, s.trial };
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
        /// <summary>
        /// Original API now supports optional embedding parameters without breaking old callers.
        /// Adding optional flags for legend/axis labels and optional outer box sizing/offset.
        /// </summary>
        internal static string TikzScaled(
            List<Slice>      slices,
            AxisScalingInfo  sc,
            bool             pres,
            string           title,
            bool             splitRareHarmPanel,
            bool             standalone           = true,
            bool             includeLegend        = true,
            bool             includeAxisLabels    = true,
            bool             includeDisputeLabels = true,
            double?          targetWidth          = null,
            double?          targetHeight         = null,
            double?          xOffset              = null,
            double?          yOffset              = null,
            bool             adaptivePadding      = false, 
            bool             minimalTicks         = false,
            bool             tickLabelsInside     = false) 
        {
            // ── dimensions ─────────────────────────────────────────────────────────
            double W = pres ? PresPanelWidth : LightPanelWidth;
            double H = pres ? PresPanelHeight: LightPanelHeight;
            if (targetWidth .HasValue) W = targetWidth .Value;
            if (targetHeight.HasValue) H = targetHeight.Value;

            double originX = xOffset ?? 0.0;
            double originY = yOffset ?? 0.0;

            var outer = new TikzRectangle(originX, originY, originX + W, originY + H);

            // ── padding ────────────────────────────────────────────────────────────
            double NeedLeft = tickLabelsInside ? 0.15               // no outside ticks → slim edge
                                               : 0.40;              // room for tick labels

            double NeedBot  = includeAxisLabels ? 0.60              // x-axis label + ticks
                                               : 0.20;              // keep at least 0.20 cm

            double NeedTop  = includeLegend     ? 0.60              // legend title line
                                               : 0.10;              // tiny headroom

            double padLR, padTop, padBot;
            if (!adaptivePadding)
            {
                padLR  = 1.5;
                padTop = 2.5;
                padBot = 1.5;
            }
            else
            {
                double relLR = W < 6.0 ? 0.04 : 0.08;   // 4 % if the pane is < 6 cm wide
                double relTB = H < 6.0 ? 0.04 : 0.08;

                padLR  = Math.Max(NeedLeft, Math.Min(1.0, W * relLR));
                padBot = Math.Max(NeedBot,  Math.Min(1.0, H * relTB));
                padTop = Math.Max(NeedTop,  Math.Min(1.5, H * relTB * 1.5));
            }


            var pane = outer.ReducedByPadding(padLR, padTop, padLR, padBot);

            string pen   = pres ? "white" : "black";
            string[] fills = pres ? PresFill : RegFill;
            var sb = new StringBuilder();

            // ── background & title (unchanged) ─────────────────────────────────────
            if (pres)
                sb.AppendLine(outer.DrawCommand("fill=black"));

            if (pres && !string.IsNullOrEmpty(title))
            {
                var head = new TikzRectangle(outer.left, pane.top, outer.right, outer.top);
                sb.AppendLine(head.DrawCommand($"draw=none,text={pen}", $"\\huge {title}"));
            }

            // ── cost rectangles (unchanged) ────────────────────────────────────────
            double sx = pane.width, sy = pane.height;
            foreach (var rc in GetComponentRects(slices, sc, splitRareHarmPanel))
            {
                var box = new TikzRectangle(
                    pane.left   + rc.X0 * sx,
                    pane.bottom + rc.Y0 * sy,
                    pane.left   + rc.X1 * sx,
                    pane.bottom + rc.Y1 * sy);
                sb.AppendLine(box.DrawCommand($"{fills[rc.Category]},draw={pen},very thin"));
            }

            // ── left y-axis ───────────────────────────────────────────────────
            var yL = new TikzLine(new(pane.left, pane.bottom),
                                  new(pane.left, pane.top));

            var ticksLeftRaw = BuildTicks(sc.YMaxLeft);
            if (minimalTicks && ticksLeftRaw.Count > 2)
                ticksLeftRaw = new() { ticksLeftRaw[^1] };
            var ticksLeft = ticksLeftRaw;

            string  anchorLeft  = tickLabelsInside ? "west" : "east";
            double   shiftXLeft = tickLabelsInside ?  0.15 : -0.40;
            double   shiftYL    = BestLabelShiftY(ticksLeft, pane.height);

            sb.AppendLine(yL.DrawAxis($"{pen},very thin", ticksLeft,
                $"font=\\small,text={pen}", anchorLeft,
                includeAxisLabels ? "Cost" : null, "center",
                TikzHorizontalAlignment.Center,
                includeAxisLabels ? $"font=\\small,rotate=90,text={pen}" : null,
                shiftXLeft, shiftYL));

            // ── right y-axis (split-panel only) ───────────────────────────────
            if (splitRareHarmPanel)
            {
                var mid = new TikzLine(new(pane.left + 0.5 * sx, pane.bottom - 0.4),
                                       new(pane.left + 0.5 * sx, pane.top));
                sb.AppendLine(mid.DrawCommand($"{pen},dashed,very thin"));

                var yR = new TikzLine(new(pane.right, pane.bottom),
                                      new(pane.right, pane.top));

                var ticksRightRaw = BuildTicks(sc.YMaxRight);
                if (minimalTicks && ticksRightRaw.Count > 2)
                    ticksRightRaw = new() { ticksRightRaw[^1] };
                var ticksRight = ticksRightRaw;

                string anchorRight  = tickLabelsInside ? "east" : "west";
                double shiftXRight  = tickLabelsInside ? -0.15 :  0.65;

                sb.AppendLine(yR.DrawAxis($"{pen},very thin", ticksRight,
                    $"font=\\small,text={pen}", anchorRight,
                    null, null, TikzHorizontalAlignment.Center,
                    includeAxisLabels ? $"font=\\small,text={pen}" : null,
                    shiftXRight, 0));
            }


            // ── x-axis & (optional) panel captions  ─────────────────────
            var xB = new TikzLine(new(pane.left, pane.bottom), new(pane.right, pane.bottom));
            var xTicks = includeAxisLabels
                ? new List<(double, string)> { (0, "0\\%"), (1, "100\\%") } // this is really tick marks but we make them disappear when dropping the labels
                : new List<(double, string)> { (0, ""), (1, "") };

            sb.AppendLine(xB.DrawAxis($"{pen},very thin", xTicks,
                $"font=\\small,text={pen}", "north",
                (!includeAxisLabels || splitRareHarmPanel) ? null : "Proportion of Cases",
                "south", TikzHorizontalAlignment.Center,
                $"font=\\small,text={pen}", 0, -0.6));

            if (splitRareHarmPanel)
            {
                double pLeft  = 0.5 / sc.XScaleLeft;
                double pRight = 0.5 / sc.XScaleRight;
                double yLabel = pane.bottom - 0.6;

                string leftLbl  = includeDisputeLabels  ? $"No\\ Dispute\\ ({Pct(pLeft)})"  : Pct(pLeft);
                string rightLbl = includeDisputeLabels  ? $"Dispute\\ ({Pct(pRight)})"     : Pct(pRight);

                sb.AppendLine(TikzHelper.DrawText(pane.left + 0.25 * sx, yLabel,
                    leftLbl,  $"font=\\small,text={pen},anchor=south"));
                sb.AppendLine(TikzHelper.DrawText(pane.left + 0.75 * sx, yLabel,
                    rightLbl, $"font=\\small,text={pen},anchor=south"));
            }

            // ── legend (identical to current code) ─────────────────────────────────
            if (includeLegend)
            {
                bool[] used = new bool[Labels.Length];
                foreach (var s in slices)
                {
                    if (s.opportunity > 1e-12) used[0] = true;
                    if (s.harm        > 1e-12) used[1] = true;
                    if (s.filing      > 1e-12) used[2] = true;
                    if (s.answer      > 1e-12) used[3] = true;
                    if (s.bargaining  > 1e-12) used[4] = true;
                    if (s.trial       > 1e-12) used[5] = true;
                }
                var active = Enumerable.Range(0, Labels.Length).Where(i => used[i]).ToList();

                if (active.Count > 0)
                {
                    sb.AppendLine($"\\draw ({pane.left + pane.width / 2},{pane.bottom}) node (B) {{}};");
                    sb.AppendLine("\\begin{scope}[align=center]");
                    sb.AppendLine($"\\matrix[scale=0.5,draw={pen},below=0.5cm of B,nodes={{draw}},column sep=0.1cm]{{");
                    for (int k = 0; k < active.Count; k++)
                    {
                        int i = active[k];
                        sb.Append($"\\node[rectangle,draw,minimum width=0.5cm,minimum height=0.5cm,{fills[i]}]{{}}; & ");
                        sb.Append($"\\node[draw=none,font=\\small,text={pen}]{{{Labels[i]}}};");
                        sb.AppendLine(k == active.Count - 1 ? @" \\\\" : " &");
                    }
                    sb.AppendLine("};\\end{scope}");
                }
            }

            // ── return ─────────────────────────────────────────────────────────────
            if (!standalone)
                return sb.ToString();

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
                TikzScaled(slices, sc, false, title, splitRareHarmPanel),
                TikzScaled(slices, sc, true, title, splitRareHarmPanel),
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
                "Width,Opportunity,Harm,File," +
                "Answer,Bargain,Trial,Total\n");
            foreach (var s in ss)
                sb.AppendLine(string.Join(",",
                    s.width.ToString("F9", CultureInfo.InvariantCulture),
                    s.opportunity.ToString("G9", CultureInfo.InvariantCulture),
                    s.harm.ToString("G9", CultureInfo.InvariantCulture),
                    s.filing.ToString("G9", CultureInfo.InvariantCulture),
                    s.answer.ToString("G9", CultureInfo.InvariantCulture),
                    s.bargaining.ToString("G9", CultureInfo.InvariantCulture),
                    s.trial.ToString("G9", CultureInfo.InvariantCulture),
                    s.Total.ToString("G9", CultureInfo.InvariantCulture)));
            return sb.ToString();
        }

        public static List<Slice> LoadSlicesFromCsv(string csvPath, bool firstRowIsHeader = true)
        {
            var slices = new List<Slice>();

            foreach (string rawLine in System.IO.File.ReadLines(csvPath))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;                                       // skip blanks

                string line = rawLine.TrimStart();
                if (line.StartsWith("#"))
                    continue;                                       // allow comment lines

                if (firstRowIsHeader)
                {
                    firstRowIsHeader = false;                       // consume header
                    continue;
                }

                string[] fields = line.Split(',');
                if (fields.Length < 7)
                    throw new FormatException(
                        $"CSV line has {fields.Length} fields; expected ≥ 7: \"{rawLine}\"");

                // parse using invariant culture to avoid locale-specific separators
                double width      = double.Parse(fields[0], CultureInfo.InvariantCulture);
                double opportunity= double.Parse(fields[1], CultureInfo.InvariantCulture);
                double harm       = double.Parse(fields[2], CultureInfo.InvariantCulture);
                double filing     = double.Parse(fields[3], CultureInfo.InvariantCulture);
                double answer     = double.Parse(fields[4], CultureInfo.InvariantCulture);
                double bargaining = double.Parse(fields[5], CultureInfo.InvariantCulture);
                double trial      = double.Parse(fields[6], CultureInfo.InvariantCulture);

                slices.Add(new Slice(width, opportunity, harm, filing, answer, bargaining, trial));
            }

            return slices;
        }


        #endregion


    }
}



