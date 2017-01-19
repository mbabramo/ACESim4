using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class FilterText : Filter
    {
        public string text;
        public FilterText(string theVariableName, string theOperation, string theText)
            : base(theVariableName, theOperation)
        {
            text = theText;
            if (operation != Operation.doesNotEqual && operation != Operation.equals)
                throw new Exception("A text filter supports only = and != operations.");
        }
        public bool DoFilterText(string comparisonText, bool trueIfNonNull = false)
        {
            if (trueIfNonNull)
                return comparisonText != null && comparisonText != "";
            if (operation == Operation.equals)
                return text == comparisonText;
            else
                return text != comparisonText;
        }

        public override string GetFilterName(string prefix)
        {
            prefix = base.GetFilterName(prefix);
            return prefix + text;
        }
    }
}
