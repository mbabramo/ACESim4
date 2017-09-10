using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim4.Util;

namespace ACESim
{
    public unsafe struct NWayTreeStorageKey
    {
        public byte PrefaceByte;
        public byte* Sequence;
        public static uint[] Table;

        public NWayTreeStorageKey(byte prefaceByte, byte* sequence)
        {
            PrefaceByte = prefaceByte;
            Sequence = sequence;
        }

        public override bool Equals(object obj)
        {
            NWayTreeStorageKey other = (NWayTreeStorageKey) obj;
            if (PrefaceByte != other.PrefaceByte)
                return false;
            byte* ptr1 = Sequence;
            byte* ptr2 = other.Sequence;
            while (*ptr1 != 255 && *ptr2 != 255)
            {
                if (*ptr1 != *ptr2)
                    return false;
                ptr1++;
                ptr2++;
            }
            if (*ptr1 != *ptr2)
                return false;
            return true;
        }

        public override int GetHashCode()
        {
            return Crc32.ComputeChecksum(this);
        }
    }
}
