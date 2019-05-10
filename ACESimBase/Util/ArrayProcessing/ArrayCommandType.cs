using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    public enum ArrayCommandType : byte
    {
        Zero,
        CopyTo,
        NextSource,
        NextDestination,
        ReusedDestination,
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
        AfterGoTo,
        Blank,
    }
}
