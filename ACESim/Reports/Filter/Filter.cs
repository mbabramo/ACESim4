using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace ACESim
{
    [Serializable]
    public class Filter
    {
        public string variableName;
        public Operation operation;
        private FieldInfo fieldInfo = null;

        public Filter(string theVariableName, string theOperation)
        {
            variableName = theVariableName;
            switch (theOperation)
            {
                case "EQ":
                    operation = Operation.equals;
                    break;
                case "NE":
                    operation = Operation.doesNotEqual;
                    break;
                case "GT":
                    operation = Operation.greaterThan;
                    break;
                case "GTEQ":
                    operation = Operation.greaterThanOrEqualTo;
                    break;
                case "LT":
                    operation = Operation.lessThan;
                    break;
                case "LTEQ":
                    operation = Operation.lessThanOrEqualTo;
                    break;
                case "OR":
                    operation = Operation.or;
                    break;
                case "AND":
                    operation = Operation.and;
                    break;
                default:
                    throw new Exception("Operation " + theOperation + " is not a valid filter operation.");
            }
        }
        public virtual string GetFilterName(string prefix)
        {
            if (prefix == "")
                return "";
            return prefix + "=";
        }

        public bool DoFilter(GameProgressReportable theOutput, bool passesFilterAsLongAsNotNull = false)
        {
            if (this is FilterOr)
                return ((FilterOr)this).DoFilterOr(theOutput, passesFilterAsLongAsNotNull); // there is no field to get for DoFilterOr, though it will call DoFilter on its contained fields
            else if (this is FilterAnd)
                return ((FilterAnd)this).DoFilterAnd(theOutput, passesFilterAsLongAsNotNull);
            object field = GetField(theOutput);
            if (field == null)
                return false;
            else if (this is FilterBool)
                return ((FilterBool)this).DoFilterBool((bool)field, passesFilterAsLongAsNotNull);
            else if (this is FilterDouble)
                return ((FilterDouble)this).DoFilterDouble((double?)field, passesFilterAsLongAsNotNull);
            else if (this is FilterInt)
                return ((FilterInt)this).DoFilterInt((int?)field, passesFilterAsLongAsNotNull);
            else if (this is FilterText)
                return ((FilterText)this).DoFilterText((string)field, passesFilterAsLongAsNotNull);
            else
                throw new Exception("The outputs in the specified file do not include a field required by the report.");
        }

        public object GetField(GameProgressReportable theOutput)
        {
            bool found;
            object theValue = theOutput.GetValueForReport(variableName, null, out found);
            if (!found)
                throw new Exception("The outputs in the specified file do not include a field required by a filter in the report, " + variableName + ".");
            if (theValue != null)
            {
                Type theType = theValue.GetType();
                if (this is FilterBool && theValue.GetType() != typeof(bool))
                    throw new Exception("The output " + variableName + " should be bool but is " + theType.ToString());
                if (this is FilterDouble && theValue.GetType() != typeof(double))
                    throw new Exception("The output " + variableName + " should be double but is " + theType.ToString()); 
                if (this is FilterInt && theValue.GetType() != typeof(int))
                    throw new Exception("The output " + variableName + " should be int but is " + theType.ToString());
                if (this is FilterText && theValue.GetType() != typeof(string))
                    throw new Exception("The output " + variableName + " should be string but is " + theType.ToString());
            }
            return theValue;
        }
    }
}
