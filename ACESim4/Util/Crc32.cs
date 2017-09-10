using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ACESim;

namespace ACESim4.Util
{
    using System;
    public static class Crc32
    {
        static uint[] table;
        private static object lockobj = new object();

        public static uint ComputeChecksum(byte[] bytes)
        {
            uint crc = 0xffffffff;
            for (int i = 0; i < bytes.Length; ++i)
            {
                byte index = (byte)(((crc) & 0xff) ^ bytes[i]);
                crc = (uint)((crc >> 8) ^ table[index]);
            }
            return ~crc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static int ComputeChecksum(NWayTreeStorageKey key)
        {
            uint crc = 0xffffffff;
            byte* ptr1 = key.Sequence;
            byte index = (byte)(((crc) & 0xff) ^ key.PrefaceByte);
            crc = (uint)((crc >> 8) ^ table[index]);
            while (*ptr1 != 255)
            {
                index = (byte)(((crc) & 0xff) ^ *ptr1);
                crc = (uint)((crc >> 8) ^ table[index]);
                ptr1++;
            }
            return unchecked((int)~crc); // keep bits
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeChecksum(NWayTreeStorageKeySafe key)
        {
            uint crc = 0xffffffff;
            byte index = (byte)(((crc) & 0xff) ^ key.PrefaceByte);
            crc = (uint)((crc >> 8) ^ table[index]);
            for (int i = 0; i < key.Sequence.Length; i++)
            {
                index = (byte)(((crc) & 0xff) ^ key.Sequence[i]);
                crc = (uint)((crc >> 8) ^ table[index]);
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
                            temp = (uint) ((temp >> 1) ^ poly);
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
