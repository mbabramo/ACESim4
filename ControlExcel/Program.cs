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

        private static List<object> GetCellValues(Excel.Worksheet worksheet, int rowNum, List<int> colNums)
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

        private static int FindColumn(Excel.Worksheet worksheet, int rowNum, string text)
        {
            const int maxCol = 100_000;
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

        private static int GetRowNumberMatching(Excel.Worksheet worksheet, List<(int colNum, string text)> requirements)
        {
            const int maxRow = 100_000;
            for (int i = 1; i <= maxRow; i++)
                if (RowMatches(worksheet, i, requirements))
                    return i;
            throw new Exception("Not found");
        }

        private static int GetRowNumber(Excel.Worksheet worksheet, string reportName, string filterName, string statisticName)
        {
            int reportColNum = FindColumn(worksheet, 1, "Report");
            int filterColNum = FindColumn(worksheet, 1, "Filter");
            int statisticColNum = FindColumn(worksheet, 1, "Iteration");
            return GetRowNumberMatching(worksheet, new List<(int colNum, string text)>() {(reportColNum, reportName), (filterColNum, filterName), (statisticColNum, statisticName)});
        }

        private static List<int> GetColumnNumbersMatching(Excel.Worksheet worksheet, List<string> columnNames)
        {
            return columnNames.Select(x => FindColumn(worksheet, 1, x)).ToList();
        }

        private static Excel.Worksheet GetWorksheet(string name)
        {
            Excel.Application oXL;
            Excel.Workbook oWB;
            Excel.Worksheet oSheet;
            oXL = (Excel.Application)Marshal.GetActiveObject("Excel.Application");
            oXL.Visible = true;
            oWB = (Excel.Workbook)oXL.ActiveWorkbook;

            Excel.Sheets worksheets = oWB.Worksheets;
            Excel.Worksheet worksheet = worksheets.get_Item(name);
            return worksheet;
        }

        static void Main(string[] args)
        {
            Excel.Worksheet sourceWS = GetWorksheet("risk neutral");
            Excel.Worksheet targetWS = GetWorksheet("graph");
            var originalColumnNames = new List<string>() {"PFiles", "DAnswers", "Settles1", "PAbandons1", "DDefaults1", "Settles2", "PAbandons2", "DDefaults2", "Settles3", "PAbandons3", "DDefaults3", "P Loses", "P Wins"};
            List<int> stagesColumnNumbers = GetColumnNumbersMatching(sourceWS, originalColumnNames);
            var graphColumnNames = new List<string>() { "P Doesn't File", "D Doesn't Answer", "Settles (1)", "P/D Quits (1)", "Settles (2)", "P/D Quits (2)", "Settles (3)", "P/D Quits (3)", "D Wins At Trial", "P Wins At Trial" };

            SetCellValue(targetWS, 1, 1, "Stage");
            SetCellValues(targetWS, 1, 2, graphColumnNames);
            string sourceReport = "Exog British";
            string subset = "All";
            string rowHead = "British Rule";
            int mainDataTarget = 2;
            int errorBarUpperTarget = 3;
            int errorBarLowerTarget = 4;
            List<double> averageResults = null;
            for (int s = 0; s < 3; s++)
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
                int rowNum = GetRowNumber(sourceWS, sourceReport, subset, stat);
                var results = GetResults(sourceWS, rowNum, stagesColumnNumbers);
                List<double> adjustedResults = results;
                string rowHeadPlus = rowHead;
                int targetRowNum = 0;
                switch (s)
                {
                    case 0:
                        averageResults = results.ToList();
                        targetRowNum = mainDataTarget;
                        break;
                    case 1:
                        rowHeadPlus += " Lower Error";
                        adjustedResults = results.Zip(averageResults, (r, a) => r - a).ToList();
                        targetRowNum = errorBarLowerTarget;
                        break;
                    case 2:
                        rowHeadPlus += " Upper Error";
                        adjustedResults = results.Zip(averageResults, (r, a) => a - r).ToList();
                        targetRowNum = errorBarUpperTarget;
                        break;
                }
                SetCellValue(targetWS, targetRowNum, 1, rowHeadPlus);
                SetCellValues(targetWS, targetRowNum, 2, adjustedResults);
                SetCellsNumberFormat(targetWS, targetRowNum, 2, adjustedResults.Count(), "0.0%");
            }
            // since we subtracted p answers and d answers numbers from 100, we must adjust here
            SwapCellValues(targetWS, errorBarUpperTarget, 2, errorBarLowerTarget, 2);
            SwapCellValues(targetWS, errorBarUpperTarget, 3, errorBarLowerTarget, 3);
        }

        private static List<double> GetResults(Excel.Worksheet sourceWS, int rowNum, List<int> stagesColumnNumbers)
        {
            List<double> results = GetCellValues(sourceWS, rowNum, stagesColumnNumbers).Select(x => (double) x).ToList();
            double pFiles = results[0];
            double dAnswers = results[1];
            double pDoesntFile = 1.0 - pFiles;
            double dDoesntAnswer = pFiles - dAnswers;
            List<(double settles, double pAbandons, double dDefaults)> bargainingRounds = new List<(double settles, double pAbandons, double dDefaults)>() {(results[2], results[3], results[4]), (results[5], results[6], results[7]), (results[8], results[9], results[10])};
            var resultsBackward = results.ToList();
            resultsBackward.Reverse();
            double pWinsAtTrial = resultsBackward.Skip(1).First();
            double dWinsAtTrial = resultsBackward.First();
            var resultsRevised = new List<double>() {pDoesntFile, dDoesntAnswer};
            foreach (var br in bargainingRounds)
            {
                resultsRevised.Add(br.settles);
                resultsRevised.Add(br.pAbandons + br.dDefaults);
            }
            resultsRevised.Add(pWinsAtTrial);
            resultsRevised.Add(dWinsAtTrial);
            return resultsRevised;
        }
    }
}
