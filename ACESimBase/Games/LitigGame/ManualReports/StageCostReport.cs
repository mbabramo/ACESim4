using ACESim;
using MathNet.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESimBase.Util.Tikz;
using System.Diagnostics;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Reporting;
using ACESimBase.Util.Mathematics;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    public class StageCostReport
    {
        static bool TargetPowerPoint = false; 
        static bool UseFixedAssessmentMaxMagnitudes = true; 
        static double[] FixedAsseessmentMaxMagnitudes = new double[] { 2.5, 2.5, 2.0 }; // 1.5, 1.5, 0.6 };  -- may need to depend on series we are running

        public static List<string> GenerateReport(List<(GameProgress theProgress, double weight)> gameProgresses)
        {
            double initialWeightSum = gameProgresses.Sum(x => x.weight);
            List<(LitigGameProgress theProgress, double weight)> litigProgresses = gameProgresses.Select(x => ((LitigGameProgress)x.theProgress, x.weight / initialWeightSum)).ToList();
            List<(Func<LitigGameProgress, bool> filter, string stageName, string shortStageName)> namedStages = new List<(Func<LitigGameProgress, bool> filter, string stageName, string shortStageName)>()
            {
                (prog => prog.PFiles == false, "P Doesn't File", "No Suit"),
                (prog => prog.PFiles && !prog.DAnswers, "D Doesn't Answer", "No Answer"),
                (prog => prog.CaseSettles, "Settles", "Settles"),
                (prog => prog.PAbandons, "P Abandons", "P Abandons"),
                (prog => prog.DDefaults, "D Defaults", "D Defaults"),
                (prog => prog.TrialOccurs && !prog.PWinsAtTrial, "P Loses", "D Wins"),
                (prog => prog.TrialOccurs && prog.PWinsAtTrial, "P Wins", "P Wins"),
            };
            List<(Func<LitigGameProgress, double> assessmentMeasure, string assessmentName)> assessments = new List<(Func<LitigGameProgress, double> assessmentMeasure, string assessmentName)>()
            {
                (prog => prog.FalseNegativeShortfall, "False Negatives"),
                (prog => prog.FalsePositiveExpenditures, "False Positives"),
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
                double maxMagnitude;
                if (UseFixedAssessmentMaxMagnitudes)
                    maxMagnitude = FixedAsseessmentMaxMagnitudes[i];
                else
                    maxMagnitude = litigProgresses.Max(x => assessment.assessmentMeasure(x.theProgress));
                maxMagnitudes.Add((assessment.assessmentName, maxMagnitude));
                List<StageInStageCostDiagram> stages = new List<StageInStageCostDiagram>();
                foreach (var namedStage in namedStages)
                {
                    var applicableProgresses = litigProgresses.Where(prog => namedStage.filter(prog.Item1)).ToList();
                    Func<(LitigGameProgress theProgress, double weight), double> assessor = prog => assessment.assessmentMeasure(prog.theProgress);
                    var ordered = applicableProgresses.OrderByDescending(assessor).ThenBy(x => x.theProgress.IsTrulyLiable).ToList();
                    var consolidated = ordered.GroupBy(x => (assessor(x), x.theProgress.IsTrulyLiable)).Select(x => ((LitigGameProgress theProgress, double weight))(x.First().theProgress, x.Sum(y => y.weight))).ToList();
                    var measures = consolidated.Select(assessor).ToList();
                    var weights = consolidated.Select(x => x.weight).ToList();
                    var isTrulyLiable = consolidated.Select(x => x.theProgress.IsTrulyLiable).ToList();
                    csvStringBuilder.Append($"{namedStage.stageName},{assessment.assessmentName},Values,");
                    csvStringBuilder.AppendLine(String.Join(",", measures));
                    csvStringBuilder.Append($"{namedStage.stageName},{assessment.assessmentName},Probabilities,");
                    csvStringBuilder.AppendLine(String.Join(",", weights));
                    if (measures.Any())
                    {
                        List<(double w, double m)> weightsAndMeasures = weights.Zip(measures, (w, m) => (w, m)).OrderByDescending(x => x.m).ToList();
                        List<(double w, double m, bool s)> weightsMeasuresAndShading = weightsAndMeasures.Zip(isTrulyLiable, (w, itl) => (w.w, w.m, itl)).OrderByDescending(x => x.m).ToList();
                        stages.Add(new StageInStageCostDiagram(namedStage.stageName, weightsMeasuresAndShading));
                    }
                }
                stagesForEachPanel.Add(stages);
            }
            // We can change the following to tweak individual charts, for example to make them comparable to each other or to squeeze some more words in
            double? heightOverride = null;
            double? maxMagnitudeForAccuracy = null; 
            if (maxMagnitudeForAccuracy is double nonNullMaxMagnitude)
            {
                maxMagnitudes[0] = (maxMagnitudes[0].assessmentName, Math.Max(maxMagnitudes[0].maxMagnitude, nonNullMaxMagnitude));
                maxMagnitudes[1] = (maxMagnitudes[1].assessmentName, Math.Max(maxMagnitudes[1].maxMagnitude, nonNullMaxMagnitude));
            }
            const double PowerPointOverallSpaceMultiplier = 2.0;
            var overallSpace = TargetPowerPoint ? new TikzRectangle(0, 0, 13.3333 * PowerPointOverallSpaceMultiplier, (heightOverride ?? 7.5) * PowerPointOverallSpaceMultiplier) : new TikzRectangle(0, 0, 20, heightOverride ?? 16);
            double proportionForText = TargetPowerPoint ? 0.03 : 0.30;
            var options = (LitigGameOptions) gameProgresses.First().theProgress.GameDefinition.GameOptions;
            string title = $"Costs: {options.CostsMultiplier}x; Fee Shift: {options.LoserPaysMultiple}x";
            bool pRiskNeutral = options.PUtilityCalculator is RiskNeutralUtilityCalculator;
            bool dRiskNeutral = options.DUtilityCalculator is RiskNeutralUtilityCalculator;
            string supplementalTitle = (pRiskNeutral, dRiskNeutral) switch
            {
                (true, true) => "",
                (true, false) => "; D Risk Averse",
                (false, true) => "; P Risk Averse",
                (false, false) => "; Both Risk Averse"
            };
            title += supplementalTitle;
            StageCostDiagram diagram = new StageCostDiagram(overallSpace, 1.5, 0.25, 0.25, proportionForText, stagesForEachPanel, maxMagnitudes, stageNames, shortStageNames, title);

            string csvFile = csvStringBuilder.ToString();
            string tikzCode = diagram.GetTikzDocument();

            return new List<string>() { csvFile, tikzCode };
        }

        public record StageCostDiagram(TikzRectangle overallSpace, double imagePadding, double padBelowPanel, double padBetweenPanels, double proportionDedicatedToText, List<List<StageInStageCostDiagram>> panelData, List<(string assessmentName, double maxMagnitude)> assessmentInfo, List<string> stageNames, List<string> shortStageNames, string title)
        {
            string TrulyLiablePattern => TargetPowerPoint ? "fill=blue" : "pattern color=blue, pattern=north east lines";
            string TrulyNotLiablePattern => TargetPowerPoint ? "fill=red" : "pattern color=orange, pattern=north west lines";
            string PenColor => TargetPowerPoint ? "white" : "black"; 
            string PenColorForStageName => TargetPowerPoint ? "yellow" : "black";

            public TikzRectangle SpaceAfterPadding => overallSpace.ReducedByPadding(imagePadding, imagePadding);
            public double imagePaddingVerticalProportion => imagePadding / overallSpace.height;
            public TikzRectangle SpaceAtTop => overallSpace.TopOrBottomPortion(imagePadding, true);
            public TikzRectangle SpaceForTextArea => SpaceAfterPadding.LeftToRightSubrectangle(1.0 - proportionDedicatedToText, 1.0).ReducedByPadding(0, padBelowPanel, 0, 0);
            public TikzRectangle SpaceForPanelsAndHorizontalAxes => SpaceAfterPadding.LeftToRightSubrectangle(0, 1.0 - proportionDedicatedToText);
            public TikzRectangle SpaceForPanels => SpaceForPanelsAndHorizontalAxes.ReducedByPadding(0, padBelowPanel, 0, 0);
            public TikzRectangle SpaceForHorizontalAxes => SpaceForPanelsAndHorizontalAxes.BottomPortion(padBelowPanel);
            public string MidpointOfHorizontalAxesString => $"({(SpaceForHorizontalAxes.left + SpaceForHorizontalAxes.right) / 2.0},{SpaceForHorizontalAxes.bottom})";
            public double[] VerticalProportions => panelData[0].Select(x => x.TotalWeight).ToArray(); // each panel has same vertical proportions
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
                var result = StageInStageCostDiagramEncompassingRectangles(panelIndex, subpanelIndex).Select((item, index) =>
                {
                    double maxMagnitude = assessmentInfo[panelIndex].maxMagnitude;
                    return item.ReduceHorizontally(maxMagnitude == 0 ? 0 : stage.regionComponents[index].magnitude / maxMagnitude, TikzHorizontalAlignment.Left) with { rectangleAttributes = stage.regionComponents[index].specialShading ? TrulyLiablePattern : TrulyNotLiablePattern };
                }).ToList();
                return result;
            }
            public List<TikzRectangle> StageInStageCostDiagramProportionalRectangles() => PanelAndSubpanelIndices.SelectMany(x => StageInStageCostDiagramProportionalRectangles(x.panelIndex, x.subpanelIndex)).ToList();
            public List<TikzLine> RegionSeparators => SpaceForPanels.DividingLines(false, false, VerticalProportions);

            public TikzPoint AxisStart => new TikzPoint(imagePadding, imagePadding + padBelowPanel);
            public TikzPoint LeftAxisTop => new TikzPoint(imagePadding, overallSpace.height - imagePadding);
            public TikzLine VerticalAxis => new TikzLine(AxisStart, LeftAxisTop);
            public TikzLine TextAreaAxis => SpaceForTextArea.leftLine;
            public List<TikzLine> HorizontalAxes => HorizontalAxesRectangles.Select(x => x.bottomLine).ToList();

            public string GetLegend()
            {
                return $@"\draw {MidpointOfHorizontalAxesString} node[draw=none] (baseCoordinate) {{}};
\begin{{scope}}[align=center]
        \matrix[scale=0.5, draw={PenColor}, below=0.5cm of baseCoordinate, nodes={{draw}}, column sep=0.1cm]{{
            \node[rectangle, draw, minimum width=0.5cm, minimum height=0.5cm, {TrulyLiablePattern}] {{}}; &
            \node[draw=none, font=\small, text={PenColor}] (B) {{Truly Liable Cases}}; &
            \node[rectangle, draw, minimum width=0.5cm, minimum height=0.5cm, {TrulyNotLiablePattern}] {{}}; &
            \node[draw=none, font=\small, text={PenColor}] (B) {{Truly Not Liable Cases}}; \\
            }};
\end{{scope}}";
            }

            public string GetTikzDocument()
            {
                StringBuilder tikzBuilder = new StringBuilder();
                string attributes = $"{PenColor}, very thin";

                if (TargetPowerPoint)
                {
                    tikzBuilder.AppendLine(overallSpace.DrawCommand("fill=black"));
                    tikzBuilder.AppendLine(SpaceAtTop.DrawCommand("text=white", $"\\huge {title}"));
                }
                bool showRectangles = false; 
                if (showRectangles)
                {
                    foreach (TikzRectangle r in new TikzRectangle[] { SpaceAfterPadding, SpaceAtTop,  SpaceForTextArea }) // SpaceForHorizontalAxes, SpaceForPanels, SpaceForPanelsAndHorizontalAxes,
                        tikzBuilder.AppendLine(r.DrawCommand("orange"));
                }

                tikzBuilder.AppendLine(VerticalAxis.DrawAxis(attributes, Enumerable.Range(0, 11).Select(x => (0.1 * x, (x * 10).ToString() + "\\%")).ToList(), $"text={PenColor}", "east", "Proportion of Cases", $"center", TikzHorizontalAlignment.Center, $"rotate=90, text={PenColor}", -1.2, 0));
                tikzBuilder.AppendLine(TextAreaAxis.DrawAxis(attributes, VerticalDivisionValues.Select(x => (x, "")).ToList(), null, "west", null, null, TikzHorizontalAlignment.Center, $"text={PenColor}", 0, 0));
                foreach (var rect in StageInStageCostDiagramProportionalRectangles())
                    tikzBuilder.AppendLine(rect.DrawCommand(attributes));
                foreach (var line in HorizontalLinesAcrossPanels)
                    tikzBuilder.AppendLine(line.DrawCommand($"{PenColor}, dotted"));
                for (int i = 0; i < HorizontalAxes.Count; i++)
                {
                    TikzLine horizontalAxis = HorizontalAxes[i];
                    tikzBuilder.AppendLine(horizontalAxis.DrawAxis(attributes, new List<(double proportion, string text)>() { ((1.0, assessmentInfo[i].maxMagnitude.ToSignificantFigures(2))) }, $"text={PenColor}", "north", assessmentInfo[i].assessmentName, "north", TikzHorizontalAlignment.Center, $"text={PenColorForStageName}", 0, 0));
                }
                var spacedLabels = new TikzSpacedLabels(TextAreaAxis, VerticalDivisionValues.ToList(), stageNames, shortStageNames, $"text={PenColorForStageName}");
                tikzBuilder.AppendLine(spacedLabels.DrawCommand());
                tikzBuilder.AppendLine(GetLegend());


                string tikzDocument = TikzHelper.GetStandaloneDocument(tikzBuilder.ToString(), null, TargetPowerPoint ? @"\usepackage[sfdefault]{ClearSans} %% option 'sfdefault' activates Clear Sans as the default text font
\usepackage[T1]{fontenc}" : null);
                return tikzDocument;
            }
        }

        public record StageInStageCostDiagram(string stageName, List<(double weight, double magnitude, bool specialShading)> regionComponents)
        {
            public double TotalWeight => regionComponents.Sum(x => x.weight);

            public double[] VerticalProportions => regionComponents.Select(x => x.weight).ToArray();
        }
    }
}
