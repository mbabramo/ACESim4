using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace ACESimBase.Util
{

    public class Bytes50
    {
        public byte[] b = new byte[50];

        public Bytes50()
        {

        }

        int maxIndex;

        public byte this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetByte(index);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                SetByte(index, value);
                if (index > maxIndex)
                    maxIndex = index;
            }
        }

        byte GetByte(int index) => b[index];

        void SetByte(int index, byte value)
        {
            b[index] = value;
        }

        public override bool Equals(object obj)
        {
            Bytes50 other = (Bytes50) obj;
            return this == other;
        }

        public static bool operator ==(Bytes50 obj1, Bytes50 obj2)
        {
            int maxIndex = Math.Max(obj1.maxIndex, obj2.maxIndex);
            for (int i = 0; i <= maxIndex; i++)
                if (obj1.b[i] != obj2.b[i])
                    return false;
            return true;
        }

        // this is second one '!='
        public static bool operator !=(Bytes50 obj1, Bytes50 obj2)
        {
            return !(obj1 == obj2);
        }

        public override int GetHashCode()
        {
            const int seedValue = 0x2D2816FE;
            const int primeNumber = 397;
            int value = seedValue;
            unchecked
            {
                for (int i = 0; i <= maxIndex; i++)
                    value = seedValue * primeNumber + b[i];
            }
            return value;
        }

    }
}
