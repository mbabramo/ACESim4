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
        private TikzRectangle sourceRectangle => new TikzRectangle(0, 0, 20, 2.3 + majorYValueNames.Count() * 4.0);

        public void Initialize()
        {
            if (Initialized)
                return;
            outerAxisSet = new TikzAxisSet(majorXValueNames, majorYValueNames, majorXAxisLabel, majorYAxisLabel, sourceRectangle, fontScale:2, xAxisSpace:1.5, yAxisSpace: 1.7, xAxisLabelOffset:0.9, yAxisLabelOffset:1.1, boxBordersAttributes: "draw=none");
            var rectangles = outerAxisSet.IndividualCells;
            innerAxisSets = rectangles.Select((row, rowIndex) => row.Select((column, columnIndex) => new TikzAxisSet(minorXValueNames, minorYValueNames, minorXAxisLabel, minorYAxisLabel, rectangles[rowIndex][columnIndex], lineGraphData: lineGraphData[rowIndex][columnIndex], fontScale: 0.7, xAxisSpace: xAxisSpaceMicro, yAxisSpace: yAxisSpaceMicro, xAxisLabelOffset: xAxisLabelOffsetMicro, yAxisLabelOffset: yAxisLabelOffsetMicro, horizontalLinesAttribute: "gray!30", verticalLinesAttribute: "gray!30", yAxisUseEndpoints: true)).ToList()).ToList();
            Initialized = true;
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
    }
}
