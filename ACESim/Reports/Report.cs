using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Diagnostics;

namespace ACESim
{
    [Serializable]
    public class Report
    {
        public string storeReportResultsFile;
        public List<string> namesOfVariableSetsChosen;
        public string reportInitiationTimeString;
        public List<string> colHeads;
        public List<string> rowHeads;
        public List<List<double?>> contents;
        public List<PointChartReport> pointChartReports;
        public bool parallelReporting;

        public Report()
        { // we need a paramaterless constructor for serialization
        }

        public Report(string theStoreReportResultsFile, List<string> theNamesOfVariableSetsChosen, string theReportInitiationTimeString, List<RowOrColumnGroup> theRowsGroups, List<RowOrColumnGroup> theColsGroups, List<PointChartReport> thePointChartReports, List<GameProgressReportable> theOutputs, bool doParallelReporting, SimulationInteraction simulationInteraction)
        {
            storeReportResultsFile = theStoreReportResultsFile;
            namesOfVariableSetsChosen = theNamesOfVariableSetsChosen;
            reportInitiationTimeString = theReportInitiationTimeString;
            parallelReporting = doParallelReporting;
            List<RowOrColInfo> theRows = GenerateRowsOrColumns(theRowsGroups, theOutputs);
            List<RowOrColInfo> theColumns = GenerateRowsOrColumns(theColsGroups, theOutputs);
            pointChartReports = thePointChartReports;

            if (pointChartReports != null && pointChartReports.Any())
                foreach (var output in theOutputs) 
                {
                    bool found;
                    double? xValue1 = (double?)output.GetValueForReport(pointChartReports[0].XAxisVariableName, null, out found);
                    double? xValue2 = (double?)output.GetValueForReport(pointChartReports[1].XAxisVariableName, null, out found);
                    double? yValue1 = (double?)output.GetValueForReport(pointChartReports[0].YAxisVariableName, null, out found);
                    double? yValue2 = (double?)output.GetValueForReport(pointChartReports[1].YAxisVariableName, null, out found);
                    //if (xValue1 > 0.40 && xValue1 < 0.45)
                    //{
                    //    Debug.WriteLine(String.Format("({0},{1}) ==> ({2},{3})", xValue1, yValue1, xValue2, yValue2));
                    //}
                }

            foreach (var chart in pointChartReports)
                Create2DPlot(theOutputs, chart, simulationInteraction);
            contents = new List<List<double?>>();
            GenerateRowAndColHeads(theRows, theColumns);
            GenerateStatistics(theRows, theColumns, theOutputs);
        }

        internal void Create2DPlot(List<GameProgressReportable> theOutputs, PointChartReport pointChartReport, SimulationInteraction simulationInteraction)
        {
            List<double[]> points = new List<double[]>();
            bool abort = false;
            foreach (GameProgressReportable output in theOutputs.ToList())
            {
                bool found;
                double? xValue = (double?) output.GetValueForReport(pointChartReport.XAxisVariableName, null, out found);
                if (!found)
                    abort = true;
                double? yValue = (double?)output.GetValueForReport(pointChartReport.YAxisVariableName, null, out found);
                if (!found)
                    abort = true;
                if (found && xValue != null && yValue != null)
                    points.Add(new double[] { (double) xValue, (double) yValue });
            }
            if (!abort)
                simulationInteraction.Create2DPlot(points, pointChartReport.Graph2DSettings, "");
        }

