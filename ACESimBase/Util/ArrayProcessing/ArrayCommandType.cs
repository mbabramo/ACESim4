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
        MultiplyByInterlocked,
        IncrementByInterlocked,
    }
}
