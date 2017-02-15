using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace ACESim
{
    [Serializable]
    public class InputValueFixInGraph
    {
        public string InputAbbreviation;
        public double Value;
    }

    [Serializable]
    public class InputValueVaryInGraph
    {
        public string InputAbbreviation;
        public double MinValue;
        public double MaxValue;
        public int NumValues;
    }

    

    [Serializable]
    public class StrategyGraphInfo
    {
        public List<InputValueVaryInGraph> InputsToGraph;
        public List<InputValueFixInGraph> InputsToFix;
        public string OutputReportFilename;
        public bool ReportAfterEachEvolutionStep;
        public bool ReportAfterEvolution;
        [OptionalSetting]
        public Graph2DSettings SettingsFor2DGraph;

        internal StringBuilder theBuilderMainReport;
        internal StringBuilder theBuilder3dReport; 
        internal string BaseOutputDirectory;

        public override string ToString()
        {
            if (theBuilderMainReport == null)
                return "";
            return theBuilderMainReport.ToString();
        }

        public string ThreeDReportString()
        {
            if (theBuilder3dReport == null)
                return "";
            return theBuilder3dReport.ToString();
        }

        public void SaveReport()
        {
            if (!isInitialized)
                return;
            TextFileCreate.CreateTextFile(
                Path.Combine(BaseOutputDirectory, SimulationInteraction.reportsSubdirectory, OutputReportFilename),
                ToString());

            string ThreeDReport = ThreeDReportString();
            if (ThreeDReport != "")
            {
                TextFileCreate.CreateTextFile(
                    Path.Combine(BaseOutputDirectory, SimulationInteraction.reportsSubdirectory, OutputReportFilename + "3d"),
                    ThreeDReportString());
            }


            InitializeReport(BaseOutputDirectory);
        }

        private bool isInitialized = false;
        private int evolveStep = 0;
        internal void InitializeReport(string baseOutputDirectory)
        {
            isInitialized = true;
            evolveStep = 0;
            BaseOutputDirectory = baseOutputDirectory;
            if (InputsToFix == null)
                InputsToFix = new List<InputValueFixInGraph>();
            theBuilderMainReport = new StringBuilder();
            List<string> colHeads = new List<string>();
            if (ReportAfterEachEvolutionStep)
                colHeads.Add("Step");
            colHeads.AddRange(InputsToGraph.Select(x => x.InputAbbreviation));
            colHeads.Add("Output");
            bool first = true;
            foreach (var colHead in colHeads)
            {
                if (!first)
                    theBuilderMainReport.Append("\t");
                first = false;
                theBuilderMainReport.Append(colHead);
            }
            theBuilderMainReport.Append("\n");
            theBuilder3dReport = new StringBuilder();
        }

        internal void AddFullLineTo3DReport(List<string> rowVals)
        {
            bool first = true;
            foreach (var rowVal in rowVals)
            {
                if (!first)
                    theBuilder3dReport.Append("\t");
                theBuilder3dReport.Append(rowVal);
                first = false;
            }
            theBuilder3dReport.Append("\n");
        }

        internal void AddSingleLineToReport(List<double> inputsToGraph, double output)
        {
            List<string> rowVals = new List<string>();
            if (ReportAfterEachEvolutionStep)
                rowVals.Add(evolveStep.ToString());
            inputsToGraph.Add(output);
            rowVals.AddRange(inputsToGraph.Select(x => x.ToSignificantFigures()));
            bool first = true;
            foreach (var rowVal in rowVals)
            {
                if (!first)
                    theBuilderMainReport.Append("\t");
                theBuilderMainReport.Append(rowVal);
                first = false;
            }
            theBuilderMainReport.Append("\n");
        }

        public void AddToReport(string baseOutputDirectory, bool newEvolveStep, Decision theDecision, Strategy theStrategy, string repetitionTagString)
        {
            if (theDecision.InformationSetAbbreviations == null)
                return;

            if (newEvolveStep)
                evolveStep++;

            if (!isInitialized)
                InitializeReport(baseOutputDirectory);
            int numInputs = theDecision.InformationSetAbbreviations.Count();

            // Check for an error
            foreach (var inputToFix in InputsToFix)
            {
                int index = theDecision.InformationSetAbbreviations.FindIndex(x => x == inputToFix.InputAbbreviation);
                if (index == -1)
                    return; 
                    //throw new Exception("The StrategyGraphInfo for report " + OutputReportFilename + " specifies a fixed input " + inputToFix.InputAbbreviation + " that does not exist in the input abbreviations for the decision.");
            }
            
            int iTG = 0;
            foreach (var inputToGraph in InputsToGraph)
            {
                int index = theDecision.InformationSetAbbreviations.FindIndex(x => x == inputToGraph.InputAbbreviation);
                if (index == -1)
                {
                    TabbedText.WriteLine("Input abbreviation " + inputToGraph.InputAbbreviation + " not found. Aborting.");
                    return;
                }
                iTG++;
            }

            // Set inputs that are fixed (either because the fixed value is specified or because the input is omitted, in which case we use the average value)
            List<double> theInputs = new List<double>(numInputs);
            for (int l = 0; l < numInputs; l++)
                theInputs.Add(0);
            for (int inpInd = 0; inpInd < numInputs; inpInd++)
            {
                var correspondingInputToGraph = InputsToGraph.Select((item, index) => new { Item = item, Index = index }).SingleOrDefault(x => x.Item.InputAbbreviation == theDecision.InformationSetAbbreviations[inpInd]);
                if (correspondingInputToGraph == null)
                {
                    var correspondingInputToFix = InputsToFix.Select((item, index) => new { Item = item, Index = index }).SingleOrDefault(x => x.Item.InputAbbreviation == theDecision.InformationSetAbbreviations[inpInd]);
                    theInputs[inpInd] = correspondingInputToFix.Item.Value;
                }
            }

            // Create a visual graph if there are 1 or 2 inputs to graph
            if (InputsToGraph.Count() == 1) // display a line graph
                Prepare2dData(theDecision, theStrategy, theInputs, InputsToGraph.First(), repetitionTagString);
            if (InputsToGraph.Count() == 2) // display a 3d scatterplot on the screen
                Prepare3dData(theDecision, theStrategy, theInputs, InputsToGraph.First(), InputsToGraph.Last());

            // Create a numeric report
            InputValueVaryInGraph nextToGraph = InputsToGraph.First();
            List<InputValueVaryInGraph> nextRemainingInputs = new List<InputValueVaryInGraph>(InputsToGraph);
            nextRemainingInputs.Remove(nextToGraph);
            CombineAllInputsToGraphHelper(theDecision, theStrategy, theInputs, nextToGraph, nextRemainingInputs);
        }


        private void Prepare2dData(Decision theDecision, Strategy theStrategy, List<double> theInputs, InputValueVaryInGraph xAxis, string repetitionTagString)
        {
            int xindex = theDecision.InformationSetAbbreviations.FindIndex(x => x == xAxis.InputAbbreviation);
            if (xindex == -1)
                return;
            if (xAxis.MinValue == 0 && xAxis.MaxValue == 0)
            {
                xAxis.MinValue = theStrategy.MinObservedInputs[xindex];
                xAxis.MaxValue = theStrategy.MaxObservedInputs[xindex];
            }
            List<double> xAxisValues = Enumerable.Range(1, xAxis.NumValues).Select(w => xAxis.MinValue + (w - 1) * (xAxis.MaxValue - xAxis.MinValue) / (xAxis.NumValues - 1)).ToList();
            bool eliminateUnobservedInputs = true;
            bool makeMorePointsIfFewPointsLeft = true; 
            if (eliminateUnobservedInputs && theStrategy.MinObservedInputs != null && theStrategy.MinObservedInputs.Length > xindex)
            {
                var xAxisValues2 = xAxisValues.Where(x => x >= theStrategy.MinObservedInputs[xindex] && x <= theStrategy.MaxObservedInputs[xindex]).ToList();
                if (makeMorePointsIfFewPointsLeft && xAxisValues2.Count() < 0.2 * xAxisValues.Count() && theStrategy.MinObservedInputs[xindex] != theStrategy.MaxObservedInputs[xindex])
                {
                    xAxisValues = Enumerable.Range(1, xAxis.NumValues).Select(w => theStrategy.MinObservedInputs[xindex] + (w - 1) * (theStrategy.MaxObservedInputs[xindex] - theStrategy.MinObservedInputs[xindex]) / (xAxis.NumValues - 1)).ToList();
                }
            }
            List<string> colHeads = new List<string> { "" }.Concat(xAxisValues.Select(w => w.ToSignificantFigures())).ToList();
            AddFullLineTo3DReport(colHeads);
            int xvalIndex = -1;
            double[] yVals = new double[xAxis.NumValues];

            // temporarily disable the general override value, which may have been set post-strategy optimization
            double? overrideValue = theStrategy.GeneralOverrideValue;
            theStrategy.GeneralOverrideValue = null;

            xvalIndex = -1;
            foreach (var xValue in xAxisValues)
            {
                xvalIndex++;
                theInputs[xindex] = xValue;
                yVals[xvalIndex] = theStrategy.Calculate(theInputs.ToList());
            }

            theStrategy.GeneralOverrideValue = overrideValue;

            List<double[]> points = new List<double[]>();
            List<System.Windows.Media.Color> colors = new List<System.Windows.Media.Color>();
            //for (xvalIndex = 0; xvalIndex < xAxis.NumValues; xvalIndex++)
            //{
            //    double[] pointToAdd = new double[] { xAxis.MinValue + xvalIndex * (xAxis.MaxValue - xAxis.MinValue) / (xAxis.NumValues - 1), yVals[xvalIndex] };
            //    points.Add(pointToAdd);
            //    colors.Add(System.Windows.Media.Colors.Beige);
            //}
            for (xvalIndex = 0; xvalIndex < xAxisValues.Count(); xvalIndex++)
            {
                double[] pointToAdd = new double[] { xAxisValues[xvalIndex], yVals[xvalIndex] };
                points.Add(pointToAdd);
                colors.Add(System.Windows.Media.Colors.Beige);
            }

            if (SettingsFor2DGraph == null)
                theStrategy.SimulationInteraction.Create2DPlot(points, new Graph2DSettings(), repetitionTagString);
            else
                theStrategy.SimulationInteraction.Create2DPlot(points,  SettingsFor2DGraph, repetitionTagString);
        }

        private void Prepare3dData(Decision theDecision, Strategy theStrategy, List<double> theInputs, InputValueVaryInGraph xAxis, InputValueVaryInGraph yAxis)
        {
            int xindex = theDecision.InformationSetAbbreviations.FindIndex(x => x == xAxis.InputAbbreviation);
            int yindex = theDecision.InformationSetAbbreviations.FindIndex(y => y == yAxis.InputAbbreviation);
            if (xindex == -1 || yindex == -1)
                return;
            List<double> xAxisValues = Enumerable.Range(1, xAxis.NumValues).Select(w => xAxis.MinValue + (w - 1) * (xAxis.MaxValue - xAxis.MinValue) / (xAxis.NumValues - 1)).ToList();
            List<double> yAxisValues = Enumerable.Range(1, yAxis.NumValues).Select(w => yAxis.MinValue + (w - 1) * (yAxis.MaxValue - yAxis.MinValue) / (yAxis.NumValues - 1)).ToList();
            List<string> colHeads = new List<string> { "" }.Concat(xAxisValues.Select(w => w.ToSignificantFigures())).ToList();
            AddFullLineTo3DReport(colHeads);
            int xvalIndex = -1, yvalIndex = -1;
            double[,] zVals = new double[xAxis.NumValues, yAxis.NumValues];
            double[,] zValsOverlay = null;
            foreach (var yValue in yAxisValues)
            {
                yvalIndex++;
                List<string> newRow = new List<string> { yValue.ToSignificantFigures() };
                xvalIndex = -1;
                foreach (var xValue in xAxisValues)
                {
                    xvalIndex++;
                    theInputs[xindex] = xValue;
                    theInputs[yindex] = yValue;
                    zVals[xvalIndex, yvalIndex] = theStrategy.Calculate(theInputs.ToList());
                    newRow.Add(zVals[xvalIndex, yvalIndex].ToSignificantFigures());
                }
                AddFullLineTo3DReport(newRow);
            }
            // Now, do the on screen version
            List<double[]> points = new List<double[]>();
            List<System.Windows.Media.Color> colors = new List<System.Windows.Media.Color>();
            for (xvalIndex = 0; xvalIndex < xAxis.NumValues; xvalIndex++)
                for (yvalIndex = 0; yvalIndex < yAxis.NumValues; yvalIndex++)
                {
                    double[] pointToAdd = new double[] { xAxis.MinValue + xvalIndex * (xAxis.MaxValue - xAxis.MinValue) / (xAxis.NumValues - 1), yAxis.MinValue + yvalIndex * (yAxis.MaxValue - yAxis.MinValue) / (yAxis.NumValues - 1), zVals[xvalIndex, yvalIndex] };
                    points.Add(pointToAdd);
                    colors.Add(System.Windows.Media.Colors.Beige);
                }
            // Uncomment and modify the following to put specific points in the 3d chart
            // Eventually, we should find a way to do this from the XML file.

            List<double[]> pointsToPlot = new List<double[]>
            {
                //new double[] { 0.02, 0.0, 0.02 },
                //new double[] { 0.02, 0.2, 0.1679 },
                //new double[] { 0.02, 0.5, 0.3665 },
                //new double[] { 0.2, 0.0, 0.2 },
                //new double[] { 0.2, 0.2, 0.2506 },
                //new double[] { 0.2, 0.5, 0.4143 },
                //new double[] { 0.5, 0.0, 0.5 },
                //new double[] { 0.5, 0.2, 0.5 },
                //new double[] { 0.5, 0.5, 0.5 },
                //new double[] { 0.8, 0.0, 0.8 },
                //new double[] { 0.8, 0.2, 0.7413 },
                //new double[] { 0.8, 0.5, 0.5854 }
            };
            foreach (var point in pointsToPlot)
            {
                points.Add(point);
                colors.Add(System.Windows.Media.Colors.Cyan);
            }

            // Uncomment this to highlight in red the correct answer for each of the points above (generally unnecessary because it will already be on the surface)
            //List<double[]> pointsToPlot2 = new List<double[]>();
            //foreach (var point in pointsToPlot)
            //{
            //    double correctAnswer = theStrategy.Calculate(new double[] { point[0], point[1] });
            //    points.Add(new double[] { point[0], point[1], correctAnswer });
            //    colors.Add(System.Windows.Media.Colors.Red);
            //}

            theStrategy.SimulationInteraction.Create3DPlot(points, colors, OutputReportFilename);
        }

        private void CombineAllInputsToGraphHelper(Decision theDecision, Strategy theStrategy, List<double> theInputs, InputValueVaryInGraph inputToGraph, List<InputValueVaryInGraph> remainingInputsToGraph)
        {
            // first find the index

            int index = theDecision.InformationSetAbbreviations.FindIndex(x => x == inputToGraph.InputAbbreviation);
            if (index == -1)
                throw new Exception("The StrategyGraphInfo for report " + OutputReportFilename + " specifies an input to graph " + inputToGraph.InputAbbreviation + " that does not exist in the input abbreviations for the decision.");
            // now go through each value to set the input
            if (inputToGraph.NumValues < 2)
                throw new Exception("The StrategyGraphInfo specifies a NumValues of less than 2.");
            double jumpSize = (inputToGraph.MaxValue - inputToGraph.MinValue) / (inputToGraph.NumValues - 1);
            double theValue = inputToGraph.MinValue;
            for (int i = 0; i < inputToGraph.NumValues; i++)
            {
                theInputs[index] = theValue;
                theValue += jumpSize;
                // if we have more inputs to graph, we have to set those up too
                if (remainingInputsToGraph.Any())
                {
                    InputValueVaryInGraph nextToGraph = remainingInputsToGraph.First();
                    List<InputValueVaryInGraph> nextRemainingInputs = new List<InputValueVaryInGraph>(remainingInputsToGraph);
                    nextRemainingInputs.Remove(nextToGraph);
                    CombineAllInputsToGraphHelper(theDecision, theStrategy, theInputs, nextToGraph, nextRemainingInputs);
                }
                else
                {
                    double output = theStrategy.Calculate(theInputs);
                    AddSingleLineToReport(theInputs.ToList(), output);
                }
            }
        }
    }
}
