using ACESim;
using ACESim.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESimBase.Util.ArrayProcessing
{
    public partial class ArrayCommandList
    {

        public ArrayCommand[] UnderlyingCommands;
        public int NextCommandIndex;
        public int MaxCommandIndex;
        public int FirstScratchIndex;
        public int NextArrayIndex;
        public int MaxArrayIndex;

        // Ordered sources: We initially develop a list of indices of the data passed to the algorithm each iteration. Before each iteration, we copy the data corresponding to these indices into the OrderedSources array in the order in which it will be needed. A command that otherwise would copy from the original data instead loads the next item in ordered sources. This may slightly improve performance because a sequence of original data will be cached. More importantly, it can improve parallelism: When a player chooses among many actions that are structurally equivalent (that is, they do not change how the game is played from that point on), we can run the same code with different slices of the OrderedSources array.
        public bool UseOrderedSources = true;
        public List<int> OrderedSourceIndices;
        public double[] OrderedSources;
        // Ordered destinations: Similarly, when the unrolled algorithm changes the data passed to it (for example, incrementing regrets in CFR), instead of directly incrementing the data, we develop in advance a list of the indices that will be changed. Then, when running the algorithm, we store the actual data that needs to be changed in an array, and on completion of the algorithm, we run through that array and change the data at the specified index for each item. This enhances parallelism because we don't have to lock around each data change, instead locking only around the final set of changes. This also may facilitate spreading the algorithm across machines, since each CPU can simply report the set of changes to make.
        public bool UseOrderedDestinations = true;
        public bool ReuseDestinations = false; // if true, then we will not add a new ordered destination index for a destination location already used within code executed not in parallel. Instead, we will just increment the previous destination.
        public List<int> OrderedDestinationIndices;
        public double[] OrderedDestinations;
        public Dictionary<int, int> ReusableOrderedDestinationIndices;
        public bool Parallelize;
        public bool InterlockWhereModifyingInitialSource => Parallelize && !UseOrderedDestinations;

        // If true, then when a command refers to an array index, 0 refers to FirstScratchIndex.
        public bool ScratchIndicesStartAt0 => UseOrderedSources && UseOrderedDestinations;
        public int FullArraySize => FirstScratchIndex + (Parallelize ? 0 : MaxArrayIndex);
        
        public Stack<int> PerDepthStartArrayIndices;
        public Stack<int?> RepeatingExistingCommandRangeStack;
        public bool RepeatingExistingCommandRange = false;

        NWayTreeStorageInternal<ArrayCommandChunk> CommandTree;
        string CommandTreeString;
        List<byte> CurrentCommandTreeLocation = new List<byte>();
        NWayTreeStorageInternal<ArrayCommandChunk> CurrentNode => (NWayTreeStorageInternal<ArrayCommandChunk>) CommandTree.GetNode(CurrentCommandTreeLocation);
        ArrayCommandChunk CurrentCommandChunk => CurrentNode.StoredValue;

        public ArrayCommandList(int maxNumCommands, int initialArrayIndex, bool parallelize)
        {
            UnderlyingCommands = new ArrayCommand[maxNumCommands];
            OrderedSourceIndices = new List<int>();
            OrderedDestinationIndices = new List<int>();
            ReusableOrderedDestinationIndices = new Dictionary<int, int>();
            FirstScratchIndex = initialArrayIndex;
            if (ScratchIndicesStartAt0)
                NextArrayIndex = 0;
            else
                NextArrayIndex = FirstScratchIndex;
            MaxArrayIndex = NextArrayIndex - 1;
            PerDepthStartArrayIndices = new Stack<int>();
            RepeatingExistingCommandRangeStack = new Stack<int?>();
            Parallelize = parallelize;
            CommandTree = new NWayTreeStorageInternal<ArrayCommandChunk>(null);
            CommandTree.StoredValue = new ArrayCommandChunk()
            {
                ChildrenParallelizable = false,
                StartCommandRange = 0,
                StartSourceIndices = 0,
                StartDestinationIndices = 0
            };
        }

        #region Depth management and parallelism

        // We are simulating a stack. When entering a new depth level, we remember the next array index. Then, when exiting this depth level, we revert to this array index. A consequence of this is that depth i + 1 can return values to depth <= i only by copying to array indices already set at this earlier depth. 

        public void IncrementDepth(bool separateCommandChunk)
        {
            PerDepthStartArrayIndices.Push(NextArrayIndex);
            if (separateCommandChunk)
                StartCommandChunk(false, null);
        }

        public void DecrementDepth(bool separateCommandChunk, bool completeCommandList = false)
        {
            NextArrayIndex = PerDepthStartArrayIndices.Pop();
            if (separateCommandChunk)
                EndCommandChunk();
            if (!PerDepthStartArrayIndices.Any() && completeCommandList)
            {
                CompleteCommandList();
            }
        }

        int NextVirtualStackID = 0;
        
        public void StartCommandChunk(bool runChildrenInParallel, int? identicalStartCommandRange, string name = "")
        {
            if (runChildrenInParallel && identicalStartCommandRange is int identical)
            {
                Debug.WriteLine($"Starting identical range (instead of {NextCommandIndex} using {identicalStartCommandRange}");
                RepeatingExistingCommandRangeStack.Push(identicalStartCommandRange);
                NextCommandIndex = identical;
                RepeatingExistingCommandRange = true;
            }
            var currentNode = CurrentNode;
            var currentNodeIsInParallel = CurrentNode?.StoredValue.ChildrenParallelizable ?? false;
            if (currentNodeIsInParallel)
                ReusableOrderedDestinationIndices = new Dictionary<int, int>();
            byte nextChild = (byte)(currentNode.StoredValue.LastChild + 1);
            NWayTreeStorageInternal<ArrayCommandChunk> childNode = new NWayTreeStorageInternal<ArrayCommandChunk>(CurrentNode);
            childNode.StoredValue = new ArrayCommandChunk()
            {
                Name = name,
                ChildrenParallelizable = runChildrenInParallel,
                StartCommandRange = NextCommandIndex,
                StartSourceIndices = OrderedSourceIndices?.Count() ?? 0,
                StartDestinationIndices = OrderedDestinationIndices?.Count() ?? 0
            };
            CurrentNode.SetBranch(nextChild, childNode);
            CurrentNode.StoredValue.LastChild = nextChild;
            CurrentCommandTreeLocation.Add(nextChild);
        }

        public void EndCommandChunk(int[] copyIncrementsToParent = null, bool endingRepeatedChunk = false)
        {
            var commandChunkBeingEnded = CurrentCommandChunk;
            commandChunkBeingEnded.EndCommandRangeExclusive = NextCommandIndex;
            commandChunkBeingEnded.EndSourceIndicesExclusive = OrderedSourceIndices?.Count() ?? 0;
            commandChunkBeingEnded.EndDestinationIndicesExclusive = OrderedDestinationIndices?.Count() ?? 0;
            // now update parent
            CurrentCommandTreeLocation = CurrentCommandTreeLocation.Take(CurrentCommandTreeLocation.Count() - 1).ToList(); // remove last item
            CurrentCommandChunk.EndCommandRangeExclusive = NextCommandIndex;
            CurrentCommandChunk.EndSourceIndicesExclusive = OrderedSourceIndices?.Count() ?? 0;
            CurrentCommandChunk.EndDestinationIndicesExclusive = OrderedDestinationIndices?.Count() ?? 0;
            commandChunkBeingEnded.CopyIncrementsToParent = copyIncrementsToParent;
            if (endingRepeatedChunk && RepeatingExistingCommandRangeStack.Any())
            {
                RepeatingExistingCommandRangeStack.Pop();
                if (!RepeatingExistingCommandRangeStack.Any())
                    RepeatingExistingCommandRange = false;
            }
        }

        public void CompleteCommandList()
        {
            MaxCommandIndex = NextCommandIndex;
            while (CurrentCommandTreeLocation.Any())
                EndCommandChunk();
            CommandTree.WalkTree(x => InsertMissingBranches((NWayTreeStorageInternal<ArrayCommandChunk>)x));
            SetupVirtualStacks();
            CommandTreeString = CommandTree.ToString();
        }

        private void InsertMissingBranches(NWayTreeStorageInternal<ArrayCommandChunk> node)
        {
            byte lastChild = node.StoredValue.LastChild;
            if (lastChild > 0)
            {
                List<NWayTreeStorageInternal<ArrayCommandChunk>> children = new List<NWayTreeStorageInternal<ArrayCommandChunk>>();
                int currentCommandIndex = node.StoredValue.StartCommandRange;
                int currentSourceIndex = node.StoredValue.StartSourceIndices;
                int currentDestinationIndex = node.StoredValue.StartDestinationIndices;
                for (byte c = 1; c <= lastChild; c++)
                {
                    var branch = (NWayTreeStorageInternal<ArrayCommandChunk>) node.GetBranch(c);
                    var next = branch.StoredValue;
                    if (branch.StoredValue.StartCommandRange > currentCommandIndex)
                    { // there is a missing command range -- insert it
                        var toInsert = new NWayTreeStorageInternal<ArrayCommandChunk>(node);
                        toInsert.StoredValue = new ArrayCommandChunk()
                        {
                            ChildrenParallelizable = false,
                            StartCommandRange = currentCommandIndex,
                            EndCommandRangeExclusive = next.StartCommandRange,
                            StartSourceIndices = currentSourceIndex,
                            EndSourceIndicesExclusive = next.StartSourceIndices,
                            StartDestinationIndices = currentDestinationIndex,
                            EndDestinationIndicesExclusive = next.StartDestinationIndices,
                        };
                        children.Add(toInsert);
                    }
                    children.Add(branch);
                    currentCommandIndex = next.EndCommandRangeExclusive;
                    currentSourceIndex = next.EndSourceIndicesExclusive;
                    currentDestinationIndex = next.EndDestinationIndicesExclusive;
                    if (c == lastChild && (currentCommandIndex < node.StoredValue.EndCommandRangeExclusive || currentSourceIndex < node.StoredValue.EndSourceIndicesExclusive || currentDestinationIndex < node.StoredValue.EndDestinationIndicesExclusive))
                    {
                        var toInsert = new NWayTreeStorageInternal<ArrayCommandChunk>(node);
                        toInsert.StoredValue = new ArrayCommandChunk()
                        {
                            ChildrenParallelizable = false,
                            StartCommandRange = currentCommandIndex,
                            EndCommandRangeExclusive = node.StoredValue.EndCommandRangeExclusive,
                            StartSourceIndices = currentSourceIndex,
                            EndSourceIndicesExclusive = node.StoredValue.EndSourceIndicesExclusive,
                            StartDestinationIndices = currentDestinationIndex,
                            EndDestinationIndicesExclusive = node.StoredValue.EndDestinationIndicesExclusive,
                        };
                        children.Add(toInsert);
                    }
                }
                node.Branches = children.ToArray();
            }
        }

        private void SetupVirtualStacks()
        {
            CommandTree.WalkTree(null, x => SetupVirtualStack((NWayTreeStorageInternal<ArrayCommandChunk>) x));
            CommandTree.WalkTree(x => SetupVirtualStackRelationships((NWayTreeStorageInternal<ArrayCommandChunk>)x)); // must visit from top to bottom, since we may share the same virtual stack across multiple levels
        }

        private void SetupVirtualStack(NWayTreeStorageInternal<ArrayCommandChunk> node)
        {
            ArrayCommandChunk c = node.StoredValue;
            c.VirtualStack = new double[MaxArrayIndex];
            c.VirtualStackID = NextVirtualStackID++;
        }

        private void SetupVirtualStackRelationships(NWayTreeStorageInternal<ArrayCommandChunk> node)
        {
            if (node.Parent != null)
            {
                if (node.Parent.StoredValue.ChildrenParallelizable == false)
                {
                    // if a node has sequential children, then those children share the same virtual stack
                    node.StoredValue.VirtualStack = node.Parent.StoredValue.VirtualStack;
                    node.StoredValue.VirtualStackID = node.Parent.StoredValue.VirtualStackID;
                    node.StoredValue.ParentVirtualStackID = node.Parent.StoredValue.VirtualStackID;
                }
                else
                {
                    node.StoredValue.ParentVirtualStack = node.Parent.StoredValue.VirtualStack;
                    node.StoredValue.ParentVirtualStackID = node.Parent.StoredValue.VirtualStackID;
                }
            }
        }

        private int HighestSourceIndexInCommandRange(int startRange, int endRangeExclusive)
        {
            int lastArrayIndex = 0;
            for (int i = 0; i < endRangeExclusive; i++)
            {
                lastArrayIndex = Math.Max(lastArrayIndex, UnderlyingCommands[i].GetTargetIndexIfUsed());
            }
            if (lastArrayIndex == 0)
                return 0;
            if (ScratchIndicesStartAt0)
                return lastArrayIndex;
            return lastArrayIndex - FirstScratchIndex;
        }

        private int HighestTargetIndexInCommandRange(int startRange, int endRangeExclusive)
        {
            int lastArrayIndex = 0;
            for (int i = 0; i < endRangeExclusive; i++)
            {
                lastArrayIndex = Math.Max(lastArrayIndex, UnderlyingCommands[i].GetTargetIndexIfUsed());
            }
            if (lastArrayIndex == 0)
                return 0;
            if (ScratchIndicesStartAt0)
                return lastArrayIndex; // we've already decremented
            return lastArrayIndex - FirstScratchIndex;
        }


        #endregion

        #region Commands

        private void AddCommand(ArrayCommand command)
        {
            if (NextCommandIndex == 0 && command.CommandType != ArrayCommandType.Blank)
                InsertBlankCommand();
            if (RepeatingExistingCommandRange)
            {
                ArrayCommand existingCommand = UnderlyingCommands[NextCommandIndex];
                if (!command.Equals(existingCommand) && existingCommand.CommandType != ArrayCommandType.GoTo) // note that goto may be specified later
                    throw new Exception("Expected repeated command to be equal but it wasn't");
                NextCommandIndex++;
                return;
            }
            if (NextCommandIndex >= UnderlyingCommands.Length)
                throw new Exception("Commands array size must be increased.");
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
            AddCommand(new ArrayCommand(ArrayCommandType.Zero, NextArrayIndex, -1));
            return NextArrayIndex++;
        }

        public int[] NewUninitializedArray(int arraySize)
        {
            int[] result = new int[arraySize];
            for (int i = 0; i<arraySize; i++)
                result[i] = NewUninitialized();
            return result;
        }

        public int NewUninitialized()
        {
            return NextArrayIndex++;
        }

        public int CopyToNew(int sourceIndex, bool fromOriginalSources)
        {
            if (UseOrderedSources && fromOriginalSources)
            {
                // Instead of copying from the source, we will add this index to our list of indices. This will improve performance, because we can preconstruct our sources and then just read from these consecutively.
                OrderedSourceIndices.Add(sourceIndex);
                AddCommand(new ArrayCommand(ArrayCommandType.NextSource, NextArrayIndex, -1));
            }
            else
                AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, NextArrayIndex, sourceIndex));
            return NextArrayIndex++;
        }

        public int[] CopyToNew(int[] sourceIndices, bool fromOriginalSources)
        {
            return sourceIndices.Select(x => CopyToNew(x, fromOriginalSources)).ToArray();
        }

        public int AddToNew(int index1, bool fromOriginalSources, int index2)
        {
            int result = CopyToNew(index1, fromOriginalSources);
            Increment(result, false, index2);
            return result;
        }

        public int MultiplyToNew(int index1, bool fromOriginalSources, int index2)
        {
            int result = CopyToNew(index1, fromOriginalSources);
            MultiplyBy(result, index2);
            return result;
        }

        // Next, methods that modify existing array items in place

        public void ZeroExisting(int[] indices)
        {
            foreach (int index in indices)
                ZeroExisting(index);
        }

        public void ZeroExisting(int index)
        {
            AddCommand(new ArrayCommand(ArrayCommandType.Zero, index, -1));
        }

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
            if (!ScratchIndicesStartAt0 && index < FirstScratchIndex)
                throw new NotSupportedException(); // use approach of increment to avoid interlocking code
            AddCommand(new ArrayCommand(ArrayCommandType.MultiplyBy, index, indexOfMultiplier));
        }

        public void IncrementArrayBy(int[] indices, bool targetOriginals,  int indexOfIncrement)
        {
            for (int i = 0; i < indices.Length; i++)
                Increment(indices[i], targetOriginals, indexOfIncrement);
        }

        public void IncrementArrayBy(int[] indices, bool targetOriginals, int[] indicesOfIncrements)
        {
            for (int i = 0; i < indices.Length; i++)
                Increment(indices[i], targetOriginals, indicesOfIncrements[i]);
        }

        public void Increment(int index, bool targetOriginal, int indexOfIncrement)
        {
            if (targetOriginal)
            {
                if (UseOrderedDestinations)
                {
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
                    AddCommand(new ArrayCommand(InterlockWhereModifyingInitialSource ? ArrayCommandType.IncrementByInterlocked : ArrayCommandType.IncrementBy, index, indexOfIncrement));
            }
            else
                AddCommand(new ArrayCommand(ArrayCommandType.IncrementBy, index, indexOfIncrement));
        }

        public void IncrementByProduct(int index, bool targetOriginal, int indexOfIncrementProduct1, int indexOfIncrementProduct2)
        {
            int spaceForProduct = CopyToNew(indexOfIncrementProduct1, false);
            MultiplyBy(spaceForProduct, indexOfIncrementProduct2);
            Increment(index, targetOriginal, spaceForProduct);
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
            if (!ScratchIndicesStartAt0 && index < FirstScratchIndex)
                throw new NotSupportedException(); // use approach of increment to avoid interlocking code
            AddCommand(new ArrayCommand(ArrayCommandType.DecrementBy, index, indexOfDecrement));
        }

        public void DecrementByProduct(int index, int indexOfDecrementProduct1, int indexOfDecrementProduct2)
        {
            int spaceForProduct = CopyToNew(indexOfDecrementProduct1, false);
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

        // When we skip some code, we need to set the ordered indices to the spots where they would have been had we not skipped the code. To make sure that this is the same when we repeat code multiple times but for different source indices, the AfterGoToTarget will store the delta

        public Stack<(int sourceIndex, int destinationIndex)> GoToSpots = new Stack<(int sourceIndex, int destinationIndex)>();
        public void RememberOrderedIndicesAtGoToSpot()
        {
            GoToSpots.Push((OrderedSourceIndices.Count(), OrderedDestinationIndices.Count()));
        }

        public void InsertAfterGoToTargetCommand()
        {
            (int sourceIndex, int destinationIndex) = GoToSpots.Pop();
            AddCommand(new ArrayCommand(ArrayCommandType.AfterGoTo, OrderedDestinationIndices.Count() - destinationIndex, OrderedSourceIndices.Count() - sourceIndex));
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
            bool useSafe = false;
            PrepareOrderedSourcesAndDestinations(array);
            if (Parallelize)
            {
                if (!UseOrderedSources || !UseOrderedDestinations)
                    throw new Exception("Must use ordered sources and destinations with parallelizable");
                CommandTree.WalkTree(n =>
                {
                    var node = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                    var commandChunk = node.StoredValue;
                    commandChunk.CopyParentVirtualStack();
                    commandChunk.ResetIncrementsForParent(); // the parent virtual stack may have already received increments from another node being run in parallel to this one. So, we set that to zero here to avoid double-counting.
                }, n =>
                {
                    var node = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                    var commandChunk = node.StoredValue;
                    if (node.Branches == null || !node.Branches.Any())
                    {
                        if (useSafe)
                            ExecuteSectionOfCommands_Safe(new Span<double>(commandChunk.VirtualStack), commandChunk.StartCommandRange, commandChunk.EndCommandRangeExclusive - 1, commandChunk.StartSourceIndices, commandChunk.StartDestinationIndices, commandChunk.EndDestinationIndicesExclusive);
                        else
                        {
                            fixed (double* arrayPointer = commandChunk.VirtualStack)
                            {
                                ExecuteSectionOfCommands(arrayPointer, commandChunk.StartCommandRange, commandChunk.EndCommandRangeExclusive - 1, commandChunk.StartSourceIndices, commandChunk.StartDestinationIndices, commandChunk.EndDestinationIndicesExclusive);
                            }
                        }
                    }
                    commandChunk.CopyIncrementsToParentIfNecessary();
                }, n =>
                {
                    var node = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                    return node.StoredValue.ChildrenParallelizable;
                });

            }
            else
            {
                if (useSafe)
                {
                    Span<double> arrayPortion = UseOrderedSources && UseOrderedDestinations ? new Span<double>(array).Slice(FirstScratchIndex) : new Span<double>(array);
                    ExecuteSectionOfCommands_Safe(arrayPortion, 0, MaxCommandIndex, 0, 0, OrderedDestinationIndices.Count());
                }
                else
                {
                    fixed (double* arrayPointer = array)
                    {
                        double* arrayPortion = UseOrderedSources && UseOrderedDestinations ? (arrayPointer + FirstScratchIndex) : arrayPointer;
                        ExecuteSectionOfCommands(arrayPortion, 0, MaxCommandIndex, 0, 0, OrderedDestinationIndices.Count());
                    }
                }
            }
            //for (int i = 0; i < OrderedDestinations.Length; i++)
            //    System.Diagnostics.Debug.WriteLine($"{i}: {OrderedDestinations[i]}");
            //PrintCommandLog();
            CopyOrderedDestinations(array, 0, OrderedDestinationIndices.Count());
        }

        ConcurrentDictionary<int, string> arrayValueAfterCommand = null;
        StringBuilder commandLog = new StringBuilder();
        private void LogCommand(int commandIndex, Span<double> array)
        {
            ArrayCommand command = UnderlyingCommands[commandIndex];
            if (command.CommandType == ArrayCommandType.GoTo || command.CommandType == ArrayCommandType.AfterGoTo || command.CommandType == ArrayCommandType.ReusedDestination || command.CommandType == ArrayCommandType.Blank)
                return;
            if (arrayValueAfterCommand == null || arrayValueAfterCommand.ContainsKey(commandIndex))
                arrayValueAfterCommand = new ConcurrentDictionary<int, string>();
            if (command.CommandType == ArrayCommandType.NextDestination)
                arrayValueAfterCommand[commandIndex] = $"{array[command.SourceIndex]} is next destination (from {command.SourceIndex}";
            else
                arrayValueAfterCommand[commandIndex] = $"{array[command.Index]} is in index {command.Index} from {command.SourceIndex} {(command.SourceIndex != -1 ? array[command.SourceIndex].ToString() : "")}";
        }

        private void PrintCommandLog()
        {
            var ordered = arrayValueAfterCommand.OrderBy(x => x.Key).ToList();
            foreach (var item in ordered)
                commandLog.AppendLine($"{item.Key}: {item.Value}");
        }


        private unsafe void ExecuteSectionOfCommands_Safe(Span<double> arrayPortion, int startCommandIndex, int endCommandIndexInclusive, int currentOrderedSourceIndex, int startOrderedDestinationIndex, int endOrderedDestinationIndex)
        {
            //System.Diagnostics.Debug.WriteLine($"command {startCommandIndex}-{endCommandIndexInclusive}");
            int currentOrderedDestinationIndex = startOrderedDestinationIndex;
            bool skipNext;
            int goTo;
            int commandIndex = startCommandIndex;
            while (commandIndex <= endCommandIndexInclusive)
            {
                ArrayCommand command = UnderlyingCommands[commandIndex];
                //System.Diagnostics.Debug.WriteLine(*command);
                skipNext = false;
                goTo = -1;
                switch (command.CommandType)
                {
                    case ArrayCommandType.Zero:
                        arrayPortion[command.Index] = 0;
                        break;
                    case ArrayCommandType.CopyTo:
                        arrayPortion[command.Index] = arrayPortion[command.SourceIndex];
                        break;
                    case ArrayCommandType.NextSource:
                        arrayPortion[command.Index] = OrderedSources[currentOrderedSourceIndex++];
                        break;
                    case ArrayCommandType.NextDestination:
                        double value = arrayPortion[command.SourceIndex];
                        OrderedDestinations[currentOrderedDestinationIndex++] = value;
                        break;
                    case ArrayCommandType.ReusedDestination:
                        value = arrayPortion[command.SourceIndex];
                        int reusedDestination = command.Index;
                        OrderedDestinations[reusedDestination] += value;
                        break;
                    case ArrayCommandType.MultiplyBy:
                        arrayPortion[command.Index] *= arrayPortion[command.SourceIndex];
                        break;
                    case ArrayCommandType.IncrementBy:
                        arrayPortion[command.Index] += arrayPortion[command.SourceIndex];
                        break;
                    case ArrayCommandType.DecrementBy:
                        arrayPortion[command.Index] -= arrayPortion[command.SourceIndex];
                        break;
                    case ArrayCommandType.MultiplyByInterlocked:
                        Interlocking.Multiply(ref arrayPortion[command.Index], arrayPortion[command.SourceIndex]);
                        break;
                    case ArrayCommandType.IncrementByInterlocked:
                        Interlocking.Add(ref arrayPortion[command.Index], arrayPortion[command.SourceIndex]);
                        break;
                    case ArrayCommandType.DecrementByInterlocked:
                        Interlocking.Subtract(ref arrayPortion[command.Index], arrayPortion[command.SourceIndex]);
                        break;
                    case ArrayCommandType.EqualsOtherArrayIndex:
                        bool conditionMet = arrayPortion[command.Index] == arrayPortion[command.SourceIndex];
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    case ArrayCommandType.NotEqualsOtherArrayIndex:
                        conditionMet = arrayPortion[command.Index] != arrayPortion[command.SourceIndex];
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    case ArrayCommandType.GreaterThanOtherArrayIndex:
                        conditionMet = arrayPortion[command.Index] > arrayPortion[command.SourceIndex];
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    case ArrayCommandType.LessThanOtherArrayIndex:
                        conditionMet = arrayPortion[command.Index] < arrayPortion[command.SourceIndex];
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    // in next two, sourceindex represents a value, not an array index
                    case ArrayCommandType.EqualsValue:
                        conditionMet = arrayPortion[command.Index] == command.SourceIndex;
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    case ArrayCommandType.NotEqualsValue:
                        conditionMet = arrayPortion[command.Index] != command.SourceIndex;
                        if (!conditionMet)
                            skipNext = true;
                        break;
                    case ArrayCommandType.GoTo:
                        // index here represents a command index -- not an array index
                        goTo = command.Index - 1; // because we are going to increment in the for loop
                        break;
                    case ArrayCommandType.AfterGoTo:
                        // indices here are indices but not into the original array
                        currentOrderedDestinationIndex += command.Index;
                        currentOrderedSourceIndex += command.SourceIndex;
                        break;
                    case ArrayCommandType.Blank:
                        break;
                    default:
                        throw new NotImplementedException();
                }
                //LogCommand(commandIndex, arrayPortion);
                if (skipNext)
                    commandIndex++; // in addition to increment below
                else if (goTo != -1)
                {
                    if (goTo < startCommandIndex || goTo > endCommandIndexInclusive)
                        throw new Exception("Goto command cannot flow out of command chunk.");
                    commandIndex = goTo;
                }
                commandIndex++;
            }
        }

        private unsafe void ExecuteSectionOfCommands(double* arrayPortion, int startCommandIndex, int endCommandIndexInclusive, int currentOrderedSourceIndex, int startOrderedDestinationIndex, int endOrderedDestinationIndex)
        {
            int currentOrderedDestinationIndex = startOrderedDestinationIndex;
            bool skipNext;
            int goTo;
            fixed (ArrayCommand* overall = &UnderlyingCommands[0])
            {
                ArrayCommand* command = overall + startCommandIndex;
                ArrayCommand* lastCommand = overall + endCommandIndexInclusive;
                while (command <= lastCommand)
                {
                    //System.Diagnostics.Debug.WriteLine(*command);
                    skipNext = false;
                    goTo = -1;
                    switch ((*command).CommandType)
                    {
                        case ArrayCommandType.Zero:
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
                            currentOrderedDestinationIndex += (*command).Index;
                            currentOrderedSourceIndex += (*command).SourceIndex;
                            break;
                        case ArrayCommandType.Blank:
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    //LogCommand((int) (command - overall), array);
                    if (skipNext)
                        command++; // in addition to increment below
                    else if (goTo != -1)
                    {
                        if (goTo < startCommandIndex || goTo > endCommandIndexInclusive)
                            throw new Exception("Goto command cannot flow out of command chunk.");
                        command = overall + goTo;
                    }
                    command++;
                }
            }
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
        public void CopyOrderedDestinations(double[] array, int startOrderedDestinationIndex, int endOrderedDestinationIndexExclusive)
        {
            Parallel.For(startOrderedDestinationIndex, endOrderedDestinationIndexExclusive, currentOrderedDestinationIndex =>
            {
                int destinationIndex = OrderedDestinationIndices[currentOrderedDestinationIndex];
                array[destinationIndex] += OrderedDestinations[currentOrderedDestinationIndex];
            });
        }

        #endregion
    }
}
