using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;

namespace ACESim
{
    public struct NWayTreeStorageKeySafe : INWayTreeStorageKey
    {
        public byte PrefaceByte { get; set; }
        public byte[] Sequence;

        public byte Element(int i) => i == Sequence.Length ? (byte) 255 : Sequence[i];

        public override string ToString() => PrefaceByte + ": " + String.Join(",", Sequence);

        public override bool Equals(object obj)
        {
            INWayTreeStorageKey other = (INWayTreeStorageKey)obj;
            if (PrefaceByte != other.PrefaceByte)
                return false;
            int i = 0;
            while (Element(i) != 255 && other.Element(i) != 255)
            {
                if (Element(i) != other.Element(i))
                    return false;
                i++;
            }
            if (Element(i) != other.Element(i))
                return false;
            return true;
        }

        public override int GetHashCode()
        {
            return Crc32.ComputeChecksum(this);
        }
    }
}
