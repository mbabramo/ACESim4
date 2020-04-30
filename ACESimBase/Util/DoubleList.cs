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

        public override string ToString()
        {
            return String.Join(",", TheList);
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

        public static bool operator ==(DoubleList obj1, DoubleList obj2)
        {
            if (ReferenceEquals(obj1, obj2))
            {
                return true;
            }

            if (ReferenceEquals(obj1, null))
            {
                return false;
            }
            if (ReferenceEquals(obj2, null))
            {
                return false;
            }

            return (obj1.Equals(obj2));
        }

        // this is second one '!='
        public static bool operator !=(DoubleList obj1, DoubleList obj2)
        {
            return !(obj1 == obj2);
        }

        public override int GetHashCode()
        {
            return TheList?.GetSequenceHashCode() ?? 0;
        }
    }
}
