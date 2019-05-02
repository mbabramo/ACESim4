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

        public int AddSetToNewCommand(double value)
        {
            int index = CurrentCommandIndex;
            AddCommand(new ArrayCommand(ArrayCommandType.SetToNew, index, value));
            return index;
        }

        public int[] AddSetToNewCommands(double[] values)
        {
            int[] indices = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
                indices[i] = AddSetToNewCommand(values[i]);
            return indices;
        }

        public void AddMultiplyByCommand(int index, double value)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.MultiplyBy, index, value));
        }

        public void AddIncrementCommand(int index, double value)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.IncrementBy, index, value));
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
