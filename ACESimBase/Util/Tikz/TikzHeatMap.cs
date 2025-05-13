using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Tikz
{
    public record TikzHeatMap(string columnAxisLabel, string rowAxisLabel, bool columnAxisIsOnTop, string contentAttributes, TikzRectangle sourceRectangle, string fillColor, List<double> relativeWidths, List<List<(string text, double darkness)>> contents)
    {
        int NumRows => contents.Count();
        int NumColumns => contents.First().Count();
        double axisSpace
        {
            get
            {
                // true when SignalOfferReport passed "font=\\tiny"
                bool tiny = contentAttributes?.Contains("font=\\tiny") ?? false;

                /*  tiny font  → keep the normal values (0.25 / 0.40)
                    normal font → give ~60 % more vertical room              */
                return columnAxisIsOnTop
                    ? (tiny ? 0.25 : 0.40)          // top-axis variant
                    : (tiny ? 0.40 : 0.55);         // bottom-axis variant
            }
        }
        TikzRectangle LeftAxisRectangle => sourceRectangle.LeftPortion(axisSpace).DivideBottomToTop(new double[] { (columnAxisIsOnTop ? NumRows - 1.0 : 1.0) / (double)NumRows, (columnAxisIsOnTop ? 1.0 : NumRows - 1.0) / (double)NumRows }).Skip(columnAxisIsOnTop ? 0 : 1).First();
        TikzRectangle HorizontalAxisRectangle => sourceRectangle.TopOrBottomPortion(axisSpace, columnAxisIsOnTop).DivideLeftToRight(new double[] { 1.0 / (double) NumColumns,  (NumColumns - 1.0) / (double)NumColumns }).Skip(1).First();
        public TikzRectangle MainRectangle => sourceRectangle.RightPortion(sourceRectangle.width - axisSpace).TopOrBottomPortion(sourceRectangle.height - axisSpace, !columnAxisIsOnTop);

        public TikzRectangle MainRectangleWithoutAxes
        {
            get
            { // very inefficient
                var individualCells = IndividualCells.SelectMany(x => x).ToList();
                var bottoms = individualCells.Select(x => x.bottom).OrderBy(x => x).Distinct().ToList();
                var lefts = individualCells.Select(x => x.left).OrderBy(x => x).Distinct().ToList();
                var bottom = bottoms.Skip(1).First();
                var top = individualCells.Max(x => x.top);
                var left = lefts.Skip(1).First();
                var right = individualCells.Max(x => x.right);
                return new TikzRectangle(left, bottom, right, top);
            }
        }

        List<TikzRectangle> Rows => Enumerable.Reverse(MainRectangle.DivideBottomToTop(NumRows)).ToList();
        List<List<TikzRectangle>> IndividualCells => Rows.Select(x => x.DivideLeftToRight(relativeWidths.ToArray())).ToList();

        private string GetRectangleCommand(TikzRectangle rectangle, string text, double darkness, bool includeLine, string additionalAttributes)
        {
            string fill = darkness == 0 ? "none" : $"{fillColor}!{darkness}";
            string rectAttributes = $"fill={fill}, text={(darkness <= 50 ? "black" : "white")}";
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
                    bool isAxis = r == (columnAxisIsOnTop ? 0 : numRows - 1) || c == 0;
                    if (isAxis)
                        adjDarkness = 0;
                    b.AppendLine(GetRectangleCommand(cell, content.text, adjDarkness, !isAxis, contentAttributes));
                }
            }
            b.AppendLine(LeftAxisRectangle.DrawTextOnly("black, rotate=90, draw=none", rowAxisLabel));
            b.AppendLine(HorizontalAxisRectangle.DrawTextOnly("black, draw=none", columnAxisLabel));
            return b.ToString();
        }

        public string GetDocument()
        {
            return TikzHelper.GetStandaloneDocument(DrawCommands(), new List<string>() { "xcolor" });
        }
    }
}
