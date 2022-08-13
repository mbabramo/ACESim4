using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Tikz
{
    public record TikzRepeatedGraph
    {
        public List<string> majorXValueNames { get; init; }
        public List<string> majorYValueNames { get; init; }
        public string majorXAxisLabel { get; init; }
        public string majorYAxisLabel { get; init; }
        public List<string> minorXValueNames { get; init; }
        public List<string> minorYValueNames { get; init; }
        public string minorXAxisLabel { get; init; }
        public string minorYAxisLabel { get; init; }
        public List<List<TikzLineGraphData>> lineGraphData { get; init; }
        public double xAxisSpaceMicro { get; init; } = 0.9;
        public double xAxisLabelOffsetMicro { get; init; } = 0.7;
        public double yAxisSpaceMicro { get; init; } = 1.1;
        public double yAxisLabelOffsetMicro { get; init; } = 0.9;

        public double xAxisLabelOffsetDown { get; init; } = 1.2;

        public double yAxisLabelOffsetLeft { get; init; } = 1;
        public bool drawInternalGraphPaperLines { get; init; } = false;

        public bool useSaveBoxes { get; init; } = true;

        public TikzAxisSet.GraphType graphType { get; init; } = TikzAxisSet.GraphType.Scatter;

        private bool Initialized;
        private TikzAxisSet outerAxisSet;
        private List<List<TikzAxisSet>> innerAxisSets;
        public double width => 20 + (majorXValueNames.Count() > 5 ? 4.0 * (majorXValueNames.Count() - 5) : 0);
        public double height => 2.3 + majorYValueNames.Count() * 4.0;
        private TikzRectangle mainRectangle => new TikzRectangle(0, 0, width, height);
        TikzLine bottomAxisLine => outerAxisSet.BottomAxisLine;
        TikzRectangle bottomAxisAsRectangle => outerAxisSet.BottomAxisLine.ToRectangle();
        public string MidpointOfBottomString => $"({(bottomAxisLine.start.x + bottomAxisLine.end.x) / 2.0 + xAxisMarkOffset},{mainRectangle.bottom})";
        private double xAxisMarkOffset, yAxisMarkOffset;

        public void Initialize()
        {
            if (Initialized)
                return;
            outerAxisSet = new TikzAxisSet(majorXValueNames, majorYValueNames, majorXAxisLabel, majorYAxisLabel, mainRectangle, fontScale:2, xAxisSpace:1.5, yAxisSpace: 1.7, xAxisLabelOffsetDown:0.9, yAxisLabelOffsetLeft:1.1, boxBordersAttributes: "draw=none", horizontalLinesAttribute: "draw=none", verticalLinesAttribute: "draw=none");
            var rectangles = outerAxisSet.IndividualCells;
            innerAxisSets = rectangles.Select((row, rowIndex) => row.Select((column, columnIndex) => new TikzAxisSet(minorXValueNames, minorYValueNames, minorXAxisLabel, minorYAxisLabel, rectangles[rowIndex][columnIndex].ConditionallyMovedToOrigin(useSaveBoxes), lineGraphData: lineGraphData[rowIndex][columnIndex], fontScale: 0.7, xAxisSpace: xAxisSpaceMicro, yAxisSpace: yAxisSpaceMicro, xAxisLabelOffsetDown: xAxisLabelOffsetMicro, xAxisLabelOffsetRight:0, yAxisLabelOffsetLeft: yAxisLabelOffsetMicro, yAxisLabelOffsetUp:0, horizontalLinesAttribute: drawInternalGraphPaperLines ? "gray!30" : "draw=none", verticalLinesAttribute: drawInternalGraphPaperLines ? "gray!30" : "draw=none", yAxisUseEndpoints: true, graphType: graphType)).ToList()).ToList();
            var firstInnerAxisSet = innerAxisSets.FirstOrDefault()?.FirstOrDefault();
            xAxisMarkOffset = firstInnerAxisSet?.LeftAxisWidth * 0.5 ?? 0;
            yAxisMarkOffset = firstInnerAxisSet?.BottomAxisHeight * 0.5 ?? 0;
            outerAxisSet = outerAxisSet with
            { // Our macro axis marks will be centered on the enter inner axis sets, but it looks better if they're centered relative to the content (excluding axes) of those sets. We then also need to shift the axis labels too.
                xAxisMarkOffset = xAxisMarkOffset, 
                yAxisMarkOffset = yAxisMarkOffset,
                xAxisLabelOffsetRight = xAxisMarkOffset,
                xAxisLabelOffsetDown = xAxisLabelOffsetDown,
                yAxisLabelOffsetUp = yAxisMarkOffset,
                yAxisLabelOffsetLeft = yAxisLabelOffsetLeft,
            };
            Initialized = true;
        }

        public string GetLegend()
        {
            var lineGraphDataInfo = lineGraphData.First().First();
            if (lineGraphDataInfo.dataSeriesNames.All(x => x == null || x == ""))
                return "";
            int numberItems = lineGraphDataInfo.dataSeriesNames.Count();
            List<(string name, string attributes, bool isLast)> legendData = lineGraphDataInfo.dataSeriesNames.Zip(lineGraphDataInfo.lineAttributes, (name, attributes) => (name, attributes)).Select((item, index) => (item.name, item.attributes, index == numberItems - 1)).ToList();
            string dataSeriesDraw = String.Join(Environment.NewLine, legendData.Select(x => $@"
\draw[{x.attributes}] (0.25,-0.25) -- (0.75,-0.25); &
\node[draw=none, font=\small] (B) {{{x.name}}}; {(x.isLast ? "\\\\" : "&")}"));
            return $@"\draw {MidpointOfBottomString} node[draw=none] (baseCoordinate) {{}};
\begin{{scope}}[align=center]
        \matrix[scale=0.5, draw=black, below=-0.4cm of baseCoordinate, nodes={{draw}}, column sep=0.1cm]{{
        {dataSeriesDraw}
            }};
\end{{scope}}";
        }

        public (string contents, string beforeDocument) GetDrawCommands()
        {
            Initialize();
            StringBuilder contentsBuilder = new StringBuilder();
            StringBuilder beforeMainPictureBuilder = new StringBuilder();
            // DEBUG // (1) Can we reuse the axes and inner lines? (2) Why is the graph being moved over? (3) fix the x axis letter on the big graph (4) stop removing all but the 25th point (5) add scatterplot option
            contentsBuilder.AppendLine($@"\clip(0, 0) rectangle + {outerAxisSet.sourceRectangle.topRight}; ");
            contentsBuilder.AppendLine(outerAxisSet.GetDrawCommands());
            List<List<TikzAxisSet>> list = innerAxisSets.Select(y => y).ToList();
            for (int rowIndex = 0; rowIndex <  list.Count; rowIndex++)
            {
                List<TikzAxisSet> row = list[rowIndex];
                for (int columnIndex = 0; columnIndex <  row.Count; columnIndex++)
                {
                    string saveBoxName = $"myboxR{GetColumnName(rowIndex)}C{GetColumnName(columnIndex)}"; // note that we can't use numbers in the box register name
                    TikzAxisSet cell = row[columnIndex];
                    StringBuilder b = useSaveBoxes ? beforeMainPictureBuilder : contentsBuilder;
                    if (useSaveBoxes)
                    {
                        b.AppendLine($@"\newsavebox{{\{saveBoxName}}} 
\begin{{lrbox}}{{\{saveBoxName}}}
  \begin{{tikzpicture}}
    \begin{{scope}}");
                    }
                    b.AppendLine($"% ");
                    b.AppendLine($"% Cell {rowIndex},{columnIndex}");
                    b.AppendLine($"% ");
                    b.AppendLine(cell.GetDrawCommands());
                    if (useSaveBoxes)
                    {
                        b.AppendLine($@"  \end{{scope}}
  \end{{tikzpicture}}
\end{{lrbox}}");
                        var rect = outerAxisSet.IndividualCells[rowIndex][columnIndex];
                        contentsBuilder.AppendLine($@"\node[anchor=north west,inner sep=0pt,outer sep=0pt] at {rect.topLeft.ToString()} {{ \usebox{{\{saveBoxName}}} }};");
                    }
                }
            }
            contentsBuilder.AppendLine(GetLegend());
            return (contentsBuilder.ToString(), beforeMainPictureBuilder.ToString());
        }

        static string GetColumnName(int index)
        {
            const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

            var value = "";

            if (index >= letters.Length)
                value += letters[index / letters.Length - 1];

            value += letters[index % letters.Length];

            return value;
        }

        public string GetStandaloneDocument()
        {
            var drawCommands = GetDrawCommands();
            return TikzHelper.GetStandaloneDocument(drawCommands.contents, new List<string>() { "xcolor" }, additionalHeaderInfo: $@"
    \usetikzlibrary{{calc}}
    \usepackage{{relsize}}
    \tikzset{{fontscale/.style = {{font=\relsize{{#1}}}}}}", contentsBeforeDocument: drawCommands.beforeDocument);
        }

    }
}
