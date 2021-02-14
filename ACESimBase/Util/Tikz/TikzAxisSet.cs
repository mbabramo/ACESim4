using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Tikz
{
    public record TikzAxisSet(List<string> xValueNames, List<string> yValueNames, string xAxisLabel, string yAxisLabel, TikzRectangle sourceRectangle, string boxBordersAttributes="draw=none", string fontAttributes="")
    {
        const double betweenMiniGraphs = 0.15;
        const double spaceForMaximumGraph = 0.5;
        int NumRows => xValueNames.Count();
        int NumColumns => yValueNames.Count();
        List<(double proportion, string value)> majorXMarks => xValueNames.Select((x, index) => ((1.0 + index) / (double) NumRows, x)).ToList();
        List<(double proportion, string value)> majorYMarks => yValueNames.Select((y, index) => ((1.0 + index) / (double) NumColumns, y)).ToList();

        TikzRectangle LeftAxisRectangle => sourceRectangle.LeftPortion(spaceForMaximumGraph).ReducedByPadding(0, spaceForMaximumGraph, 0, 0);
        TikzLine LeftAxisLine => LeftAxisRectangle.rightLine;
        TikzRectangle BottomAxisRectangle => sourceRectangle.BottomPortion(spaceForMaximumGraph).ReducedByPadding(spaceForMaximumGraph, 0, 0, 0);
        TikzLine BottomAxisLine => BottomAxisRectangle.topLine;

        TikzRectangle MainRectangle => sourceRectangle.RightPortion(sourceRectangle.width - spaceForMaximumGraph).TopPortion(sourceRectangle.height - spaceForMaximumGraph);
        List<TikzRectangle> RowsWithSpaceBetweenMiniGraphs => Enumerable.Reverse(MainRectangle.DivideBottomToTop(NumRows)).ToList();
        List<List<TikzRectangle>> IndividualCellsWithSpaceBetweenMiniGraphs => RowsWithSpaceBetweenMiniGraphs.Select(x => x.DivideLeftToRight(NumColumns).ToList()).ToList();
        public List<List<TikzRectangle>> IndividualCells => IndividualCellsWithSpaceBetweenMiniGraphs.Select(row => row.Select(x => x.ReducedByPadding(betweenMiniGraphs, betweenMiniGraphs, 0, 0)).ToList()).ToList();

        public string GetDrawAxesCommands()
        {
            string leftAxisCommand = LeftAxisLine.DrawAxis("black", majorYMarks, fontAttributes, "east", yAxisLabel, "center", TikzHorizontalAlignment.Center, $"rotate=90, {fontAttributes}", -0.8, 0);
            string bottomAxisCommand = BottomAxisLine.DrawAxis("black", majorXMarks, fontAttributes, "north", xAxisLabel, "center", TikzHorizontalAlignment.Center, fontAttributes, 0, -0.8);
            string boxBorders = String.Join(Environment.NewLine, IndividualCells.SelectMany(x => x.Select(y => y.DrawCommand("blue"))));
            return leftAxisCommand + "\r\n" + bottomAxisCommand + "\r\n" + boxBorders;
        }

    }
}