        internal void GenerateStatistics(List<RowOrColInfo> theRows, List<RowOrColInfo> theColumns, List<GameProgressReportable> theOutputs)
        {
            bool getOutputValuesForEachCellIndividually = true; // DEBUG // if this is set to true, then we proceed entirely on a cell-by-cell basis. If false, then we figure out all the filters and variable values for each row and column separately, to make it faster to calculate each individual cell. It may be easier to trace problems by setting this to true.
            List<double?>[] outputValuesForRow = new List<double?>[theRows.Count], outputValuesForColumn = new List<double?>[theColumns.Count];
            BitArray[] filtersForRow = new BitArray[theRows.Count], filtersForColumn = new BitArray[theColumns.Count];
            BitArray[] nonNullFiltersForRow = new BitArray[theRows.Count], nonNullFiltersForColumn = new BitArray[theColumns.Count];
            if (!getOutputValuesForEachCellIndividually)
            {
                Parallelizer.Go(parallelReporting, 0, theRows.Count, r =>
                {
                    RowOrColInfo row = theRows[r];
                    outputValuesForRow[r] = GetOutputValuesIncludingNulls(theOutputs, row);
                    filtersForRow[r] = FilterOutputsForSingleRowOrColumnToBitArray(theOutputs, row);
                    nonNullFiltersForRow[r] = FilterOutputsForSingleRowOrColumnToBitArray(theOutputs, row, true);
                });
                Parallelizer.Go(parallelReporting, 0, theColumns.Count, c =>
                {
                    RowOrColInfo column = theColumns[c];
                    outputValuesForColumn[c] = GetOutputValuesIncludingNulls(theOutputs, column);
                    filtersForColumn[c] = FilterOutputsForSingleRowOrColumnToBitArray(theOutputs, column);
                    nonNullFiltersForColumn[c] = FilterOutputsForSingleRowOrColumnToBitArray(theOutputs, column, true);
                });
            }
            for (int r = 0; r < theRows.Count; r++)
            {
                RowOrColInfo row = theRows[r];
                double?[] newRow = new double?[theColumns.Count];

                Parallelizer.Go(parallelReporting, 0, theColumns.Count, c =>
                {
                    RowOrColInfo column = theColumns[c];
                    double? calculatedStatistic;
                    getOutputValuesForEachCellIndividually = true; // DEBUG -- false should be faster but isn't working right now ; // Set this to true to improve ability to step through and figure out what is causing an unexpected reporting result
                    if (getOutputValuesForEachCellIndividually)
                        calculatedStatistic = GenerateStatisticForIntersection(row, column, theOutputs); // this is slower, original algorithm -- keep for now
                    else
                    {
                        Statistic theStatistic = row.GetStatisticForIntersection(column);
                        string variableName = row.GetVariableNameForIntersection(column);
                        List<double?> outputValues;
                        if (row.variableName == variableName)
                            outputValues = outputValuesForRow[r];
                        else
                            outputValues = outputValuesForColumn[c];
                        int bitArraySize = filtersForRow[0].Count;
                        BitArray intersection = new BitArray(bitArraySize);
                        for (int baind = 0; baind < bitArraySize; baind++)
                            intersection[baind] = filtersForRow[r][baind] && filtersForColumn[c][baind];
                        calculatedStatistic = GenerateStatisticForOutputsWithFilterInfo(outputValues, intersection, theStatistic);
                        if (theStatistic == Statistic.percentOfAllCases || theStatistic == Statistic.percentOfCasesFilteredOpposite)
                        { // The calculated statistic is only the numerator (to be multiplied by 100), i.e. a count of the variable specified (which may be a dummy variable) in all cases in which the filter variables are true. If we are using percentOfNonNull, then the denominator is all those instances in which the filters are not null. If we are using percentOfTrue for a row or column, then the denominator is only those instances in which the filters are TRUE for that row or column and not null for the other row or column.
                            BitArray nonNullIntersection = new BitArray(bitArraySize);
                            bool rowFiltersMustBeTrueForObservationToCountInDenominator = false, colFiltersMustBeTrueForObservationToCountInDenominator = false; // For percentOfAllCases, both of these are false. For percentOfCasesFilteredOpposite,  the one corresponding to the opposite row or column is true. For example, if we have a column called PCT that is percentOfAllCases with no additional filter variables (using a DummyVariable that is always equal to 1), and a row called CloseCase whose filters will be true in close cases, then we are calculating the DummyVariable count in the cases in which the CloseCase filters are true times 100, divided by the DummyVariable count in all cases (since none of the cases is null). We've already done the work of calculating the numerator above, so here we are just calculating the denominator. If we switched that to percentOfCasesFilteredOpposite, then the row filters would need to come out true in the denominator, and we would end up with a percent of 100% in all cases (not what we want). 
                            if (theStatistic == Statistic.percentOfCasesFilteredOpposite)
                            { // the opposite row/col filters must be true. note that there could be an intersection of two percent of trues. 
                                rowFiltersMustBeTrueForObservationToCountInDenominator = row.statistic != Statistic.percentOfCasesFilteredOpposite;
                                colFiltersMustBeTrueForObservationToCountInDenominator = column.statistic != Statistic.percentOfCasesFilteredOpposite;
                            }
                            for (int baind = 0; baind < bitArraySize; baind++)
                            {
                                BitArray rowBitArray = rowFiltersMustBeTrueForObservationToCountInDenominator ? filtersForRow[r] : nonNullFiltersForRow[r];
                                BitArray colBitArray = colFiltersMustBeTrueForObservationToCountInDenominator ? filtersForColumn[c] : nonNullFiltersForColumn[c];
                                nonNullIntersection[baind] = rowBitArray[baind] && colBitArray[baind];
                            }
                            double? calculatedStatisticDenominator = GenerateStatisticForOutputsWithFilterInfo(outputValues, nonNullIntersection, theStatistic);
                            calculatedStatistic = 100.0 * calculatedStatistic / calculatedStatisticDenominator;
                            if (calculatedStatistic == null && calculatedStatisticDenominator != null)
                                calculatedStatistic = 0;
                        }
                    }
                    newRow[c] = calculatedStatistic;
                });
                contents.Add(newRow.ToList());
            }
        }

