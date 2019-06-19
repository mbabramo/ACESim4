using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Excel = Microsoft.Office.Interop.Excel;

namespace ControlExcel
{
    class Program
    {
        static Excel.Range GetCell(Excel.Worksheet worksheet, int rowNum, int colNum)
        {
            return worksheet.Cells.get_Item(rowNum, colNum);

        }

        private static string GetCellReference(int rowNum, int colNum)
        {
            if (colNum > 26)
                throw new NotImplementedException();
            char c = (char) ('A' + colNum - 1);
            string cellReference = c + rowNum.ToString();
            return cellReference;
        }

        private static List<object> GetCellValuesInColumn(Excel.Worksheet worksheet, List<int> rowNums, int colNum)
        {
            return rowNums.Select(rowNum => GetCellValue(worksheet, rowNum, colNum)).ToList();
        }

        private static List<object> GetCellValuesInRow(Excel.Worksheet worksheet, int rowNum, List<int> colNums)
        {
            return colNums.Select(colNum => GetCellValue(worksheet, rowNum, colNum)).ToList();
        }

        private static object GetCellValue(Excel.Worksheet worksheet, int rowNum, int colNum)
        {
            var cell = GetCell(worksheet, rowNum, colNum);
            return cell.Value;
        }

        private static void SwapCellValues(Excel.Worksheet worksheet, int rowNum1, int colNum1, int rowNum2, int colNum2)
        {
            object cell1Val = GetCellValue(worksheet, rowNum1, colNum1);
            object cell2Val = GetCellValue(worksheet, rowNum2, colNum2);
            SetCellValue(worksheet, rowNum1, colNum1, cell2Val);
            SetCellValue(worksheet, rowNum2, colNum2, cell1Val);
        }

        private static void SetCellValues(Excel.Worksheet worksheet, int rowNum, int firstColNum, List<string> values)
        {
            SetCellValues(worksheet, rowNum, firstColNum, values.Select(x => (object)x).ToList());
        }

        private static void SetCellValues(Excel.Worksheet worksheet, int rowNum, int firstColNum, List<double> values)
        {
            SetCellValues(worksheet, rowNum, firstColNum, values.Select(x => (object) x).ToList());
        }

        private static void SetCellValues(Excel.Worksheet worksheet, int rowNum, int firstColNum,  List<object> values)
        {
            for (int i = 0; i < values.Count(); i++)
                SetCellValue(worksheet, rowNum, firstColNum + i, values[i]);
        }

        private static void SetCellValue(Excel.Worksheet worksheet, int rowNum, int colNum, object value)
        {
            var cell = GetCell(worksheet, rowNum, colNum);
            cell.Value = value;
            cell.NumberFormat = "0.0%";
        }

        private static void SetCellsNumberFormat(Excel.Worksheet worksheet, int rowNum, int firstColNum, int number, string numberFormat)
        {
            for (int i = 0; i < number; i++)
                SetCellNumberFormat(worksheet, rowNum, firstColNum + i, numberFormat);
        }

        private static void SetCellNumberFormat(Excel.Worksheet worksheet, int rowNum, int colNum, string numberFormat)
        {
            var cell = GetCell(worksheet, rowNum, colNum);
            cell.NumberFormat = numberFormat;
        }

        private static List<int> GetColumnNumbers(Excel.Worksheet worksheet, int rowNum, List<string> colNames)
        {
            return colNames.Select(x => GetColumnNumber(worksheet, rowNum, x)).ToList();
        }

        private static int GetColumnNumber(Excel.Worksheet worksheet, int rowNum, string text)
        {
            const int maxCol = 5_000;
            for (int i = 1; i <= maxCol; i++)
                if ((string) GetCellValue(worksheet, rowNum, i) == text)
                    return i;
            throw new Exception("Not found");
        }

        private static bool CellMatches(Excel.Worksheet worksheet, int rowNum, int colNum, string text)
        {
            object value = GetCellValue(worksheet, rowNum, colNum);
            if (value is string && (string) value == text)
                return true;
            return false;
        }

