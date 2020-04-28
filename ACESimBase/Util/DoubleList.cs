using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Util
{
    [Serializable]
    public class DoubleList
    {
        public List<double> TheList;

        public DoubleList(IEnumerable<double> listItems)
        {
            TheList = listItems?.ToList() ?? new List<double>();
        }

        public DoubleList(List<double> theList)
        {
            TheList = theList ?? new List<double>();
        }

        public DoubleList DeepCopy()
        {
            return new DoubleList(TheList?.ToList());
        }

        public override bool Equals(object obj)
        {
            DoubleList other = obj as DoubleList;
            if (other == null)
                return TheList == null;
            if (TheList == null)
                return false;
            return TheList.SequenceEqual(other.TheList);
        }

        public override int GetHashCode()
        {
            return TheList?.GetSequenceHashCode() ?? 0;
        }
    }
}