        private List<double?> GetOutputValuesIncludingNulls(List<GameProgressReportable> theOutputs, RowOrColInfo row)
        {
            List<double?> outputValuesList;
            if (row.variableName != "" && row.variableName != null)
                outputValuesList = GetOutputValuesIncludingNulls(row.variableName, row.listIndex, theOutputs);
            else
                outputValuesList = null;
            return outputValuesList;
        }

        public static string GetFormatStringForSpecifiedWidth(int width)
        {
            return "{0,-" + (width + 3).ToString() + "}";
        }

        public override string ToString()
        {
            string[] columnFormatStrings = new string[colHeads.Count + 1];
            StringBuilder theBuilder = new StringBuilder();
            int maxRowHeadLength = rowHeads.Max(x => x.Length);
            columnFormatStrings[0] = GetFormatStringForSpecifiedWidth(maxRowHeadLength + 3);
            theBuilder.Append(String.Format(columnFormatStrings[0], " ")); // empty cell at top left
            int colForHead = 1;
            foreach (var colHead in colHeads)
            {
                theBuilder.Append("\t");
                columnFormatStrings[colForHead] = GetFormatStringForSpecifiedWidth(Math.Max(colHead.Length + 3, 10));
                theBuilder.Append(String.Format(columnFormatStrings[colForHead], colHead));
                colForHead++;
            }
            theBuilder.Append("\n");
            for (int row = 0; row < contents.Count; row++)
            {
                theBuilder.Append(String.Format(columnFormatStrings[0], rowHeads[row]));
                var rowContents = contents[row];
                for (int col = 0; col < rowContents.Count; col++)
                {
                    theBuilder.Append("\t");
                    theBuilder.Append(String.Format(columnFormatStrings[col + 1], NumberPrint.ToSignificantFigures(rowContents[col], 4)));
                }
                if (row != contents.Count - 1)
                    theBuilder.Append("\n");
            }
            return theBuilder.ToString();
        }

        public void AddToDatabase(List<string> namesOfVariableSets, List<string> namesOfVariableSetsChosen, DateTime commandSetStartTime)
        {
            try
            {
                var db = new SaveExecutionResults();
                db.CreateNewExecutionResultSet(namesOfVariableSets, namesOfVariableSetsChosen, "", "", commandSetStartTime);
                db.AddRowsAndColumns(rowHeads.ToList(), colHeads.ToList());
                for (int row = 0; row < contents.Count; row++)
                {
                    string rowName = rowHeads[row];
                    var rowContents = contents[row];
                    for (int col = 0; col < rowContents.Count; col++)
                    {
                        string colName = colHeads[col];
                        double? value = rowContents[col];
                        db.AddDataPoint(rowName, colName, value);
                    }
                }
                db.SaveChanges();
            }
            catch
            {
            }
        }

        public void AddToMetareport(StringBuilder stringBuilder, List<string> namesOfVariableSets)
        {
            for (int row = 0; row < contents.Count; row++)
            {
                string rowName = rowHeads[row];
                var rowContents = contents[row];
                bool individualVariableSetsToReport = true;
                string[] setsChosen = storeReportResultsFile.Split(' ').Where(x => x != "").ToArray();
                if (setsChosen.Count() == 1)
                    individualVariableSetsToReport = false;
                else
                    setsChosen = setsChosen.Take(setsChosen.Count() - 1).ToArray(); // skip last one
                if (individualVariableSetsToReport &&  setsChosen.Count() != namesOfVariableSets.Count())
                    throw new Exception("Internal exception: Wrong number of variable sets. (Possible cause: Inclusion of space in name of defineVariableAlternatives.)");
                for (int col = 0; col < rowContents.Count; col++)
                {
                    string colName = colHeads[col];
                    string valueString = NumberPrint.ToSignificantFigures(rowContents[col], 4);
                    rowName = rowName.Replace(',',' '); // eliminate commas since this is a csv file
                    colName = colName.Replace(',',' '); // eliminate commas since this is a csv file
                    stringBuilder.Append(rowName + "," + colName + "," + valueString + "," + reportInitiationTimeString);
                    if (individualVariableSetsToReport)
                        for (int n = 0; n < namesOfVariableSets.Count; n++)
                        {
                            stringBuilder.Append("," + setsChosen[n]);
                        }
                    stringBuilder.Append("\n");
                }
            }
        }

