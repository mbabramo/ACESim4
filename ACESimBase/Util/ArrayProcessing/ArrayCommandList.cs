using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace ACESimBase.Util.ArrayProcessing
{
    public class ArrayCommandList
    {
        public const bool UseInterlockingWhereRequested = false; // DEBUG // can disable interlocking if we know this won't be run in parallel

        public ArrayCommand[] UnderlyingCommands;
        public int[] ArrayIndexFirstUsed, ArrayIndexLastUsed;
        public int NextCommandIndex;
        public int InitialArrayIndex;
        public int NextArrayIndex;
        public int MaxArrayIndex;
        public int UnrollDepth;

        public ArrayCommandList(int maxNumCommands, int initialArrayIndex)
        {
            UnderlyingCommands = new ArrayCommand[maxNumCommands];
            ArrayIndexFirstUsed = new int[maxNumCommands]; // these are the max size that might be necessary
            ArrayIndexLastUsed = new int[maxNumCommands];
            InitialArrayIndex = NextArrayIndex = MaxArrayIndex = initialArrayIndex;
            MaxArrayIndex--;
            UnrollDepth = 0;
        }

        #region Array consolidation

        // Keep track of the first and last time an array index is used
        public void UpdateArrayIndexUsed(int arrayIndex)
        {
            if (arrayIndex == 7207)
            {
                var DEBUG = 0;
            }
            if (arrayIndex > 0)
            {
                if (ArrayIndexFirstUsed[arrayIndex] == 0)
                {
                    ArrayIndexFirstUsed[arrayIndex] = NextCommandIndex;
                }
                ArrayIndexLastUsed[arrayIndex] = NextCommandIndex;
            }
        }

        private void SpecifyResultIndices(IEnumerable<int> indices)
        {
            foreach (int i in indices)
                ArrayIndexLastUsed[i] = int.MaxValue; // we have never used this
        }

        public IEnumerable<int> ConsolidateArrayIndices(IEnumerable<int> resultIndices)
        {
            // the algorithm produces certain final results, in the form of indices into the array.
            // now that we are consolidating array indices created by the algorithm, we must translate
            // these.
            SpecifyResultIndices(resultIndices);
            Dictionary<int, int> translatedResultIndices = new Dictionary<int, int>();

            // go through command indices again. If we get to a command where an array is used for the last time, then we add it to a recycling queue. Then, when we get to a command where an array is used for the first time, if there is something available in the recycling queue, we use it. Meanwhile, we maintain a translation dictionary, so that we can translate.
            int lastArrayIndex = NextArrayIndex - 1;
            int lastCommandIndex = NextCommandIndex - 1;

            int nextArrayIndexToUse = InitialArrayIndex;

            Queue<int> recycling = new Queue<int>();
            Dictionary<int, int> translation = new Dictionary<int, int>();
            int[] indices = new int[2];
            int revisedMaxArrayIndex = -1;
            for (int c = 0; c <= lastCommandIndex; c++)
            {
                var command = UnderlyingCommands[c];
                if (command.CommandType != ArrayCommandType.GoTo)
                {
                    indices[0] = command.Index;
                    indices[1] = command.SourceIndex;
                    for (int i = 0; i <= 1; i++)
                    {
                        int originalIndexOrSourceIndex = indices[i];
                        if (originalIndexOrSourceIndex >= InitialArrayIndex)
                        { // we're only recycling the array indices created in the array command list
                            int firstUse = ArrayIndexFirstUsed[originalIndexOrSourceIndex];
                            int lastUse = ArrayIndexLastUsed[originalIndexOrSourceIndex];
                            if (c < firstUse || c > lastUse)
                                throw new Exception();
                            bool isFirstUse = firstUse == c;
                            bool isLastUse = lastUse == c;
                            if (isFirstUse)
                            {
                                //if (lastUse > firstUse + 1000)
                                //    System.Diagnostics.Debug.WriteLine($"first {firstUse} last {lastUse} difference {lastUse - firstUse} source {(i == 1 ? "Yes" : "No")}"); // DEBUG
                                if (isLastUse)
                                {
                                    // We never actually use this later in the command list, so this command has no effect and we can ignore it. Note that if this were a result of the algorithm as a whole, then it would not be designated as a last use.
                                    command = new ArrayCommand(ArrayCommandType.Blank, -1, -1);
                                    break;
                                }
                                if (recycling.Any())
                                {
                                    translation[originalIndexOrSourceIndex] = recycling.Dequeue();
                                }
                                else
                                    translation[originalIndexOrSourceIndex] = nextArrayIndexToUse++;
                                // if this is a result of the algorithm, we must record the translation fo the old result index into the new one
                                if (lastUse == int.MaxValue)
                                    translatedResultIndices[originalIndexOrSourceIndex] = translation[originalIndexOrSourceIndex];
                            }

                            if (translation.ContainsKey(originalIndexOrSourceIndex))
                            {
                                int translated = translation[originalIndexOrSourceIndex];
                                if (i == 0)
                                    command = command.WithIndex(translated);
                                else
                                    command = command.WithSourceIndex(translated);
                                if (isLastUse)
                                {
                                    translation.Remove(originalIndexOrSourceIndex);
                                    recycling.Enqueue(translated);
                                }
                            }
                            else if (isLastUse)
                            { // this was not recycled, but it now can be.
                                recycling.Enqueue(originalIndexOrSourceIndex);
                            }
                        }
                    }
                    UnderlyingCommands[c] = command;
                    if (command.Index > revisedMaxArrayIndex)
                        revisedMaxArrayIndex = command.Index;
                    if (command.SourceIndex > revisedMaxArrayIndex)
                        revisedMaxArrayIndex = command.SourceIndex;
                }
            }
            NextArrayIndex = nextArrayIndexToUse;
            MaxArrayIndex = revisedMaxArrayIndex;
            ArrayIndexFirstUsed = null;
            ArrayIndexLastUsed = null;
            return resultIndices.Select(x => translatedResultIndices[x]);
        }

        #endregion

        #region Commands

        private void AddCommand(ArrayCommand command)
        {
            if (NextCommandIndex == 0 && command.CommandType != ArrayCommandType.Blank)
                InsertBlankCommand();
            if (command.CommandType != ArrayCommandType.GoTo)
            {
                UpdateArrayIndexUsed(command.Index);
                UpdateArrayIndexUsed(command.SourceIndex);
            }
            UnderlyingCommands[NextCommandIndex] = command;
            NextCommandIndex++;
            if (NextArrayIndex > MaxArrayIndex)
                MaxArrayIndex = NextArrayIndex - 1;
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

        public int[] CopyToNew(int[] sourceIndices)
        {
            return sourceIndices.Select(x => CopyToNew(x)).ToArray();
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

        public void CopyToExisting(int[] indices, int[] sourceIndices)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                CopyToExisting(indices[i], sourceIndices[i]);
            }
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
            AddCommand(new ArrayCommand(interlocked && UseInterlockingWhereRequested ? ArrayCommandType.MultiplyByInterlocked : ArrayCommandType.MultiplyBy, index, indexOfMultiplier));
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
            AddCommand(new ArrayCommand(interlocked && UseInterlockingWhereRequested ? ArrayCommandType.IncrementByInterlocked : ArrayCommandType.IncrementBy, index, indexOfIncrement));
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
            AddCommand(new ArrayCommand(interlocked && UseInterlockingWhereRequested ? ArrayCommandType.DecrementByInterlocked : ArrayCommandType.DecrementBy, index, indexOfDecrement));
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

        #endregion

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
                        // DEBUG -- breaking it down this way reveals that about half of the time is from the array accesses (second is basically free) and half from multiplication
                        //double getTarget = array[command.Index];
                        //double getSource = array[command.SourceIndex];
                        //getTarget *= getSource;
                        //array[command.Index] = getTarget;
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
                    case ArrayCommandType.Blank:
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
