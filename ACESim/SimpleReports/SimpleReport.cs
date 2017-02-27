using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class SimpleReport
    {
        SimpleReportDefinition Definition;
        StatCollector[] StatCollectors;

        public SimpleReport(SimpleReportDefinition definition)
        {
            Definition = definition;
            for (int i = 0; i < Definition.TotalCells; i++)
                StatCollectors[i] = new StatCollector();
        }

        public void ProcessGameProgress(GameProgress completedGame, double weight)
        {
            int i = 0;
            foreach (SimpleReportFilter metaFilter in Definition.MetaFilters)
            {
                bool metaFilterSatisfied = metaFilter.IsInFilter(completedGame);
                if (metaFilterSatisfied)
                {
                    foreach (SimpleReportFilter rowFilter in Definition.RowFilters)
                    {
                        bool rowFilterSatisfied = metaFilter.IsInFilter(completedGame);
                        if (rowFilterSatisfied)
                        {
                            bool isFirstColumnItem = true;
                            foreach (SimpleReportColumnItem colItem in Definition.ColumnItems)
                            {
                                var v = colItem.GetValueToRecord(completedGame);
                                if (isFirstColumnItem && v != 1.0)
                                    throw new Exception("First column item must be a filter that is always true, so that we can calculate percentage correctly.");
                                StatCollectors[i].Add(v, weight);
                                i++;
                                isFirstColumnItem = false;
                            }
                        }
                        else
                            i += Definition.ColumnItems.Count();
                    }
                }
                else
                    i += Definition.RowFilters.Count() * Definition.ColumnItems.Count();
            }
        }

        public string GetReport(bool commaSeparated)
        {
            StringBuilder sb = new StringBuilder();
            int? metaColumnWidth = null, rowFilterColumnWidth = null;
            if (!commaSeparated)
            {
                metaColumnWidth = Math.Max(9, Definition.MetaFilters.Max(x => x.Name.Length) + 3);
                rowFilterColumnWidth = Math.Max(9, Definition.RowFilters.Max(x => x.Name.Length) + 3);
            }
            SimpleReportColumnItem lastColumn = Definition.ColumnItems.Last();
            bool printMetaColumn = Definition.MetaFilters.Count() > 1;
            foreach (SimpleReportFilter metaFilter in Definition.MetaFilters)
            {
                // print column headers
                if (printMetaColumn)
                    sb.Append(FormatTableString("Filter1", metaColumnWidth, false));
                sb.Append(FormatTableString(printMetaColumn ? "Filter2" : "Filter", rowFilterColumnWidth, false));
                foreach (SimpleReportColumnItem colItem in Definition.ColumnItems)
                    sb.Append(FormatTableString(colItem.Name, colItem.Width, colItem == lastColumn));

                // print rows
                bool isFirstRowInTable = true;
                double proportionItemsInTable = 1.0, proportionItemsInRow = 0;
                int i = 0;
                foreach (SimpleReportFilter rowFilter in Definition.RowFilters)
                {
                    if (printMetaColumn)
                        sb.Append(FormatTableString(metaFilter.Name, metaColumnWidth, false));
                    sb.Append(FormatTableString(rowFilter.Name, rowFilterColumnWidth, false));
                    bool isFirstColumnInRow = true;
                    foreach (SimpleReportColumnItem colItem in Definition.ColumnItems)
                    {
                        double? value;
                        if (colItem is SimpleReportColumnFilter)
                        {
                            SimpleReportColumnFilter cf = (SimpleReportColumnFilter) colItem;
                            if (isFirstColumnInRow)
                                proportionItemsInRow = StatCollectors[i].Average();
                            double denominator = (cf.ReportAsPercentageOfAll ? 1.0 : proportionItemsInRow);
                            value = denominator == 0 ? null : (double?) StatCollectors[i].Average() / denominator;
                        }
                        else
                        {
                            SimpleReportColumnVariable cv = (SimpleReportColumnVariable)colItem;
                            if (StatCollectors[i].Num() == 0)
                                value = null;
                            else
                                value = cv.Stdev ? StatCollectors[i].StandardDeviation() : StatCollectors[i].Average();
                        }
                        string valueString = value == null ? "" : value.ToSignificantFigures();
                        sb.Append(FormatTableString(valueString, colItem.Width, colItem == lastColumn));
                        isFirstColumnInRow = false;
                        i++;
                    }
                    isFirstRowInTable = false;
                }
            }
            
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
