using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class RowOrColInfo : RowOrColInfoOrGenerator
    {
        public string rowOrColName;
        public string variableName;
        public int? listIndex;
        public string statisticString;
        public Statistic statistic;
        public List<Filter> filters;
        public RowOrColInfo(string theRowOrColName, string theVariableName, int? theListIndex, string theStatistic)
        {
            rowOrColName = theRowOrColName;
            variableName = theVariableName;
            listIndex = theListIndex;
            statisticString = theStatistic;
            switch (theStatistic.ToUpper())
            {
                case "NONE":
                case "":
                    statistic = Statistic.none;
                    break;
                case "COUNT":
                    statistic = Statistic.count;
                    break;
                case "PERCENTOFALLCASES":
                    statistic = Statistic.percentOfAllCases;
                    break;
                case "PERCENTOFCASESFILTEREDOPPOSITE":
                    statistic = Statistic.percentOfCasesFilteredOpposite;
                    break;
                case "MEAN":
                    statistic = Statistic.mean;
                    break;
                case "MEDIAN":
                    statistic = Statistic.median;
                    break;
                case "SUM":
                    statistic = Statistic.sum;
                    break;
                case "STDEV":
                    statistic = Statistic.stdev;
                    break;
                default:
                    throw new Exception("Statistic " + theStatistic + " is not a known or available statistic.");
            }
            filters = new List<Filter>();
        }
        public RowOrColInfo(string theRowOrColName, string theVariableName, int? theListIndex, string theStatistic, List<Filter> theFilters)
            : this(theRowOrColName, theVariableName, theListIndex, theStatistic)
        {
            filters = theFilters;
        }

        public override string ToString()
        {
            return rowOrColName + " " + variableName + statistic.ToString();
        }

        public string GetVariableNameForIntersection(RowOrColInfo intersectingRowOrColInfo)
        {
            if (this.variableName == intersectingRowOrColInfo.variableName)
                return this.variableName;
            if (this.variableName == "" || this.variableName == null)
                return intersectingRowOrColInfo.variableName;
            else
                return this.variableName;
        }

        public Statistic GetStatisticForIntersection(RowOrColInfo intersectingRowOrColInfo)
        {
            if (this.variableName != intersectingRowOrColInfo.variableName && this.variableName != "" && this.variableName != null && intersectingRowOrColInfo.variableName != "" && intersectingRowOrColInfo.variableName != null)
                return Statistic.none; // calculate no statistic when intersecting variables are named.

            if (this.statistic == intersectingRowOrColInfo.statistic)
                return this.statistic;
            if (this.statistic == Statistic.none)
                return intersectingRowOrColInfo.statistic;
            if (intersectingRowOrColInfo.statistic == Statistic.none)
                return this.statistic;
            if (this.statistic == Statistic.percentOfCasesFilteredOpposite || intersectingRowOrColInfo.statistic == Statistic.percentOfCasesFilteredOpposite)
                return Statistic.percentOfCasesFilteredOpposite;
            if (this.statistic == Statistic.percentOfAllCases || intersectingRowOrColInfo.statistic == Statistic.percentOfAllCases)
                return Statistic.percentOfAllCases;
            if (this.statistic == Statistic.count || intersectingRowOrColInfo.statistic == Statistic.count)
                return Statistic.count;
            return Statistic.none; // Return null when generating statistic b/c of conflict.
        }
    }
}
