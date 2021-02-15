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

        private bool Initialized;
        private TikzAxisSet outerAxisSet;
        private List<List<TikzAxisSet>> innerAxisSets;
        public double width => 20 + (majorXValueNames.Count() > 5 ? 4.0 * (majorXValueNames.Count() - 5) : 0);
        public double height => 2.3 + majorYValueNames.Count() * 4.0;
        private TikzRectangle mainRectangle => new TikzRectangle(0, 0, width, height);

        public string MidpointOfBottomString => $"({(mainRectangle.left + mainRectangle.right) / 2.0},{mainRectangle.bottom})";

        public void Initialize()
        {
            if (Initialized)
                return;
            outerAxisSet = new TikzAxisSet(majorXValueNames, majorYValueNames, majorXAxisLabel, majorYAxisLabel, mainRectangle, fontScale:2, xAxisSpace:1.5, yAxisSpace: 1.7, xAxisLabelOffset:0.9, yAxisLabelOffset:1.1, boxBordersAttributes: "draw=none");
            var rectangles = outerAxisSet.IndividualCells;
            innerAxisSets = rectangles.Select((row, rowIndex) => row.Select((column, columnIndex) => new TikzAxisSet(minorXValueNames, minorYValueNames, minorXAxisLabel, minorYAxisLabel, rectangles[rowIndex][columnIndex], lineGraphData: lineGraphData[rowIndex][columnIndex], fontScale: 0.7, xAxisSpace: xAxisSpaceMicro, yAxisSpace: yAxisSpaceMicro, xAxisLabelOffset: xAxisLabelOffsetMicro, yAxisLabelOffset: yAxisLabelOffsetMicro, horizontalLinesAttribute: "gray!30", verticalLinesAttribute: "gray!30", yAxisUseEndpoints: true)).ToList()).ToList();
            Initialized = true;
        }

        public string GetLegend()
        {
            return $@"\draw {MidpointOfBottomString} node[draw=none] (baseCoordinate) {{}};
\begin{{scope}}[align=center]
        \matrix[scale=0.5, draw=black, below=0.5cm of baseCoordinate, nodes={{draw}}, column sep=0.1cm]{{
            \node[rectangle, draw, minimum width=0.5cm, minimum height=0.5cm, pattern=north east lines, pattern color=red] {{}}; &
            \node[draw=none, font=\small] (B) {{Truly Liable Cases}}; &
            \node[rectangle, draw, minimum width=0.5cm, minimum height=0.5cm, pattern=north west lines, pattern color=blue] {{}}; &
            \node[draw=none, font=\small] (B) {{Truly Not Liable Cases}}; \\
            }};
\end{{scope}}";
        }

        public string GetDrawCommands()
        {
            Initialize();
            StringBuilder b = new StringBuilder();
            b.AppendLine(outerAxisSet.GetDrawCommands());
            foreach (var innerSet in innerAxisSets.SelectMany(y => y))
                b.AppendLine(innerSet.GetDrawCommands());
            return b.ToString();
        }

        public string GetStandaloneDocument() => TikzHelper.GetStandaloneDocument(GetDrawCommands(), new List<string>() { "xcolor" }, additionalHeaderInfo: $@"
    \usetikzlibrary{{calc}}
    \usepackage{{relsize}}
    \tikzset{{fontscale/.style = {{font=\relsize{{#1}}}}}}");

    }
}
