using ACESim;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
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
    public class SignalsChart
    {
        public DiscreteValueSignalParameters liabilityParams;
        public double ExogenousProbabilityTrulyLiable = 0.5;
        public double[] ProbabilitiesLiabilityStrength_TrulyNotLiable;
        public double[] ProbabilitiesLiabilityStrength_TrulyLiable;
        public double[] ProbabilityOfDamagesStrengthValues;
        public int NumLiabilityStrengthPoints = 10, NumDamagesStrengthPoints = 10, NumLiabilitySignals = 10, NumDamagesSignals = 10;
        public double PDamagesNoiseStdev = 0.2, DDamagesNoiseStdev = 0.2, PLiabilityNoiseStdev = 0.2, DLiabilityNoiseStdev = 0.2;
        public double StdevNoiseToProduceLiabilityStrength = 0.35;
        public DiscreteValueSignalParameters PDamagesSignalParameters, DDamagesSignalParameters, PLiabilitySignalParameters, DLiabilitySignalParameters;

        public record SignalsChartNode(int column, int row, double height, double proportionOfTotal, string nodeName, List<double> proportionOfThisNodeToEachInNextColumn)
        {
            public List<double> proportionOfTotalToEachInNextColumn => proportionOfThisNodeToEachInNextColumn.Select(x => x * proportionOfTotal).ToList();
        }
        public void CreateDiagram()
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
            DLiabilitySignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = NumLiabilityStrengthPoints,
                StdevOfNormalDistribution = DLiabilityNoiseStdev,
                NumSignals = NumLiabilitySignals
            };

            List<List<SignalsChartNode>> SignalsChartNodes = new List<List<SignalsChartNode>>();

            int numColumns = 3; // true liability, case strength, signals, but we only have arrows from each of first two to the third
            List<SignalsChartNode> trueLiabilityColumn = new List<SignalsChartNode>()
            {
                new SignalsChartNode(0, 0, 0.0, 1.0 - ExogenousProbabilityTrulyLiable, "Not Truly Liable", ProbabilitiesLiabilityStrength_TrulyNotLiable.ToList()),
                new SignalsChartNode(0, 1, 1.0, ExogenousProbabilityTrulyLiable, "Truly Liable", ProbabilitiesLiabilityStrength_TrulyLiable.ToList()),
            };
            double[] proportionOfTotalInSecondColumn = Enumerable.Range(0, NumLiabilityStrengthPoints).Select(strengthPointIndex => trueLiabilityColumn.Sum(y => y.proportionOfTotalToEachInNextColumn[strengthPointIndex])).ToArray();
            double[] uniformLiabilityStrengths = Enumerable.Range(0, NumLiabilityStrengthPoints).Select(strengthPointIndex => Game.ConvertActionToUniformDistributionDraw((byte)(strengthPointIndex + 1), NumLiabilityStrengthPoints, false)).ToArray();
            List<SignalsChartNode> caseStrengthColumn = Enumerable.Range(0, NumLiabilityStrengthPoints).Select<int, SignalsChartNode>(strengthPointIndex =>
                new SignalsChartNode(1, strengthPointIndex, uniformLiabilityStrengths[strengthPointIndex], proportionOfTotalInSecondColumn[strengthPointIndex], "Liability Strength " + uniformLiabilityStrengths[strengthPointIndex].ToDecimalPlaces(3), DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(strengthPointIndex + 1, PLiabilitySignalParameters).ToList())).ToList();
            double[] uniformSignalValues = Enumerable.Range(0, NumLiabilitySignals).Select(signalIndex => PLiabilitySignalParameters.MapSourceTo0To1(signalIndex + 1)).ToArray();
            List<SignalsChartNode> signalColumn = Enumerable.Range(0, NumLiabilitySignals).Select(signalIndex => new SignalsChartNode(2, signalIndex, uniformSignalValues[signalIndex], 1.0 / (double)NumLiabilitySignals, "Signal " + uniformSignalValues[signalIndex].ToDecimalPlaces(3), null)).ToList();
            SignalsChartNodes.Add(trueLiabilityColumn);
            SignalsChartNodes.Add(caseStrengthColumn);
            SignalsChartNodes.Add(signalColumn);

            const double spaceBetweenNodes = 5;
            StringBuilder b = new StringBuilder();
            for (int col = 0; col < 3; col++)
            {
                double xLoc = col * spaceBetweenNodes;
                foreach (SignalsChartNode node in SignalsChartNodes[col])
                {
                    double yLoc = 15.0 * node.height;
                    string nodeCommand = $@"\draw[color=black] ({xLoc}, {yLoc}) circle (0.4cm) node[draw=none] (C{col}R{node.row}) {{{node.row + 1}}};
\node[draw=none, below=0.25cm of C{col}R{node.row}] (C{col}R{node.row}T) {{{node.nodeName}}};";
                    b.AppendLine(nodeCommand);
                }
            }

            string document = TikzHelper.GetStandaloneDocument(b.ToString());

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
            DDamagesSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = NumDamagesStrengthPoints,
                StdevOfNormalDistribution = DDamagesNoiseStdev,
                NumSignals = NumDamagesSignals,
                SourcePointsIncludeExtremes = false
            };



        }

    }
}
