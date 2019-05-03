using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    public enum ArrayCommandType : byte
    {
        ZeroNew,
        CopyTo,
        MultiplyBy,
        IncrementBy,
        DecrementBy,
        MultiplyByInterlocked,
        IncrementByInterlocked,
        DecrementByInterlocked,
        EqualsOtherArrayIndex,
        NotEqualsOtherArrayIndex,
        GreaterThanOtherArrayIndex,
        LessThanOtherArrayIndex,
        EqualsValue,
        NotEqualsValue,
        GoTo,
        Blank
    }
}
