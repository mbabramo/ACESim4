using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace ACESimBase.Util.Collections
{
    public static class SpanBitArray
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Set(ref byte aByte, int pos, bool value)
        {
            if (value)
            {
                //left-shift 1, then bitwise OR
                aByte = (byte)(aByte | 1 << pos);
            }
            else
            {
                //left-shift 1, then take complement, then bitwise AND
                aByte = (byte)(aByte & ~(1 << pos));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Get(byte aByte, int pos)
        {
            //left-shift 1, then bitwise AND, then check for non-zero
            return (aByte & 1 << pos) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Set(Span<byte> span, int pos, bool on)
        {
            int byteIndex = pos / 8;
            int bitInByte = pos % 8;

            byte b = span[byteIndex];
            Set(ref b, bitInByte, on);
            span[byteIndex] = b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Get(Span<byte> span, int pos)
        {
            int byteIndex = pos / 8;
            int bitInByte = pos % 8;

            byte b = span[byteIndex];
            return Get(b, bitInByte);
        }
    }
}
