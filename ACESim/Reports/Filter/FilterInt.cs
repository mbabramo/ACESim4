using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class FilterInt : Filter
    {
        public int? value;
        public FilterInt(string theVariableName, string theOperation, int? theValue)
            : base(theVariableName, theOperation)
        {
            value = theValue;
        }
        public bool DoFilterInt(int? filterByValue, bool trueIfNonNull = false)
        {
            if (trueIfNonNull)
                return filterByValue != null;
            switch (operation)
            {
                case Operation.equals:
                    if (filterByValue == null || value == null)
                        return filterByValue == value;
                    return (Math.Abs((int)filterByValue - (int)value) < 0.00001);
                case Operation.doesNotEqual:
                    if (filterByValue == null || value == null)
                        return filterByValue != value;
                    return (Math.Abs((int)filterByValue - (int)value) > 0.00001);
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
