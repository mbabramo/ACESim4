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
        public TikzRectangle sourceRectangle { get; init; }
        public List<List<TikzLineGraphData>> lineGraphData { get; init; }

        private bool Initialized;
        private TikzAxisSet outerAxisSet;
        private List<List<TikzAxisSet>> innerAxisSets;

        public void Initialize()
        {
            if (Initialized)
                return;
            outerAxisSet = new TikzAxisSet(majorXValueNames, majorYValueNames, majorXAxisLabel, majorYAxisLabel, sourceRectangle, fontScale:3, xAxisSpace:2.0, yAxisSpace: 2.3, xAxisLabelOffset:1.0, yAxisLabelOffset:1.3);
            var rectangles = outerAxisSet.IndividualCells;
            innerAxisSets = rectangles.Select((row, rowIndex) => row.Select((column, columnIndex) => new TikzAxisSet(minorXValueNames, minorYValueNames, minorXAxisLabel, minorYAxisLabel, rectangles[rowIndex][columnIndex], lineGraphData: lineGraphData[rowIndex][columnIndex], fontScale: 1, xAxisSpace: 1.0, yAxisSpace: 1.5, xAxisLabelOffset: 0.8, yAxisLabelOffset: 1.3)).ToList()).ToList();
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
