using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util
{
    public static class SpanBitArray
    {
        private static void Set(ref byte aByte, int pos, bool value)
        {
            if (value)
            {
                //left-shift 1, then bitwise OR
                aByte = (byte)(aByte | (1 << pos));
            }
            else
            {
                //left-shift 1, then take complement, then bitwise AND
                aByte = (byte)(aByte & ~(1 << pos));
            }
        }

        private static bool Get(byte aByte, int pos)
        {
            //left-shift 1, then bitwise AND, then check for non-zero
            return ((aByte & (1 << pos)) != 0);
        }

        public static void Set(Span<byte> span, int pos, bool on)
        {
            int byteIndex = pos / 8;
            int bitInByte = pos % 8;

            byte b = span[byteIndex];
            Set(ref b, bitInByte, on);
        }

        public static bool Get(Span<byte> span, int pos)
        {
            int byteIndex = pos / 8;
            int bitInByte = pos % 8;

            byte b = span[byteIndex];
            return Get(b, pos);
        }
    }
}
