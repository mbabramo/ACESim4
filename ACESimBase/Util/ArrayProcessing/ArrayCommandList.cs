using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    public class ArrayCommandList
    {
        const int MaxNumCommands = 1_000_000;
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

        public int AddCopyToCommand(int sourceIndex)
        {
            int index = CurrentCommandIndex;
            AddCommand(new ArrayCommand(ArrayCommandType.Copy, index, sourceIndex));
            return index;
        }

        public int AddMultiplyByCommand(double value)
        {
            int index = CurrentCommandIndex;
            AddCommand(new ArrayCommand(ArrayCommandType.MultipyBy, index, value));
            return index;
        }

        public int AddIncrementCommand(double value)
        {
            int index = CurrentCommandIndex;
            AddCommand(new ArrayCommand(ArrayCommandType.IncrementBy, index, value));
            return index;
        }

        public void ExecuteAll(double[] array)
        {
            int MaxCommandIndex = CurrentCommandIndex;
            for (CurrentCommandIndex = 0; CurrentCommandIndex < MaxCommandIndex; CurrentCommandIndex++)
            {
                ArrayCommand command = UnderlyingCommands[CurrentCommandIndex];
                switch (command.CommandType)
                {
                    case ArrayCommandType.Copy:
                        array[command.Index] = command.SourceIndex;
                        break;
                    case ArrayCommandType.MultipyBy:
                        array[command.Index] *= command.Value;
                        break;
                    case ArrayCommandType.IncrementBy:
                        array[command.Index] += command.Value;
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

        }
    }
}