        private double? GenerateStatisticForOutputsWithFilterInfo(List<double?> outputValues, BitArray outputsToInclude, Statistic theStatistic)
        {
            if (theStatistic == Statistic.none)
                return null;
            if (theStatistic == Statistic.median)
                return outputValues.Where(x => x != null).Select(x => (double)x).ToList().Median();
            StatCollector sc = new StatCollector();
            for (int i = 0; i < outputValues.Count; i++)
            {
                if (outputsToInclude[i])
                {
                    double? val = outputValues[i];
                    if (val != null)
                        sc.Add((double)val);
                }
            }
            if (sc.Num() == 0)
                return null;
            switch (theStatistic)
            {
                case Statistic.count:
                case Statistic.percentOfAllCases: // Note that this count will be part of a fraction of filtered outputs / non-null outputs
                case Statistic.percentOfCasesFilteredOpposite:
                    return sc.Num();

                case Statistic.mean:
                    return sc.Average();

                case Statistic.stdev:
                    return sc.StandardDeviation();

                case Statistic.sum:
                    return sc.Num() * sc.Average();

                default:
                    throw new Exception("Unknown statistic requested.");
            }
        }

        internal double? GenerateStatisticForIntersection(RowOrColInfo theRow, RowOrColInfo theColumn, List<GameProgressReportable> theOutputs)
        {
            List<GameProgressReportable> filteredOutputs;
            filteredOutputs = FilterOutputs(theRow, theColumn, theOutputs);
            Statistic theStatistic = theRow.GetStatisticForIntersection(theColumn);
            string variableName = theRow.GetVariableNameForIntersection(theColumn);
            int? listIndex = null;
            if (variableName == theRow.variableName)
                listIndex = theRow.listIndex;
            else if (variableName == theColumn.variableName)
                listIndex = theColumn.listIndex;
            double? statisticForFilteredOutputs = GenerateStatisticForFilteredOutputs(filteredOutputs, theStatistic, variableName, listIndex);
            if (theStatistic == Statistic.percentOfAllCases)
            {
                List<GameProgressReportable> nonNullOutputs = FilterOutputs(theRow, theColumn, theOutputs, rowFiltersMustBePassed: false, columnFiltersMustBePassed: false);
                double? denominatorStatistic = GenerateStatisticForFilteredOutputs(nonNullOutputs, theStatistic, variableName, listIndex);
                return 100.0 * statisticForFilteredOutputs / denominatorStatistic;
            }
            else if (theStatistic == Statistic.percentOfCasesFilteredOpposite)
            {
                List<GameProgressReportable> outputsWhereOppositeIsFiltered = FilterOutputs(theRow, theColumn, theOutputs, rowFiltersMustBePassed: theRow.statistic != Statistic.percentOfCasesFilteredOpposite, columnFiltersMustBePassed: theColumn.statistic != Statistic.percentOfCasesFilteredOpposite);
                double? denominatorStatistic = GenerateStatisticForFilteredOutputs(outputsWhereOppositeIsFiltered, theStatistic, variableName, listIndex);
                return 100.0 * statisticForFilteredOutputs / denominatorStatistic;
            }
            else
                return statisticForFilteredOutputs;
        }

        private double? GenerateStatisticForFilteredOutputs(List<GameProgressReportable> filteredOutputs, Statistic theStatistic, string variableName, int? listIndex)
        {
            if (theStatistic == Statistic.none)
                return null;
            if (theStatistic == Statistic.count || theStatistic == Statistic.percentOfAllCases || theStatistic == Statistic.percentOfCasesFilteredOpposite)
                return filteredOutputs.Count;
            List<double> outputValues = GetOutputValuesExcludingNulls(variableName, listIndex, filteredOutputs);
            if (outputValues.Count == 0)
                return null;
            switch (theStatistic)
            {
                case Statistic.mean:
                    return outputValues.Average();

                case Statistic.median:
                    return outputValues.Median();

                case Statistic.stdev:
                    return outputValues.Stdev();

                case Statistic.sum:
                    return outputValues.Sum();

                default:
                    throw new Exception("Unknown statistic requested.");
            }
        }

