using ACESim;
using ACESimBase.Util;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.LitigGame
{
    public class LitigGameStageCostReport
    {
        public record TikzPoint(double x, double y)
        {
            public override string ToString()
            {
                return $"({x.ToSignificantFigures(3)},{y.ToSignificantFigures(3)})";
            }
        }

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

            public string DrawLineWithText(string attributes, string label, string labelAttributes, string anchor, TikzHorizontalAlignment alignment, double shiftX=0, double shiftY=0)
            {
                string labelWithComma = labelAttributes == null ? "" : labelAttributes + ", ";
                (double x, double y) = alignment switch
                {
                    TikzHorizontalAlignment.Center => (0.5 * start.x + 0.5 * end.x + shiftX, 0.5 * start.y + 0.5 * end.y + shiftY),
                    TikzHorizontalAlignment.Left => (start.x + shiftX, start.y + shiftY),
                    TikzHorizontalAlignment.Right or _ => (end.x + shiftX, end.y + shiftY),
                };
                return $@"{DrawCommand(attributes)}
{TikzHelper.DrawText(x, y, label, labelWithComma + "anchor=" + anchor)}";
            }

            public string DrawAxis(string attributes, List<(double proportion, string text)> axisMarks, string anchor, string label, string labelAnchor, TikzHorizontalAlignment labelAlignment, string labelAttributes, double labelShiftX, double labelShiftY)
            {
                StringBuilder b = new StringBuilder();
                if (label == null)
                    b.AppendLine(DrawCommand(attributes));
                else
                {
                    b.AppendLine(DrawLineWithText(attributes, label, labelAttributes, labelAnchor, labelAlignment, labelShiftX, labelShiftY)) ;
                }
                if (axisMarks != null)
                {
                    double axisMarkHalfWidth = 0.05;
                    List<TikzPoint> points = axisMarks.Select(x => PointAlongLine(x.proportion)).ToList();
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
                        string markCommand = markLine.DrawLineWithText(attributes, text, null, anchor, TikzHorizontalAlignment.Left);
                        b.AppendLine(markCommand);
                    }
                }
                return b.ToString();
            }
        }

        public enum TikzHorizontalAlignment
        {
            Center,
            Left,
            Right
        }

        public record TikzRectangle(double left, double bottom, double right, double top)
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

            public string DrawCommand(string attributes)
            {
                string drawCommand = $"\\draw[{attributes}] {bottomLeft} rectangle {topRight};";
                return drawCommand;
            }

            public TikzRectangle ReducedByPadding(double horizontalPadding, double verticalPadding) => new TikzRectangle(left + horizontalPadding, bottom + verticalPadding, right - horizontalPadding, top - verticalPadding);

            public TikzRectangle ReducedByPadding(double leftPadding, double bottomPadding, double rightPadding, double topPadding) => new TikzRectangle(left + leftPadding, bottom + bottomPadding, right - rightPadding, top - topPadding);

            public TikzRectangle BottomPortion(double amountToKeep) => new TikzRectangle(left, bottom, right, bottom + amountToKeep);
            public TikzRectangle TopPortion(double amountToKeep) => new TikzRectangle(left, top - amountToKeep, right, top);
            public TikzRectangle LeftPortion(double amountToKeep) => new TikzRectangle(left, top, left + amountToKeep, top);
            public TikzRectangle RightPortion(double amountToKeep) => new TikzRectangle(right - amountToKeep, top, right, top);

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
                return new TikzRectangle(left + (right - left) * leftProportion, bottom , left + (right - left) * rightProportion, top);
            }

            public List<TikzRectangle> DivideBottomToTop(int n) => DivideBottomToTop(Enumerable.Range(0, n).Select(x => 1.0 / n).ToArray());

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
                var lines = rectangles.Select(x => vertical ?  x.leftLine : x.bottomLine).ToList();
                if (includeEndpoints)
                    lines.Add(vertical ? rectangles.Last().rightLine : rectangles.Last().topLine);
                else
                    lines.RemoveAt(0);
                return lines;
            }
        }



        public static class TikzHelper
        {
            static double[] relativeWidthsComputerModernFont = new double[255] { 4.625108242034912, 15.496148109436035, 12.42404556274414, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 60.1806640625, 4.625108242034912, 4.625108242034912, 16.94742774963379, 4.625108242034912, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 4.625108242034912, 4.625108242034912, 4.625108242034912, 4.625108242034912, 4.629629135131836, 8.603694915771484, 11.78430461883545, 16.538265228271484, 11.78430461883545, 16.538265228271484, 15.744808197021484, 8.603694915771484, 10.177045822143555, 10.177045822143555, 11.78430461883545, 15.758371353149414, 8.590131759643555, 9.383588790893555, 8.603694915771484, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 8.590131759643555, 8.603694915771484, 15.758371353149414, 15.758371353149414, 15.758371353149414, 11.37740421295166, 15.744808197021484, 15.35825252532959, 14.74790096282959, 14.951351165771484, 15.54135799407959, 14.354562759399414, 13.95444393157959, 15.839752197265625, 15.35825252532959, 9.79049015045166, 11.960628509521484, 15.744808197021484, 13.567888259887695, 17.725059509277344, 15.35825252532959, 15.744808197021484, 14.354562759399414, 15.744808197021484, 15.154802322387695, 12.56419849395752, 14.951351165771484, 15.35825252532959, 15.35825252532959, 19.318754196166992, 15.35825252532959, 15.35825252532959, 13.37121868133545, 8.603694915771484, 11.78430461883545, 8.603694915771484, 13.37121868133545, 15.758371353149414, 11.78430461883545, 11.78430461883545, 12.56419849395752, 10.97728443145752, 12.56419849395752, 10.97728443145752, 8.990251541137695, 11.78430461883545, 12.56419849395752, 8.590131759643555, 8.990251541137695, 12.164079666137695, 8.590131759643555, 16.538265228271484, 12.56419849395752, 11.78430461883545, 12.56419849395752, 12.164079666137695, 10.21773624420166, 10.25842571258545, 10.177045822143555, 12.56419849395752, 12.164079666137695, 14.951351165771484, 12.164079666137695, 12.164079666137695, 10.97728443145752, 11.78430461883545, 8.603694915771484, 11.78430461883545, 13.37121868133545, 12.491862297058105, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 4.625108242034912, 4.625108242034912, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 9.383588790893555, 8.603694915771484, 11.78430461883545, 13.764556884765625, 13.373480796813965, 15.35825252532959, 8.71446418762207, 10.97728443145752, 11.78430461883545, 20.51911163330078, 10.97728443145752, 12.56419849395752, 14.171457290649414, 4.625108242034912, 20.51911163330078, 11.78430461883545, 9.383588790893555, 15.758371353149414, 9.867350578308105, 9.867350578308105, 11.78430461883545, 12.56419849395752, 13.37121868133545, 8.603694915771484, 10.97728443145752, 9.867350578308105, 10.97728443145752, 12.56419849395752, 17.74766731262207, 17.74766731262207, 17.74766731262207, 11.37740421295166, 15.35825252532959, 15.35825252532959, 15.35825252532959, 15.35825252532959, 15.35825252532959, 15.35825252532959, 17.541954040527344, 14.951351165771484, 14.354562759399414, 14.354562759399414, 14.354562759399414, 14.354562759399414, 9.79049015045166, 9.79049015045166, 9.79049015045166, 9.79049015045166, 15.55492115020752, 15.35825252532959, 15.744808197021484, 15.744808197021484, 15.744808197021484, 15.744808197021484, 15.744808197021484, 13.814290046691895, 15.758371353149414, 15.35825252532959, 15.35825252532959, 15.35825252532959, 15.35825252532959, 15.35825252532959, 13.567888259887695, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 14.951351165771484, 10.97728443145752, 10.97728443145752, 10.97728443145752, 10.97728443145752, 10.97728443145752, 8.590131759643555, 8.590131759643555, 8.590131759643555, 9.383588790893555, 11.78430461883545, 12.56419849395752, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 15.758371353149414, 11.78430461883545, 12.56419849395752, 12.56419849395752, 12.56419849395752, 12.56419849395752, 12.164079666137695, 12.56419849395752 };

            public static double RelativeCharWidth(char c)
            {
                if (c >= 0 && c <= 255)
                    return relativeWidthsComputerModernFont[(byte)c];
                return 12.0; // guess
            }

            public static double ApproximateStringWidth(string s, double pointSize)
            {
                return s.ToCharArray().Sum(x => RelativeCharWidth(x)) * (pointSize / 10.0) / 69.0; // gives rough approximation in cm.
            }

            public static string GetStandaloneDocument(string contents)
            {
                return $@"\documentclass{{standalone}}
\usepackage{{tikz}}
\usetikzlibrary{{patterns}}
\begin{{document}}
\begin{{tikzpicture}}
{contents}
\end{{tikzpicture}}
\end{{document}}";
            }

            public static string DrawText(double x, double y, string text, string attributes = "black, very thin")
            {
                return $"\\node[{attributes}] at ({x.ToSignificantFigures(3)}, {y.ToSignificantFigures(3)}) {{{text}}};";
            }
        }

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
                const double multiplier = 1.2; // DEBUG
                textWidths = text.Select(x => TikzHelper.ApproximateStringWidth(x, 10) * multiplier).ToList();
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
                        textWidths[i] = TikzHelper.ApproximateStringWidth(shortText[i], 10) * multiplier;
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

        public record StageCostDiagram(TikzRectangle overallSpace, double imagePadding, double padBelowPanel, double padBetweenPanels, double proportionDedicatedToText, List<List<StageInStageCostDiagram>> panelData, List<(string assessmentName, double maxMagnitude)> assessmentInfo, List<string> stageNames, List<string> shortStageNames)
        {
            public TikzRectangle SpaceAfterPadding => overallSpace.ReducedByPadding(imagePadding, imagePadding);
            public TikzRectangle SpaceForTextArea => SpaceAfterPadding.LeftToRightSubrectangle(1.0 - proportionDedicatedToText, 1.0).ReducedByPadding(0, padBelowPanel, 0, 0);
            public TikzRectangle SpaceForPanelsAndHorizontalAxes => SpaceAfterPadding.LeftToRightSubrectangle(0, 1.0 - proportionDedicatedToText);
            public TikzRectangle SpaceForPanels => SpaceForPanelsAndHorizontalAxes.ReducedByPadding(0, padBelowPanel, 0, 0);
            public TikzRectangle SpaceForHorizontalAxes => SpaceForPanelsAndHorizontalAxes.BottomPortion(padBelowPanel);
            public double[] VerticalProportions => panelData[0].Select(x => x.TotalWeight).ToArray(); // each panel has same vertical proportions // DEBUG new double[] { 0.01, .08, .23, .1, .01 }.Proportionally(1.0).ToArray();
            public double[] VerticalDivisionValues => new double[] { 0 }.Then(VerticalProportions.CumulativeSum()).ToArray();
            public int NumPanels => panelData.Count();
            public int NumSubpanels => panelData.First().Count;
            public IEnumerable<(int panelIndex, int subpanelIndex)> PanelAndSubpanelIndices => Enumerable.Range(0, NumPanels).SelectMany(panelIndex => Enumerable.Range(0, NumSubpanels).Select(subpanelIndex => (panelIndex, subpanelIndex)));
            public List<TikzRectangle> Panels => SpaceForPanels.ReducedByPadding(padBetweenPanels,0,0,0).DivideLeftToRight(NumPanels).Select(x => x.ReducedByPadding(0, 0, padBetweenPanels, 0)).ToList();
            public TikzRectangle Panel(int panelIndex) => Panels[panelIndex];
            public List<TikzRectangle> HorizontalAxesRectangles => SpaceForHorizontalAxes.ReducedByPadding(padBetweenPanels, 0, 0, 0).DivideLeftToRight(NumPanels).Select(x => x.ReducedByPadding(0, 0, padBetweenPanels, 0)).ToList();
            public List<TikzRectangle> SlicesAcrossPanels => SpaceForPanels.DivideBottomToTop(VerticalProportions);
            public List<TikzLine> HorizontalLinesAcrossPanels => SpaceForPanels.DividingLines(false, false, VerticalProportions);
            public List<TikzRectangle> Subpanels(int panelIndex) => Panel(panelIndex).DivideBottomToTop(VerticalProportions);
            public TikzRectangle Subpanel(int panelIndex, int subpanelIndex) => Subpanels(panelIndex)[subpanelIndex];
            public List<TikzRectangle> TextSubpanels => SpaceForTextArea.DivideBottomToTop(VerticalProportions);
            public TikzRectangle TextSubpanel(int subpanelIndex) => TextSubpanels[subpanelIndex];
            public StageInStageCostDiagram StageInStageCostDiagram(int panelIndex, int subpanelIndex) => panelData[panelIndex][subpanelIndex];
            public List<TikzRectangle> StageInStageCostDiagramEncompassingRectangles(int panelIndex, int subpanelIndex) => Subpanel(panelIndex, subpanelIndex).DivideBottomToTop(StageInStageCostDiagram(panelIndex, subpanelIndex).VerticalProportions);
            public List<TikzRectangle> StageInStageCostDiagramProportionalRectangles(int panelIndex, int subpanelIndex)
            {
                var stage = StageInStageCostDiagram(panelIndex, subpanelIndex);
                var result = StageInStageCostDiagramEncompassingRectangles(panelIndex, subpanelIndex).Select((item, index) => item.ReduceHorizontally(stage.regionComponents[index].magnitude / assessmentInfo[panelIndex].maxMagnitude, TikzHorizontalAlignment.Left)).ToList();
                return result;
            }
            public List<TikzRectangle> StageInStageCostDiagramProportionalRectangles() => PanelAndSubpanelIndices.SelectMany(x => StageInStageCostDiagramProportionalRectangles(x.panelIndex, x.subpanelIndex)).ToList();
            public List<TikzLine> RegionSeparators => SpaceForPanels.DividingLines(false, false, VerticalProportions);

            public TikzPoint AxisStart => new TikzPoint(imagePadding, imagePadding + padBelowPanel);
            public TikzPoint LeftAxisTop => new TikzPoint(imagePadding, overallSpace.height - imagePadding);
            public TikzLine VerticalAxis => new TikzLine(AxisStart, LeftAxisTop);
            public TikzLine TextAreaAxis => SpaceForTextArea.leftLine;
            public List<TikzLine> HorizontalAxes => HorizontalAxesRectangles.Select(x => x.bottomLine).ToList();

            public string GetTikzDocument()
            {
                StringBuilder tikzBuilder = new StringBuilder();
                string attributes = "black, very thin";

                tikzBuilder.AppendLine(VerticalAxis.DrawAxis(attributes, Enumerable.Range(0, 11).Select(x => (0.1 * x, (x * 10).ToString() + "\\%")).ToList(), "east", "Proportion of Cases", "center", TikzHorizontalAlignment.Center, "rotate=90", -1.2, 0));
                tikzBuilder.AppendLine(TextAreaAxis.DrawAxis(attributes, VerticalDivisionValues.Select(x => (x, "")).ToList(), "west", null, null, TikzHorizontalAlignment.Center, null, 0, 0));
                //foreach (var rect in Panels)
                //    tikzBuilder.AppendLine(rect.DrawCommand(attributes));
                foreach (var rect in StageInStageCostDiagramProportionalRectangles())
                    tikzBuilder.AppendLine(rect.DrawCommand(attributes));
                //foreach (var rect in TextSubpanels) // DEBUG StageInStageCostDiagramProportionalRectangles())
                //    tikzBuilder.AppendLine(rect.DrawCommand("red"));
                foreach (var line in HorizontalLinesAcrossPanels)
                    tikzBuilder.AppendLine(line.DrawCommand("black, dotted"));
                for (int i = 0; i < HorizontalAxes.Count; i++)
                {
                    TikzLine horizontalAxis = HorizontalAxes[i];
                    tikzBuilder.AppendLine(horizontalAxis.DrawAxis(attributes, new List<(double proportion, string text)>() { ((1.0, assessmentInfo[i].maxMagnitude.ToSignificantFigures(2))) }, "north", assessmentInfo[i].assessmentName, "north", TikzHorizontalAlignment.Center, null, 0, 0));
                }
                var spacedLabels = new TikzSpacedLabels(TextAreaAxis, VerticalDivisionValues.ToList(), stageNames, shortStageNames);
                tikzBuilder.AppendLine(spacedLabels.DrawCommand());


                string tikzDocument = TikzHelper.GetStandaloneDocument(tikzBuilder.ToString());
                return tikzDocument;
            }
        }

        public record StageInStageCostDiagram(string stageName, List<(double weight, double magnitude)> regionComponents, string color)
        {
            public double TotalWeight => regionComponents.Sum(x => x.weight);

            public double[] VerticalProportions => regionComponents.Select(x => x.weight).ToArray();
        }

        public static string StageCostReport(List<(GameProgress theProgress, double weight)> gameProgresses)
        {
            double initialWeightSum = gameProgresses.Sum(x => x.weight);
            List<(LitigGameProgress theProgress, double weight)> litigProgresses = gameProgresses.Select(x => ((LitigGameProgress)x.theProgress, x.weight / initialWeightSum)).ToList();
            List<(Func<LitigGameProgress, bool> filter, string stageName, string shortStageName, string color)> namedStages = new List<(Func<LitigGameProgress, bool> filter, string stageName, string shortStageName, string color)>()
            {
                (prog => prog.PFiles == false, "P Doesn't File", "No Suit", "violet"),
                (prog => prog.PFiles && !prog.DAnswers, "D Doesn't Answer", "No Answer", "purple"),
                (prog => prog.CaseSettles, "Settles", "Settles", "blue"),
                (prog => prog.PAbandons, "P Abandons", "Abandoned", "green"),
                (prog => prog.DDefaults, "D Defaults", "Defaulted", "yellow"),
                (prog => prog.TrialOccurs && !prog.PWinsAtTrial, "P Loses", "D Wins", "orange"),
                (prog => prog.TrialOccurs && prog.PWinsAtTrial, "P Wins", "P Wins", "red"),
            };
            List<(Func<LitigGameProgress, double> assessmentMeasure, string assessmentName)> assessments = new List<(Func<LitigGameProgress, double> assessmentMeasure, string assessmentName)>()
            {
                (prog => prog.FalsePositiveExpenditures, "False Positives"),
                (prog => prog.FalsePositiveExpenditures, "False Negatives"),
                (prog => prog.TotalExpensesIncurred, "Total Expenditures"),
            };
            StringBuilder csvStringBuilder = new StringBuilder();
            csvStringBuilder.AppendLine($"Filter,Assessment,Data Type");
            List<List<StageInStageCostDiagram>> stagesForEachPanel = new List<List<StageInStageCostDiagram>>();
            List<(string assessmentName, double maxMagnitude)> maxMagnitudes = new List<(string assessmentName, double maxMagnitude)>();
            List<string> stageNames = namedStages.Where(namedStage => litigProgresses.Any(prog => namedStage.filter(prog.Item1))).Select(x => x.stageName).ToList();
            List<string> shortStageNames = namedStages.Where(namedStage => litigProgresses.Any(prog => namedStage.filter(prog.Item1))).Select(x => x.shortStageName).ToList();
            for (int i = 0; i < assessments.Count(); i++)
            {
                (Func<LitigGameProgress, double> assessmentMeasure, string assessmentName) assessment = assessments[i];
                var maxMagnitude = litigProgresses.Max(x => assessment.assessmentMeasure(x.theProgress));
                maxMagnitudes.Add((assessment.assessmentName, maxMagnitude));
                List<StageInStageCostDiagram> stages = new List<StageInStageCostDiagram>();
                foreach (var namedStage in namedStages)
                {
                    var applicableProgresses = litigProgresses.Where(prog => namedStage.filter(prog.Item1)).ToList();
                    Func<(LitigGameProgress theProgress, double weight), double> assessor = prog => assessment.assessmentMeasure(prog.theProgress);
                    var ordered = applicableProgresses.OrderByDescending(assessor).ToList();
                    var consolidated = ordered.GroupBy(x => assessor(x)).Select(x => ((LitigGameProgress theProgress, double weight))(x.First().theProgress, x.Sum(y => y.weight))).ToList();
                    var measures = consolidated.Select(assessor).ToList();
                    var weights = consolidated.Select(x => x.weight).ToList();
                    csvStringBuilder.Append($"{namedStage.stageName},{assessment.assessmentName},Values,");
                    csvStringBuilder.AppendLine(String.Join(",", measures));
                    csvStringBuilder.Append($"{namedStage.stageName},{assessment.assessmentName},Probabilities,");
                    csvStringBuilder.AppendLine(String.Join(",", weights));
                    if (measures.Any())
                    {
                        List<(double w, double m)> weightsAndMeasures = weights.Zip(measures, (w, m) => (w, m)).OrderByDescending(x => x.m).ToList();
                        stages.Add(new StageInStageCostDiagram(namedStage.stageName, weightsAndMeasures, namedStage.color));
                    }
                }
                stagesForEachPanel.Add(stages);
            }
            StageCostDiagram diagram = new StageCostDiagram(new TikzRectangle(0, 0, 20, 16), 1.5, 0.25, 0.25, 0.30, stagesForEachPanel, maxMagnitudes, stageNames, shortStageNames);
            string tikzCode = diagram.GetTikzDocument();
            return csvStringBuilder.ToString();
        }
    }
}
