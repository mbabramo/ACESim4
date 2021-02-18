using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class SimpleReport
    {
        SimpleReportDefinition Definition;
        SimpleReport EarlierReportToDivideColumnFiltersBy;
        StatCollector[] StatCollectors;

        public SimpleReport(SimpleReportDefinition definition, SimpleReport earlierReportToDivideColumnFiltersBy = null)
        {
            Definition = definition;
            EarlierReportToDivideColumnFiltersBy = earlierReportToDivideColumnFiltersBy;
            StatCollectors = new StatCollector[Definition.TotalCells];
            for (int i = 0; i < Definition.TotalCells; i++)
                StatCollectors[i] = new StatCollector();
        }

        public void ProcessGameProgress(GameProgress completedGame, double weight)
        {
            int i = 0;
            int rowFiltersCount = Definition.RowFilters.Count();
            int columnItemsCount = Definition.ColumnItems.Count();
            int numPerMetaFilter = rowFiltersCount * columnItemsCount;
            int numProcessed = 0;
            foreach (SimpleReportFilter metaFilter in Definition.MetaFilters)
            {
                bool metaFilterSatisfied = metaFilter.IsInFilter(completedGame);
                if (metaFilterSatisfied)
                {
                    double[,] results = new double[rowFiltersCount, columnItemsCount];
                    for (int rowFilterIndex = 0; rowFilterIndex < rowFiltersCount; rowFilterIndex++) // DEBUG
                    // DEBUGParallel.For(0, rowFiltersCount, rowFilterIndex =>
                    {
                        SimpleReportFilter rowFilter = Definition.RowFilters[rowFilterIndex];
                        bool rowFilterSatisfied = rowFilter.IsInFilter(completedGame);
                        int colItemIndex = 0;
                        foreach (SimpleReportColumnItem colItem in Definition.ColumnItems)
                        {
                            var v = colItem.GetValueToRecord(completedGame);
                            bool recordThis;
                            recordThis = rowFilterSatisfied;
                            if (colItem is SimpleReportColumnFilter colFilter)
                            {
                                if (colFilter.ColumnFilterOptions == SimpleReportColumnFilterOptions.ProportionOfAll || colFilter.ColumnFilterOptions == SimpleReportColumnFilterOptions.ProportionOfFirstRowOfColumn)
                                {
                                    // For proportion of all, we are determining what percentage of all items in the table have both the row filter and column filter satisfied. For proportion of first row in column, we do the same calculation, but then divide by the calculation for the first row. 
                                    recordThis = true;
                                    if (!rowFilterSatisfied)
                                        v = 0;  // record this as a 0 (regardless of whether the column filter is satisfied); we record as a 1 only if column and row filters are satisfied.
                                }
                                // NOTE: We deal with ProportionOfFirstRowOfColumn below by simply dividing values by the first row when printing out.
                            }
                            //if (recordThis && v == null)
                            //    StatCollectors[i] = null; // this collection is invalid -- we only report stats when every item is present.
                            if (recordThis && StatCollectors[i] != null && v != null)
                            {
                                int statCollectorIndex = numProcessed + rowFilterIndex * columnItemsCount + colItemIndex;
                                StatCollectors[statCollectorIndex].Add((double)v, weight);
                            }
                            colItemIndex++;
                        }
                    } // DEBUG );
                }
                numProcessed += numPerMetaFilter;
            }
            completedGame.Dispose();
        }

        public void Append(StringBuilder standardReport, StringBuilder csvReport, bool isNumeric, string text, int? columnWidth, bool isLastColumn)
        {
            if (!isNumeric)
                csvReport.Append("\"");
            csvReport.Append(text);
            if (!isNumeric)
                csvReport.Append("\"");
            if (!isLastColumn)
                csvReport.Append(",");
            standardReport.Append(FormatTableString(text, columnWidth, isLastColumn));
        }

        public ReportCollection BuildReport()
        {
            StringBuilder standardReport = new StringBuilder();
            StringBuilder csvReport = new StringBuilder();
            standardReport.AppendLine(Definition.Name);
            int? metaColumnWidth = null, rowFilterColumnWidth = null;
            if (!Definition.RowFilters.Any() || !Definition.ColumnItems.Any())
                return new ReportCollection() ;
            metaColumnWidth = Math.Max(9, Definition.MetaFilters.Max(x => x.Name.Length) + 3);
            rowFilterColumnWidth = Math.Max(9, Definition.RowFilters.Max(x => x.Name.Length) + 3);
            SimpleReportColumnItem lastColumn = Definition.ColumnItems.Last();
            bool printMetaColumn = Definition.MetaFilters.Count() > 1;
            double?[] firstRowValues = new double?[Definition.ColumnItems.Count];
            foreach (SimpleReportFilter metaFilter in Definition.MetaFilters)
            {
                // print column headers
                if (printMetaColumn)
                {
                    Append(standardReport, csvReport, false, "Filter1", metaColumnWidth, false);
                }
                if (Definition.StaticTextColumns != null)
                    foreach (var textColumn in Definition.StaticTextColumns)
                    {
                        Append(standardReport, csvReport, false, textColumn.textColumnName, 15, false);
                    }
                Append(standardReport, csvReport, false, printMetaColumn ? "Filter2" : "Filter", rowFilterColumnWidth, false);
                foreach (SimpleReportColumnItem colItem in Definition.ColumnItems)
                    Append(standardReport, csvReport, false, colItem.Name, colItem.Width, colItem == lastColumn);
                standardReport.AppendLine();
                csvReport.AppendLine();

                // print rows
                int i = 0;
                int r = 0;
                Dictionary<string, double?> firstColumnValues = new Dictionary<string, double?>();
                foreach (SimpleReportFilter rowFilter in Definition.RowFilters)
                {
                    if (printMetaColumn)
                        Append(standardReport, csvReport, false, metaFilter.Name, metaColumnWidth, false);
                    if (Definition.StaticTextColumns != null)
                        foreach (var textColumn in Definition.StaticTextColumns)
                        {
                            Append(standardReport, csvReport, false, textColumn.textColumnContent, 15, false);
                        }
                    Append(standardReport, csvReport, false, rowFilter.Name, rowFilterColumnWidth, false);
                    int c = 0;
                    foreach (SimpleReportColumnItem colItem in Definition.ColumnItems)
                    {
                        double? value;
                        if (StatCollectors[i] == null || StatCollectors[i].Num() == 0)
                            value = null;
                        else
                        {
                            if (colItem is SimpleReportColumnFilter cf)
                            {
                                if (rowFilter.UseSum)
                                    value = StatCollectors[i].Average() * StatCollectors[i].Num();
                                else
                                    value = (double?)StatCollectors[i].Average();
                                if (EarlierReportToDivideColumnFiltersBy != null)
                                {
                                    double? earlierValue = EarlierReportToDivideColumnFiltersBy.StatCollectors[i].Average();
                                    if (earlierValue == null || earlierValue == 0)
                                        value = null;
                                    else
                                        value = value / earlierValue;
                                }
                            }
                            else
                            {
                                SimpleReportColumnVariable cv = (SimpleReportColumnVariable)colItem;
                                value = cv.Stdev ? StatCollectors[i].StandardDeviation() : StatCollectors[i].Average();
                            }
                        }
                        if (c == 0)
                            firstColumnValues.Add(rowFilter.Name, value);
                        else
                            value = rowFilter.Manipulate(firstColumnValues, value);
                        if (r == 0)
                            firstRowValues[c] = value; // record this so that we can divide each value (including this one) by this.
                        if (colItem is SimpleReportColumnFilter colFilter && colFilter.ColumnFilterOptions == SimpleReportColumnFilterOptions.ProportionOfFirstRowOfColumn)
                        {
                            double? firstRowValue = firstRowValues[c];
                            if (firstRowValue == 0 || firstRowValue == null)
                                value = null;
                            else
                                value /= firstRowValues[c];
                        }
                        string valueString = value == null ? "" : value.ToSignificantFigures_MaxLength(6, colItem.Width);
                        Append(standardReport, csvReport, true, valueString, colItem.Width, colItem == lastColumn);
                        i++;
                        c++;
                    }
                    standardReport.AppendLine();
                    csvReport.AppendLine();
                    r++;
                }
            }
            return new ReportCollection(standardReport.ToString(), csvReport.ToString());
        }

        public static string FormatTableString(string s, int? width, bool isLastColumn)
        {
            if (width == null)
                return s + (isLastColumn ? "" : ",");
            return String.Format(GetFormatStringForSpecifiedWidth((int)width), s);
        }

        public static string GetFormatStringForSpecifiedWidth(int width)
        {
            return "{0,-" + (width + 3).ToString() + "}";
        }
    }
}