        internal BitArray FilterOutputsForSingleRowOrColumnToBitArray(List<GameProgressReportable> theOutputs, RowOrColInfo theRowOrCol, bool trueIfNonNull = false)
        {
            List<bool> boolList = theOutputs.Select(x => theRowOrCol.filters.All(y => y.DoFilter(x, trueIfNonNull))).ToList();
            BitArray ba = new BitArray(boolList.ToArray());
            return ba;
        }

        internal List<GameProgressReportable> FilterOutputs(RowOrColInfo theRow, RowOrColInfo theColumn, List<GameProgressReportable> theOutputs, bool rowFiltersMustBePassed = true, bool columnFiltersMustBePassed = true)
        {
            List<Filter> combinedFilters = new List<Filter>();
            combinedFilters.AddRange(theRow.filters);
            combinedFilters.AddRange(theColumn.filters);
            List<bool> filterMustBePassed = new List<bool>();
            foreach (var row in theRow.filters)
                filterMustBePassed.Add(rowFiltersMustBePassed);
            foreach (var column in theColumn.filters)
                filterMustBePassed.Add(columnFiltersMustBePassed);
            List<GameProgressReportable> filteredOutputs = theOutputs;
            for (int f = 0; f < combinedFilters.Count; f++)
            {
                Filter filter = combinedFilters[f];
                filteredOutputs = (List<GameProgressReportable>)filteredOutputs.Where(x => filter.DoFilter(x, !filterMustBePassed[f])).ToList();
            }
            return filteredOutputs;
        }

        internal List<double> GetOutputValuesExcludingNulls(string variableName, int? listIndex, List<GameProgressReportable> theOutputs)
        {
            if (!theOutputs.Any())
                return new List<double>();

            List<double> theOutputValues = new List<double>();
            foreach (var output in theOutputs)
            {
                bool found;
                object theValue;
                GetOutputValue(variableName, listIndex, output, out found, out theValue);
                double? theValueAsDouble = (double?)theValue;
                if (theValue != null)
                    theOutputValues.Add((double)theValueAsDouble);
            }
            return theOutputValues;
        }

        internal List<double?> GetOutputValuesIncludingNulls(string variableName, int? listIndex, List<GameProgressReportable> theOutputs)
        {
            if (!theOutputs.Any())
                return new List<double?>();

            List<double?> theOutputValues = new List<double?>();
            foreach (var output in theOutputs)
            {
                bool found;
                object theValue;
                GetOutputValue(variableName, listIndex, output, out found, out theValue);
                double? theValueAsNullableDouble = (double?)theValue;
                if (theValueAsNullableDouble != null && double.IsNaN((double)theValueAsNullableDouble))
                    theValueAsNullableDouble = null;
                theOutputValues.Add(theValueAsNullableDouble);
            }
            return theOutputValues;
        }

        private static void GetOutputValue(string variableName, int? listIndex, GameProgressReportable output, out bool found, out object theValue)
        {
            theValue = output.GetValueForReport(variableName, listIndex, out found);
            if (!found)
            {
                output.GetValueForReport(variableName, listIndex, out found);
                throw new Exception("Variable " + variableName + " was not found.");
            }
            Type theType = null;
            if (theValue != null)
                theType = theValue.GetType();
            if (theType == typeof(int?) || theType == typeof(int))
                theValue = (double)((int)theValue);
            else if (theType != typeof(double?) && theType != typeof(double))
                theValue = null;
                //if (theValue != null && theType != typeof(double?) && theType != typeof(double))
                //throw new Exception("The report definition requires that output " + variableName + " be a floating-point value to calculate non-trivial statistics based on it.");
        }

        internal void GenerateRowAndColHeads(List<RowOrColInfo> theRows, List<RowOrColInfo> theColumns)
        {
            colHeads = new List<string>();
            rowHeads = new List<string>();
            foreach (var column in theColumns)
                colHeads.Add(column.rowOrColName);
            foreach (var row in theRows)
                rowHeads.Add(row.rowOrColName);
        }

        internal List<RowOrColInfo> GenerateRowsOrColumns(List<RowOrColumnGroup> theRowsOrCols, List<GameProgressReportable> theOutputs)
        {
            List<RowOrColInfo> theIndividualRowsOrCols = new List<RowOrColInfo>();
            foreach (var group in theRowsOrCols)
            {
                group.rowOrColsIncludingGenerated = null; // reset
                group.Generate(theOutputs);
                foreach (var row in group.rowOrColsIncludingGenerated)
                    theIndividualRowsOrCols.Add(row);
            }
            return theIndividualRowsOrCols;
        }
    }
}
