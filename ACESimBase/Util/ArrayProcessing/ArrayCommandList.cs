using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    public class ArrayCommandList
    {
        public ArrayCommand[] UnderlyingCommands;
        int NextCommandIndex;
        public double[] InitialArray;

        public ArrayCommandList(int maxNumCommands, double[] initialArray)
        {
            UnderlyingCommands = new ArrayCommand[maxNumCommands];
            InitialArray = initialArray;
        }

        private void AddCommand(ArrayCommand command)
        {
            UnderlyingCommands[NextCommandIndex] = command;
            if (InitialArray != null)
                ExecuteCommand(InitialArray, command);
            NextCommandIndex++;
        }

        public int[] NewZeroArray(int arraySize)
        {
            int[] result = new int[arraySize];
            for (int i = 0; i < arraySize; i++)
                result[i] = NewZero();
            return result;
        }

        public int NewZero()
        {
            int index = NextCommandIndex;
            AddCommand(new ArrayCommand(ArrayCommandType.ZeroNew, index, -1));
            return index;
        }

        public int CopyToNew(int sourceIndex)
        {
            int index = NextCommandIndex;
            AddCommand(new ArrayCommand(ArrayCommandType.CopyToNew, index, sourceIndex));
            return index;
        }

        public void MultiplyArrayBy(int[] indices, int indexOfMultiplier)
        {
            for (int i = 0; i < indices.Length; i++)
                MultiplyBy(indices[i], indexOfMultiplier);
        }

        public void MultiplyArrayBy(int[] indices, int[] indicesOfMultipliers)
        {
            for (int i = 0; i < indices.Length; i++)
                MultiplyBy(indices[i], indicesOfMultipliers[i]);
        }

        public void MultiplyBy(int index, int indexOfMultiplier)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.MultiplyBy, index, indexOfMultiplier));
        }
        public void IncrementArrayBy(int[] indices, int indexOfIncrement)
        {
            for (int i = 0; i < indices.Length; i++)
                Increment(indices[i], indexOfIncrement);
        }

        public void IncrementArrayBy(int[] indices, int[] indicesOfIncrements)
        {
            for (int i = 0; i < indices.Length; i++)
                Increment(indices[i], indicesOfIncrements[i]);
        }

        public void Increment(int index, int indexOfIncrement)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.IncrementBy, index, indexOfIncrement));
        }

        public void ExecuteAll(double[] array)
        {
            int MaxCommandIndex = NextCommandIndex;
            for (NextCommandIndex = 0; NextCommandIndex < MaxCommandIndex; NextCommandIndex++)
            {
                ArrayCommand command = UnderlyingCommands[NextCommandIndex];
                ExecuteCommand(array, command);
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ExecuteCommand(double[] array, in ArrayCommand command)
        {
            switch (command.CommandType)
            {
                case ArrayCommandType.ZeroNew:
                    array[command.Index] = 0;
                    break;
                case ArrayCommandType.CopyToNew:
                    array[command.Index] = array[command.SourceIndex];
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
