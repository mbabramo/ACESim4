using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    [Serializable]
    public readonly struct ArrayCommand
    {
        public readonly ArrayCommandType CommandType;
        public readonly int Index;
        public readonly int SourceIndex;

        public ArrayCommand(ArrayCommandType type, int index, int sourceIndex)
        {
            CommandType = type;
            if ((index < -1 || sourceIndex < -1) && !(index == ArrayCommandList.CheckpointTrigger))
                throw new Exception("Invalid index or source index. This may occur when decrementing, using ordered sources and destinations. It indicates that an index int the original array was used directly. Instead, all sources in the initial array must be copied using CopyToNew, and then destinations must be written to using Increment. Determine which command index this is by looking in the call stack; then intercept this command being written in AddCommand and figure out the source of the array index and change it so that it is copied.");
            Index = index;
            SourceIndex = sourceIndex;
        }

        public override string ToString()
        {
            return $"{CommandType} {Index} source:{SourceIndex}";
        }

        public ArrayCommand Clone() => new ArrayCommand(CommandType, Index, SourceIndex);

        public int GetSourceIndexIfUsed()
        {
            if (CommandType == ArrayCommandType.If || CommandType == ArrayCommandType.EndIf || CommandType == ArrayCommandType.EqualsValue || CommandType == ArrayCommandType.NotEqualsValue)
                return -1;
            return SourceIndex;
        }

        public int GetTargetIndexIfUsed()
        {
            if (CommandType == ArrayCommandType.If || CommandType == ArrayCommandType.EndIf || CommandType == ArrayCommandType.ReusedDestination)
                return -1;
            return Index;
        }
        public static void VerifyCorrectnessRange(ArrayCommand[] commands, int start, int endExclusive)
        {
            return; // DEBUG

            int ifs = 0, endIfs = 0;
            int incDepths = 0, decDepths = 0;

            bool IsComparison(ArrayCommandType t) => t switch
            {
                ArrayCommandType.EqualsOtherArrayIndex or
                ArrayCommandType.NotEqualsOtherArrayIndex or
                ArrayCommandType.GreaterThanOtherArrayIndex or
                ArrayCommandType.LessThanOtherArrayIndex or
                ArrayCommandType.EqualsValue or
                ArrayCommandType.NotEqualsValue => true,
                _ => false
            };

            for (int idx = start; idx < endExclusive; idx++)
            {
                var t = commands[idx].CommandType;

                switch (t)
                {
                    case ArrayCommandType.If: ifs++; break;
                    case ArrayCommandType.EndIf: endIfs++; break;
                    case ArrayCommandType.IncrementDepth: incDepths++; break;
                    case ArrayCommandType.DecrementDepth: decDepths++; break;
                }

                if (IsComparison(t))
                {
                    bool followedByIf =
                        idx + 1 < endExclusive &&
                        commands[idx + 1].CommandType == ArrayCommandType.If;

                    if (!followedByIf)
                        throw new InvalidOperationException(
                            $"Comparison at command {idx} is not immediately followed by an If.");
                }
            }

            if (ifs != endIfs)
                throw new InvalidOperationException($"Mismatch: If={ifs}  EndIf={endIfs}");

            if (incDepths != decDepths)
                throw new InvalidOperationException(
                    $"Mismatch: IncrementDepth={incDepths}  DecrementDepth={decDepths}");
        }


        public ArrayCommand WithIndex(int index) => new ArrayCommand(CommandType, index, SourceIndex);

        public ArrayCommand WithSourceIndex(int sourceIndex) => new ArrayCommand(CommandType, Index, sourceIndex);

        public override bool Equals(object obj)
        {
            ArrayCommand ac = (ArrayCommand)obj;
            return ac.CommandType == CommandType && ac.Index == Index && ac.SourceIndex == SourceIndex;
        }
        public static bool operator ==(ArrayCommand command1, ArrayCommand command2)
        {
            return command1.Equals(command2);
        }
        public static bool operator !=(ArrayCommand command1, ArrayCommand command2)
        {
            return !command1.Equals(command2);
        }

        public override int GetHashCode()
        {
            return (CommandType, Index, SourceIndex).GetHashCode();
        }
    }
}
