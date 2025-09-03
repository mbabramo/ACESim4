using ACESim;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using ACESimBase.Util.Reporting;
using ACESimBase.Util.Tikz;
using MathNet.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LitigCharts
{
    public class SignalsDiagram
    {
        // Existing flags/modes
        bool ChartIsForDamagesSignals = false;

        // show uncollapsed signals from Hidden State -> Signal
        public bool ChartIsForHiddenState = true; 

        // Liability mode is now the fallback when neither damages nor hidden is selected
        bool ChartIsForLiabilitySignals => !ChartIsForDamagesSignals && !ChartIsForHiddenState;

        // Parameters for liability/damages (unchanged)
        public DiscreteValueSignalParameters liabilityParams;
        public double ExogenousProbabilityTrulyLiable = 0.5;
        public double[] ProbabilitiesLiabilityStrength_TrulyNotLiable;
        public double[] ProbabilitiesLiabilityStrength_TrulyLiable;
        public double[] ProbabilityOfDamagesStrengthValues;
        public int NumLiabilityStrengthPoints = 10, NumDamagesStrengthPoints = 10, NumLiabilitySignals = 10, NumDamagesSignals = 10;
        public const double NoiseMultiplier = 1.0;
        public double PDamagesNoiseStdev = 0.2 * NoiseMultiplier, PLiabilityNoiseStdev = 0.2 * NoiseMultiplier;
        public double StdevNoiseToProduceLiabilityStrength = 0.35;
        public DiscreteValueSignalParameters PDamagesSignalParameters, PLiabilitySignalParameters;

        // New counts/parameters for hidden-state diagram
        public int NumHiddenStates = 8, NumHiddenSignals = 8;
        public DiscreteValueSignalParameters PHiddenSignalParameters;

        public record SignalsChartNode(int column, int row, double magnitude, double proportionOfTotal, string nodeName, List<double> proportionOfThisNodeToEachInNextColumn)
        {
            public double yBottom;
            public double yTop;
            public double height => yTop - yBottom;
            public double yCenter => 0.5 * (yBottom + yTop);
            public double xLeftRectangleLeft;
            public double xLeftRectangleRight;
            public double xRightRectangleRight;
            public double xRightRectangleLeft;
            double textWidthBetweenRectangles => xRightRectangleLeft - xLeftRectangleRight;
            string color = $"blue!{magnitude * 100}!orange";
            public string GetDrawCommand()
            {
                StringBuilder b = new StringBuilder();
                TikzRectangle leftRectangle = new TikzRectangle(xLeftRectangleLeft, yBottom, xLeftRectangleRight, yTop);
                TikzRectangle rightRectangle = new TikzRectangle(xRightRectangleLeft, yBottom, xRightRectangleRight, yTop);
                if (leftRectangle.left != leftRectangle.right)
                    b.AppendLine(leftRectangle.DrawCommand($"fill={color}"));
                if (rightRectangle.left != rightRectangle.right)
                    b.AppendLine(rightRectangle.DrawCommand($"fill={color}"));

                string textCommand = $@"\draw[draw=black, fill=white, text=black, minimum width={textWidthBetweenRectangles}cm, minimum height=1cm, anchor=west] ({xLeftRectangleRight}, {yCenter}) node {{{nodeName}}};";
                b.AppendLine(textCommand);

                foreach (var flow in flowsToNextColumn)
                    b.AppendLine(flow.GetDrawCommand());

                return b.ToString();
            }

            public List<double> flowValuesToNextColumn => proportionOfThisNodeToEachInNextColumn?.Select(p => p * proportionOfTotal).ToList();
            public List<double> flowValuesToNextColumnDistances => flowValuesToNextColumn == null ? new List<double>() : flowValuesToNextColumn.Select(f => f * height / proportionOfTotal).ToList();
            public List<Flow> flowsToNextColumn = new List<Flow>();

            public double fromFlowsTotal = 0;
            public double toFlowsTotal = 0;
            public void AllocateFlowsToNextColumn(List<SignalsChartNode> toNodes)
            {
                if (proportionOfThisNodeToEachInNextColumn == null || toNodes == null)
                    return;

                for (int i = 0; i < toNodes.Count; i++)
                {
                    var toNode = toNodes[i];
                    var flowSize = flowValuesToNextColumnDistances[i];

                    var fromBottom = yBottom + fromFlowsTotal;
                    var fromTop = fromBottom + flowSize;
                    fromFlowsTotal += flowSize;

                    var toBottom = toNode.yBottom + toNode.toFlowsTotal;
                    var toTop = toBottom + flowSize;
                    toNode.toFlowsTotal += flowSize;

                    Flow f = new Flow(xRightRectangleRight, fromBottom, fromTop, toNode.xLeftRectangleLeft, toBottom, toTop, color, toNode.color);
                    flowsToNextColumn.Add(f);
                }
            }

        }

        public record Flow(double fromX, double fromBottom, double fromTop, double toX, double toBottom, double toTop, string leftColor, string rightColor)
        {
            public string GetDrawCommand()
            {
                if (double.IsNaN(fromBottom) || double.IsNaN(fromTop) || double.IsNaN(toBottom) || double.IsNaN(toTop))
                    return "";
                string textCommand = $@"\draw[draw=black, dotted, left color={leftColor}, right color={rightColor}, opacity=0.2, text=black] ({fromX}, {fromBottom}) -- ({fromX}, {fromTop}) -- ({toX}, {toTop}) -- ({toX}, {toBottom}) -- cycle ;";
                return textCommand;
            }
        }

        public string CreateDiagram()
        {
            // Returns a LaTeX math-mode string for the fraction (reduced)
            static string FractionLabel(int numerator, int denominator)
            {
                int Gcd(int a, int b)
                {
                    a = Math.Abs(a); b = Math.Abs(b);
                    if (a == 0) return b;
                    if (b == 0) return a;
                    while (b != 0) { int t = a % b; a = b; b = t; }
                    return a;
                }
                int g = Gcd(numerator, denominator);
                int n = numerator / g, d = denominator / g;
                return d == 1 ? $"${n}$" : $"$\\frac{{{n}}}{{{d}}}$";
            }

            if (ChartIsForLiabilitySignals)
            {
                liabilityParams = new DiscreteValueSignalParameters()
                {
                    NumPointsInSourceUniformDistribution = 2,
                    NumSignals = NumLiabilityStrengthPoints,
                    StdevOfNormalDistribution = StdevNoiseToProduceLiabilityStrength,
                    SourcePointsIncludeExtremes = true
                };
                ProbabilitiesLiabilityStrength_TrulyNotLiable = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, liabilityParams);
                ProbabilitiesLiabilityStrength_TrulyLiable = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(2, liabilityParams);
                PLiabilitySignalParameters = new DiscreteValueSignalParameters()
                {
                    NumPointsInSourceUniformDistribution = NumLiabilityStrengthPoints,
                    StdevOfNormalDistribution = PLiabilityNoiseStdev,
                    NumSignals = NumLiabilitySignals,
                    SourcePointsIncludeExtremes = false
                };
            }
            else if (ChartIsForHiddenState)
            {
                // Hidden state → Signal (uncollapsed), σ = 0.2 (shared)
                PHiddenSignalParameters = new DiscreteValueSignalParameters()
                {
                    NumPointsInSourceUniformDistribution = NumHiddenStates,
                    StdevOfNormalDistribution = PLiabilityNoiseStdev,
                    NumSignals = NumHiddenSignals,
                    SourcePointsIncludeExtremes = false
                };
            }
            else
            {
                // Damages column unchanged
                ProbabilityOfDamagesStrengthValues = new double[NumDamagesStrengthPoints];
                for (int i = 0; i < NumDamagesStrengthPoints; i++)
                    ProbabilityOfDamagesStrengthValues[i] = 1.0 / (double)NumDamagesStrengthPoints;
                PDamagesSignalParameters = new DiscreteValueSignalParameters()
                {
                    NumPointsInSourceUniformDistribution = NumDamagesStrengthPoints,
                    StdevOfNormalDistribution = PDamagesNoiseStdev,
                    NumSignals = NumDamagesSignals,
                    SourcePointsIncludeExtremes = false
                };
            }

            var SignalsChartNodes = new List<List<SignalsChartNode>>();
            int numColumns = ChartIsForLiabilitySignals ? 3 : 2;

            double[] proportionOfTotalInSecondColumn = null;
            List<SignalsChartNode> trueLiabilityColumn = null;

            if (ChartIsForLiabilitySignals)
            {
                trueLiabilityColumn = new List<SignalsChartNode>()
                {
                    new SignalsChartNode(0, 0, 0, 1.0 - ExogenousProbabilityTrulyLiable, "Truly Not Liable", ProbabilitiesLiabilityStrength_TrulyNotLiable.ToList()),
                    new SignalsChartNode(0, 1, 1.0, ExogenousProbabilityTrulyLiable, "Truly Liable", ProbabilitiesLiabilityStrength_TrulyLiable.ToList()),
                };
                proportionOfTotalInSecondColumn = Enumerable.Range(0, NumLiabilityStrengthPoints)
                    .Select(i => trueLiabilityColumn.Sum(y => y.flowValuesToNextColumn[i]))
                    .ToArray();
            }

            int numStrengthPoints =
                ChartIsForLiabilitySignals ? NumLiabilityStrengthPoints :
                ChartIsForHiddenState ? NumHiddenStates :
                NumDamagesStrengthPoints;

            // Strength/hidden numeric values for layout (0..1), but labels may be fractions
            double[] strengthValuesForLayout;
            if (ChartIsForHiddenState)
            {
                // Hidden index to (0,1): (h+1)/(H+1)
                strengthValuesForLayout = Enumerable.Range(0, NumHiddenStates)
                    .Select(h => (h + 1.0) / (NumHiddenStates + 1.0))
                    .ToArray();
            }
            else
            {
                strengthValuesForLayout = Enumerable.Range(0, numStrengthPoints)
                    .Select(i => Game.ConvertActionToUniformDistributionDraw((byte)(i + 1), numStrengthPoints, false))
                    .ToArray();
            }

            List<SignalsChartNode> caseStrengthColumn;

            if (ChartIsForLiabilitySignals)
            {
                double[][] probabilitiesOfDiscreteSignals = Enumerable.Range(0, NumLiabilityStrengthPoints)
                    .Select(i => DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(i + 1, PLiabilitySignalParameters).ToArray())
                    .ToArray();

                caseStrengthColumn = Enumerable.Range(0, NumLiabilityStrengthPoints)
                    .Select(i =>
                        new SignalsChartNode(
                            1, i,
                            strengthValuesForLayout[i],
                            proportionOfTotalInSecondColumn[i],
                            "Liability Strength " + strengthValuesForLayout[i].ToDecimalPlaces(2),
                            probabilitiesOfDiscreteSignals[i].ToList()))
                    .ToList();
            }
            else if (ChartIsForHiddenState)
            {
                double uniformPrior = 1.0 / NumHiddenStates;
                int denomHidden = NumHiddenStates + 1;
                caseStrengthColumn = Enumerable.Range(0, NumHiddenStates)
                    .Select(h =>
                        new SignalsChartNode(
                            0, h,
                            strengthValuesForLayout[h],
                            uniformPrior,
                            "Hidden State " + FractionLabel(h + 1, denomHidden),
                            DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h + 1, PHiddenSignalParameters).ToList()))
                    .ToList();
            }
            else
            {
                caseStrengthColumn = Enumerable.Range(0, NumDamagesStrengthPoints)
                    .Select(i =>
                        new SignalsChartNode(
                            0, i,
                            strengthValuesForLayout[i],
                            ProbabilityOfDamagesStrengthValues[i],
                            "Damages Strength " + strengthValuesForLayout[i].ToDecimalPlaces(2),
                            DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(i + 1, PDamagesSignalParameters).ToList()))
                    .ToList();
            }

            int numSignals =
                ChartIsForLiabilitySignals ? NumLiabilitySignals :
                ChartIsForHiddenState ? NumHiddenSignals :
                NumDamagesSignals;

            var signalParameters =
                ChartIsForLiabilitySignals ? PLiabilitySignalParameters :
                ChartIsForHiddenState ? PHiddenSignalParameters :
                PDamagesSignalParameters;

            // Signal numeric values for layout (0..1) and labels (fractions for hidden-state mode)
            double[] signalValuesForLayout;
            string[] signalLabels;
            if (ChartIsForHiddenState)
            {
                // Midpoints: (j+0.5)/S = (2j+1)/(2S), reduced
                signalValuesForLayout = Enumerable.Range(0, numSignals)
                    .Select(j => (j + 0.5) / numSignals)
                    .ToArray();

                int denom = 2 * numSignals;
                signalLabels = Enumerable.Range(0, numSignals)
                    .Select(j => FractionLabel(2 * j + 1, denom))
                    .ToArray();
            }
            else
            {
                signalValuesForLayout = Enumerable.Range(0, numSignals)
                    .Select(j => signalParameters.MapSourceTo0To1(j + 1))
                    .ToArray();

                signalLabels = Enumerable.Range(0, numSignals)
                    .Select(j => signalValuesForLayout[j].ToDecimalPlaces(3))
                    .ToArray();
            }

            // Aggregate mass in the signal column
            double[] signalsLevels = new double[numSignals];
            for (int i = 0; i < caseStrengthColumn.Count; i++)
                for (int j = 0; j < numSignals; j++)
                    signalsLevels[j] += caseStrengthColumn[i].flowValuesToNextColumn[j];

            List<SignalsChartNode> signalColumn = Enumerable.Range(0, numSignals)
                .Select(j => new SignalsChartNode(
                    numColumns - 1,
                    j,
                    signalValuesForLayout[j],
                    signalsLevels[j],
                    "Signal " + signalLabels[j],
                    null))
                .ToList();

            if (ChartIsForLiabilitySignals)
                SignalsChartNodes.Add(trueLiabilityColumn);
            SignalsChartNodes.Add(caseStrengthColumn);
            SignalsChartNodes.Add(signalColumn);

            const double horizontalSpaceBetweenNodes = 7, darkenedRectangleWidth = 1;

            double[] textWidthBetweenRectangles =
                ChartIsForLiabilitySignals ? new double[3] { 3.0, 3.9, 2.0 } :
                ChartIsForHiddenState ? new double[2] { 3.9, 2.2 } :
                new double[2] { 4.1, 2.2 };

            double combinedVerticalDistance = numSignals * 2;
            double combinedVerticalSpaceBetweenNodes = 0.1 * combinedVerticalDistance;
            StringBuilder b = new StringBuilder();

            double horizontalLoc = 0;
            for (int col = 0; col < numColumns; col++)
            {
                int numRows = SignalsChartNodes[col].Count;

                double xLeftRectangleLeft = horizontalLoc;
                if (col > 0)
                    horizontalLoc += darkenedRectangleWidth;
                double xLeftRectangleRight = horizontalLoc;
                horizontalLoc += textWidthBetweenRectangles[col];
                double xRightRectangleLeft = horizontalLoc;
                if (col < numColumns - 1)
                    horizontalLoc += darkenedRectangleWidth;
                double xRightRectangleRight = horizontalLoc;
                horizontalLoc += horizontalSpaceBetweenNodes;

                double verticalSpaceToAllocate = combinedVerticalDistance - combinedVerticalSpaceBetweenNodes;
                double verticalSpaceBetweenNodes = numRows > 1 ? combinedVerticalSpaceBetweenNodes / (numRows - 1.0) : 0.0;

                double verticalLoc = 0;
                for (int row = 0; row < numRows; row++)
                {
                    var node = SignalsChartNodes[col][row];
                    node.xLeftRectangleLeft = xLeftRectangleLeft;
                    node.xLeftRectangleRight = xLeftRectangleRight;
                    node.xRightRectangleRight = xRightRectangleRight;
                    node.xRightRectangleLeft = xRightRectangleLeft;

                    node.yBottom = verticalLoc;
                    node.yTop = node.yBottom + node.proportionOfTotal * verticalSpaceToAllocate;
                    verticalLoc = node.yTop + verticalSpaceBetweenNodes;
                }
            }

            for (int col = 0; col < numColumns; col++)
            {
                var nodesInCol = SignalsChartNodes[col];
                var nodesInNextCol = col == numColumns - 1 ? null : SignalsChartNodes[col + 1];

                foreach (var nodeInCol in nodesInCol)
                {
                    if (nodesInNextCol != null)
                        nodeInCol.AllocateFlowsToNextColumn(nodesInNextCol);
                    b.AppendLine(nodeInCol.GetDrawCommand());
                }
            }

            string document = TikzHelper.GetStandaloneDocument(b.ToString(), additionalPackages: new List<string>() { "xcolor" });
            return document;
        }



    }
}
