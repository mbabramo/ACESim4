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
            "pattern=north east lines, pattern color=green",
            "pattern=north west lines, pattern color=yellow",
            "pattern=dots,            pattern color=blue!30",
            "pattern=vertical lines,  pattern color=blue!60",
            "pattern=crosshatch,      pattern color=blue!90",
            "pattern=grid,            pattern color=red!70!black"
        };

        private static readonly string[] Labels =
            { "Opportunity", "Harm", "File", "Answer", "Bargaining", "Trial" };

        // ─────────────────────────────────────────────────────────────────────────────
        public static string GenerateRepeatedReport(
            List<List<List<Slice>>> sliceGrid,
            List<string>            majorXValueNames,
            List<string>            majorYValueNames,
            string                  majorXAxisLabel,
            string                  majorYAxisLabel,
            double                  peakProportion            = 0.8,
            bool                    keepAxisLabels            = false,
            bool                    keepAxisTicks             = true) 
        {
            // ── basic checks ─────────────────────────────────────────────────────────
            if (sliceGrid is null || sliceGrid.Count == 0 || sliceGrid.Any(r => r.Count == 0))
                throw new ArgumentException(nameof(sliceGrid));

            int rows = sliceGrid.Count;
            int cols = sliceGrid[0].Count;

            if (majorXValueNames.Count != cols || majorYValueNames.Count != rows)
                throw new ArgumentException("Dimension mismatch between axis labels and data grid.");

            // ── shared scaling across all panels ─────────────────────────────────────
            var scales      = ComputeScaling(sliceGrid.SelectMany(r => r).ToList(), peakProportion);
            int scaleCursor = 0;

            // ── geometry parameters (allow caller overrides) ─────────────────────────
            double yAxisGapCm    = Math.Max(1.0, 0.35 *
                                            (majorXValueNames.Count == 0
                                                 ? 1
                                                 : majorXValueNames.Max(s => s.Length)));

            double outerLeftBand = Math.Max(OuterLeftBandCm, yAxisGapCm + 0.9);
            double yLabelOffset  = Math.Max(0.9, outerLeftBand - 1.0);

            double widthCm  = outerLeftBand + cols * CellPitchCm;
            double heightCm = 2.3 + rows * CellPitchCm;                   // 2.3 cm top band

            var outerRect = new TikzRectangle(0, 0, widthCm, heightCm);
            var outerAxis = new TikzAxisSet(
                majorXValueNames,
                majorYValueNames,
                majorXAxisLabel,
                majorYAxisLabel,
                outerRect,
                fontScale: 2,
                xAxisSpace: 1.0,
                yAxisSpace: yAxisGapCm,
                xAxisLabelOffsetDown: 0.9,
                yAxisLabelOffsetLeft: yLabelOffset,
                boxBordersAttributes: "draw=none",
                horizontalLinesAttribute: "draw=none",
                verticalLinesAttribute: "draw=none");

            // ── legend entries actually present ──────────────────────────────────────
            bool[] used = new bool[Labels.Length];
            foreach (var s in sliceGrid.SelectMany(r => r).SelectMany(x => x))
            {
                if (s.opportunity > 1e-12) used[0] = true;
                if (s.harm        > 1e-12) used[1] = true;
                if (s.filing      > 1e-12) used[2] = true;
                if (s.answer      > 1e-12) used[3] = true;
                if (s.bargaining  > 1e-12) used[4] = true;
                if (s.trial       > 1e-12) used[5] = true;
            }
            var activeCats = Enumerable.Range(0, Labels.Length).Where(i => used[i]).ToList();

            // ── build TikZ body ──────────────────────────────────────────────────────
            var body = new StringBuilder();
            body.AppendLine($@"\clip(0,-{LegendSpaceCm}) rectangle +{outerRect.topRight.WithYTranslation(LegendSpaceCm)};");
            body.AppendLine(outerAxis.GetDrawCommands());

            foreach (var (row, rIdx) in sliceGrid.Select((r, i) => (r, i)))
            {
                foreach (var (slices, cIdx) in row.Select((s, i) => (s, i)))
                {
                    var cell = outerAxis.IndividualCells[rIdx][cIdx];
                    var sc   = scales[scaleCursor++];

                    body.AppendLine(
                        TikzScaled(
                            slices,
                            sc,
                            pres:false,
                            title:"",
                            splitRareHarmPanel:HasTwoPanels(slices),
                            standalone:false,
                            includeLegend:false,
                            includeAxisLabels: keepAxisLabels && keepAxisTicks,
                            includeDisputeLabels: false,
                            targetWidth:  cell.width,
                            targetHeight: cell.height,
                            xOffset:      cell.left,
                            yOffset:      cell.bottom,
                            adaptivePadding: true,
                            minimalTicks: true));
                }
            }

            // ── legend underneath x-axis ─────────────────────────────────────────────
            if (activeCats.Count > 0)
            {
                double midX = (outerAxis.BottomAxisLine.start.x + outerAxis.BottomAxisLine.end.x) / 2.0;
                body.AppendLine($@"\coordinate (LegendAnchor) at ({midX},{outerRect.bottom});");
                body.AppendLine(@"\begin{scope}[align=center]");
                body.AppendLine(@"\matrix[scale=0.6,draw=black,below=0.2cm of LegendAnchor,nodes={draw},column sep=0.12cm]{");
                for (int i = 0; i < activeCats.Count; i++)
                {
                    int cat = activeCats[i];
                    string sep = i == activeCats.Count - 1 ? @"\\" : "&";
                    body.AppendLine(
                        $@"\node[rectangle,draw,minimum width=0.55cm,minimum height=0.55cm,{RegFill[cat]}]{{}}; &
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
