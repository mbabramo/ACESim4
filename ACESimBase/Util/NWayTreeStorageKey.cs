using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;

namespace ACESim
{
    public readonly struct NWayTreeStorageKey
    {
        public readonly byte PrefaceByte;
        public readonly byte[] Sequence;

        public NWayTreeStorageKey(byte prefaceByte, byte[] sequence)
        {
            PrefaceByte = prefaceByte;
            Sequence = sequence;
        }

        [ThreadStatic]
        private static NWayTreeStorageKey _KeyForThread;
        public static NWayTreeStorageKey KeyForThread
        {
            get
            {
                // We do the initialization here because we must do it once for each thread.
                if (_KeyForThread.Sequence == null)
                    _KeyForThread = new NWayTreeStorageKey(0, new byte[256]);
                return _KeyForThread;
            }
            set
            {
                _KeyForThread = value;
            }
        } 

        public byte Element(int i) => i == Sequence.Length ? (byte) 255 : Sequence[i];

        public override string ToString() => PrefaceByte + ": " + String.Join(",", Sequence);

        public override bool Equals(object obj)
        {
            NWayTreeStorageKey other = (NWayTreeStorageKey)obj;
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
