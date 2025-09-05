using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.Util.Tikz;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    /// <summary>
    /// Signals×Signals win-rate diagram with switchable orientation:
    ///   • Default (defendant horizontal): x = defendant signals; stacked y = P(p | d, Accident, Strategy).
    ///   • Plaintiff horizontal: x = plaintiff signals; stacked y = P(d | p, Accident, Strategy) computed via Bayes:
    ///       P(d | p, A) ∝ P(p | d, A) * P(d | A), normalised by P(p | A).
    /// Cell shading in both modes is Pr(P wins/liable | p, d, Accident, Strategy).
    ///
    /// Two outputs are returned: [0] non-presentation, [1] presentation.
    ///
    /// TeX constants:
    ///   \DFractionOffset   – vertical offset for the “D: …”/“P: …” line (default 0.4)
    ///   \AxisLabelYAdjust  – tweak for horizontal axis label (default -0.235)
    /// </summary>
    public static class WinRateBySignalReport
    {
        // Overall canvas size (cm)
        const double PrintWidthCm  = 12.0;
        const double PrintHeightCm = 8.0;
        const double PresWidthCm   = 12.0;
        const double PresHeightCm  = 8.0;

        // Layout paddings (cm)
        const double LeftPad   = 0.60;
        const double RightPad  = 0.60;
        const double TopPad    = 0.50;
        const double BottomPad = 2.00; // room below for captions and axis label

        // Legend box (cm)
        const double LegendWidth = 0.90;
        const double LegendPad   = 0.35;
        const int    LegendTicks = 5;

        // Gaps (cm)
        const double ColumnGapCm = 0.10; // between columns
        const double RowGapCm    = 0.08; // between stacked rows

        // Label thresholds (cm)
        const double MinLabelWidthCm    = 0.50; // single-line
        const double MinLabelHeightCm   = 0.40;
        const double MinTwoLineWidthCm  = 0.80; // two-line ("X: a/b" over "%")
        const double MinTwoLineHeightCm = 0.82;

        // Caption baseline just below bars (cm)
        const double ColumnCaptionDrop = 0.40;

        /// <summary>
        /// Build two TikZ variants: non-presentation and presentation.
        /// Set plaintiffHorizontal=true to flip axes (plaintiff on x, defendant stacked).
        /// </summary>
        public static List<string> GenerateReport(
            PrecautionWinHeatmapData data,
            PrecautionSignalModel signalModel,
            bool plaintiffHorizontal = false)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (signalModel == null) throw new ArgumentNullException(nameof(signalModel));

            string nonPresentation = BuildTikz(data, presentationMode: false, PrintWidthCm, PrintHeightCm, plaintiffHorizontal);
            string presentation    = BuildTikz(data, presentationMode: true,  PresWidthCm,  PresHeightCm,  plaintiffHorizontal);

            return new List<string> { nonPresentation, presentation };
        }

        static string BuildTikz(
            PrecautionWinHeatmapData data,
            bool presentationMode,
            double totalWidthCm,
            double totalHeightCm,
            bool plaintiffHorizontal)
        {
            int P = data.ProbabilityPlaintiffSignalGivenDefendantSignal_Accident_RowHeight.Length;
            if (P == 0) throw new ArgumentException("No plaintiff-signal rows.", nameof(data));
            int D = data.ProbabilityPlaintiffSignalGivenDefendantSignal_Accident_RowHeight[0].Length;
            if (D == 0) throw new ArgumentException("No defendant-signal columns.", nameof(data));

            var overall = new TikzRectangle(0, 0, totalWidthCm, totalHeightCm);

            double legendBlockWidth = LegendWidth + LegendPad;
            var chartRect = new TikzRectangle(
                overall.left + LeftPad,
                overall.bottom + BottomPad,
                overall.right - RightPad - legendBlockWidth,
                overall.top - TopPad);

            var legendRect = new TikzRectangle(
                chartRect.right + LegendPad,
                chartRect.bottom,
                chartRect.right + LegendPad + LegendWidth,
                chartRect.top);

            // --- Prepare orientation-specific inputs ---
            // Base matrices:
            //   P(p | d, A):  rowHeightPD[p][d]
            //   win[p][d]   :  cell win rate
            double[][] rowHeightPD = data.ProbabilityPlaintiffSignalGivenDefendantSignal_Accident_RowHeight;
            double[][] winPD = data.ProbabilityOfLiabilityGivenSignals_Accident_CellValue;

            // Column probabilities and column-level win rate depend on orientation.
            double[] colProb;   // P(d | A) or P(p | A)
            double[] colWin;    // win% per column (D- or P-perspective)

            // Row heights matrix to render for the chosen orientation (indexing [rowIndex][colIndex]).
            // For defendant-horizontal: rows = P, cols = D, values = P(p | d, A) — already normalised per d.
            // For plaintiff-horizontal : rows = D, cols = P, values = P(d | p, A) computed via Bayes.
            double[][] renderRowHeights;
            double[][] renderCellWin; // aligned to renderRowHeights shape

            if (!plaintiffHorizontal)
            {
                colProb = data.DefendantSignalProbabilityGivenAccident ?? new double[D];
                colWin  = data.DColumnPWinProbabilityGivenAccident     ?? new double[D];

                renderRowHeights = new double[P][];
                for (int p = 0; p < P; p++)
                {
                    renderRowHeights[p] = new double[D];
                    for (int d = 0; d < D; d++)
                        renderRowHeights[p][d] = Clamp01(rowHeightPD[p][d]); // P(p | d, A)
                }

                renderCellWin = winPD; // [P][D]
            }
            else
            {
                // Compute P(d | p, A) column-by-column using Bayes:
                //   P(d | p, A) = P(p | d, A) * P(d | A) / P(p | A).
                var PdGivenA = data.DefendantSignalProbabilityGivenAccident ?? new double[D];
                var PpGivenA = data.PlaintiffSignalProbabilityGivenAccident ?? new double[P];

                colProb = PpGivenA;                                   // columns are plaintiff signals
                colWin  = data.PColumnPWinProbabilityGivenAccident ?? new double[P];

                renderRowHeights = new double[D][];
                for (int d = 0; d < D; d++)
                    renderRowHeights[d] = new double[P];

                for (int p = 0; p < P; p++)
                {
                    double denom = PpGivenA.Length > p ? PpGivenA[p] : 0.0;
                    if (denom <= 0.0)
                    {
                        // No accident-conditioned mass at this plaintiff signal → no visible stack.
                        for (int d = 0; d < D; d++)
                            renderRowHeights[d][p] = 0.0;
                    }
                    else
                    {
                        for (int d = 0; d < D; d++)
                        {
                            double pGivenD = rowHeightPD[p][d];  // P(p | d, A)
                            double dGivenA = (d < PdGivenA.Length) ? PdGivenA[d] : 0.0; // P(d | A)
                            renderRowHeights[d][p] = Clamp01((pGivenD * dGivenA) / denom); // P(d | p, A)
                        }
                    }
                }

                // Cell win matrix aligned as [rows=D][cols=P]
                renderCellWin = new double[D][];
                for (int d = 0; d < D; d++)
                {
                    renderCellWin[d] = new double[P];
                    for (int p = 0; p < P; p++)
                        renderCellWin[d][p] = Clamp01(winPD[p][d]);
                }
            }

            // --- Column widths from colProb ---
            int numCols = colProb.Length;
            int nonZeroCols = colProb.Count(x => x > 0.0);
            double totalGap = Math.Max(0, nonZeroCols - 1) * ColumnGapCm;
            double usableWidth = Math.Max(0, chartRect.width - totalGap);

            double sumProb = colProb.Sum();
            if (sumProb <= 0.0) sumProb = 1.0;

            double[] colWidthCm = colProb.Select(p => p <= 0.0 ? 0.0 : p / sumProb * usableWidth).ToArray();

            // Color ramp (vibrant, light palette)
            string ColorRamp(double t)
            {
                t = Clamp01(t);
                int bluePortion  = (int)Math.Round((1 - t) * 100.0);
                int orangeMix    = (int)Math.Round(t * 100.0);
                int whitePortion = !presentationMode
                    ? (int)Math.Round(88 - 33 * t)
                    : (int)Math.Round(70 - 25 * t);
                return $"blue!{bluePortion}!orange!{orangeMix}!white!{whitePortion}!white";
            }

            var sb = new StringBuilder();

            // --- TeX-adjustable constants (tuned defaults) ---
            sb.AppendLine(@"\newcommand{\DFractionOffset}{0.4}");     // relative to % baseline (anchor=north)
            sb.AppendLine(@"\newcommand{\AxisLabelYAdjust}{-0.235}"); // extra tweak for axis label

            // Presentation mode background
            if (presentationMode)
            {
                sb.AppendLine(@"\pagecolor{black}");
                var bg = new TikzRectangle(overall.left - 2.0, overall.bottom - 2.0, overall.right + 2.0, overall.top + 2.0);
                sb.AppendLine(bg.DrawCommand("fill=black, draw=none"));
            }

            // Chart frame
            sb.AppendLine(chartRect.DrawCommand(presentationMode ? "draw=white" : "draw=black"));

            // D captions baseline (win-rate % sits here)
            double captionY = chartRect.bottom - ColumnCaptionDrop;

            // --- Draw columns ---
            double xCursor = chartRect.left;
            for (int col = 0; col < numCols; col++)
            {
                double w = colWidthCm[col];
                if (w <= 0.0)
                    continue;

                double xLeft = xCursor;
                double xRight = xLeft + w;

                // Count visible rows to compute total row gaps for this column
                int rowCount = plaintiffHorizontal ? D : P;
                int nonZeroRowsInCol = 0;
                for (int r = 0; r < rowCount; r++)
                {
                    double frac = plaintiffHorizontal ? renderRowHeights[r][col] : renderRowHeights[r][col];
                    if (frac > 0.0) nonZeroRowsInCol++;
                }

                double columnGapTotal = Math.Max(0, nonZeroRowsInCol - 1) * RowGapCm;
                double effectiveColumnHeight = Math.Max(0, chartRect.height - columnGapTotal);

                // Draw stacked cells with gaps
                double yCursor = chartRect.bottom;
                for (int r = 0; r < rowCount; r++)
                {
                    double fracHeight = plaintiffHorizontal ? renderRowHeights[r][col] : renderRowHeights[r][col];
                    if (fracHeight <= 0.0) continue;

                    double h = fracHeight * effectiveColumnHeight;
                    if (h <= 0.0) continue;

                    double t = plaintiffHorizontal ? renderCellWin[r][col] : renderCellWin[r][col];

                    var cell = new TikzRectangle(xLeft, yCursor, xRight, yCursor + h);
                    sb.AppendLine(cell.DrawCommand($"fill={ColorRamp(t)}, draw={(presentationMode ? "white" : "black")}"));

                    // In-cell labels
                    double cellWidth = w;
                    double cellHeight = h;
                    bool roomForTwoLines = cellWidth >= MinTwoLineWidthCm && cellHeight >= MinTwoLineHeightCm;
                    bool roomForOneLine  = cellWidth >= MinLabelWidthCm   && cellHeight >= MinLabelHeightCm;

                    if (roomForOneLine)
                    {
                        string textColor = "black"; // light fills
                        string pctText = PercentString(t);

                        double xMid = (xLeft + xRight) / 2.0;
                        double yMid = (yCursor + yCursor + h) / 2.0;

                        string firstLine;
                        if (!plaintiffHorizontal)
                        {
                            // rows are P
                            string pFracMath = MidpointFractionLabelUniformFrac(r, P);
                            firstLine = $"P: {pFracMath}";
                        }
                        else
                        {
                            // rows are D
                            string dFracMath = MidpointFractionLabelUniformFrac(r, D);
                            firstLine = $"D: {dFracMath}";
                        }

                        string text = roomForTwoLines ? $"{firstLine}\\\\ {pctText}" : pctText;
                        sb.AppendLine($@"\draw ({xMid.ToString(CultureInfo.InvariantCulture)}, {yMid.ToString(CultureInfo.InvariantCulture)}) node[font=\scriptsize, text={textColor}, align=center] {{{text}}};");
                    }

                    yCursor += h;

                    // Add gap except after last visible row
                    if (RowGapCm > 0.0)
                    {
                        bool lastVisible = true;
                        for (int r2 = r + 1; r2 < rowCount; r2++)
                        {
                            double frac2 = plaintiffHorizontal ? renderRowHeights[r2][col] : renderRowHeights[r2][col];
                            if (frac2 > 0.0) { lastVisible = false; break; }
                        }
                        if (!lastVisible)
                            yCursor += RowGapCm;
                    }
                }

                // Column captions:
                //   • % (at captionY)
                //   • X: fraction (at captionY + \DFractionOffset)
                string fracMathCol = !plaintiffHorizontal
                    ? MidpointFractionLabelUniformFrac(col, D) // "D: …"
                    : MidpointFractionLabelUniformFrac(col, P); // "P: …"

                string colPct = PercentString(Clamp01(colWin[col]));
                double xCenter = (xLeft + xRight) / 2.0;
                string captionTextColor = presentationMode ? "white" : "black";

                // Win rate %
                sb.AppendLine($@"\draw ({xCenter.ToString(CultureInfo.InvariantCulture)}, {captionY.ToString(CultureInfo.InvariantCulture)}) node[anchor=north, font=\scriptsize, text={captionTextColor}] {{{colPct}}};");

                // Fraction label (D: … or P: …)
                string fracYExpr = "{" + captionY.ToString(CultureInfo.InvariantCulture) + " + \\DFractionOffset}";
                string fracLabel = !plaintiffHorizontal ? $"D: {fracMathCol}" : $"P: {fracMathCol}";
                sb.AppendLine($@"\draw ({xCenter.ToString(CultureInfo.InvariantCulture)}, {fracYExpr}) node[anchor=north, font=\scriptsize, text={captionTextColor}] {{{fracLabel}}};");

                xCursor = xRight + ColumnGapCm;
            }

            // Axis labels
            string axisTextColor = presentationMode ? "white" : "black";
            double axisBaselineDrop = 0.50; // cm below captionY
            string axisYExpr = "{" + (captionY - axisBaselineDrop).ToString(CultureInfo.InvariantCulture) + " + \\AxisLabelYAdjust}";

            if (!plaintiffHorizontal)
            {
                sb.AppendLine($@"\node[font=\small, text={axisTextColor}] at ({(chartRect.left + chartRect.width / 2.0).ToString(CultureInfo.InvariantCulture)}, {axisYExpr}) {{Defendant Signals}};");
                double yMidChart = chartRect.bottom + chartRect.height / 2.0;
                double xLeftAxis = chartRect.left - 0.65;
                sb.AppendLine($@"\node[font=\small, text={axisTextColor}, rotate=90] at ({xLeftAxis.ToString(CultureInfo.InvariantCulture)}, {yMidChart.ToString(CultureInfo.InvariantCulture)}) {{Plaintiff Signals}};");
            }
            else
            {
                sb.AppendLine($@"\node[font=\small, text={axisTextColor}] at ({(chartRect.left + chartRect.width / 2.0).ToString(CultureInfo.InvariantCulture)}, {axisYExpr}) {{Plaintiff Signals}};");
                double yMidChart = chartRect.bottom + chartRect.height / 2.0;
                double xLeftAxis = chartRect.left - 0.65;
                sb.AppendLine($@"\node[font=\small, text={axisTextColor}, rotate=90] at ({xLeftAxis.ToString(CultureInfo.InvariantCulture)}, {yMidChart.ToString(CultureInfo.InvariantCulture)}) {{Defendant Signals}};");
            }

            // Legend
            sb.AppendLine(DrawLegend(legendRect, presentationMode));

            // Wrap as standalone LaTeX document
            string doc = TikzHelper.GetStandaloneDocument(
                sb.ToString(),
                additionalPackages: new List<string> { "xcolor" } // xcolor provides \pagecolor
            );

            return doc;
        }

        static string DrawLegend(TikzRectangle rect, bool presentationMode)
        {
            var sb = new StringBuilder();

            // Frame and title
            sb.AppendLine(rect.DrawCommand(presentationMode ? "draw=white" : "draw=black"));
            double titleY = rect.top + 0.15;
            string titleColor = presentationMode ? "white" : "black";
            sb.AppendLine($@"\draw ({((rect.left + rect.right) / 2.0).ToString(CultureInfo.InvariantCulture)}, {titleY.ToString(CultureInfo.InvariantCulture)}) node[font=\scriptsize, text={titleColor}] {{P win rate}};");

            // Swatches – light blue→light orange (same ramp as cells)
            int steps = 10;
            double stepH = rect.height / steps;
            for (int i = 0; i < steps; i++)
            {
                double y0 = rect.bottom + i * stepH;
                double y1 = y0 + stepH;
                double t = (double)i / (steps - 1); // include 0 and 1
                var r = new TikzRectangle(rect.left, y0, rect.right, y1);

                int bluePortion  = (int)Math.Round((1 - t) * 100.0);
                int orangeMix    = (int)Math.Round(t * 100.0);
                int whitePortion = !presentationMode
                    ? (int)Math.Round(88 - 33 * t)
                    : (int)Math.Round(70 - 25 * t);

                string color = $"blue!{bluePortion}!orange!{orangeMix}!white!{whitePortion}!white";
                sb.AppendLine(r.DrawCommand($"fill={color}, draw={(presentationMode ? "white" : "black")}"));
            }

            // Tick labels (legend sits on black page; keep these white in presentation mode)
            for (int k = 0; k <= LegendTicks; k++)
            {
                double t = (double)k / LegendTicks;
                double y = rect.bottom + t * rect.height;
                string label = PercentString(t);
                string tickColor = presentationMode ? "white" : "black";
                sb.AppendLine($@"\draw ({rect.right + 0.06}, {y.ToString(CultureInfo.InvariantCulture)}) node[anchor=west, font=\tiny, text={tickColor}] {{{label}}};");
            }

            return sb.ToString();
        }

        // Helpers
        static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);

        static string PercentString(double x)
        {
            x = Clamp01(x);
            double pct = x * 100.0;
            string s = Math.Abs(pct - Math.Round(pct)) < 1e-9
                ? Math.Round(pct).ToString(CultureInfo.InvariantCulture)
                : pct.ToString("0.#", CultureInfo.InvariantCulture);
            return s + "\\%";
        }

        // Uniform midpoint fraction as math with a common denominator (no reduction).
        // Example: index=2, count=8 -> "$\frac{5}{16}$"
        static string MidpointFractionLabelUniformFrac(int indexZeroBased, int count)
        {
            int num = 2 * indexZeroBased + 1;
            int den = 2 * count;
            return $"$\\frac{{{num}}}{{{den}}}$";
        }
    }
}
