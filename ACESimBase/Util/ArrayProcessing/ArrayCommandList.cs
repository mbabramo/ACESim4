using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    public class ArrayCommandList
    {
        public ArrayCommand[] UnderlyingCommands;
        public int NextCommandIndex;
        public int NextArrayIndex;
        public double[] InitialArray;

        public ArrayCommandList(int maxNumCommands, double[] initialArray, int initialArrayIndex)
        {
            UnderlyingCommands = new ArrayCommand[maxNumCommands];
            InitialArray = initialArray;
            NextArrayIndex = initialArrayIndex;
        }

        private void AddCommand(ArrayCommand command)
        {
            UnderlyingCommands[NextCommandIndex] = command;
            if (InitialArray != null)
                ExecuteCommand(InitialArray, command);
            NextCommandIndex++;
        }

        // First, methods to create commands that use new spots in the array

        public int[] NewZeroArray(int arraySize)
        {
            int[] result = new int[arraySize];
            for (int i = 0; i < arraySize; i++)
                result[i] = NewZero();
            return result;
        }

        public int NewZero()
        {
            AddCommand(new ArrayCommand(ArrayCommandType.ZeroNew, NextArrayIndex, -1));
            return NextArrayIndex++;
        }

        public int CopyToNew(int sourceIndex)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, NextArrayIndex, sourceIndex));
            return NextArrayIndex++;
        }

        public int AddToNew(int index1, int index2)
        {
            int result = CopyToNew(index1);
            Increment(result, index2, false);
            return result;
        }

        public int MultiplyToNew(int index1, int index2)
        {
            int result = CopyToNew(index1);
            MultiplyBy(result, index2, false);
            return result;
        }

        // Next, methods that modify existing array items in place

        public void CopyToExisting(int index, int sourceIndex)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, index, sourceIndex));
        }

        public void MultiplyArrayBy(int[] indices, int indexOfMultiplier, bool interlocked)
        {
            for (int i = 0; i < indices.Length; i++)
                MultiplyBy(indices[i], indexOfMultiplier, interlocked);
        }

        public void MultiplyArrayBy(int[] indices, int[] indicesOfMultipliers, bool interlocked)
        {
            for (int i = 0; i < indices.Length; i++)
                MultiplyBy(indices[i], indicesOfMultipliers[i], interlocked);
        }

        public void MultiplyBy(int index, int indexOfMultiplier, bool interlocked)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.MultiplyBy, index, indexOfMultiplier));
        }

        public void IncrementArrayBy(int[] indices, int indexOfIncrement, bool interlocked)
        {
            for (int i = 0; i < indices.Length; i++)
                Increment(indices[i], indexOfIncrement, interlocked);
        }

        public void IncrementArrayBy(int[] indices, int[] indicesOfIncrements, bool interlocked)
        {
            for (int i = 0; i < indices.Length; i++)
                Increment(indices[i], indicesOfIncrements[i], interlocked);
        }

        public void Increment(int index, int indexOfIncrement, bool interlocked)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.IncrementBy, index, indexOfIncrement));
        }

        public void IncrementByProduct(int index, int indexOfIncrementProduct1, int indexOfIncrementProduct2, bool interlocked)
        {
            int spaceForProduct = CopyToNew(indexOfIncrementProduct1);
            MultiplyBy(spaceForProduct, indexOfIncrementProduct2, interlocked);
            Increment(index, spaceForProduct, interlocked);
            NextArrayIndex--; // we've set aside an array index to be used for this command. But we no longer need it, so we can now allocate it to some other purpose (e.g., incrementing by another product)
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
                case ArrayCommandType.CopyTo:
                    array[command.Index] = array[command.SourceIndex];
                    break;
                case ArrayCommandType.MultiplyBy:
                    array[command.Index] *= array[command.SourceIndex];
                    break;
                case ArrayCommandType.IncrementBy:
                    array[command.Index] += array[command.SourceIndex];
                    break;
                case ArrayCommandType.MultiplyByInterlocked:
                    Interlocking.Multiply(ref array[command.Index], array[command.SourceIndex]);
                    break;
                case ArrayCommandType.IncrementByInterlocked:
                    Interlocking.Add(ref array[command.Index], array[command.SourceIndex]);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