        private static bool RowMatches(Excel.Worksheet worksheet, int rowNum, List<(int colNum, string text)> requirements)
        {
            return requirements.All(x => CellMatches(worksheet, rowNum, x.colNum, x.text));
        }

        private static int LastRowNumberSearched = 1;

        private static int GetRowNumberMatching(Excel.Worksheet worksheet, List<(int colNum, string text)> requirements)
        {
            // to boost speed, we remember the last row number searched and first look around there
            for (int i = LastRowNumberSearched; i <= LastRowNumberSearched + 100; i++)
                if (RowMatches(worksheet, i, requirements))
                {
                    LastRowNumberSearched = i;
                    return i;
                }
            for (int i = Math.Max(1, LastRowNumberSearched - 300); i < LastRowNumberSearched; i++)
                if (RowMatches(worksheet, i, requirements))
                {
                    LastRowNumberSearched = i;
                    return i;
                }
            // Now do a broad search
            const int maxRow = 25_000;
            for (int i = 1; i <= maxRow; i++)
                if (RowMatches(worksheet, i, requirements))
                {
                    LastRowNumberSearched = i;
                    return i;
                }
            throw new Exception("Not found");
        }

        private static List<int> GetRowNumbers(Excel.Worksheet worksheet, string reportName, string statisticName, List<string> filterNames)
        {
            return filterNames.Select(x => GetRowNumber(worksheet, reportName, x, statisticName)).ToList();
        }

        private static List<int> GetRowNumbers(Excel.Worksheet worksheet, List<string> reportNames, string statisticName, string filterName)
        {
            return reportNames.Select(x => GetRowNumber(worksheet, x, filterName, statisticName)).ToList();
        }
        private static List<int> GetRowNumbers(Excel.Worksheet worksheet, List<string> reportNames, List<string> statisticNames, string filterName)
        {
            List<int> results = new List<int>();
            foreach (string statisticName in statisticNames)
                results.AddRange(reportNames.Select(x => GetRowNumber(worksheet, x, filterName, statisticName)));
            return results;
        }

        private static int GetRowNumber(Excel.Worksheet worksheet, string reportName, string filterName, string statisticName)
        {
            int reportColNum = GetColumnNumber(worksheet, 1, "Report");
            int filterColNum = GetColumnNumber(worksheet, 1, "Filter");
            int statisticColNum = GetColumnNumber(worksheet, 1, "Iteration");
            return GetRowNumberMatching(worksheet, new List<(int colNum, string text)>() {(reportColNum, reportName), (filterColNum, filterName), (statisticColNum, statisticName)});
        }

        private static List<int> GetColumnNumbersMatching(Excel.Worksheet worksheet, List<string> columnNames)
        {
            return columnNames.Select(x => GetColumnNumber(worksheet, 1, x)).ToList();
        }

        private static Excel.Worksheet GetWorksheet(string name)
        {
            Excel.Application oXL;
            Excel.Workbook oWB;
            oXL = (Excel.Application)Marshal.GetActiveObject("Excel.Application");
            oXL.Visible = true;
            oWB = (Excel.Workbook)oXL.ActiveWorkbook;

            Excel.Sheets worksheets = oWB.Worksheets;
            Excel.Worksheet worksheet = worksheets.get_Item(name);
            return worksheet;
        }



