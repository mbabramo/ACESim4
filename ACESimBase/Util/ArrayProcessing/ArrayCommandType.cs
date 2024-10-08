﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    [Serializable]
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
        EqualsOtherArrayIndex,
        NotEqualsOtherArrayIndex,
        GreaterThanOtherArrayIndex,
        LessThanOtherArrayIndex,
        EqualsValue,
        NotEqualsValue,
        If,
        EndIf,
        Blank,
    }
}
