using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ACESimBase.Util.NWayTreeStorage;

namespace ACESimBase.Util.Serialization
{
    public static class Crc32
    {
        static uint[] table;
        private static object lockobj = new object();

        public static uint ComputeChecksum(byte[] bytes)
        {
            uint crc = 0xffffffff;
            for (int i = 0; i < bytes.Length; ++i)
            {
                byte index = (byte)(crc & 0xff ^ bytes[i]);
                crc = crc >> 8 ^ table[index];
            }
            return ~crc;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeChecksum(NWayTreeStorageKey key)
        {
            uint crc = 0xffffffff;
            byte index = (byte)(crc & 0xff ^ key.PrefaceByte);
            crc = crc >> 8 ^ table[index];
            for (int i = 0; i < key.Sequence.Length; i++)
            {
                index = (byte)(crc & 0xff ^ key.Sequence[i]);
                crc = crc >> 8 ^ table[index];
            }
            return unchecked((int)~crc); // keep bits
        }


        public static byte[] ComputeChecksumBytes(byte[] bytes)
        {
            return BitConverter.GetBytes(ComputeChecksum(bytes));
        }

        public static void InitializeIfNecessary()
        {
            if (table != null)
                return;
            lock (lockobj)
            {
                if (table != null)
                    return;
                uint poly = 0xedb88320;
                table = new uint[256];
                uint temp = 0;
                for (uint i = 0; i < table.Length; ++i)
                {
                    temp = i;
                    for (int j = 8; j > 0; --j)
                    {
                        if ((temp & 1) == 1)
                        {
                            temp = temp >> 1 ^ poly;
                        }
                        else
                        {
                            temp >>= 1;
                        }
                    }
                    table[i] = temp;
                }
            }
        }
    }
}