        private static List<double> GetStagesResults(Excel.Worksheet sourceWS, int rowOrColumnNumber, bool mainNumberIsRowNumber, List<int> columnsOrRowsNumbers, bool dataArePercents, bool stages)
        {
            List<object> unprocessedResults;
            if (mainNumberIsRowNumber)
                unprocessedResults = GetCellValuesInRow(sourceWS, rowOrColumnNumber, columnsOrRowsNumbers);
            else
                unprocessedResults = GetCellValuesInColumn(sourceWS, columnsOrRowsNumbers, rowOrColumnNumber);
            List<double> results = ConvertCellValuesToDoubles(unprocessedResults);
            if (!stages)
                return results;
            double noDisputeArises = results[0];
            double pDoesntFile = results[1];
            double dDoesntAnswer = results[2];
            List<(double settles, double pAbandons, double dDefaults)> bargainingRounds = new List<(double settles, double pAbandons, double dDefaults)>() { (results[3], results[4], results[5]), (results[6], results[7], results[8]), (results[9], results[10], results[11]) };
            var resultsBackward = results.ToList();
            resultsBackward.Reverse();
            double dWinsAtTrial = resultsBackward.Skip(1).First();
            double pWinsAtTrial = resultsBackward.First();
            var resultsRevised = new List<double>() { noDisputeArises, pDoesntFile, dDoesntAnswer };
            foreach (var br in bargainingRounds)
            {
                resultsRevised.Add(br.settles);
                resultsRevised.Add(br.pAbandons);
                resultsRevised.Add(br.dDefaults);
                // Possible to do: Newer reports will have a column grouping abandons and defaults. We can't just do the math at this point
            }
            resultsRevised.Add(dWinsAtTrial);
            resultsRevised.Add(pWinsAtTrial);
            return resultsRevised;
        }

        private static List<double> ConvertCellValuesToDoubles(List<object> cellValuesAsObjects)
        {
            return cellValuesAsObjects.Select(x => (x is string && (string) x == "NaN") ? 0 : ((double?) x ?? 0)).ToList();
        }

        private static void ProcessStageResults(string targetWorksheetName, string sourceWorksheetName, string sourceReport, List<string> crossStats, bool crossStatIsRow, List<string> rowHeads, int mainDataFirstRowTarget, bool omitNoDispute, bool divideByAllCount, bool stages)
        {
            Excel.Worksheet targetWS;
            List<string> sourceColOrRowNames;
            List<string> graphColumnNames;
            var sourceWS = GetWorksheetsAndColumnNames(targetWorksheetName, sourceWorksheetName, omitNoDispute, stages, out targetWS, out sourceColOrRowNames, out graphColumnNames);

            bool dataAreProportions = divideByAllCount;
            List<double> allRow = null;
            if (divideByAllCount)
                allRow = GetStagesResults(sourceReport, "AllCount", crossStatIsRow, dataAreProportions, sourceWS, "Average", sourceColOrRowNames, stages);

            SetCellValue(targetWS, 1, 1, "Stage");
            SetCellValues(targetWS, 1, 2, graphColumnNames);
            for (int s = 0; s < crossStats.Count(); s++)
            {
                string stat = "Average";
                string crossStat = crossStats[s];
                List<double> results = GetStagesResults(sourceReport, crossStat, crossStatIsRow, dataAreProportions, sourceWS, stat, sourceColOrRowNames, stages);
                if (divideByAllCount)
                    results = results.Zip(allRow, (r, p) => p == 0 ? 0 : r / p).ToList();

                List<double> adjustedResults = results.ToList();
                string rowHeadPlus = rowHeads[s];
                int targetRowNum = mainDataFirstRowTarget + s;
                if (omitNoDispute)
                    adjustedResults = adjustedResults.Skip(1).ToList();
                SetCellValue(targetWS, targetRowNum, 1, rowHeadPlus);
                SetCellValues(targetWS, targetRowNum, 2, adjustedResults);
                SetCellsNumberFormat(targetWS, targetRowNum, 2, adjustedResults.Count(), dataAreProportions ? "0.0%" : "#,###");
            }
        }


