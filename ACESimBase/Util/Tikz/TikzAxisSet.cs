using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Tikz
{

    public record TikzAxisSet(List<string> xValueNames, List<string> yValueNames, string xAxisLabel, string yAxisLabel, TikzRectangle sourceRectangle, string boxBordersAttributes="draw=none", int fontScale=1, double xAxisSpace = 2.0, double xAxisLabelOffset=1, double yAxisSpace=2.0, double yAxisLabelOffset=1, TikzLineGraphData lineGraphData=null)
    {
        const double betweenMiniGraphs = 0.1;
        const double spaceForAxesUnscaled = 0.5;
        int NumColumns => xValueNames.Count();
        int NumRows => yValueNames.Count();
        List<(double proportion, string value)> xMarks => xValueNames.Select((x, index) => ((0.5 + index) / (double) NumColumns, x)).ToList();
        List<(double proportion, string value)> yMarks => yValueNames.Select((y, index) => ((0.5 + index) / (double) NumRows, y)).ToList();

        TikzRectangle LeftAxisRectangle => sourceRectangle.LeftPortion(xAxisSpace).ReducedByPadding(0, yAxisSpace, 0, 0);
        TikzLine LeftAxisLine => LeftAxisRectangle.rightLine;
        List<TikzPoint> LeftAxisMarkPoints => yMarks.Select(m => LeftAxisLine.PointAlongLine(m.proportion)).ToList();
        List<TikzPoint> LeftAxisSpecifiedPoints(List<double> proportionalHeights) => proportionalHeights.Select(m => LeftAxisLine.PointAlongLine(m)).ToList();
        TikzRectangle BottomAxisRectangle => sourceRectangle.BottomPortion(yAxisSpace).ReducedByPadding(xAxisSpace, 0, 0, 0);
        TikzLine BottomAxisLine => BottomAxisRectangle.topLine;
        List<TikzPoint> BottomAxisMarkPoints => xMarks.Select(m => BottomAxisLine.PointAlongLine(m.proportion)).ToList();
        List<TikzPoint> GraphedPoints(List<double> proportionalHeights) => BottomAxisMarkPoints.Zip(LeftAxisSpecifiedPoints(proportionalHeights), (bottomAxisMark, leftAxisIntersection) => new TikzPoint(bottomAxisMark.x, leftAxisIntersection.y)).ToList();

        TikzRectangle MainRectangle => sourceRectangle.RightPortion(sourceRectangle.width - yAxisSpace).TopPortion(sourceRectangle.height - xAxisSpace);
        List<TikzRectangle> RowsWithSpaceBetweenMiniGraphs => Enumerable.Reverse(MainRectangle.DivideBottomToTop(NumRows)).ToList();
        List<List<TikzRectangle>> IndividualCellsWithSpaceBetweenMiniGraphs => RowsWithSpaceBetweenMiniGraphs.Select(x => x.DivideLeftToRight(NumColumns).ToList()).ToList();
        public List<List<TikzRectangle>> IndividualCells => IndividualCellsWithSpaceBetweenMiniGraphs.Select(row => row.Select(x => x.ReducedByPadding(betweenMiniGraphs, betweenMiniGraphs, 0, 0)).ToList()).ToList();

        public string GetDrawLineGraphCommands()
        {
            if (lineGraphData == null)
                return "";
            int numDataSeries = lineGraphData.lineAttributes.Count();
            if (lineGraphData.proportionalHeights.Any(x => x.Count() != NumColumns) || numDataSeries != lineGraphData.proportionalHeights.Count())
                throw new Exception("Invalid line graph data");
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < numDataSeries; i++)
            {
                List<double> proportionalHeights = lineGraphData.proportionalHeights[i];
                List<TikzPoint> graphedPoints = GraphedPoints(proportionalHeights);
                b.AppendLine($"\\draw[{lineGraphData.lineAttributes[i]}] {String.Join(" -- ", graphedPoints.Select(x => x.ToString()))};");
            }
            return b.ToString();
        }

        public string GetDrawAxesCommands()
        {
            string fontAttributes = $"fontscale={fontScale}";
            string leftAxisCommand = LeftAxisLine.DrawAxis("black", yMarks, fontAttributes, "east", yAxisLabel, "center", TikzHorizontalAlignment.Center, $"rotate=90, {fontAttributes}", 0 - yAxisLabelOffset, 0);
            string bottomAxisCommand = BottomAxisLine.DrawAxis("black", xMarks, fontAttributes, "north", xAxisLabel, "center", TikzHorizontalAlignment.Center, fontAttributes, 0, 0 - xAxisLabelOffset);
            string boxBorders = String.Join(Environment.NewLine, IndividualCells.SelectMany(x => x.Select(y => y.DrawCommand("blue"))));
            return leftAxisCommand + "\r\n" + bottomAxisCommand + "\r\n" + boxBorders;
        }

        public string GetDrawCommands()
        {
            return $"{GetDrawLineGraphCommands()}{GetDrawAxesCommands()}";
        }

    }
}
