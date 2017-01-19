using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class FilterDouble : Filter
    {
        public double? value;
        public FilterDouble(string theVariableName, string theOperation, double? theValue)
            : base(theVariableName, theOperation)
        {
            value = theValue;
        }
        public bool DoFilterDouble(double? filterByValue, bool trueIfNonNull = false)
        {
            if (trueIfNonNull)
                return filterByValue != null;
            switch (operation)
            {
                case Operation.equals:
                    if (filterByValue == null || value == null)
                        return filterByValue == value;
                    return (Math.Abs((double) filterByValue - (double) value) < 0.00001);
                case Operation.doesNotEqual:
                    if (filterByValue == null || value == null)
                        return filterByValue != value;
                    return (Math.Abs((double)filterByValue - (double)value) > 0.00001);
                case Operation.greaterThan:
                    return (filterByValue > value);
                case Operation.greaterThanOrEqualTo:
                    return (filterByValue >= value);
                case Operation.lessThan:
                    return (filterByValue < value);
                case Operation.lessThanOrEqualTo:
                    return (filterByValue <= value);
                default:
                    throw new Exception("Unknown operation.");
            }
        }

        public override string GetFilterName(string prefix)
        {
            prefix = base.GetFilterName(prefix);
            return prefix + value.ToString();
        }
    }
}
