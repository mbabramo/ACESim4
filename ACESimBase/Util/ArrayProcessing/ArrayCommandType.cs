using System;
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
        IncrementDepth,
        DecrementDepth,
        Comment,
        Blank,
        Checkpoint,
        NextDestination,
    }
}
