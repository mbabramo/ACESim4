using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim4.Util;

namespace ACESim
{
    public unsafe struct NWayTreeStorageKey : INWayTreeStorageKey
    {
        public byte PrefaceByte { get; set; }
        public byte* Sequence;

        public byte Element(int i) => Sequence[i];

        public NWayTreeStorageKey(byte prefaceByte, byte* sequence)
        {
            PrefaceByte = prefaceByte;
            Sequence = sequence;
        }

        public override string ToString() => PrefaceByte + ": " + String.Join(",", Util.ListExtensions.GetPointerAsList_255Terminated(Sequence));

        public override bool Equals(object obj)
        {
            INWayTreeStorageKey other = (INWayTreeStorageKey) obj;
            if (PrefaceByte != other.PrefaceByte)
                return false;
            int i = 0;
            while (*(Sequence + i) != 255 && other.Element(i) != 255)
            {
                if (*(Sequence + i) != other.Element(i))
                    return false;
                i++;
            }
            if (*(Sequence + i) != other.Element(i))
                return false;
            return true;
        }

        public override int GetHashCode()
        {
            return Crc32.ComputeChecksum(this);
        }

        public NWayTreeStorageKeySafe ToSafe()
        {
            return new NWayTreeStorageKeySafe() {PrefaceByte = PrefaceByte, Sequence = Util.ListExtensions.GetPointerAsList_255Terminated(Sequence).ToArray()};
        }
    }
}
