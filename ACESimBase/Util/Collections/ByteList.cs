using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Util.Collections
{
    [Serializable]
    public class ByteList
    {
        public List<byte> TheList;

        public ByteList(IEnumerable<byte> listItems)
        {
            TheList = listItems?.ToList() ?? new List<byte>();
        }

        public ByteList(List<byte> theList)
        {
            TheList = theList ?? new List<byte>();
        }

        public ByteList DeepCopy()
        {
            return new ByteList(TheList?.ToList());
        }

        public override bool Equals(object obj)
        {
            ByteList other = obj as ByteList;
            if (other == null)
                return TheList == null;
            if (TheList == null)
                return false;
            return TheList.SequenceEqual(other.TheList);
        }
        public static bool operator ==(ByteList obj1, ByteList obj2)
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

            return obj1.Equals(obj2);
        }

        // this is second one '!='
        public static bool operator !=(ByteList obj1, ByteList obj2)
        {
            return !(obj1 == obj2);
        }

        public override int GetHashCode()
        {
            return TheList?.GetSequenceHashCode() ?? 0;
        }

        public override string ToString()
        {
            return string.Join(",", TheList);
        }
    }
}
