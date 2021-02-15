using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Tikz
{

    public record TikzAxisSet(List<string> xValueNames, List<string> yValueNames, string xAxisLabel, string yAxisLabel, TikzRectangle sourceRectangle, string boxBordersAttributes="draw=none", string horizontalLinesAttribute="draw=none", string verticalLinesAttribute="draw=none", double fontScale=1, double xAxisSpace = 2.2, double xAxisLabelOffset=1.2, double yAxisSpace=2.0, double yAxisLabelOffset=1, bool xAxisUseEndpoints=false, bool yAxisUseEndpoints=false, TikzLineGraphData lineGraphData=null)
    {
        const double betweenMiniGraphs = 0.05;
        const double spaceForAxesUnscaled = 0.5;
        int NumColumns => xValueNames.Count();
        int NumRows => yValueNames.Count();
        List<(double proportion, string value)> xMarks => xValueNames.Select((x, index) => xAxisUseEndpoints ? ((index) / (double)(NumColumns - 1.0), x) : ((0.5 + index) / (double) NumColumns, x)).ToList();
        List<(double proportion, string value)> yMarks => yValueNames.Select((y, index) => yAxisUseEndpoints ? ((index) / (double)(NumRows - 1.0), y) : ((0.5 + index) / (double) NumRows, y)).ToList();

        TikzRectangle LeftAxisRectangle => sourceRectangle.LeftPortion(yAxisSpace).ReducedByPadding(0, xAxisSpace, 0, 0);
        TikzLine LeftAxisLine => LeftAxisRectangle.rightLine;
        List<TikzPoint> LeftAxisMarkPoints => yMarks.Select(m => LeftAxisLine.PointAlongLine(m.proportion)).ToList();
        List<TikzPoint> LeftAxisSpecifiedPoints(List<double> proportionalHeights) => proportionalHeights.Select(m => LeftAxisLine.PointAlongLine(m)).ToList();
        TikzRectangle BottomAxisRectangle => sourceRectangle.BottomPortion(xAxisSpace).ReducedByPadding(yAxisSpace, 0, 0, 0);
        TikzLine BottomAxisLine => BottomAxisRectangle.topLine;
        List<TikzPoint> BottomAxisMarkPoints => xMarks.Select(m => BottomAxisLine.PointAlongLine(m.proportion)).ToList();
        
        List<TikzLine> VerticalLines => BottomAxisMarkPoints.Select(bottomAxisPoint => new TikzLine(bottomAxisPoint, new TikzPoint(bottomAxisPoint.x, MainRectangle.top))).ToList();
        List<TikzLine> HorizontalLines => LeftAxisMarkPoints.Select(leftAxisPoint => new TikzLine(leftAxisPoint, new TikzPoint(MainRectangle.right, leftAxisPoint.y))).ToList();

        TikzRectangle MainRectangle => sourceRectangle.RightPortion(sourceRectangle.width - yAxisSpace).TopPortion(sourceRectangle.height - xAxisSpace);
        List<TikzRectangle> RowsWithSpaceBetweenMiniGraphs => Enumerable.Reverse(MainRectangle.DivideBottomToTop(NumRows)).ToList();
        List<List<TikzRectangle>> IndividualCellsWithSpaceBetweenMiniGraphs => RowsWithSpaceBetweenMiniGraphs.Select(x => x.DivideLeftToRight(NumColumns).ToList()).ToList();
        public List<List<TikzRectangle>> IndividualCells => IndividualCellsWithSpaceBetweenMiniGraphs.Select(row => row.Select(x => x.ReducedByPadding(betweenMiniGraphs, betweenMiniGraphs, 0, 0)).ToList()).Reverse().ToList();

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
                List<double?> proportionalHeights = lineGraphData.proportionalHeights[i];

                // Null values create discontinuities. So, we may end up with a series of paths, as well as some isolated points.
                // We'll have to separate the data into these points and paths.
                List<List<(int index, double value)>> pointPaths = new List<List<(int index, double value)>>();
                List<(int index, double value)> soloPoints = new List<(int index, double value)>();
                List<(int index, double value)> activeList = new List<(int index, double value)>();
                void addActiveList()
                {
                    if (activeList != null && activeList.Count() > 0)
                    {
                        if (activeList.Count() == 1)
                            soloPoints.Add(activeList.First());
                        else
                            pointPaths.Add(activeList);
                    }
                    activeList = new List<(int index, double value)>();
                }
                for (int heightIndex = 0; heightIndex < proportionalHeights.Count; heightIndex++)
                {
                    double? d = proportionalHeights[heightIndex];
                    if (d == null)
                    {
                        addActiveList();
                    }
                    else
                    {
                        activeList.Add((heightIndex, (double)d));
                    }
                }
                addActiveList();
                foreach (var pointPath in pointPaths)
                {
                    List<TikzPoint> graphedPoints = GraphedPoints(pointPath);
                    b.AppendLine($"\\draw[{lineGraphData.lineAttributes[i]}] {String.Join(" -- ", graphedPoints.Select(x => x.ToString()))};");
                }
                foreach (var soloPoint in soloPoints)
                {
                    b.AppendLine($"\\draw[{lineGraphData.lineAttributes[i]}] {GraphedPoints(new List<(int index, double value)>() { soloPoint }).First()} circle(0.05cm); ");
                }
            }
            return b.ToString();
        }

        List<TikzPoint> GraphedPoints(List<(int index, double value)> proportionalHeights) => BottomAxisMarkPoints
            .Select((item, index) => (item, index))
            .Where(x => proportionalHeights.Any(y => y.index == x.index))
            .Select(x => x.item)
            .Zip(
                LeftAxisSpecifiedPoints(proportionalHeights.Select(x => x.value).ToList()), 
                (bottomAxisMark, leftAxisIntersection) => new TikzPoint(bottomAxisMark.x, leftAxisIntersection.y))
            .ToList();

        public string GetDrawAxesCommands()
        {
            StringBuilder b = new StringBuilder();
            string fontAttributes = $"fontscale={fontScale}";

            if (boxBordersAttributes != "draw=none")
            {
                string boxBorders = String.Join(Environment.NewLine, IndividualCells.SelectMany(x => x.Select(y => y.DrawCommand(boxBordersAttributes))));
                b.AppendLine(boxBorders);
            }
            if (horizontalLinesAttribute != "draw=none")
            {
                string lines = String.Join(Environment.NewLine, HorizontalLines.Select(x => x.DrawCommand(horizontalLinesAttribute)));
                b.AppendLine(lines);
            }
            if (verticalLinesAttribute != "draw=none")
            {
                string lines = String.Join(Environment.NewLine, VerticalLines.Select(x => x.DrawCommand(verticalLinesAttribute)));
                b.AppendLine(lines);
            }

            string leftAxisCommand = LeftAxisLine.DrawAxis("black", yMarks, fontAttributes, "east", yAxisLabel, "center", TikzHorizontalAlignment.Center, $"rotate=90, {fontAttributes}", 0 - yAxisLabelOffset, 0);
            b.AppendLine(leftAxisCommand);
            string bottomAxisCommand = BottomAxisLine.DrawAxis("black", xMarks, fontAttributes, "north", xAxisLabel, "center", TikzHorizontalAlignment.Center, fontAttributes, 0, 0 - xAxisLabelOffset);
            b.AppendLine(bottomAxisCommand);
            return b.ToString();
        }

        public string GetDrawCommands()
        {
            return $"{GetDrawAxesCommands()}{GetDrawLineGraphCommands()}";
        }

    }
}
