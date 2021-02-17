using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Util.Tikz
{
    public record TikzLine(TikzPoint start, TikzPoint end)
    {
        public string DrawCommand(string attributes)
        {
            return $"\\draw[{attributes}] {start} -- {end};";
        }

        public TikzPoint PointAlongLine(double proportion)
        {
            return new TikzPoint((1.0 - proportion) * start.x + (proportion) * end.x, (1.0 - proportion) * start.y + (proportion) * end.y);
        }

        public TikzRectangle ToRectangle() => new TikzRectangle(start.x, start.y, end.x, end.y);

        public double Slope => (end.y - start.y) / (end.x - start.x);

        public double PerpindicularSlope => (end.x == start.x) ? 0 : -1.0 / Slope;

        public bool IsVertical => end.x == start.x;

        public string DrawLineWithText(string attributes, string label, string labelAttributes, string anchor, TikzHorizontalAlignment alignment, double shiftX = 0, double shiftY = 0)
        {
            string labelAttributesWithComma = labelAttributes == null ? "" : labelAttributes + ", ";
            (double x, double y) = alignment switch
            {
                TikzHorizontalAlignment.Center => (0.5 * start.x + 0.5 * end.x + shiftX, 0.5 * start.y + 0.5 * end.y + shiftY),
                TikzHorizontalAlignment.Left => (start.x + shiftX, start.y + shiftY),
                TikzHorizontalAlignment.Right or _ => (end.x + shiftX, end.y + shiftY),
            };
            return $@"{DrawCommand(attributes)}
{TikzHelper.DrawText(x, y, label, labelAttributesWithComma + "anchor=" + anchor)}";
        }

        public string DrawAxis(string attributes, List<(double proportion, string text)> axisMarks, string markTextAttributes, string anchor, string label, string labelAnchor, TikzHorizontalAlignment labelAlignment, string labelAttributes, double labelShiftX, double labelShiftY, double axisMarkShiftX=0, double axisMarkShiftY=0)
        {
            StringBuilder b = new StringBuilder();
            if (label == null)
                b.AppendLine(DrawCommand(attributes));
            else
            {
                b.AppendLine(DrawLineWithText(attributes, label, labelAttributes, labelAnchor, labelAlignment, labelShiftX, labelShiftY));
            }
            if (axisMarks != null)
            {
                double axisMarkHalfWidth = 0.05;
                List<TikzPoint> points = axisMarks.Select(x => PointAlongLine(x.proportion).WithTranslation(axisMarkShiftX, axisMarkShiftY)).ToList();
                for (int i = 0; i < axisMarks.Count; i++)
                {
                    var point = points[i];
                    string text = axisMarks[i].text;
                    TikzPoint first, second;
                    if (IsVertical)
                    {
                        // draw horizontal mark
                        first = new TikzPoint(point.x - axisMarkHalfWidth, point.y);
                        second = new TikzPoint(point.x + axisMarkHalfWidth, point.y);
                    }
                    else
                    {
                        // draw vertical mark
                        first = new TikzPoint(point.x, point.y - axisMarkHalfWidth);
                        second = new TikzPoint(point.x, point.y + axisMarkHalfWidth);
                    }
                    TikzLine markLine = new TikzLine(first, second);
                    string markCommand = markLine.DrawLineWithText(attributes, text, markTextAttributes, anchor, TikzHorizontalAlignment.Left);
                    b.AppendLine(markCommand);
                }
            }
            return b.ToString();
        }
    }
}
