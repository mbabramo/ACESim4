using ACESim;
using ACESimBase.Util;
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

            public double Slope => (end.y - start.y) / (end.x - start.x);

            public double PerpindicularSlope => (end.x == start.x) ? 0 : -1.0 / Slope;

            public bool IsVertical => end.x == start.x;

            public string DrawLineWithText(string attributes, string text, string anchor, bool atStart)
            {
                return $@"{DrawCommand(attributes)}
{TikzHelper.DrawText(atStart ? start.x : end.x, atStart ? start.y : end.y, text, attributes + ", anchor=" + anchor)}";
            }

            public string DrawAxis(string attributes, List<(double proportion, string text)> axisMarks, bool textBeforeMarks)
            {
                StringBuilder b = new StringBuilder();
                b.AppendLine(DrawCommand(attributes));
                if (axisMarks != null)
                {
                    double axisMarkHalfWidth = 0.05;
                    List<TikzPoint> points = axisMarks.Select(x => PointAlongLine(x.proportion)).ToList();
                    for (int i = 0; i < axisMarks.Count; i++)
                    {
                        var point = points[i];
                        string text = axisMarks[i].text;
                        TikzPoint first, second;
                        string anchor;
                        if (IsVertical)
                        {
                            // draw horizontal mark
                            first = new TikzPoint(point.x - axisMarkHalfWidth, point.y);
                            second = new TikzPoint(point.x + axisMarkHalfWidth, point.y);
                            anchor = textBeforeMarks ? "east" : "west";
                        }
                        else
                        {
                            // draw vertical mark
                            first = new TikzPoint(point.x, point.y - axisMarkHalfWidth);
                            second = new TikzPoint(point.x, point.y + axisMarkHalfWidth);
                            anchor = textBeforeMarks ? "north" : "south";
                        }
                        TikzLine markLine = new TikzLine(first, second);
                        string markCommand = markLine.DrawLineWithText(attributes, text, anchor, true);
                        b.AppendLine(markCommand);
                    }
                }
                return b.ToString();
            }
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

            public TikzRectangle ReducedByPadding_Pct(double horizontalPctToKeep, double verticalPctToKeep) => ReducedByPadding(0.5 * (width - horizontalPctToKeep * width), 0.5 * (height - verticalPctToKeep * height));

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

        public record StageCostDiagram(TikzRectangle overallSpace, double imagePadding, double padBelowPanel, double padBetweenPanels, double proportionDedicatedToText, List<List<StageInStageCostDiagram>> panelData, double maxMagnitude)
        {
            public TikzRectangle SpaceAfterPadding => overallSpace.ReducedByPadding(imagePadding, imagePadding);
            public TikzRectangle SpaceForTextArea => SpaceAfterPadding.LeftToRightSubrectangle(1.0 - proportionDedicatedToText, 1.0).ReducedByPadding(0, padBelowPanel, 0, 0);
            public TikzRectangle SpaceForPanels => SpaceAfterPadding.LeftToRightSubrectangle(0, 1.0 - proportionDedicatedToText).ReducedByPadding(0, padBelowPanel, 0, 0);
            public double[] VerticalProportions => panelData[0].Select(x => x.TotalWeight).CumulativeSum().ToArray(); // each panel has same vertical proportions
            public int NumPanels => panelData.Count();
            public int NumSubpanels => panelData.First().Count;
            public IEnumerable<(int panelIndex, int subpanelIndex)> PanelAndSubpanelIndices => Enumerable.Range(0, NumPanels).SelectMany(panelIndex => Enumerable.Range(0, NumSubpanels).Select(subpanelIndex => (panelIndex, subpanelIndex)));
            public List<TikzRectangle> Panels => SpaceForPanels.DivideLeftToRight(NumPanels).Select(x => x.ReducedByPadding(padBetweenPanels, 0, 0, 0)).ToList();
            public TikzRectangle Panel(int panelIndex) => Panels[panelIndex];
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
                var result = StageInStageCostDiagramEncompassingRectangles(panelIndex, subpanelIndex).Select((item, index) => item.ReducedByPadding_Pct(stage.regionComponents[index].magnitude / maxMagnitude, 1.0)).ToList();
                return result;
            }
            public List<TikzRectangle> StageInStageCostDiagramProportionalRectangles() => PanelAndSubpanelIndices.SelectMany(x => StageInStageCostDiagramProportionalRectangles(x.panelIndex, x.subpanelIndex)).ToList();
            public List<TikzLine> RegionSeparators => SpaceForPanels.DividingLines(false, false, VerticalProportions);

            public TikzPoint AxisBottom => new TikzPoint(imagePadding, imagePadding);
            public TikzPoint AxisTop => new TikzPoint(imagePadding, overallSpace.height - imagePadding);
            public TikzLine AxisLine => new TikzLine(AxisBottom, AxisTop);

            public string GetTikzDocument()
            {
                StringBuilder tikzBuilder = new StringBuilder();
                string attributes = "black, very thin";
                tikzBuilder.AppendLine(AxisLine.DrawAxis(attributes, Enumerable.Range(1, 10).Select(x => (0.1 * x, (x * 10).ToString() + "\\%")).ToList(), true));
                foreach (var rect in Panels) // DEBUG StageInStageCostDiagramProportionalRectangles())
                    tikzBuilder.AppendLine(rect.DrawCommand(attributes));
                foreach (var rect in StageInStageCostDiagramProportionalRectangles())
                    tikzBuilder.AppendLine(rect.DrawCommand(attributes));
                foreach (var rect in TextSubpanels) // DEBUG StageInStageCostDiagramProportionalRectangles())
                    tikzBuilder.AppendLine(rect.DrawCommand("red"));
                foreach (var line in HorizontalLinesAcrossPanels)
                    tikzBuilder.AppendLine(line.DrawCommand("black, dotted"));
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
            List<(Func<LitigGameProgress, bool> filter, string stageName, string color)> namedFilters = new List<(Func<LitigGameProgress, bool> filter, string stageName, string color)>()
            {
                (prog => prog.PFiles == false, "P Doesn't File", "violet"),
                (prog => prog.PFiles && !prog.DAnswers, "D Doesn't Answer", "purple"),
                (prog => prog.CaseSettles, "Settles", "blue"),
                (prog => prog.PAbandons, "P Abandons", "green"),
                (prog => prog.DDefaults, "D Defaults", "yellow"),
                (prog => prog.TrialOccurs && !prog.PWinsAtTrial, "P Loses", "orange"),
                (prog => prog.TrialOccurs && prog.PWinsAtTrial, "P Wins", "red"),
            };
            List<(Func<LitigGameProgress, double> assessmentMeasure, string assessmentName)> assessments = new List<(Func<LitigGameProgress, double> assessmentMeasure, string assessmentName)>()
            {
                (prog => prog.FalsePositiveExpenditures, "False +"),
                (prog => prog.FalsePositiveExpenditures, "False -"),
                (prog => prog.TotalExpensesIncurred, "Total Expenses"),
            };
            StringBuilder csvStringBuilder = new StringBuilder();
            csvStringBuilder.AppendLine($"Filter,Assessment,Data Type");
            List<List<StageInStageCostDiagram>> stagesForEachPanel = new List<List<StageInStageCostDiagram>>();
            for (int i = 0; i < assessments.Count(); i++)
            {
                List<StageInStageCostDiagram> stages = new List<StageInStageCostDiagram>();
                foreach (var namedFilter in namedFilters)
                {
                    var applicableProgresses = litigProgresses.Where(x => namedFilter.filter(x.Item1)).ToList();
                    var assessment = assessments[i];
                    Func<(LitigGameProgress theProgress, double weight), double> assessor = prog => assessment.assessmentMeasure(prog.theProgress);
                    var ordered = applicableProgresses.OrderByDescending(assessor).ToList();
                    var consolidated = ordered.GroupBy(x => assessor(x)).Select(x => ((LitigGameProgress theProgress, double weight))(x.First().theProgress, x.Sum(y => y.weight))).ToList();
                    var measures = consolidated.Select(assessor).ToList();
                    var weights = consolidated.Select(x => x.weight).ToList();
                    csvStringBuilder.Append($"{namedFilter.stageName},{assessment.assessmentName},Values,");
                    csvStringBuilder.AppendLine(String.Join(",", measures));
                    csvStringBuilder.Append($"{namedFilter.stageName},{assessment.assessmentName},Probabilities,");
                    csvStringBuilder.AppendLine(String.Join(",", weights));
                    if (measures.Any())
                    {
                        List<(double w, double m)> weightsAndMeasures = weights.Zip(measures, (w, m) => (w, m)).OrderByDescending(x => x.m).ToList();
                        stages.Add(new StageInStageCostDiagram(namedFilter.stageName, weightsAndMeasures, namedFilter.color));
                    }
                }
                stagesForEachPanel.Add(stages);
            }
            StageCostDiagram diagram = new StageCostDiagram(new TikzRectangle(0, 0, 10, 6), 0.1, 0.1, 0.1, 0.30, stagesForEachPanel, 2.0);
            string tikzCode = diagram.GetTikzDocument();
            return csvStringBuilder.ToString();
        }
    }
}
