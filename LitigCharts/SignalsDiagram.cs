using ACESim;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using ACESimBase.Util.Tikz;
using MathNet.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LitigCharts
{
    public class SignalsDiagram
    {
        bool ChartIsForDamagesSignals = false; 
        bool ChartIsForLiabilitySignals => !ChartIsForDamagesSignals;
        public DiscreteValueSignalParameters liabilityParams;
        public double ExogenousProbabilityTrulyLiable = 0.5; 
        public double[] ProbabilitiesLiabilityStrength_TrulyNotLiable;
        public double[] ProbabilitiesLiabilityStrength_TrulyLiable;
        public double[] ProbabilityOfDamagesStrengthValues;
        public int NumLiabilityStrengthPoints = 10, NumDamagesStrengthPoints = 10, NumLiabilitySignals = 10, NumDamagesSignals = 10;
        public const double NoiseMultiplier = 1.0;
        public double PDamagesNoiseStdev = 0.2 * NoiseMultiplier, PLiabilityNoiseStdev = 0.2 * NoiseMultiplier;
        public double StdevNoiseToProduceLiabilityStrength = 0.35;
        public double CostPerParty = 0.2;
        public DiscreteValueSignalParameters PDamagesSignalParameters, PLiabilitySignalParameters;
        public double ProbabilityOfLitigation = 0;

        public record SignalsChartNode(int column, int row, double magnitude, double proportionOfTotal, string nodeName, List<double> proportionOfThisNodeToEachInNextColumn)
        {
            public double yBottom;
            public double yTop;
            public double height => yTop - yBottom;
            public double yCenter => 0.5* (yBottom + yTop);
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

            public List<double> flowValuesToNextColumn => proportionOfThisNodeToEachInNextColumn.Select(p => p * proportionOfTotal).ToList();
            public List<double> flowValuesToNextColumnDistances => flowValuesToNextColumn.Select(f => f * height / proportionOfTotal).ToList();
            public List<Flow> flowsToNextColumn = new List<Flow>();

            public double fromFlowsTotal = 0; // the total allocated flows starting in this column and going to the next column
            public double toFlowsTotal = 0; // the total allocated flows starting in the previous column and going to this one
            public void AllocateFlowsToNextColumn(List<SignalsChartNode> toNodes)
            {
                for (int i = 0; i < toNodes.Count; i++)
                {
                    var toNode = toNodes[i];
                    var flowSize = flowValuesToNextColumnDistances[i];

                    // account for flow from here to next column
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
            if (ChartIsForLiabilitySignals)
            {
                liabilityParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = 2, NumSignals = NumLiabilityStrengthPoints, StdevOfNormalDistribution = StdevNoiseToProduceLiabilityStrength, SourcePointsIncludeExtremes = true };
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
            else
            {
                // damages is simpler -- each damages level is equally likely. A case's damages strength is assumed to be equal to the true damages value. Of course, parties may still misestimate the damages strength.
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

            List<List<SignalsChartNode>> SignalsChartNodes = new List<List<SignalsChartNode>>();

            int numColumns = ChartIsForDamagesSignals ? 2 : 3; // true liability, case strength, signals, but we only have arrows from each of first two to the third
            double[] proportionOfTotalInSecondColumn = null;
            List<SignalsChartNode> trueLiabilityColumn = null;
            if (ChartIsForLiabilitySignals)
            {
                trueLiabilityColumn = new List<SignalsChartNode>()
                {
                    new SignalsChartNode(0, 0, 0, 1.0 - ExogenousProbabilityTrulyLiable, "Not Truly Liable", ProbabilitiesLiabilityStrength_TrulyNotLiable.ToList()),
                    new SignalsChartNode(0, 1, 1.0, ExogenousProbabilityTrulyLiable, "Truly Liable", ProbabilitiesLiabilityStrength_TrulyLiable.ToList()),
                };
                proportionOfTotalInSecondColumn = Enumerable.Range(0, NumLiabilityStrengthPoints).Select(strengthPointIndex => trueLiabilityColumn.Sum(y => y.flowValuesToNextColumn[strengthPointIndex])).ToArray();
            }
            int numStrengthPoints = ChartIsForLiabilitySignals ? NumLiabilityStrengthPoints : NumDamagesStrengthPoints;
            double[] uniformStrengthLevels = Enumerable.Range(0, numStrengthPoints).Select(strengthPointIndex => Game.ConvertActionToUniformDistributionDraw((byte)(strengthPointIndex + 1), numStrengthPoints, false)).ToArray();
            List<SignalsChartNode> caseStrengthColumn;
            if (ChartIsForLiabilitySignals)
            {
                double[][] probabilitiesOfDiscreteSignals = Enumerable.Range(0, NumLiabilityStrengthPoints).Select(strengthPointIndex => DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(strengthPointIndex + 1, PLiabilitySignalParameters).ToArray()).ToArray();
                // Estimate the probability of winning for each signal
                double[] winWeightAtSignal = new double[NumLiabilitySignals];
                double[] lossWeightAtSignal = new double[NumLiabilitySignals];
                double[] winProbabilityAtSignal = new double[NumLiabilitySignals];
                double[] caseGoesToTrial = new double[NumLiabilitySignals];
                foreach (var trueLiabilityProb in new double[] { 1 - ExogenousProbabilityTrulyLiable, ExogenousProbabilityTrulyLiable })
                {
                    for (int liabilityStrengthIndex = 0; liabilityStrengthIndex < NumLiabilityStrengthPoints; liabilityStrengthIndex++)
                    {
                        bool plaintiffWouldWin = liabilityStrengthIndex >= NumLiabilityStrengthPoints / 2; // e.g., if 10 strength points, then 5 and up are plaintiff winners
                        double liabilityStrengthProbability = proportionOfTotalInSecondColumn[liabilityStrengthIndex];
                        for (int signalIndex = 0; signalIndex < NumLiabilitySignals; signalIndex++)
                        {
                            double pSignalProbability = probabilitiesOfDiscreteSignals[liabilityStrengthIndex][signalIndex];
                            double combinedProbability = trueLiabilityProb * liabilityStrengthProbability * pSignalProbability;
                            if (plaintiffWouldWin)
                                winWeightAtSignal[signalIndex] += combinedProbability;
                            else
                                lossWeightAtSignal[signalIndex] += combinedProbability;
                        }
                    }
                }
                for (int signalIndex = 0; signalIndex < NumLiabilitySignals; signalIndex++)
                    winProbabilityAtSignal[signalIndex] = winWeightAtSignal[signalIndex] / (winWeightAtSignal[signalIndex] + lossWeightAtSignal[signalIndex]);
                // Now figure out which cases will be tried. We want to look at each combination of (pSignal, dSignal). We can calculate
                // each party's estimate of the plaintiff's win probability at the signal. 
                double combinedTrialPercentage = 0;
                foreach (var trueLiabilityProb in new double[] { 1 - ExogenousProbabilityTrulyLiable, ExogenousProbabilityTrulyLiable })
                {
                    for (int liabilityStrengthIndex = 0; liabilityStrengthIndex < NumLiabilityStrengthPoints; liabilityStrengthIndex++)
                    {
                        bool plaintiffWouldWin = liabilityStrengthIndex >= NumLiabilityStrengthPoints / 2; // e.g., if 10 strength points, then 5 and up are plaintiff winners
                        double liabilityStrengthProbability = proportionOfTotalInSecondColumn[liabilityStrengthIndex];
                        for (int pSignalIndex = 0; pSignalIndex < NumLiabilitySignals; pSignalIndex++)
                        {
                            double pSignalProbability = probabilitiesOfDiscreteSignals[liabilityStrengthIndex][pSignalIndex];
                            double combinedProbabilityWithPSignal = trueLiabilityProb * liabilityStrengthProbability * pSignalProbability;
                            if (plaintiffWouldWin)
                                winWeightAtSignal[pSignalIndex] += combinedProbabilityWithPSignal;
                            else
                                lossWeightAtSignal[pSignalIndex] += combinedProbabilityWithPSignal;
                            double pEstimateWinProbability = winProbabilityAtSignal[pSignalIndex];
                            for (int dSignalIndex = 0; dSignalIndex < NumLiabilitySignals; dSignalIndex++)
                            {
                                double dSignalProbability = probabilitiesOfDiscreteSignals[liabilityStrengthIndex][dSignalIndex];
                                double combinedProbabilityBothParties = combinedProbabilityWithPSignal * dSignalProbability;
                                double dEstimateWinProbability = winProbabilityAtSignal[dSignalIndex];
                                bool goesToTrial = pEstimateWinProbability - dEstimateWinProbability > 2 * CostPerParty;
                                if (goesToTrial)
                                    combinedTrialPercentage += combinedProbabilityBothParties;
                            }
                        }
                    }
                }
                ProbabilityOfLitigation = combinedTrialPercentage;

                StringBuilder explanation = new StringBuilder();
                explanation.AppendLine("Probability of signals given different liabilty strengths");
                for (int i = 0; i < NumLiabilityStrengthPoints; i++)
                {
                    explanation.Append("Case Strength " + uniformStrengthLevels[i].ToDecimalPlaces(2) + ": ");
                    for (int j = 0; j < probabilitiesOfDiscreteSignals[i].Length; j++)
                        explanation.Append($"{probabilitiesOfDiscreteSignals[i][j].ToDecimalPlaces(2)}{(j < probabilitiesOfDiscreteSignals[i].Length - 1 ? ", " : "")}");
                    explanation.AppendLine("");
                }
                caseStrengthColumn = Enumerable.Range(0, NumLiabilityStrengthPoints).Select<int, SignalsChartNode>(strengthPointIndex =>
                new SignalsChartNode(1, strengthPointIndex, uniformStrengthLevels[strengthPointIndex], proportionOfTotalInSecondColumn[strengthPointIndex], "Case Strength " + uniformStrengthLevels[strengthPointIndex].ToDecimalPlaces(2), probabilitiesOfDiscreteSignals[strengthPointIndex].ToList())).ToList();
            }
            else
                caseStrengthColumn = Enumerable.Range(0, NumDamagesStrengthPoints).Select<int, SignalsChartNode>(strengthPointIndex =>
                new SignalsChartNode(0, strengthPointIndex, uniformStrengthLevels[strengthPointIndex], ProbabilityOfDamagesStrengthValues[strengthPointIndex], "Damages Strength " + uniformStrengthLevels[strengthPointIndex].ToDecimalPlaces(2), DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(strengthPointIndex + 1, PDamagesSignalParameters).ToList())).ToList();
            int numSignals = ChartIsForLiabilitySignals ? NumLiabilitySignals : NumDamagesSignals;
            DiscreteValueSignalParameters signalParameters = ChartIsForLiabilitySignals ? PLiabilitySignalParameters : PDamagesSignalParameters;
            double[] uniformSignalValues = Enumerable.Range(0, numSignals).Select(signalIndex => signalParameters.MapSourceTo0To1(signalIndex + 1)).ToArray();
            double[] signalsLevels = new double[numSignals];
            for (int i = 0; i < caseStrengthColumn.Count(); i++)
            {
                for (int j = 0; j < signalsLevels.Count(); j++)
                {
                    double contribution = caseStrengthColumn[i].flowValuesToNextColumn[j];
                    signalsLevels[j] += contribution;
                }
            }
            List<SignalsChartNode> signalColumn = Enumerable.Range(0, numSignals).Select(signalIndex => new SignalsChartNode(numColumns - 1, signalIndex, uniformSignalValues[signalIndex], signalsLevels[signalIndex], "Signal " + uniformSignalValues[signalIndex].ToDecimalPlaces(2), null)).ToList();
            if (ChartIsForLiabilitySignals)
                SignalsChartNodes.Add(trueLiabilityColumn);
            SignalsChartNodes.Add(caseStrengthColumn);
            SignalsChartNodes.Add(signalColumn);

            const double horizontalSpaceBetweenNodes = 7, darkenedRectangleWidth = 1;
            double[] textWidthBetweenRectangles = ChartIsForLiabilitySignals ? new double[3] { 3.0, 3.9, 2.0 } : new double[2] { 4.1, 2.2 };
            double combinedVerticalDistance = NumLiabilitySignals * 2;
            double combinedVerticalSpaceBetweenNodes = 0.1 * combinedVerticalDistance;
            StringBuilder b = new StringBuilder();

            double horizontalLoc = 0;
            for (int col = 0; col < numColumns; col++)
            {
                int numRows = SignalsChartNodes[col].Count();

                double xLeftRectangleLeft = horizontalLoc;
                if (col > 0)
                    horizontalLoc += darkenedRectangleWidth;
                double xLeftRectangleRight = horizontalLoc;
                horizontalLoc += textWidthBetweenRectangles[col];
                double xRightRectangleLeft = horizontalLoc;
                if (col < numColumns - 1)
                    horizontalLoc += darkenedRectangleWidth;
                double xRightRectangleRight = horizontalLoc;
                double xCenter = 0.5 * (xLeftRectangleLeft + xRightRectangleRight);
                horizontalLoc += horizontalSpaceBetweenNodes;

                double verticalSpaceToAllocate = combinedVerticalDistance - combinedVerticalSpaceBetweenNodes;
                double verticalSpaceBetweenNodes = combinedVerticalSpaceBetweenNodes / (numRows - 1.0);

                double verticalLoc = 0;

                for (int row = 0; row < numRows; row++)
                {
                    SignalsChartNode node = SignalsChartNodes[col][row];
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
