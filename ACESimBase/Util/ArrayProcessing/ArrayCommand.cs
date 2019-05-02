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
        public readonly int SourceIndex;

        public ArrayCommand(ArrayCommandType type, int index, double value)
        {
            CommandType = type;
            Index = index;
            Value = value;
            SourceIndex = default;
        }

        public ArrayCommand(ArrayCommandType type, int index, int sourceIndex)
        {
            CommandType = type;
            Index = index;
            Value = default;
            SourceIndex = sourceIndex;
        }
    }
}
