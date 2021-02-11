using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Tikz
{
    public record TikzHeatMap(string columnAxisLabel, string rowAxisLabel, string contentAttributes, TikzRectangle sourceRectangle, string fillColor, List<double> relativeWidths, List<List<(string text, double darkness)>> contents)
    {
        int NumRows => contents.Count();
        int NumColumns => contents.First().Count();
        const double axisSpace = 0.25;
        TikzRectangle LeftAxisRectangle => sourceRectangle.LeftPortion(axisSpace).DivideBottomToTop(new double[] { (NumRows - 1.0) / (double)NumRows, 1.0 / (double)NumRows }).First();
        TikzRectangle TopAxisRectangle => sourceRectangle.TopPortion(axisSpace).DivideLeftToRight(new double[] { 1.0 / (double) NumRows,  (NumRows - 1.0) / (double)NumRows }).Skip(1).First();
        TikzRectangle MainRectangle => sourceRectangle.RightPortion(sourceRectangle.width - axisSpace).BottomPortion(sourceRectangle.height - axisSpace);

        List<TikzRectangle> Rows => Enumerable.Reverse(MainRectangle.DivideBottomToTop(NumRows)).ToList();
        List<List<TikzRectangle>> IndividualCells => Rows.Select(x => x.DivideLeftToRight(relativeWidths.ToArray())).ToList();

        private string GetRectangleCommand(TikzRectangle rectangle, string text, double darkness, bool includeLine, string additionalAttributes)
        {
            string rectAttributes = $"fill={fillColor}!{darkness}, text={(darkness <= 50 ? "black" : "white")}";
            if (!includeLine)
                rectAttributes += ", draw=none";
            if (additionalAttributes != null)
                rectAttributes += ", " + additionalAttributes;
            return rectangle.DrawCommand(rectAttributes, text);
        }

        public string DrawCommands()
        {
            int numColumns = NumColumns;
            int numRows = NumRows;
            var cells = IndividualCells;
            StringBuilder b = new StringBuilder();
            for (int r = 0; r < numRows; r++)
            {
                for (int c = 0; c < numColumns; c++)
                {
                    var cell = cells[r][c];
                    var content = contents[r][c];
                    double adjDarkness = content.darkness * 100;
                    if (adjDarkness < 0.001)
                        adjDarkness = 0.001;
                    b.AppendLine(GetRectangleCommand(cell, content.text, adjDarkness, r != 0 && c != 0, contentAttributes));
                }
            }
            b.AppendLine(LeftAxisRectangle.DrawTextOnly("black, rotate=90, draw=none", rowAxisLabel));
            b.AppendLine(TopAxisRectangle.DrawTextOnly("black, draw=none", columnAxisLabel));
            return b.ToString();
        }

        public string GetDocument()
        {
            return TikzHelper.GetStandaloneDocument(DrawCommands(), new List<string>() { "xcolor" });
        }
    }
}
