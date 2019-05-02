using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    public readonly struct ArrayCommand
    {
        public readonly ArrayCommandType CommandType;
        public readonly int Index;
        public readonly double Value;
        public readonly int AdditionalIndex;
    }
}
