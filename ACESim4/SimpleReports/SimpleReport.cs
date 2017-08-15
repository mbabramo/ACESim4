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
            foreach (SimpleReportFilter metaFilter in Definition.MetaFilters)
            {
                bool metaFilterSatisfied = metaFilter.IsInFilter(completedGame);
                if (metaFilterSatisfied)
                {
                    foreach (SimpleReportFilter rowFilter in Definition.RowFilters)
                    {
                        bool rowFilterSatisfied = rowFilter.IsInFilter(completedGame);
                        foreach (SimpleReportColumnItem colItem in Definition.ColumnItems)
                        {
                            var v = colItem.GetValueToRecord(completedGame);
                            bool recordThis;
                            if (colItem is SimpleReportColumnVariable)
                                recordThis = rowFilterSatisfied;
                            else
                            {
                                var cf = (SimpleReportColumnFilter)colItem;
                                recordThis = cf.ReportAsPercentageOfAll || rowFilterSatisfied;
                                if (recordThis && !rowFilterSatisfied)
                                    v = 0; // record this as a 0; we record as a 1 only if column and row filters are satisfied.
                            }
                            //if (recordThis && v == null)
                            //    StatCollectors[i] = null; // this collection is invalid -- we only report stats when every item is present.
                            if (recordThis && StatCollectors[i] != null && v != null)
                                StatCollectors[i].Add((double) v, weight);
                            i++;
                        }
                    }
                }
                else
                    i += Definition.RowFilters.Count() * Definition.ColumnItems.Count();
            }
        }

        public void GetReport(StringBuilder sb, bool commaSeparated)
        {
            if (!commaSeparated)
                sb.AppendLine(Definition.Name);
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
                sb.AppendLine();

                // print rows
                int i = 0;
                foreach (SimpleReportFilter rowFilter in Definition.RowFilters)
                {
                    if (printMetaColumn)
                        sb.Append(FormatTableString(metaFilter.Name, metaColumnWidth, false));
                    sb.Append(FormatTableString(rowFilter.Name, rowFilterColumnWidth, false));
                    foreach (SimpleReportColumnItem colItem in Definition.ColumnItems)
                    {
                        double? value;
                        if (StatCollectors[i] == null || StatCollectors[i].Num() == 0)
                            value = null;
                        else
                        {
                            if (colItem is SimpleReportColumnFilter)
                            {
                                SimpleReportColumnFilter cf = (SimpleReportColumnFilter)colItem;
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
                        string valueString = value == null ? "" : value.ToSignificantFigures();
                        sb.Append(FormatTableString(valueString, colItem.Width, colItem == lastColumn));
                        i++;
                    }
                    sb.AppendLine();
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
