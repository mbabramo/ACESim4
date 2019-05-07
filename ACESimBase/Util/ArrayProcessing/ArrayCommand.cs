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
            if (index < -1 || sourceIndex < -1)
                throw new Exception();
            Index = index;
            SourceIndex = sourceIndex;
        }

        public override string ToString()
        {
            return $"{CommandType} {Index} source:{SourceIndex}";
        }

        public ArrayCommand WithDecrements(int decrement) => new ArrayCommand(CommandType, Index == -1 ? -1 : Index - decrement, SourceIndex == -1 ? -1 : SourceIndex - decrement);

        public ArrayCommand WithIndex(int index) => new ArrayCommand(CommandType, index, SourceIndex);

        public ArrayCommand WithSourceIndex(int sourceIndex) => new ArrayCommand(CommandType, Index, sourceIndex);
    }
}
