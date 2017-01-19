using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class RowOrColInfoGenerator : RowOrColInfoOrGenerator
    {
        public string filterVariableName;
        public string rowOrColNamePrefix;
        public string outputVariableName;
        public string statistic;
        public int? numDynamicRanges;
        public bool evenlySpaceDynamicRanges;
        public double? evenlySpaceDynamicRangesMinOverride = null; // if null, we use the min in the data
        public double? evenlySpaceDynamicRangesMaxOverride = null;
        public bool reportIndexStartingAtZero;
        public int? numberElementsInList;
        public List<double> ranges;
        public List<Filter> filters;

        public override string ToString()
        {
            return String.Format("Filter: {0}; Prefix: {1}; Output: {2}; Statistic: {3}", filterVariableName, rowOrColNamePrefix, outputVariableName, statistic);
        }

        public RowOrColInfoGenerator(string theFilterVariableName, string theRowOrColNamePrefix, string theOutputVariableName, string theStatistic, List<double> theRanges, int? theNumDynamicRanges, bool theEvenlySpaceDynamicRanges, double? theEvenlySpaceDynamicRangesMinOverride, double? theEvenlySpaceDynamicRangesMaxOverride, bool theZeroBasedList, int? theMaxListIndex)
        {
            filterVariableName = theFilterVariableName;
            rowOrColNamePrefix = theRowOrColNamePrefix;
            outputVariableName = theOutputVariableName;
            statistic = theStatistic;
            ranges = theRanges;
            filters = new List<Filter>();
            numDynamicRanges = theNumDynamicRanges;
            evenlySpaceDynamicRanges = theEvenlySpaceDynamicRanges;
            evenlySpaceDynamicRangesMinOverride = theEvenlySpaceDynamicRangesMinOverride;
            evenlySpaceDynamicRangesMaxOverride = theEvenlySpaceDynamicRangesMaxOverride;
            reportIndexStartingAtZero = theZeroBasedList;
            numberElementsInList = theMaxListIndex;
        }

        public RowOrColInfoGenerator(string theFilterVariableName, string theRowOrColNamePrefix, string theOutputVariableName, string theStatistic, List<double> theRanges, List<Filter> theFilters, int? theNumDynamicRanges, bool theEvenlySpaceDynamicRanges, double? theEvenlySpaceDynamicRangesMinOverride, double? theEvenlySpaceDynamicRangesMaxOverride, bool theZeroBasedList, int? theMaxListIndex)
            : this(theFilterVariableName, theRowOrColNamePrefix, theOutputVariableName, theStatistic, theRanges, theNumDynamicRanges, theEvenlySpaceDynamicRanges, theEvenlySpaceDynamicRangesMinOverride, theEvenlySpaceDynamicRangesMaxOverride, theZeroBasedList, theMaxListIndex)
        {
            filters = theFilters;
        }

        public List<RowOrColInfo> GenerateRowOrColInfosForFilters(List<List<Filter>> additionalFilters, List<string> filterGroupNames)
        {
            List<RowOrColInfo> theList = new List<RowOrColInfo>();
            int filterCount = additionalFilters.Count;
            for (int i = 0; i < filterCount; i++)
            {
                List<Filter> combinedFilters = new List<Filter>();
                combinedFilters.AddRange(filters);
                combinedFilters.AddRange(additionalFilters[i]);
                RowOrColInfo additionalRowOrColInfo = new RowOrColInfo(filterGroupNames[i], outputVariableName, null, statistic, combinedFilters);
                theList.Add(additionalRowOrColInfo);
            }
            return theList;
        }

        internal bool AllValuesAreIntegral(List<double?> theValues)
        {
            return !theValues.Any(x => x == null || Math.Abs((double)x - Math.Round((double)x)) > 0.001);
        }

        public List<RowOrColInfo> GenerateRowOrColInfosFromRanges(List<double?> theValues)
        {
            bool valuesAreIntegral = AllValuesAreIntegral(theValues);
            List<List<Filter>> additionalFilters = new List<List<Filter>>();
            List<string> filterNames = new List<string>();
            for (int i = 1; i < ranges.Count; i++)
            {
                double lowerValue = ranges[i - 1];
                double higherValue = (valuesAreIntegral && i < ranges.Count - 1) ? ranges[i] - 1 : ranges[i];
                List<Filter> filterGroup = new List<Filter>();
                FilterDouble filterToAdd = new FilterDouble(filterVariableName, i == 1 ? "GTEQ" : "GT", lowerValue);
                filterGroup.Add(filterToAdd);
                FilterDouble filter2ToAdd = new FilterDouble(filterVariableName, "LTEQ", higherValue);
                filterGroup.Add(filter2ToAdd);
                additionalFilters.Add(filterGroup);
                filterNames.Add(rowOrColNamePrefix + " " + lowerValue.ToSignificantFigures() + " to " + higherValue.ToSignificantFigures());
            }
            return GenerateRowOrColInfosForFilters(additionalFilters, filterNames);
        }

        public List<RowOrColInfo> GenerateRowOrColInfosFromRanges(List<int> theValues)
        {
            bool valuesAreIntegral = true;
            List<List<Filter>> additionalFilters = new List<List<Filter>>();
            List<string> filterNames = new List<string>();
            for (int i = 1; i < ranges.Count; i++)
            {
                int lowerValue = (int) ranges[i - 1];
                int  higherValue = (valuesAreIntegral && i < ranges.Count - 1) ? (int) ranges[i] - 1 : (int) ranges[i];
                List<Filter> filterGroup = new List<Filter>();
                FilterDouble filterToAdd = new FilterDouble(filterVariableName, i == 1 ? "GTEQ" : "GT", lowerValue);
                filterGroup.Add(filterToAdd);
                FilterInt filter2ToAdd = new FilterInt(filterVariableName, "LTEQ", higherValue);
                filterGroup.Add(filter2ToAdd);
                additionalFilters.Add(filterGroup);
                filterNames.Add(rowOrColNamePrefix + " " + lowerValue + " to " + higherValue);
            }
            return GenerateRowOrColInfosForFilters(additionalFilters, filterNames);
        }

        bool dynamicRangesCreated = false;
        private void CreateDynamicRanges(List<double?> theValues)
        {
            if (numDynamicRanges != null && (int) numDynamicRanges > 1 && !dynamicRangesCreated)
            {
                List<double> nonNullValues = theValues.Where(x => x != null).Select(x => (double) x).OrderBy(x => x).ToList();
                if (nonNullValues.Any())
                {
                    if (evenlySpaceDynamicRanges)
                    {
                        double min, max;
                        if (evenlySpaceDynamicRangesMinOverride != null)
                            min = (double)evenlySpaceDynamicRangesMinOverride;
                        else
                            min = nonNullValues.Min();
                        if (evenlySpaceDynamicRangesMaxOverride != null)
                            max = (double)evenlySpaceDynamicRangesMaxOverride;
                        else
                            max = nonNullValues.Max();
                        double stepSize = (max - min) / (double) numDynamicRanges;
                        for (int r = 0; r <= numDynamicRanges; r++)
                            ranges.Add(r == numDynamicRanges ? max : min + ((double)r) * stepSize);
                    }
                    else
                    {
                        int index = 0;
                        ranges.Add(nonNullValues[0]);
                        for (int dr = 1; dr <= numDynamicRanges; dr++)
                        {
                            index = (int)(((double)dr / (double)numDynamicRanges) * (double)nonNullValues.Count) - 1;
                            if (index >= 0 && index < nonNullValues.Count)
                                ranges.Add(nonNullValues[index]);
                        }
                    }
                }
                dynamicRangesCreated = true;
            }
        }

        private void CreateDynamicRanges(List<int> theValues)
        {
            if (numDynamicRanges != null && (int)numDynamicRanges > 1 && !dynamicRangesCreated)
            {
                List<int> nonNullValues = theValues.Select(x => (int)x).OrderBy(x => x).ToList();
                int index = 0;
                ranges.Add(nonNullValues[0]);
                for (int dr = 1; dr <= numDynamicRanges; dr++)
                {
                    index = (int)(((double)dr / (double)numDynamicRanges) * (double)nonNullValues.Count) - 1;
                    ranges.Add(nonNullValues[index]);
                }
                dynamicRangesCreated = true;
            }
        }

        public List<RowOrColInfo> GenerateRowOrColInfos(List<double?> theValues)
        {
            List<RowOrColInfo> listWithoutRegardToArrayIndex = GenerateRowOrColInfosWithoutRegardToArrayIndex(theValues);
            if (numberElementsInList == null)
                return listWithoutRegardToArrayIndex;
            if (listWithoutRegardToArrayIndex.Count == 0)
                listWithoutRegardToArrayIndex.Add(new RowOrColInfo(rowOrColNamePrefix, outputVariableName, null, statistic));
            List<RowOrColInfo> listWithIndices = new List<RowOrColInfo>();
            for (int i = (int)0; i < (int)numberElementsInList; i++)
            {
                int indexToReport = i;
                if (!reportIndexStartingAtZero)
                    indexToReport++;
                foreach (RowOrColInfo original in listWithoutRegardToArrayIndex)
                {
                    RowOrColInfo withListIndex = new RowOrColInfo(original.rowOrColName  + " " + indexToReport.ToString(), original.variableName, i, original.statisticString, original.filters);
                    listWithIndices.Add(withListIndex);
                }
            }
            return listWithIndices;
        }

        public List<RowOrColInfo> GenerateRowOrColInfosWithoutRegardToArrayIndex(List<double?> theValues)
        {
            if (filterVariableName == null || filterVariableName == "")
                return new List<RowOrColInfo>();

            CreateDynamicRanges(theValues);
            if (ranges.Count > 1)
                return GenerateRowOrColInfosFromRanges(theValues);

            if (theValues.Count <= 10 && !theValues.All(x => x == null))
            {
                List<List<Filter>> additionalFilters = new List<List<Filter>>();
                List<string> filterNames = new List<string>();
                foreach (double theValue in theValues)
                {
                    FilterDouble filterToAdd = new FilterDouble(filterVariableName, "EQ", theValue);
                    additionalFilters.Add(new List<Filter>() { filterToAdd });
                    filterNames.Add(filterToAdd.GetFilterName(rowOrColNamePrefix));
                }
                return GenerateRowOrColInfosForFilters(additionalFilters, filterNames);
            }

            return new List<RowOrColInfo>();
        }

        public List<RowOrColInfo> GenerateRowOrColInfos(List<int> theValues)
        {
            CreateDynamicRanges(theValues);
            if (ranges.Count > 1)
                return GenerateRowOrColInfosFromRanges(theValues);

            List<List<Filter>> additionalFilters = new List<List<Filter>>();
            List<string> filterNames = new List<string>();
            foreach (int theValue in theValues)
            {
                FilterInt filterToAdd = new FilterInt(filterVariableName, "EQ", theValue);
                additionalFilters.Add(new List<Filter>() { filterToAdd });
                filterNames.Add(filterToAdd.GetFilterName(rowOrColNamePrefix));
            }
            return GenerateRowOrColInfosForFilters(additionalFilters, filterNames);
        }

        public List<RowOrColInfo> GenerateRowOrColInfos(List<bool> theValues)
        {
            List<List<Filter>> additionalFilters = new List<List<Filter>>();
            List<string> filterNames = new List<string>();
            foreach (bool theValue in theValues)
            {
                FilterBool filterToAdd = new FilterBool(filterVariableName, "EQ", theValue);
                additionalFilters.Add(new List<Filter>() { filterToAdd });
                filterNames.Add(filterToAdd.GetFilterName(rowOrColNamePrefix));
            }
            return GenerateRowOrColInfosForFilters(additionalFilters, filterNames);
        }
        public List<RowOrColInfo> GenerateRowOrColInfos(List<string> theValues)
        {
            List<List<Filter>> additionalFilters = new List<List<Filter>>();
            List<string> filterNames = new List<string>();
            foreach (string theValue in theValues)
            {
                FilterText filterToAdd = new FilterText(filterVariableName, "EQ", theValue);
                additionalFilters.Add(new List<Filter>() { filterToAdd });
                filterNames.Add(filterToAdd.GetFilterName(rowOrColNamePrefix));
            }
            return GenerateRowOrColInfosForFilters(additionalFilters, filterNames);
        }
    }
}
