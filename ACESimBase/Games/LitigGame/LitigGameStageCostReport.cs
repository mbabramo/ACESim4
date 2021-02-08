using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.LitigGame
{
    public class LitigGameStageCostReport
    {
        public record TikzPoint(double x, double y);
        public record TikzLine(TikzPoint start, TikzPoint end);
        public record TikzRectangle(double left, double bottom, double right, double top)
        {
            public TikzRectangle ReducedByPadding(double padding) => new TikzRectangle(left + padding, bottom + padding, right - padding, top - padding);

            public TikzRectangle VerticalPortion(double bottomProportion, double topProportion)
            {
                return new TikzRectangle(left, bottom + (top - bottom) * bottomProportion, right, bottom + (top - bottom) * topProportion);
            }

            public TikzRectangle HorizontalPortion(double leftProportion, double rightProportion)
            {
                return new TikzRectangle(left + (right - left) * leftProportion, bottom , right + (right - left) * rightProportion, top);
            }

            public List<TikzRectangle> DivideVertically(double[] proportions)
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
                return ranges.Select(x => VerticalPortion(x.bottom, x.top)).ToList();
            }

            public List<TikzRectangle> DivideHorizontally(double[] proportions)
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
                return ranges.Select(x => VerticalPortion(x.left, x.right)).ToList();
            }
        }

        public record StageCostDiagramLayout(double totalWidth, double totalHeight, double imagePadding, double padBelowPanel, double padBetweenPanels, double proportionDedicatedToText, int numPanels)
        {
            public TikzPoint AxisBottom => new TikzPoint(imagePadding, imagePadding);
            public TikzPoint AxisTop => new TikzPoint(imagePadding, totalHeight - imagePadding);
            public TikzLine AxisLine => new TikzLine(AxisBottom, AxisTop);

            public double PanelHeight => totalHeight - 2 * imagePadding - padBelowPanel;
            public double PanelBottom => imagePadding + padBelowPanel;
            public double PanelTop => PanelBottom + PanelHeight;
            public double PanelsAndRightStuffWidthIncludingPadding => totalWidth - 2 * imagePadding;
            public double RightStuffWidthIncludingPadding => PanelsAndRightStuffWidthIncludingPadding * proportionDedicatedToText;
            public double TextAreaWidth => RightStuffWidthIncludingPadding - 2 * padBetweenPanels;
            public double TextAreaLeft => totalWidth - TextAreaWidth;
            public double PanelsWidthIncludingPadding => PanelsAndRightStuffWidthIncludingPadding * (1.0 - proportionDedicatedToText);
            public double PerPanelWidthIncludingPadding => PanelsWidthIncludingPadding / numPanels;
            public double PanelLeftIncludingPadding(int i) => imagePadding + PerPanelWidthIncludingPadding * i;
            public double PanelRightIncludingPadding(int i) => PanelLeftIncludingPadding(i) + PerPanelWidthIncludingPadding;
            public double PanelLeftAfterPadding(int i) => PanelLeftIncludingPadding(i) + padBetweenPanels;
            public double PanelRightAfterPadding(int i) => PanelRightIncludingPadding(i) + padBetweenPanels;
            public TikzRectangle PanelRectangle(int i) => new TikzRectangle(PanelLeftAfterPadding(i), PanelBottom, PanelRightAfterPadding(i), PanelTop);
            public TikzRectangle TextAreaRectangle => new TikzRectangle(TextAreaLeft, PanelBottom, TextAreaLeft + TextAreaWidth, PanelTop);
        }

        public record AssessmentInStageCostDiagram(string assessmentName, List<StageInStageCostDiagram> stages)
        {
            public string GetTikzCodeForAssessment(StageCostDiagramLayout layout, double maxMagnitude, int panelIndex)
            {
                bool includeTextArea = panelIndex == layout.numPanels - 1;
                StringBuilder b = new StringBuilder();
                double cumulativeHeight = 0;
                List<(double bottom, double top)> ranges = new List<(double bottom, double top)>();
                List<string> correspondingText = new List<string>();
                for (int i = 0; i < stages.Count(); i++)
                {
                    var stage = stages[i];
                    double initialCumulativeHeight = cumulativeHeight;
                    string stageCode = stage.GetTikzCodeForAllComponents(layout.PanelLeftIncludingPadding(panelIndex),  maxMagnitude, ref cumulativeHeight);
                    double height = cumulativeHeight - initialCumulativeHeight;
                    ranges.Add((initialCumulativeHeight, cumulativeHeight));
                    correspondingText.Add($"\\shortstack[l]{{{stage.stageName} \\\\ ({(height * 100.0 / heightOfPanel).ToSignificantFigures(3)}\\%, {stage.GetWeightedAverageString()})}}");
                    b.AppendLine(stageCode);
                }
                for (int i = 0; i < ranges.Count; i++)
                {
                    (double bottom, double top) range = ranges[i];
                    b.AppendLine(DrawRegionSeparator(x, widthForMainImage, range.bottom, range.top));
                    string text = correspondingText[i];
                    b.AppendLine(TikzHelper.DrawText(x + widthForMainImage, ((range.bottom + range.top) / 2.0), text));
                }

                return b.ToString();
            }

            public string DrawRegionSeparator(double left, double widthForImage, double bottom, double top, bool includeText)
            {
                const double spaceToSeparationBars = 0.1;
                const double spaceToText = 0.05;
                const double widthExtendingBeyond = 0.025;
                double widthForImageAndPadding = widthForImage + spaceToSeparationBars;
                double separationBarJutOutLeft = left; // widthForImageAndPadding - 0.5 * widthOfSeparationBars;
                double separationBarJutOutRight = left + widthForImageAndPadding + widthExtendingBeyond;
                StringBuilder b = new StringBuilder();
                b.AppendLine(TikzHelper.DrawVerticalLine(left + widthForImageAndPadding, bottom, top - bottom));
                b.AppendLine(TikzHelper.DrawHorizontalLine(separationBarJutOutLeft, bottom, separationBarJutOutRight - separationBarJutOutLeft, "black, very thin, dotted"));
                b.AppendLine(TikzHelper.DrawHorizontalLine(separationBarJutOutLeft, top, separationBarJutOutRight - separationBarJutOutLeft, "black, very thin, dotted"));
                return b.ToString();
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

            public static string DrawVerticalLine(double x, double yBottom, double height, string attributes = "black, very thin", List<(double gridlineHeight, string text, bool left)> axisMarks = null)
            {
                StringBuilder b = new StringBuilder();
                b.AppendLine($"\\draw[{attributes}] ({x.ToSignificantFigures(3)},{yBottom.ToSignificantFigures(3)}) -- ({(x).ToSignificantFigures(3)}, {(yBottom + height).ToSignificantFigures(3)});");
                if (axisMarks != null)
                {
                    foreach (var axisMark in axisMarks)
                    {
                        double axisMarkWidth = 0.05;
                        string code = DrawHorizontalLine(x - 0.5 * axisMarkWidth, axisMark.gridlineHeight, axisMarkWidth, attributes);
                        b.AppendLine(code);
                        if (axisMark.left)
                        {
                            code = DrawText(x - axisMarkWidth, axisMark.gridlineHeight, axisMark.text, "black, anchor=east");
                        }
                        else
                        {
                            code = DrawText(x + axisMarkWidth, axisMark.gridlineHeight, axisMark.text, "black, anchor=west");
                        }
                        b.AppendLine(code);
                    }
                }
                return b.ToString();
            }

            public static string DrawHorizontalLine(double xLeft, double y, double width, string attributes = "black, very thin")
            {
                return $"\\draw[{attributes}] ({xLeft.ToSignificantFigures(3)},{y.ToSignificantFigures(3)}) -- ({(xLeft + width).ToSignificantFigures(3)}, {y.ToSignificantFigures(3)});";
            }

            public static string DrawText(double x, double y, string text, string attributes = "black, anchor=west")
            {
                return $"\\node[{attributes}] at ({x.ToSignificantFigures(3)}, {y.ToSignificantFigures(3)}) {{{text}}};";
            }
        }

        public record StageInStageCostDiagram(string stageName, List<(double weight, double magnitude)> regionComponents, string color)
        {
            public string GetWeightedAverageString()
            {
                string numberString = (regionComponents.Sum(x => x.weight * x.magnitude) / regionComponents.Sum(x => x.weight)).ToSignificantFigures(3);
                if (regionComponents.Select(x => x.magnitude).Count() > 1)
                    return numberString + " avg.";
                return numberString;
            }

            public string GetTikzCodeForAllComponents(double left, double bottom, double width, double totalHeight, double maxMagnitude)
            {
                StringBuilder b = new StringBuilder();
                for (int i = 0; i < regionComponents.Count; i++)
                {
                    (double weight, double magnitude) component = regionComponents[i];
                    if (component.magnitude > maxMagnitude)
                        throw new Exception();
                    string componentCode = GetTikzCodeForComponent(i, left, width, totalHeight, maxMagnitude, ref weightsSoFar);
                    b.AppendLine(componentCode);
                }
                return b.ToString();
            }

            private string GetTikzCodeForComponent(int indexInComponents, double left, double totalWidth, double totalHeight, double maxMagnitude, ref double heightSoFar)
            {
                (double weight, double magnitude) = regionComponents[indexInComponents];
                double height = weight * totalHeight;
                double width = totalWidth * (magnitude / maxMagnitude);
                double emptyWidth = totalWidth - width;
                double componentLeft = left + 0.5 * emptyWidth;
                double componentRight = componentLeft + width;
                double componentBottom = heightSoFar;
                double componentTop = componentBottom + height;
                heightSoFar = componentTop;
                string drawCommand = $"\\draw[black, very thin, fill={color}] ({componentLeft.ToSignificantFigures(3)},{componentBottom.ToSignificantFigures(3)}) rectangle ({componentRight.ToSignificantFigures(3)},{componentTop.ToSignificantFigures(3)});";
                return drawCommand;
            }
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
            List<AssessmentInStageCostDiagram> stageCostDiagram = new List<AssessmentInStageCostDiagram>();
            for (int i = 0; i < assessments.Count(); i++)
                stageCostDiagram.Add(new AssessmentInStageCostDiagram(assessments[i].assessmentName, new List<StageInStageCostDiagram>()));
            foreach (var namedFilter in namedFilters)
            {
                var applicableProgresses = litigProgresses.Where(x => namedFilter.filter(x.Item1)).ToList();
                for (int i = 0; i < assessments.Count; i++)
                {
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
                        stageCostDiagram[i].stages.Add(new StageInStageCostDiagram(namedFilter.stageName, weights.Zip(measures, (w, m) => (w, m)).OrderByDescending(x => x.m).ToList(), namedFilter.color));
                }
            }
            BuildTikzDiagram(stageCostDiagram);
            return csvStringBuilder.ToString();
        }

        private static void BuildTikzDiagram(List<AssessmentInStageCostDiagram> stageCostDiagram)
        {
            StringBuilder tikzBuilder = new StringBuilder();
            double width = 3;
            double height = 7;
            tikzBuilder.AppendLine(stageCostDiagram[0].GetTikzCodeForAssessment(1.25, width, 0.4, height, 1.5));
            tikzBuilder.AppendLine(TikzHelper.DrawVerticalLine(1, 0, height, axisMarks: Enumerable.Range(1, 10).Select(x => (height * (double)x / 10.0, (x * 10).ToString() + "\\%", true)).ToList()));
            string tikzDocument = TikzHelper.GetStandaloneDocument(tikzBuilder.ToString());
        }
    }
}
