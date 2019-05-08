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

        public ArrayCommand[] UnderlyingCommands;
        public int NextCommandIndex;
        public int InitialArrayIndex;
        public int NextArrayIndex;
        public int MaxArrayIndex;

        // Ordered sources: We initially develop a list of indices of the data passed to the algorithm each iteration. Before each iteration, we copy the data corresponding to these indices into the OrderedSources array in the order in which it will be needed. A command that otherwise would copy from the original data instead loads the next item in ordered sources. This may slightly improve performance because a sequence of original data will be cached. More importantly, it can improve parallelism: When a player chooses among many actions that are structurally equivalent (that is, they do not change how the game is played from that point on), we can run the same code with different slices of the OrderedSources array.
        public bool UseOrderedSources = true;
        public List<int> OrderedSourceIndices;
        public double[] OrderedSources;
        // Ordered destinations: Similarly, when the unrolled algorithm changes the data passed to it (for example, incrementing regrets in CFR), instead of directly incrementing the data, we develop in advance a list of the indices that will be changed. Then, when running the algorithm, we store the actual data that needs to be changed in an array, and on completion of the algorithm, we run through that array and change the data at the specified index for each item. This enhances parallelism because we don't have to lock around each data change, instead locking only around the final set of changes. This also may facilitate spreading the algorithm across machines, since each CPU can simply report the set of changes to make.
        public bool UseOrderedDestinations = true;
        public List<int> OrderedDestinationIndices;
        public double[] OrderedDestinations;
        public bool ReuseDestinations = true; // if true, then we will not add a new ordered destination index for a destination location already used within code executed not in parallel. Instead, we will just increment the previous destination.
        public Dictionary<int, int> ReusableOrderedDestinationIndices;
        public bool Parallelize;
        public bool InterlockWhereModifyingInitialSource => Parallelize && !UseOrderedDestinations;

        public bool DoNotReuseArrayIndices = false;
        public Stack<int> PerDepthStartArrayIndices;

        public ArrayCommandList(int maxNumCommands, int initialArrayIndex, bool parallelize)
        {
            UnderlyingCommands = new ArrayCommand[maxNumCommands];
            OrderedSourceIndices = new List<int>();
            OrderedDestinationIndices = new List<int>();
            ReusableOrderedDestinationIndices = new Dictionary<int, int>();
            InitialArrayIndex = NextArrayIndex = MaxArrayIndex = initialArrayIndex;
            MaxArrayIndex--;
            PerDepthStartArrayIndices = new Stack<int>();
            Parallelize = parallelize;
        }

        #region Depth management

        // We are simulating a stack. When entering a new depth level, we remember the next array index. Then, when exiting this depth level, we revert to this array index. A consequence of this is that depth i + 1 can return values to depth <= i only by copying to array indices already set at this earlier depth. 

        public void IncrementDepth()
        {
            PerDepthStartArrayIndices.Push(NextArrayIndex);
        }

        public void DecrementDepth(bool completeCommandList = false)
        {
            NextArrayIndex = PerDepthStartArrayIndices.Pop();
            if (!PerDepthStartArrayIndices.Any() && completeCommandList)
                CompleteCommandList();
        }

        public void CompleteCommandList()
        {
            if (UseOrderedSources && UseOrderedDestinations)
            {
                // The input array will no longer be needed when processing commands. Thus, we should instead adjust indices so that all indices refer to a smaller array, consisting only of the virtual stack.
                for (int i = 0; i < NextCommandIndex; i++)
                {
                    UnderlyingCommands[i] = UnderlyingCommands[i].WithArrayIndexDecrements(InitialArrayIndex);
                }
            }
        }

        #endregion

        #region Commands

        private void AddCommand(ArrayCommand command)
        {
            if (NextCommandIndex == 129829)
            {
                var DEBUG = 0;
            }
            if (NextCommandIndex == 0 && command.CommandType != ArrayCommandType.Blank)
                InsertBlankCommand();

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

        public int[] NewUninitializedArray(int arraySize)
        {
            int[] result = new int[arraySize];
            for (int i = 0; i < arraySize; i++)
                result[i] = NewUninitialized();
            return result;
        }

        public int NewUninitialized()
        {
            return NextArrayIndex++;
        }

        public int CopyToNew(int sourceIndex)
        {
            if (UseOrderedSources && sourceIndex < InitialArrayIndex)
            {
                // Instead of copying from the source, we will add this index to our list of indices. This will improve performance, because we can preconstruct our sources and then just read from these consecutively.
                OrderedSourceIndices.Add(sourceIndex);
                AddCommand(new ArrayCommand(ArrayCommandType.NextSource, NextArrayIndex, -1));
            }
            else
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
            Increment(result, index2);
            return result;
        }

        public int MultiplyToNew(int index1, int index2)
        {
            int result = CopyToNew(index1);
            MultiplyBy(result, index2);
            return result;
        }

        // Next, methods that modify existing array items in place

        public void CopyToExisting(int index, int sourceIndex)
        {
            if (UseOrderedDestinations && index < InitialArrayIndex)
            {
                throw new NotSupportedException("Only incrementing source item is currently supported.");
            }
            AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, index, sourceIndex));
        }

        public void CopyToExisting(int[] indices, int[] sourceIndices)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                CopyToExisting(indices[i], sourceIndices[i]);
            }
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
            if (index < InitialArrayIndex)
                throw new NotSupportedException(); // use approach of increment to avoid interlocking code
            AddCommand(new ArrayCommand(index < InitialArrayIndex && InterlockWhereModifyingInitialSource ? ArrayCommandType.MultiplyByInterlocked : ArrayCommandType.MultiplyBy, index, indexOfMultiplier));
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
            if (UseOrderedDestinations && index < InitialArrayIndex)
            {
                if (indexOfIncrement < InitialArrayIndex)
                    throw new Exception("Must increment from the array command list stack, not from the original array.");
                if (ReuseDestinations && ReusableOrderedDestinationIndices.ContainsKey(index))
                {
                    AddCommand(new ArrayCommand(ArrayCommandType.ReusedDestination, ReusableOrderedDestinationIndices[index], indexOfIncrement));
                }
                else
                {
                    OrderedDestinationIndices.Add(index);
                    if (ReuseDestinations)
                        ReusableOrderedDestinationIndices.Add(index, OrderedDestinationIndices.Count() - 1);
                    AddCommand(new ArrayCommand(ArrayCommandType.NextDestination, -1, indexOfIncrement));
                }
            }
            else
                AddCommand(new ArrayCommand(index < InitialArrayIndex && InterlockWhereModifyingInitialSource ? ArrayCommandType.IncrementByInterlocked : ArrayCommandType.IncrementBy, index, indexOfIncrement));
        }

        public void IncrementByProduct(int index, int indexOfIncrementProduct1, int indexOfIncrementProduct2)
        {
            int spaceForProduct = CopyToNew(indexOfIncrementProduct1);
            MultiplyBy(spaceForProduct, indexOfIncrementProduct2);
            Increment(index, spaceForProduct);
            NextArrayIndex--; // we've set aside an array index to be used for this command. But we no longer need it, so we can now allocate it to some other purpose (e.g., incrementing by another product)
        }

        public void DecrementArrayBy(int[] indices, int indexOfDecrement)
        {
            for (int i = 0; i < indices.Length; i++)
                Decrement(indices[i], indexOfDecrement);
        }

        public void DecrementArrayBy(int[] indices, int[] indicesOfDecrements)
        {
            for (int i = 0; i < indices.Length; i++)
                Decrement(indices[i], indicesOfDecrements[i]);
        }

        public void Decrement(int index, int indexOfDecrement)
        {
            if (index < InitialArrayIndex)
                throw new NotSupportedException(); // use approach of increment to avoid interlocking code
            AddCommand(new ArrayCommand(index < InitialArrayIndex && InterlockWhereModifyingInitialSource ? ArrayCommandType.DecrementByInterlocked : ArrayCommandType.DecrementBy, index, indexOfDecrement));
        }

        public void DecrementByProduct(int index, int indexOfDecrementProduct1, int indexOfDecrementProduct2)
        {
            int spaceForProduct = CopyToNew(indexOfDecrementProduct1);
            MultiplyBy(spaceForProduct, indexOfDecrementProduct2);
            Decrement(index, spaceForProduct);
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

        public void InsertAfterGoToTargetCommand()
        {
            AddCommand(new ArrayCommand(ArrayCommandType.AfterGoTo, OrderedDestinationIndices.Count(), OrderedSourceIndices.Count()));
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

        public unsafe void ExecuteAll(double[] array)
        {
            PrepareOrderedSourcesAndDestinations(array);
            int currentOrderedSourceIndex = 0;
            int currentOrderedDestinationIndex = 0;
            int MaxCommandIndex = NextCommandIndex;
            bool skipNext;
            int goTo;
            fixed (ArrayCommand* overall = &UnderlyingCommands[0])
            fixed (double* arrayPointer = &array[0])
            {
                double* arrayPortion = UseOrderedSources && UseOrderedDestinations ? (arrayPointer + InitialArrayIndex) : arrayPointer;
                ArrayCommand* command = overall;
                ArrayCommand* lastCommand = command + MaxCommandIndex;
                while (command <= lastCommand)
                {
                    //System.Diagnostics.Debug.WriteLine(*command);
                    skipNext = false;
                    goTo = -1;
                    switch ((*command).CommandType)
                    {
                        case ArrayCommandType.ZeroNew:
                            arrayPortion[(*command).Index] = 0;
                            break;
                        case ArrayCommandType.CopyTo:
                            arrayPortion[(*command).Index] = arrayPortion[(*command).SourceIndex];
                            break;
                        case ArrayCommandType.NextSource:
                            arrayPortion[(*command).Index] = OrderedSources[currentOrderedSourceIndex++];
                            break;
                        case ArrayCommandType.NextDestination:
                            double value = arrayPortion[(*command).SourceIndex];
                            OrderedDestinations[currentOrderedDestinationIndex++] = value;
                            break;
                        case ArrayCommandType.ReusedDestination:
                            value = arrayPortion[(*command).SourceIndex];
                            int reusedDestination = (*command).Index;
                            OrderedDestinations[reusedDestination] += value;
                            break;
                        case ArrayCommandType.MultiplyBy:
                            // DEBUG -- breaking it down this way reveals that about half of the time is from the array accesses (second is basically free) and half from multiplication
                            //double getTarget = array[(*command).Index];
                            //double getSource = array[(*command).SourceIndex];
                            //getTarget *= getSource;
                            //array[(*command).Index] = getTarget;
                            arrayPortion[(*command).Index] *= arrayPortion[(*command).SourceIndex];
                            break;
                        case ArrayCommandType.IncrementBy:
                            arrayPortion[(*command).Index] += arrayPortion[(*command).SourceIndex];
                            break;
                        case ArrayCommandType.DecrementBy:
                            arrayPortion[(*command).Index] -= arrayPortion[(*command).SourceIndex];
                            break;
                        case ArrayCommandType.MultiplyByInterlocked:
                            Interlocking.Multiply(ref arrayPortion[(*command).Index], arrayPortion[(*command).SourceIndex]);
                            break;
                        case ArrayCommandType.IncrementByInterlocked:
                            Interlocking.Add(ref arrayPortion[(*command).Index], arrayPortion[(*command).SourceIndex]);
                            break;
                        case ArrayCommandType.DecrementByInterlocked:
                            Interlocking.Subtract(ref arrayPortion[(*command).Index], arrayPortion[(*command).SourceIndex]);
                            break;
                        case ArrayCommandType.EqualsOtherArrayIndex:
                            bool conditionMet = arrayPortion[(*command).Index] == arrayPortion[(*command).SourceIndex];
                            if (!conditionMet)
                                skipNext = true;
                            break;
                        case ArrayCommandType.NotEqualsOtherArrayIndex:
                            conditionMet = arrayPortion[(*command).Index] != arrayPortion[(*command).SourceIndex];
                            if (!conditionMet)
                                skipNext = true;
                            break;
                        case ArrayCommandType.GreaterThanOtherArrayIndex:
                            conditionMet = arrayPortion[(*command).Index] > arrayPortion[(*command).SourceIndex];
                            if (!conditionMet)
                                skipNext = true;
                            break;
                        case ArrayCommandType.LessThanOtherArrayIndex:
                            conditionMet = arrayPortion[(*command).Index] < arrayPortion[(*command).SourceIndex];
                            if (!conditionMet)
                                skipNext = true;
                            break;
                            // in next two, sourceindex represents a value, not an array index
                        case ArrayCommandType.EqualsValue:
                            conditionMet = arrayPortion[(*command).Index] == (*command).SourceIndex;
                            if (!conditionMet)
                                skipNext = true;
                            break;
                        case ArrayCommandType.NotEqualsValue:
                            conditionMet = arrayPortion[(*command).Index] != (*command).SourceIndex;
                            if (!conditionMet)
                                skipNext = true;
                            break;
                        case ArrayCommandType.GoTo:
                            // index here represents a command index -- not an array index
                            goTo = (*command).Index - 1; // because we are going to increment in the for loop
                            break;
                        case ArrayCommandType.AfterGoTo:
                            // indices here are indices but not into the original array
                            currentOrderedDestinationIndex = (*command).Index;
                            currentOrderedSourceIndex = (*command).SourceIndex;
                            break;
                        case ArrayCommandType.Blank:
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    if (skipNext)
                        command += sizeof(ArrayCommand); // in addition to increment below
                    else if (goTo != -1)
                        command = overall + goTo ;
                    command += 1;
                }
            }
            CopyOrderedDestinations(array);
        }

        public void PrepareOrderedSourcesAndDestinations(double[] array)
        {
            int sourcesCount = OrderedSourceIndices.Count();
            int destinationsCount = OrderedDestinationIndices.Count();
            if (OrderedSources == null)
            {
                OrderedSources = new double[sourcesCount];
                OrderedDestinations = new double[destinationsCount];
            }
            int currentOrderedSourceIndex = 0;
            foreach (int orderedSourceIndex in OrderedSourceIndices)
            {
                OrderedSources[currentOrderedSourceIndex++] = array[orderedSourceIndex];
            }
            for (int i = 0; i < destinationsCount; i++)
                OrderedDestinations[i] = 0;
        }

        static object DestinationCopier = new object();
        public void CopyOrderedDestinations(double[] array)
        {
            lock (DestinationCopier)
            {
                int currentOrderedDestinationIndex = 0;
                foreach (int destinationIndex in OrderedDestinationIndices)
                {
                    array[destinationIndex] += OrderedDestinations[currentOrderedDestinationIndex++];
                    if (double.IsNaN(array[destinationIndex]))
                        throw new Exception();
                }
            }
        }

        #endregion
    }
}
