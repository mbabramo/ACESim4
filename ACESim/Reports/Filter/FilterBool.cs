using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class FilterBool : Filter
    {
        public bool value;
        public FilterBool(string theVariableName, string theOperation, bool theValue)
            : base(theVariableName, theOperation)
        {
            value = theValue;
            if (operation != Operation.doesNotEqual && operation != Operation.equals)
                throw new Exception("A bool filter supports only = and != operations.");
        }
        public bool DoFilterBool(bool filterByValue, bool trueIfNonNull = false)
        {
            if (trueIfNonNull)
                return true;
            if (operation == Operation.equals)
                return value == filterByValue;
            else
                return value != filterByValue;
        }
        public override string GetFilterName(string prefix)
        {
            prefix = base.GetFilterName(prefix);
            return prefix + value.ToString();
        }
    }
}