        private static void ProcessStageResults(string targetWorksheetName, string sourceWorksheetName, string sourceReport, string crossStat, bool crossStatIsRow, string rowHead, int mainDataTarget, int errorBarLowerTarget, int errorBarUpperTarget, bool dataAreProportions, bool omitNoDispute, bool multiplyByProportionOfAll, bool omitErrorBars, bool stages)
        {
            Excel.Worksheet targetWS;
            List<string> sourceColOrRowNames;
            List<string> graphColumnNames;
            var sourceWS = GetWorksheetsAndColumnNames(targetWorksheetName, sourceWorksheetName, omitNoDispute, stages, out targetWS, out sourceColOrRowNames, out graphColumnNames);

            List<double> proportionsOfSample = null;
            if (multiplyByProportionOfAll)
                proportionsOfSample = GetStagesResults(sourceReport, "All", crossStatIsRow, dataAreProportions, sourceWS, "Average", sourceColOrRowNames, stages);

            SetCellValue(targetWS, 1, 1, "Stage");
            SetCellValues(targetWS, 1, 2, graphColumnNames);
            List<double> averageResults = null;
            for (int s = 0; s < (omitErrorBars ? 1 : 3); s++)
            {
                string stat = "";
                switch (s)
                {
                    case 0:
                        stat = "Average";
                        break;
                    case 1:
                        stat = "LowerBound";
                        break;
                    case 2:
                        stat = "UpperBound";
                        break;
                }
                List<double> results = GetStagesResults(sourceReport, crossStat, crossStatIsRow, dataAreProportions, sourceWS, stat, sourceColOrRowNames, stages);
                if (multiplyByProportionOfAll)
                    results = results.Zip(proportionsOfSample, (r, p) => r * p).ToList();

                List<double> adjustedResults = null;
                string rowHeadPlus = rowHead;
                int targetRowNum = 0;
                switch (s)
                {
                    case 0:
                        averageResults = results.ToList();
                        targetRowNum = mainDataTarget;
                        adjustedResults = results;
                        break;
                    case 1:
                        rowHeadPlus += " Lower Error";
                        adjustedResults = ConvertLowerBoundToErrorBar(results, averageResults);
                        targetRowNum = errorBarLowerTarget;
                        break;
                    case 2:
                        rowHeadPlus += " Upper Error";
                        adjustedResults = ConvertUpperBoundToErrorBar(results, averageResults);
                        targetRowNum = errorBarUpperTarget;
                        break;
                }
                if (omitNoDispute)
                    adjustedResults = adjustedResults.Skip(1).ToList();
                SetCellValue(targetWS, targetRowNum, 1, rowHeadPlus);
                SetCellValues(targetWS, targetRowNum, 2, adjustedResults);
                SetCellsNumberFormat(targetWS, targetRowNum, 2, adjustedResults.Count(), dataAreProportions ? "0.0%" : "#,###");
            }
        }

        private static List<double> ConvertUpperBoundToErrorBar(List<double> upperBounds, List<double> averageResults)
        {
            List<double> adjustedResults;
            adjustedResults = upperBounds.Zip(averageResults, (r, a) => r - a).ToList();
            return adjustedResults;
        }

        private static List<double> ConvertLowerBoundToErrorBar(List<double> lowerBounds, List<double> averageResults)
        {
            List<double> adjustedResults;
            adjustedResults = lowerBounds.Zip(averageResults, (r, a) => a - r).ToList();
            return adjustedResults;
        }

