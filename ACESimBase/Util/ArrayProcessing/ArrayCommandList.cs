using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    public class ArrayCommandList
    {
        const int MaxNumCommands = 1_000_000;
        public ArrayCommand[] UnderlyingCommands;
        int CurrentCommand;

        public ArrayCommandList()
        {
            UnderlyingCommands = new ArrayCommand[MaxNumCommands];
        }

        public void AddCommand(ArrayCommand command)
        {
            UnderlyingCommands[CurrentCommand++] = command;
        }

        public void ExecuteAll(ArrayProcessor arrayProcessor)
        {
            int MaxCommand = CurrentCommand;
            for (CurrentCommand = 0; CurrentCommand < MaxCommand; CurrentCommand++)
            {
                ArrayCommand command = UnderlyingCommands[CurrentCommand];
                switch (command.CommandType)
                {
                    case ArrayCommandType.Copy:
                        arrayProcessor.CopyToIndex(command.Index, command.AdditionalIndex);
                        break;
                    case ArrayCommandType.MultipyBy:
                        arrayProcessor.MultiplyBy(command.Index, command.Value);
                        break;
                    case ArrayCommandType.IncrementBy:
                        arrayProcessor.IncrementBy(command.Index, command.Value);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

        }
    }
}
