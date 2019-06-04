using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Util
{
    public class ByteList
    {
        public List<byte> TheList;

        public ByteList(IEnumerable<byte> listItems)
        {
            TheList = TheList.ToList();
        }

        public ByteList(List<byte> theList)
        {
            TheList = theList;
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

        public override int GetHashCode()
        {
            return TheList?.GetSequenceHashCode() ?? 0;
        }
    }
}
