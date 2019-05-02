using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    public class ArrayCommandList
    {
        const int MaxNumCommands = 10_000_000;
        public ArrayCommand[] UnderlyingCommands;
        int CurrentCommandIndex;

        public ArrayCommandList()
        {
            UnderlyingCommands = new ArrayCommand[MaxNumCommands];
        }

        public void AddCommand(ArrayCommand command)
        {
            UnderlyingCommands[CurrentCommandIndex++] = command;
        }

        public int AddCopyToNewCommand(int sourceIndex)
        {
            int index = CurrentCommandIndex;
            AddCommand(new ArrayCommand(ArrayCommandType.CopyToNew, index, sourceIndex));
            return index;
        }

        public void AddMultiplyByCommand(int index, int sourceIndex)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.MultiplyBy, index, sourceIndex));
        }

        public void AddIncrementCommand(int index, int sourceIndex)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.IncrementBy, index, sourceIndex));
        }

        public void ExecuteAll(double[] array)
        {
            int MaxCommandIndex = CurrentCommandIndex;
            for (CurrentCommandIndex = 0; CurrentCommandIndex < MaxCommandIndex; CurrentCommandIndex++)
            {
                ArrayCommand command = UnderlyingCommands[CurrentCommandIndex];
                switch (command.CommandType)
                {
                    case ArrayCommandType.CopyToNew:
                        array[command.Index] = command.SourceIndex;
                        break;
                    case ArrayCommandType.MultiplyBy:
                        array[command.Index] *= array[command.SourceIndex];
                        break;
                    case ArrayCommandType.IncrementBy:
                        array[command.Index] += array[command.SourceIndex];
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

        }
    }
}
