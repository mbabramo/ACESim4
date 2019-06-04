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
