using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Util.Tikz
{
    public record TikzSpacedLabels(TikzLine line, List<double> divisionPoints, List<string> text, List<string> shortText)
    {
        double distance => line.IsVertical ? line.end.y - line.start.y : line.end.x - line.start.x;
        List<(double start, double end)> ranges;
        List<double> textWidths;
        List<TikzHorizontalAlignment> alignments;
        List<int> row;
        List<bool> useShortText = new List<bool>();
        int numItems => text.Count();
        const double distanceForFirstRow = 0.32;
        const double rowHeight = 0.5;
        bool moveOverlapsFurtherBack = false;

        private (double start, double end) textRange(int i)
        {
            var originalRange = ranges[i];
            return alignments[i] switch
            {
                TikzHorizontalAlignment.Center => (0.5 * originalRange.start + 0.5 * originalRange.end - textWidths[i] / 2.0, 0.5 * originalRange.start + 0.5 * originalRange.end + textWidths[i] / 2.0),
                TikzHorizontalAlignment.Left => (originalRange.start, originalRange.start + textWidths[i]),
                TikzHorizontalAlignment.Right => (originalRange.end - textWidths[i], originalRange.end),
                _ => throw new NotImplementedException(),
            };
        }

        private TikzPoint textAnchorPoint(int i)
        {
            var originalRange = ranges[i];
            double anchorSpot = alignments[i] switch
            {
                TikzHorizontalAlignment.Center => (0.5 * originalRange.start + 0.5 * originalRange.end),
                TikzHorizontalAlignment.Left => (originalRange.start),
                TikzHorizontalAlignment.Right => (originalRange.end),
                _ => throw new NotImplementedException(),
            };
            TikzPoint anchorPoint;
            if (line.IsVertical)
                anchorPoint = new TikzPoint(line.start.x + distanceForFirstRow + (row[i]) * rowHeight, line.start.y + anchorSpot);
            else
                anchorPoint = new TikzPoint(line.start.x + anchorSpot, line.start.y - distanceForFirstRow - (row[i]) * rowHeight);
            return anchorPoint;
        }

        private string GetDrawCommand(int i)
        {
            TikzPoint anchorPoint = textAnchorPoint(i);
            string sideOfAnchor = alignments[i] switch
            {
                TikzHorizontalAlignment.Center => "centered",
                TikzHorizontalAlignment.Left => "right",
                TikzHorizontalAlignment.Right => "left",
                _ => throw new NotImplementedException(),
            };
            string command = "";
            if (textWidths[i] <= ranges[i].end - ranges[i].start || moveOverlapsFurtherBack)
            {
                command = TikzHelper.DrawText(anchorPoint.x, anchorPoint.y, useShortText[i] ? shortText[i] : text[i], "black, " + sideOfAnchor + (line.IsVertical ? ", rotate=90" : ""));
                if (row[i] > 0)
                {
                    TikzLine smallLine = line.IsVertical ? new TikzLine(new TikzPoint(line.start.x + .1, anchorPoint.y), new TikzPoint(anchorPoint.x, anchorPoint.y)) : new TikzLine(new TikzPoint(anchorPoint.x, line.start.y + .1), new TikzPoint(anchorPoint.x, anchorPoint.y));
                    TikzLine evenSmallerLine = line.IsVertical ? new TikzLine(smallLine.end, new TikzPoint(smallLine.end.x, smallLine.end.y + .15)) : new TikzLine(smallLine.end, new TikzPoint(smallLine.end.x + .15, smallLine.end.y));
                    command += $"\r\n{smallLine.DrawCommand("black, very thin")}";
                    command += $"\r\n{evenSmallerLine.DrawCommand("black, very thin")}";
                }
            }
            return command;
        }

        public string DrawCommand()
        {
            MakeAdjustments();
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < numItems; i++)
                b.AppendLine(GetDrawCommand(i));
            return b.ToString();
        }

        private bool overlaps(int earlier, int later) => textRange(earlier).end > textRange(later).start && row[earlier] == row[later];

        private bool overlapsLater(int earlier) => Enumerable.Range(earlier + 1, numItems - earlier - 1).Any(later => overlaps(earlier, later));

        private void MakeAdjustments()
        {
            textWidths = text.Select(x => TikzHelper.ApproximateStringWidth(x, 10)).ToList();
            alignments = textWidths.Select(x => TikzHorizontalAlignment.Center).ToList();
            useShortText = text.Select(x => false).ToList();
            row = textWidths.Select(x => 0).ToList();
            ranges = divisionPoints.Pairs().Select(x => (x.first * distance, x.last * distance)).ToList();
            for (int i = 0; i < numItems; i++)
            {
                var range = textRange(i);
                if (textWidths[i] > ranges[i].end - ranges[i].start)
                {
                    useShortText[i] = true;
                    textWidths[i] = TikzHelper.ApproximateStringWidth(shortText[i], 10);
                }
            }
            // realign to make fit
            for (int i = 0; i < numItems; i++)
            {
                var range = textRange(i);
                if (range.start < 0)
                    alignments[i] = TikzHorizontalAlignment.Left;
                else if (range.end > distance)
                    alignments[i] = TikzHorizontalAlignment.Right;
            }
            // remove overlaps
            for (int i = numItems - 2; i >= 0; i--)
            {
                while (moveOverlapsFurtherBack && overlapsLater(i))
                    row[i]++;
            }
        }
    }
}
