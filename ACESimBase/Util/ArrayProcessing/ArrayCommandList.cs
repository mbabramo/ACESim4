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
        public int MaxArrayIndex;
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
            NextCommandIndex++;
            if (NextArrayIndex > MaxArrayIndex)
                MaxArrayIndex = NextArrayIndex;
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

        public void DecrementArrayBy(int[] indices, int indexOfDecrement, bool interlocked)
        {
            for (int i = 0; i < indices.Length; i++)
                Decrement(indices[i], indexOfDecrement, interlocked);
        }

        public void DecrementArrayBy(int[] indices, int[] indicesOfDecrements, bool interlocked)
        {
            for (int i = 0; i < indices.Length; i++)
                Decrement(indices[i], indicesOfDecrements[i], interlocked);
        }

        public void Decrement(int index, int indexOfDecrement, bool interlocked)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.DecrementBy, index, indexOfDecrement));
        }

        public void DecrementByProduct(int index, int indexOfDecrementProduct1, int indexOfDecrementProduct2, bool interlocked)
        {
            int spaceForProduct = CopyToNew(indexOfDecrementProduct1);
            MultiplyBy(spaceForProduct, indexOfDecrementProduct2, interlocked);
            Decrement(index, spaceForProduct, interlocked);
            NextArrayIndex--; // we've set aside an array index to be used for this command. But we no longer need it, so we can now allocate it to some other purpose (e.g., Decrementing by another product)
        }

        // Flow control. We do flow control by a combination of comparison commands and go to commands. When a comparison is made, if the comparison fails, the next command is skipped. Thus, the combination of the comparison and the go to command ensures that the go to command will be obeyed only if the comparison succeeds.

        public void InsertEqualsOtherArrayIndexCommand(int index1, int index2)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.EqualsOtherArrayIndex, index1, index2));
        }

        public void InsertNotEqualsOtherArrayIndexCommand(int index1, int index2)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.NotEqualsOtherArrayIndex, index1, index2));
        }

        public void InsertGreaterThanOtherArrayIndexCommand(int index1, int index2)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.GreaterThanOtherArrayIndex, index1, index2));
        }

        public void InsertLessThanOtherArrayIndexCommand(int index1, int index2)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.LessThanOtherArrayIndex, index1, index2));
        }

        /// <summary>
        /// Inserts a command to make a comparison between the item at index1 and an integral value that is provided. This can be useful to achieve iteration over an index variable.
        /// </summary>
        /// <param name="index1"></param>
        /// <param name="valueToCompareTo"></param>
        public void InsertEqualsValueCommand(int index1, int valueToCompareTo)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.EqualsValue, index1, valueToCompareTo));
        }

        public void InsertNotEqualsValueCommand(int index1, int valueToCompareTo)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.NotEqualsValue, index1, valueToCompareTo));
        }

        public void InsertGoToCommand(int commandIndexToReplace, int commandIndexToGoTo)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.GoTo, commandIndexToGoTo, -1 /* ignored */));
        }

        /// <summary>
        ///  Inserts a blank command. This is useful to demarcate a command spot, which might be a spot to go to later or which might later be replaced with a goto command once the goto location is determined.
        /// </summary>
        /// <returns>The command index (not an array index, as with most comamnds)</returns>
        public int InsertBlankCommand()
        {
            int commandIndex = NextCommandIndex; // not the array index
            AddCommand(new ArrayCommand(ArrayCommandType.Blank, -1, -1));
            return commandIndex;
        }

        /// <summary>
        /// Replaces a command (ordinarily a blank command) with a go to command, thus allowing the client code to add statements once the goto is complete.
        /// </summary>
        /// <param name="commandIndexToReplace">The command index to replace (ordinarily representing a blank command)</param>
        /// <param name="commandIndexToGoTo">The command index to go to (or, by default, the next command)</param>
        public void ReplaceCommandWithGoToCommand(int commandIndexToReplace, int commandIndexToGoTo = -1)
        {
            if (commandIndexToGoTo == -1)
                commandIndexToGoTo = NextCommandIndex;
            UnderlyingCommands[commandIndexToReplace] = new ArrayCommand(ArrayCommandType.GoTo, commandIndexToGoTo, -1 /* ignored */);
        }

        #region Execution

        public void ExecuteAll(double[] array)
        {
            int MaxCommandIndex = NextCommandIndex;
            bool skipNext;
            int goTo;
            for (NextCommandIndex = 0; NextCommandIndex < MaxCommandIndex; NextCommandIndex++)
            {
                ArrayCommand command = UnderlyingCommands[NextCommandIndex];
                skipNext = false;
                goTo = -1;
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
                    case ArrayCommandType.DecrementBy:
                        array[command.Index] -= array[command.SourceIndex];
                        break;
                    case ArrayCommandType.MultiplyByInterlocked:
                        Interlocking.Multiply(ref array[command.Index], array[command.SourceIndex]);
                        break;
                    case ArrayCommandType.IncrementByInterlocked:
                        Interlocking.Add(ref array[command.Index], array[command.SourceIndex]);
                        break;
                    case ArrayCommandType.DecrementByInterlocked:
                        Interlocking.Subtract(ref array[command.Index], array[command.SourceIndex]);
                        break;
                    case ArrayCommandType.EqualsOtherArrayIndex:
                        bool conditionMet = array[command.Index] == array[command.SourceIndex];
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    case ArrayCommandType.NotEqualsOtherArrayIndex:
                        conditionMet = array[command.Index] != array[command.SourceIndex];
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    case ArrayCommandType.GreaterThanOtherArrayIndex:
                        conditionMet = array[command.Index] > array[command.SourceIndex];
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    case ArrayCommandType.LessThanOtherArrayIndex:
                        conditionMet = array[command.Index] < array[command.SourceIndex];
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    case ArrayCommandType.EqualsValue:
                        conditionMet = array[command.Index] == command.SourceIndex;
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    case ArrayCommandType.NotEqualsValue:
                        conditionMet = array[command.Index] != command.SourceIndex;
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    case ArrayCommandType.GoTo:
                        goTo = command.Index - 1; // because we are going to increment in the for loop
                        break;
                    default:
                        throw new NotImplementedException();
                }
                if (skipNext)
                    NextCommandIndex++; // in addition to increment in for statement
                else if (goTo != -1)
                    NextCommandIndex = goTo;
            }
        }

        /// <summary>
        /// Executes a command. Returns true if the next command should be skipped.
        /// </summary>
        /// <param name="array"></param>
        /// <param name="command"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ExecuteCommand(double[] array, in ArrayCommand command, ref bool skipNext, ref int goTo)
        {
            
        }

        #endregion
    }
}
