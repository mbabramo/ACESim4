using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class SimpleReportMerging
    {
        public static string GetMergedReports(string cumulativeReport, string reportName, bool includeFirstLine)
        {
            var averageReport = SimpleReportMerging.GetAggregationReport(cumulativeReport, reportName, SimpleReportAggregations.Average, includeFirstLine);
            var lowerBoundReport = SimpleReportMerging.GetAggregationReport(cumulativeReport, reportName, SimpleReportAggregations.LowerBound, false);
            var upperBoundReport = SimpleReportMerging.GetAggregationReport(cumulativeReport, reportName, SimpleReportAggregations.UpperBound, false);
            return averageReport + lowerBoundReport + upperBoundReport;
        }

        public enum SimpleReportAggregations
        {
            Average,
            LowerBound,
            UpperBound
        }

        public static string GetSimpleReportAggregationName(SimpleReportAggregations aggregation)
        {
            switch (aggregation)
            {
                case SimpleReportAggregations.Average:
                    return "\"Average\"";
                case SimpleReportAggregations.LowerBound:
                    return "\"LowerBound\"";
                case SimpleReportAggregations.UpperBound:
                    return "\"UpperBound\"";
                default:
                    throw new ArgumentOutOfRangeException(nameof(aggregation), aggregation, null);
            }
        }

        public static Func<IEnumerable<double>, double> GetSimpleReportAggregationFunction(SimpleReportAggregations aggregation)
        {
            switch (aggregation)
            {
                case SimpleReportAggregations.Average:
                    return x => x.Average();
                case SimpleReportAggregations.LowerBound:
                case SimpleReportAggregations.UpperBound:
                    bool isLower = aggregation == SimpleReportAggregations.LowerBound;
                    return x =>
                    {
                        if (x.All(y => y == 0))
                            return 0;
                        else if (x.All(y => y == 1.0))
                            return 1.0;
                        bool doLogitTransformation = x.All(y => y >= 0 && y <= 1);
                        StatCollector statCollector = new StatCollector();
                        foreach (double d in x)
                        {
                            double dTransformed = d;
                            if (doLogitTransformation)
                            {
                                if (dTransformed == 0)
                                    dTransformed = 1E-10;
                                else if (dTransformed == 1)
                                    dTransformed = 1.0 - 1E-10;
                                dTransformed = Math.Log(dTransformed / (1.0 - dTransformed));
                            }
                            statCollector.Add(dTransformed);
                        }
                        double confInterval = statCollector.ConfInterval();
                        double bound = isLower ? statCollector.Average() - confInterval : statCollector.Average() + confInterval;
                        if (doLogitTransformation)
                            bound = Math.Exp(bound) / (Math.Exp(bound) + 1.0);
                        return bound;
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(aggregation), aggregation, null);
            }
        }


        public static string GetAggregationReport(string cumulativeReport, string reportName, SimpleReportAggregations simpleReportAggregation, bool includeFirstLine)
        {
            MergeCumulativeReportToDictionary(cumulativeReport, reportName, simpleReportAggregation, out List<string> variableNames, out List<string> filterNames, out Dictionary<string, List<(string theString, double? theValue)>> aggregatedReport);
            return GetAggregatedCSV(variableNames, filterNames, aggregatedReport, includeFirstLine);
        }

        public static string AddReportInformationColumns(string report, string reportName, string reportIteration, bool includeFirst)
        {
            int numMainLines;
            StringBuilder sb = new StringBuilder();
            using (StringReader reader = new StringReader(report))
            {
                string line;
                bool isFirst = true;
                numMainLines = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    if (isFirst && !includeFirst)
                    {
                        isFirst = false;
                        continue;
                    }
                    string lineWithPreface;
                    if (isFirst)
                        lineWithPreface = $"\"Report\",\"Iteration\",\"LineNum\",{line}";
                    else
                        lineWithPreface = $"\"{reportName}\",{reportIteration},{numMainLines},{line}";
                    sb.Append(lineWithPreface);
                    sb.AppendLine();
                    if (!isFirst)
                        numMainLines++;
                    isFirst = false;
                }
            }
            string differentiatedReport = sb.ToString();
            return differentiatedReport;
        }

        private static string GetAggregatedCSV(List<string> variableNames, List<string> filterNames, Dictionary<string, List<ValueTuple<string, double?>>> aggregatedReport, bool includeFirstLine)
        {
            StringBuilder sb = new StringBuilder();
            if (includeFirstLine)
            {
                sb.Append(String.Join(",", variableNames));
                sb.AppendLine();
            }

            foreach (string filterName in filterNames)
            {
                var theList = aggregatedReport[filterName];
                for (var index = 0; index < theList.Count; index++)
                {
                    var item = theList[index];
                    sb.Append(item.Item1);
                    if (index != theList.Count - 1)
                        sb.Append(",");
                }
                sb.AppendLine();
            }
            var aggregatedCSV = sb.ToString();
            return aggregatedCSV;
        }

        private static void MergeCumulativeReportToDictionary(string cumulativeReport, string reportName, SimpleReportAggregations simpleReportAggregation, out List<string> variableNames, out List<string> filterNames, out Dictionary<string, List<ValueTuple<string, double?>>> aggregatedReport)
        {
            (string headerLine, List<string> otherLines) = GetHeaderLineAndOtherLines(cumulativeReport);
            variableNames = headerLine.Split(',').ToList();
            var listOfDictionaries = GetLinesAsDictionaries(variableNames, otherLines);
            var narrowedToReport = Enumerable.Where<Dictionary<string, (string theString, double? theValue)>>(listOfDictionaries, x => x["\"Report\""].theString == $"\"{reportName}\"").ToList();
            filterNames = narrowedToReport.Select(x => x["\"Filter\""].theString).Distinct().ToList();

            aggregatedReport = new Dictionary<string, List<(string theString, double? theValue)>>();
            foreach (string filterName in filterNames)
            {
                var narrowedToFilter = Enumerable.Where<Dictionary<string, (string theString, double? theValue)>>(listOfDictionaries, x => x["\"Filter\""].theString == filterName).ToList();
                var dictionaryOfLists = ConvertToDictionaryOfLists(narrowedToFilter);
                List<(string theString, double? theValue)> variableList = new List<(string theString, double? theValue)>();
                foreach (string variableName in variableNames)
                {
                    var itemsForVariable = dictionaryOfLists[variableName];
                    if (variableName == "\"Iteration\"")
                        variableList.Add((GetSimpleReportAggregationName(simpleReportAggregation), null));
                    else if (variableName == "\"Filter\"")
                        variableList.Add((filterName, null));
                    else if (Enumerable.All<(string theString, double? theValue)>(itemsForVariable, x => x.theString == "" || x.theValue != null))
                    {
                        var numericItems = Enumerable.Where<(string theString, double? theValue)>(itemsForVariable, x => x.theValue != null).Select(x => (double) x.theValue).ToList();
                        if (numericItems.Any())
                        {
                            var aggregationFunc = GetSimpleReportAggregationFunction(simpleReportAggregation);
                            var aggregation = aggregationFunc(numericItems);
                            variableList.Add((aggregation.ToString(), aggregation));
                        }
                        else
                        {
                            variableList.Add(("", null));
                        }
                    }
                    else
                    {
                        var theStringValues = Enumerable.Select<(string theString, double? theValue), string>(itemsForVariable, x => x.theString).Distinct().ToList();
                        if (theStringValues.Count() == 1)
                            variableList.Add((theStringValues.Single(), null));
                    }
                }
                aggregatedReport[filterName] = variableList;
            }
        }

        private static (string, List<string>) GetHeaderLineAndOtherLines(string csvString)
        {
            List<string> lines = GetLinesAsStrings(csvString);
            return (lines.First(), lines.Skip(1).ToList());
        }

        private static List<string> GetLinesAsStrings(string csvString)
        {
            List<string> lines = new List<string>();
            using (StringReader reader = new StringReader(csvString))
            {
                string line = string.Empty;
                do
                {
                    line = reader.ReadLine();
                    if (line != null)
                    {
                        lines.Add(line);
                    }

                } while (line != null);
                return lines;
            }
        }

        private static Dictionary<T, List<U>> ConvertToDictionaryOfLists<T,U>(List<Dictionary<T, U>> listOfDictionaries)
        {
            Dictionary<T, List<U>> outerDict = new Dictionary<T, List<U>>();
            foreach (var key in listOfDictionaries.SelectMany(x => x.Keys).Distinct())
            {
                List<U> innerList = new List<U>();
                foreach (Dictionary<T, U> innerDict in listOfDictionaries)
                {
                    foreach (var keyValuePair in innerDict)
                    {
                        if (keyValuePair.Key.Equals(key))
                            innerList.Add(keyValuePair.Value);
                    }
                }
                outerDict[key] = innerList;
            }
            return outerDict;
        }

        private static List<Dictionary<string, (string theString, double? theValue)>> GetLinesAsDictionaries(List<string> variableNames, List<string> linesToParse)
        {
            List<Dictionary<string, (string theString, double? theValue)>> list = new List<Dictionary<string, (string theString, double? theValue)>>();
            foreach (string line in linesToParse)
            {
                string[] parsed = line.Split(',');
                Dictionary<string, (string theString, double? theValue)> dict = new Dictionary<string, (string theString, double? theValue)>();
                for (int i = 0; i < parsed.Length; i++)
                {
                    string variableName = variableNames[i];
                    string value = parsed[i];
                    bool parsable = double.TryParse(value, out double parseResult);
                    dict[variableName] = parsable ? (value, (double?) parseResult) : (value, null);
                }
                list.Add(dict);
            }
            return list;
        }
    }
}