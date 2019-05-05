using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    public readonly struct ArrayCommand
    {
        public readonly ArrayCommandType CommandType;
        public readonly int Index;
        public readonly int SourceIndex;

        public ArrayCommand(ArrayCommandType type, int index, int sourceIndex)
        {
            CommandType = type;
            Index = index;
            SourceIndex = sourceIndex;
        }

        public ArrayCommand WithIndex(int index) => new ArrayCommand(CommandType, index, SourceIndex);

        public ArrayCommand WithSourceIndex(int sourceIndex) => new ArrayCommand(CommandType, Index, sourceIndex);
    }
}