        private static Excel.Worksheet GetWorksheetsAndColumnNames(string targetWorksheetName, string sourceWorksheetName, bool omitFirstColumn, bool stages, out Excel.Worksheet targetWS, out List<string> sourceColOrRowNames, out List<string> graphColumnNames)
        {
            Excel.Worksheet sourceWS = GetWorksheet(sourceWorksheetName);
            targetWS = GetWorksheet(targetWorksheetName);
            if (stages)
            {
                sourceColOrRowNames = new List<string>() {"NoDispute", "PDoesntFile", "DDoesntAnswer", "Settles1", "PAbandons1", "DDefaults1", "Settles2", "PAbandons2", "DDefaults2", "Settles3", "PAbandons3", "DDefaults3", "P Loses", "P Wins"};
                graphColumnNames = new List<string>() {"No Dispute", "P Doesn't File", "D Doesn't Answer", "Settles (1)", "P Abandons (1)", "D Defaults (1)", "Settles (2)", "P Abandons (2)", "D Defaults (2)", "Settles (3)", "P Abandons (3)", "D Defaults (3)", "D Wins At Trial", "P Wins At Trial"};
            }
            else
            {
                sourceColOrRowNames = new List<string>() { "POffer1", "POffer2", "POffer3", "DOffer1", "DOffer2", "DOffer3", "LitigQuality" };
                graphColumnNames = new List<string>() { "P Offer (1)", "P Offer (2)", "P Offer (3)", "D Offer (1)", "D Offer (2)", "D Offer (3)", "Litigation Quality" };
            }
            if (omitFirstColumn && stages)
                graphColumnNames = graphColumnNames.Skip(1).ToList();
            return sourceWS;
        }

        private static List<double> GetStagesResults(string sourceReport, string crossStat, bool crossStatIsRow, bool dataAreProportions, Excel.Worksheet sourceWS, string stat, List<string> sourceColOrRowNames, bool stages)
        {
            int mainRowOrColumnNum;
            List<int> stagesColOrRowNumbers = null;
            if (crossStatIsRow)
            {
                stagesColOrRowNumbers = GetColumnNumbers(sourceWS, 1, sourceColOrRowNames);
                mainRowOrColumnNum = GetRowNumber(sourceWS, sourceReport, crossStat, stat); // column number is same for all statistics
            }
            else
            {
                stagesColOrRowNumbers = GetRowNumbers(sourceWS, sourceReport, stat, sourceColOrRowNames);
                mainRowOrColumnNum = GetColumnNumber(sourceWS, 1, crossStat);
            }
            var results = GetStagesResults(sourceWS, mainRowOrColumnNum, crossStatIsRow, stagesColOrRowNumbers, dataAreProportions, stages);
            return results;
        }

        private static void CompletionStageGraph()
        {
            string targetWorksheetName = "completion stage graph";
            string sourceWorksheetName = "risk neutral basic";
            string sourceReport = "Exog British";
            string rowHead = "British Rule";
            bool omitNoDispute = true;
            bool omitErrorBars = false;
            string crossStat = "All";
            bool multiplyByPercentageOfAll = false;
            bool crossStatIsPercent = true;
            bool crossStatIsRow = false;
            int mainDataTarget = 2;
            int errorBarUpperTarget = 3;
            int errorBarLowerTarget = 4;

            ProcessStageResults(targetWorksheetName, sourceWorksheetName, sourceReport, crossStat, crossStatIsRow, rowHead, mainDataTarget, errorBarLowerTarget, errorBarUpperTarget, crossStatIsPercent, omitNoDispute, multiplyByPercentageOfAll, omitErrorBars, true);
        }

        private static void ErrorAllocationGraph()
        {
            string targetWorksheetName = "error allocation graph";
            string sourceWorksheetName = "risk neutral basic";
            string sourceReport = "Exog British";
            string rowHead = "British Rule";
            bool omitNoDispute = true;
            bool omitErrorBars = true;
            string crossStat = "False-";
            bool multiplyByPercentageOfAll = true;
            bool crossStatIsPercent = false;
            bool crossStatIsRow = false;
            int mainDataTarget = 2;
            int errorBarUpperTarget = 3;
            int errorBarLowerTarget = 4;

            ProcessStageResults(targetWorksheetName, sourceWorksheetName, sourceReport, crossStat, crossStatIsRow, rowHead, mainDataTarget, errorBarLowerTarget, errorBarUpperTarget, crossStatIsPercent, omitNoDispute, multiplyByPercentageOfAll, omitErrorBars, true);
        }


