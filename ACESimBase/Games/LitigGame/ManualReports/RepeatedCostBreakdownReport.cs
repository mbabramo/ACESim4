using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESimBase.Util.Tikz;
using static ACESimBase.Games.LitigGame.ManualReports.CostBreakdownReport;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    /// <summary>Creates an n×m grid of cost-breakdown panels sharing one legend.</summary>
    public static class RepeatedCostBreakdownReport
    {
        // ───────────  single-point tunables  ────────────────────────────────────────
        private const double CellPitchCm     = 2.4; // origin-to-origin spacing (shrink ↘)
        private const double OuterLeftBandCm = 6.0; // space for y-axis + row labels
        private const double LegendSpaceCm   = 1.0; // gap below outer x-axis

        // ───────────  legend styling copied from CostBreakdownReport  ───────────────
        private static readonly string[] RegFill =
        {
            "pattern=crosshatch, pattern color=green",
            "pattern=vertical lines, pattern color=orange",
            "pattern=crosshatch dots, pattern color=yellow",
            "pattern=north east lines, pattern color=blue!30",
            "pattern=dots, pattern color=blue!60",
            "pattern=crosshatch, pattern color=blue!90",
            "pattern=grid, pattern color=red!70!black"
        };

        private static readonly string[] Labels =
            { "Precaution", "TL Harm", "TNL Harm", "File", "Answer", "Bargaining", "Trial" };

        // ─────────────────────────────────────────────────────────────────────────────
        public static string GenerateRepeatedReport(
            List<List<List<Slice>>> sliceGrid,
            List<string>            majorXValueNames,
            List<string>            majorYValueNames,
            string                  majorXAxisLabel,
            string                  majorYAxisLabel,
            double                  peakProportion            = 0.8,
            bool                    keepAxisLabels            = false,
            bool                    keepAxisTicks             = true,
            bool                    tickLabelsInside          = true)
        {
            // ── basic checks ─────────────────────────────────────────────────────────
            if (sliceGrid is null || sliceGrid.Count == 0 || sliceGrid.Any(r => r.Count == 0))
                throw new ArgumentException(nameof(sliceGrid));

            int rows = sliceGrid.Count;
            int cols = sliceGrid[0].Count;

            if (majorXValueNames.Count != cols || majorYValueNames.Count != rows)
                throw new ArgumentException("Dimension mismatch between axis labels and data grid.");

            // ── legend entries actually present ──────────────────────────────────────
            bool[] used = new bool[Labels.Length];
            foreach (var s in sliceGrid.SelectMany(r => r).SelectMany(x => x))
            {
                if (s.opportunity > 1e-12) used[0] = true;
                if (s.trulyLiableHarm > 1e-12) used[1] = true;
                if (s.trulyNotLiableHarm > 1e-12) used[2] = true;
                if (s.filing      > 1e-12) used[3] = true;
                if (s.answer      > 1e-12) used[4] = true;
                if (s.bargaining  > 1e-12) used[5] = true;
                if (s.trial       > 1e-12) used[6] = true;
            }
            var activeCats = Enumerable.Range(0, Labels.Length).Where(i => used[i]).ToList();

            // helper for legend width (cm)
            double LegendWidthCm()
            {
                const double boxW = 0.55;
                const double sep  = 0.12;
                const double charW = 0.18;
                if (activeCats.Count == 0) return 0;
                double total = 0;
                foreach (int cat in activeCats)
                    total += boxW + sep + Labels[cat].Length * charW;
                // matrix adds an extra sep after every entry except the last
                total += sep * (activeCats.Count - 1);
                return total;
            }

            // ── geometry parameters ────────────────────────────────────────────────
            double yAxisGapCm = Math.Max(1.0, 0.35 * (majorYValueNames.Count == 0
                                           ? 1
                                           : majorYValueNames.Max(s => s.Length)));

            double baseLeft       = yAxisGapCm + 0.9;                  // axis + tick labels
            double outerLeftBand  = Math.Max(OuterLeftBandCm, baseLeft);

            // If we had to fall back to the fixed 6 cm band, keep the axis label
            // inside the clip by limiting how far left we offset it.
            double yLabelOffset = Math.Max(0.9,
                baseLeft < OuterLeftBandCm ? yAxisGapCm - 0.3           // stay within view
                                           : outerLeftBand - 1.0);

            double cellBand    = cols * CellPitchCm;
            double legendWidth = LegendWidthCm();

            bool legendFitsAxis = legendWidth <= cellBand;
            double widthCm = outerLeftBand + cellBand;

            if (!legendFitsAxis && legendWidth + outerLeftBand > widthCm)
                widthCm = legendWidth + outerLeftBand;          // stretch figure

            double heightCm = 2.3 + rows * CellPitchCm;         // 2.3 cm top band

            // ── outer rectangles and axis set ────────────────────────────────────────
            var outerRect = new TikzRectangle(0, 0, widthCm, heightCm);
            var outerAxis = new TikzAxisSet(
                majorXValueNames, majorYValueNames,
                majorXAxisLabel,  majorYAxisLabel,
                outerRect,
                fontScale: 2,
                xAxisSpace: 1.0,
                yAxisSpace: yAxisGapCm,
                xAxisLabelOffsetDown: 0.9,
                yAxisLabelOffsetLeft: yLabelOffset,
                boxBordersAttributes: "draw=none",
                horizontalLinesAttribute: "draw=none",
                verticalLinesAttribute: "draw=none");

            // ── shared scaling across all panels ─────────────────────────────────────

            var scales      = ComputeScaling(sliceGrid.SelectMany(r => r).ToList(), peakProportion);
            int scaleCursor = 0;

            // ── build TikZ body ──────────────────────────────────────────────────────
            var body = new StringBuilder();
            body.AppendLine($@"\clip(0,-{LegendSpaceCm}) rectangle +{outerRect.topRight.WithYTranslation(LegendSpaceCm)};");
            body.AppendLine(outerAxis.GetDrawCommands());

            const double MiniGraphBottomPadCm = 0.20;
            foreach (var (row, rIdx) in sliceGrid.Select((r, i) => (r, i)))
            {
                foreach (var (slices, cIdx) in row.Select((s, i) => (s, i)))
                {
                    var pane = outerAxis.IndividualCells[rIdx][cIdx]
                                        .ReducedByPadding(0, MiniGraphBottomPadCm, 0, 0);

                    var sc = scales[scaleCursor++];

                    body.AppendLine(
                        TikzScaled(
                            slices, sc, pres: false, title: "",
                            splitRareHarmPanel: HasTwoPanels(slices),
                            standalone: false,
                            includeLegend: false,
                            includeAxisLabels: keepAxisLabels && keepAxisTicks,
                            includeDisputeLabels: cols <= 2,
                            targetWidth:  pane.width,
                            targetHeight: pane.height,
                            xOffset:      pane.left,
                            yOffset:      pane.bottom,
                            adaptivePadding: true,
                            minimalTicks: true,
                            tickLabelsInside: tickLabelsInside));
                }
            }

            // ── legend placement ─────────────────────────────────────────────────────
            if (activeCats.Count > 0)
            {
                double anchorX = legendFitsAxis
                    ? (outerAxis.BottomAxisLine.start.x + outerAxis.BottomAxisLine.end.x) / 2.0
                    : widthCm / 2.0;

                body.AppendLine($@"\coordinate (LegendAnchor) at ({anchorX},{outerRect.bottom});");
                body.AppendLine(@"\begin{scope}[align=center]");
                body.AppendLine(@"\matrix[scale=0.6,draw=black,below=0.2cm of LegendAnchor,nodes={draw},column sep=0.12cm]{");
                for (int i = 0; i < activeCats.Count; i++)
                {
                    int cat = activeCats[i];
                    string sep = i == activeCats.Count - 1 ? @"\\" : "&";
                    body.AppendLine($@"\node[rectangle,draw,minimum width=0.55cm,minimum height=0.55cm,{RegFill[cat]}]{{}}; &
        \node[draw=none,font=\small]{{{Labels[cat]}}}; {sep}");
                }
                body.AppendLine("};\\end{scope}");
            }

            // ── minimal header so \fontscale is always defined ───────────────────────
            const string extraHeader = @"\usetikzlibrary{calc}
        \usepackage{relsize}
        \tikzset{fontscale/.style = {font=\relsize{#1}}}";

            return TikzHelper.GetStandaloneDocument(
                body.ToString(),
                additionalHeaderInfo: extraHeader,
                additionalTikzLibraries: new() { "patterns", "positioning" });
        }


    }
}
