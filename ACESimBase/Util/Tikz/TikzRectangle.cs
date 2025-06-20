using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Util.Tikz
{
    public record TikzRectangle(double left, double bottom, double right, double top, string rectangleAttributes = "")
    {
        public double width => right - left;
        public double height => top - bottom;
        public TikzPoint bottomLeft => new TikzPoint(left, bottom);
        public TikzPoint bottomRight => new TikzPoint(right, bottom);
        public TikzPoint topLeft => new TikzPoint(left, top);
        public TikzPoint topRight => new TikzPoint(right, top);
        public TikzLine bottomLine => new TikzLine(new TikzPoint(left, bottom), new TikzPoint(right, bottom));
        public TikzLine topLine => new TikzLine(new TikzPoint(left, top), new TikzPoint(right, top));
        public TikzLine leftLine => new TikzLine(new TikzPoint(left, bottom), new TikzPoint(left, top));
        public TikzLine rightLine => new TikzLine(new TikzPoint(right, bottom), new TikzPoint(right, top));

        public TikzRectangle Offset(double x, double y) => new TikzRectangle(left + x, bottom + y, right + x, top + y, rectangleAttributes);
        
        public TikzRectangle MovedToOrigin => new TikzRectangle(0, 0, right - left, top - bottom, rectangleAttributes);

        public TikzRectangle ConditionallyMovedToOrigin(bool move) => move ? MovedToOrigin : this;

        public string DrawCommand(string attributes, string text=null)
        {
            string textDraw = text == null ? "" : $" node[midway] {{{text}}}";
            string drawCommand = $"\\draw[{attributes}{(rectangleAttributes is not (null or "") ? ", " + rectangleAttributes : "")}] {bottomLeft} rectangle {topRight}{textDraw};";
            return drawCommand;
        }

        public string DrawTextOnly(string attributes, string text, bool useContour = false)
        {
            return TikzHelper.DrawText((left + right) / 2.0, (bottom + top) / 2.0, text, attributes, useContour);
        }

        public TikzRectangle ReducedByPadding(double horizontalPadding, double verticalPadding) => new TikzRectangle(left + horizontalPadding, bottom + verticalPadding, right - horizontalPadding, top - verticalPadding);

        public TikzRectangle ReducedByPadding(double leftPadding, double bottomPadding, double rightPadding, double topPadding) => new TikzRectangle(left + leftPadding, bottom + bottomPadding, right - rightPadding, top - topPadding);

        public TikzRectangle TopOrBottomPortion(double amountToKeep, bool top) => top ? TopPortion(amountToKeep) : BottomPortion(amountToKeep);
        public TikzRectangle BottomPortion(double amountToKeep) => new TikzRectangle(left, bottom, right, bottom + amountToKeep);
        public TikzRectangle TopPortion(double amountToKeep) => new TikzRectangle(left, top - amountToKeep, right, top);
        public TikzRectangle LeftOrRightPortion(double amountToKeep, bool left) => left ? LeftPortion(amountToKeep) : RightPortion(amountToKeep);
        public TikzRectangle LeftPortion(double amountToKeep) => new TikzRectangle(left, bottom, left + amountToKeep, top);
        public TikzRectangle RightPortion(double amountToKeep) => new TikzRectangle(right - amountToKeep, bottom, right, top);

        public TikzRectangle ReduceHorizontally(double horizontalPctToKeep, TikzHorizontalAlignment alignment)
        {
            double horizontalAmountToEliminate = (1.0 - horizontalPctToKeep) * width;
            double reduceOnLeft = alignment switch
            {
                TikzHorizontalAlignment.Center => 0.5 * horizontalAmountToEliminate,
                TikzHorizontalAlignment.Left => 0,
                TikzHorizontalAlignment.Right or _ => horizontalAmountToEliminate,
            };
            double reduceOnRight = horizontalAmountToEliminate - reduceOnLeft;
            return ReducedByPadding(reduceOnLeft, 0, reduceOnRight, 0);
        }

        public TikzRectangle BottomToTopSubrectangle(double bottomProportion, double topProportion)
        {
            return new TikzRectangle(left, bottom + (top - bottom) * bottomProportion, right, bottom + (top - bottom) * topProportion);
        }

        public TikzRectangle LeftToRightSubrectangle(double leftProportion, double rightProportion)
        {
            return new TikzRectangle(left + (right - left) * leftProportion, bottom, left + (right - left) * rightProportion, top);
        }

        public List<TikzRectangle> DivideBottomToTop(int n) => DivideBottomToTop(Enumerable.Range(0, n).Select(x => 1.0 / n).ToArray());

        public List<TikzRectangle> DivideTopToBottom(double[] proportions)
        {
            var rectangles = DivideBottomToTop(proportions);
            rectangles.Reverse();
            return rectangles;
        }


        public List<TikzRectangle> DivideBottomToTop(double[] proportions)
        {
            double[] relative = proportions.Select(x => x / proportions.Sum()).ToArray();
            List<(double bottom, double top)> ranges = new List<(double bottom, double top)>();
            double bottomCumulative = 0;
            for (int r = 0; r < relative.Length; r++)
            {
                double topCumulative = bottomCumulative + relative[r];
                ranges.Add((bottomCumulative, topCumulative));
                bottomCumulative = topCumulative;
            }
            return ranges.Select(x => BottomToTopSubrectangle(x.bottom, x.top)).ToList();
        }

        public List<TikzRectangle> DivideLeftToRight(int n) => DivideLeftToRight(Enumerable.Range(0, n).Select(x => 1.0 / n).ToArray());

        public List<TikzRectangle> DivideLeftToRight(double[] proportions)
        {
            double[] relative = proportions.Select(x => x / proportions.Sum()).ToArray();
            List<(double left, double right)> ranges = new List<(double left, double right)>();
            double leftCumulative = 0;
            for (int r = 0; r < relative.Length; r++)
            {
                double rightCumulative = leftCumulative + relative[r];
                ranges.Add((leftCumulative, rightCumulative));
                leftCumulative = rightCumulative;
            }
            return ranges.Select(x => LeftToRightSubrectangle(x.left, x.right)).ToList();
        }

        public List<TikzLine> DividingLines(bool includeEndpoints, bool verticalLines, int n)
        {
            var rectangles = verticalLines ? DivideLeftToRight(n) : DivideBottomToTop(n);
            return DividingLines(includeEndpoints, verticalLines, rectangles);
        }

        public List<TikzLine> DividingLines(bool includeEndpoints, bool vertical, double[] proportions)
        {
            var rectangles = vertical ? DivideLeftToRight(proportions) : DivideBottomToTop(proportions);
            return DividingLines(includeEndpoints, vertical, rectangles);
        }

        private List<TikzLine> DividingLines(bool includeEndpoints, bool vertical, List<TikzRectangle> rectangles)
        {
            var lines = rectangles.Select(x => vertical ? x.leftLine : x.bottomLine).ToList();
            if (includeEndpoints)
                lines.Add(vertical ? rectangles.Last().rightLine : rectangles.Last().topLine);
            else
                lines.RemoveAt(0);
            return lines;
        }
    }
}