        private static void OffersGraph()
        {
            string targetWorksheetName = "offers graph";
            string sourceWorksheetName = "risk neutral basic";
            string sourceReport = "Exog American";
            bool omitNoDispute = true;
#pragma warning disable CS0219
            bool omitErrorBars = true;
            List<string> crossStats = new List<string>() { "PLiabilitySignal1 Count", "PLiabilitySignal2 Count", "PLiabilitySignal3 Count", "PLiabilitySignal4 Count", "PLiabilitySignal5 Count", "PLiabilitySignal6 Count", "PLiabilitySignal7 Count", "PLiabilitySignal8 Count", "PLiabilitySignal9 Count", "PLiabilitySignal10 Count" };
            List<string> rowHeads = new List<string>() { "P LiabilitySignal 1", "P LiabilitySignal 2", "P LiabilitySignal 3", "P LiabilitySignal 4", "P LiabilitySignal 5", "P LiabilitySignal 6", "P LiabilitySignal 7", "P LiabilitySignal 8", "P LiabilitySignal 9", "P LiabilitySignal 10", "D LiabilitySignal 1", "D LiabilitySignal 2", "D LiabilitySignal 3", "D LiabilitySignal 4", "D LiabilitySignal 5", "D LiabilitySignal 6", "D LiabilitySignal 7", "D LiabilitySignal 8", "D LiabilitySignal 9", "D LiabilitySignal 10" };
            crossStats.Reverse();
            rowHeads.Reverse();
            //List<string> crossStats = new List<string>() { "MutOpt5 Count", "MutOpt4 Count", "MutOpt3 Count", "MutOpt2 Count", "MutOpt1 Count", "MutOpt0 Count", "MutOpt-1 Count", "MutOpt-2 Count", "MutOpt-3 Count", "MutOpt-4 Count", "MutOpt-5 Count" };
            //List<string> rowHeads = new List<string>() { "Mutually Optimistic 5+", "Mutually Optimistic 4", "Mutually Optimistic 3", "Mutually Optimistic 2", "Mutually Optimistic 1", "Same LiabilitySignal", "Mutually Pessimistic 1", "Mutually Pessimistic 2", "Mutually Pessimistic 3", "Mutually Pessimistic 4", "Mutually Pessimistic 5+" };
            bool divideByAllCount = true;
            bool crossStatIsPercent = false;
            bool crossStatIsRow = true;
            int mainDataTarget = 2;
            int errorBarUpperTarget = 3;
            int errorBarLowerTarget = 4;

            ProcessStageResults(targetWorksheetName, sourceWorksheetName, sourceReport, crossStats, crossStatIsRow, rowHeads, mainDataTarget, omitNoDispute, divideByAllCount, false);
        }

        private static void DistributionAtStageGraph()
        {
            string targetWorksheetName = "distribution stage graph";
            string sourceWorksheetName = "risk neutral basic";
            string sourceReport = "Exog American";
            bool omitNoDispute = true;
            bool omitErrorBars = true;
            List<string> crossStats = new List<string>() { "PLiabilitySignal1 Count", "PLiabilitySignal2 Count", "PLiabilitySignal3 Count", "PLiabilitySignal4 Count", "PLiabilitySignal5 Count", "PLiabilitySignal6 Count", "PLiabilitySignal7 Count", "PLiabilitySignal8 Count", "PLiabilitySignal9 Count", "PLiabilitySignal10 Count" };
            List<string> rowHeads = new List<string>() { "PLiabilitySignal1", "PLiabilitySignal2", "PLiabilitySignal3", "PLiabilitySignal4", "PLiabilitySignal5", "PLiabilitySignal6", "PLiabilitySignal7", "PLiabilitySignal8", "PLiabilitySignal9", "PLiabilitySignal10" };
            crossStats.Reverse();
            rowHeads.Reverse();
            //List<string> crossStats = new List<string>() { "MutOpt5 Count", "MutOpt4 Count", "MutOpt3 Count", "MutOpt2 Count", "MutOpt1 Count", "MutOpt0 Count", "MutOpt-1 Count", "MutOpt-2 Count", "MutOpt-3 Count", "MutOpt-4 Count", "MutOpt-5 Count" };
            //List<string> rowHeads = new List<string>() { "Mutually Optimistic 5+", "Mutually Optimistic 4", "Mutually Optimistic 3", "Mutually Optimistic 2", "Mutually Optimistic 1", "Same LiabilitySignal", "Mutually Pessimistic 1", "Mutually Pessimistic 2", "Mutually Pessimistic 3", "Mutually Pessimistic 4", "Mutually Pessimistic 5+" };
            bool divideByAllCount = true;
            bool crossStatIsPercent = false;
            bool crossStatIsRow = true;
            int mainDataTarget = 2;
            int errorBarUpperTarget = 3;
            int errorBarLowerTarget = 4;
#pragma warning restore CS0219

            ProcessStageResults(targetWorksheetName, sourceWorksheetName, sourceReport, crossStats, crossStatIsRow, rowHeads, mainDataTarget, omitNoDispute, divideByAllCount, true);
        }

