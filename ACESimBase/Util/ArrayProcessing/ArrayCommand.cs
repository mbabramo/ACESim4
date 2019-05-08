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
                throw new Exception("Invalid index or source index. This may occur when decrementing, using ordered sources and destinations. It indicates that an index int the original array was used directly. Instead, all sources in the initial array must be copied using CopyToNew, and then destinations must be written to using Increment. Determine which command index this is by looking in the call stack; then intercept this command being written in AddCommand and figure out the source of the array index and change it so that it is copied.");
            if (type == ArrayCommandType.MultiplyBy && sourceIndex == -1)
                throw new Exception("DEBUG");
            Index = index;
            SourceIndex = sourceIndex;
        }

        public override string ToString()
        {
            return $"{CommandType} {Index} source:{SourceIndex}";
        }

        public ArrayCommand Clone() => new ArrayCommand(CommandType, Index, SourceIndex);

        public ArrayCommand WithArrayIndexDecrements(int decrement)
        {
            if (CommandType == ArrayCommandType.GoTo || CommandType == ArrayCommandType.AfterGoTo)
                return Clone();
            if (CommandType == ArrayCommandType.EqualsValue || CommandType == ArrayCommandType.NotEqualsValue) // source index does not represent an array index
                return new ArrayCommand(CommandType, Index == -1 ? -1 : Index - decrement, SourceIndex); 
            if (CommandType == ArrayCommandType.ReusedDestination) // index does not represent an array index
                return new ArrayCommand(CommandType, Index, SourceIndex == -1 ? -1 : SourceIndex - decrement);
            return new ArrayCommand(CommandType, Index == -1 ? -1 : Index - decrement, SourceIndex == -1 ? -1 : SourceIndex - decrement);
        }

        public ArrayCommand WithIndex(int index) => new ArrayCommand(CommandType, index, SourceIndex);

        public ArrayCommand WithSourceIndex(int sourceIndex) => new ArrayCommand(CommandType, Index, sourceIndex);
    }
}