        private static void SocialWelfareComparisonGraph()
        {
            string targetWorksheetName = "social welfare graph";
            string sourceWorksheetName = "risk neutral basic";
            Excel.Worksheet sourceWS = GetWorksheet(sourceWorksheetName);
            Excel.Worksheet targetWS = GetWorksheet(targetWorksheetName);
            List<string> crossStats = new List<string>() {"TotExpense", "False+", "False-"};
            List<string> columnHeads = new List<string>() {"Litigation Expenditures", "False Positive Expenditures", "False Negative Shortfall"};
            List<string> reportNames = new List<string>() {"Exog American", "Exog British"};
            List<string> rowHeads = new List<string>() { "American Rule", "British Rule" };
            List<string> statistics = new List<string>() {"Average", "UpperBound", "LowerBound"};
            List<string> statisticAppend = new List<string>() { "", " Upper Error", " Lower Error" };
            var columnNumbers = GetColumnNumbers(sourceWS, 1, crossStats);
            
            Dictionary<(string statistic, string rowHead),List<double>> dict = new Dictionary<(string statistic, string rowHead), List<double>>();

            int statisticFirstRow = 1;
            List<List<double>> averages = new List<List<double>>();
            for (var statisticIndex = 0; statisticIndex < statistics.Count; statisticIndex++)
            {
                string statistic = statistics[statisticIndex];
                statisticFirstRow++;
                int targetRowNum = statisticFirstRow;
                var sourceRowNumbers = GetRowNumbers(sourceWS, reportNames, statistic, "All");
                for (int reportIndex = 0; reportIndex < rowHeads.Count(); reportIndex++)
                {
                    int rowNumber = sourceRowNumbers[reportIndex];
                    string rowHead = rowHeads[reportIndex] + statisticAppend[statisticIndex];
                    List<double> data = ConvertCellValuesToDoubles(GetCellValuesInRow(sourceWS, rowNumber, columnNumbers));
                    if (statisticIndex == 0)
                        averages.Add(data);
                    else if (statisticIndex == 1)
                        data = ConvertUpperBoundToErrorBar(data, averages[reportIndex]);
                    else if (statisticIndex == 2)
                        data = ConvertLowerBoundToErrorBar(data, averages[reportIndex]);

                    SetCellValue(targetWS, targetRowNum, 1, rowHead);
                    SetCellValues(targetWS, targetRowNum, 2, data);
                    SetCellsNumberFormat(targetWS, targetRowNum, 2, columnHeads.Count(), "#,###");
                    targetRowNum += statistics.Count();
                }
            }

            SetCellValue(targetWS, 1, 1, "Welfare Measure");
            SetCellValues(targetWS, 1, 2, columnHeads);
        }

        static void Main(string[] args)
        {
            //SocialWelfareComparisonGraph();
            OffersGraph(); // needs work and more data
            //DistributionAtStageGraph();
            //CompletionStageGraph();
            //ErrorAllocationGraph();
        }
    }
}
