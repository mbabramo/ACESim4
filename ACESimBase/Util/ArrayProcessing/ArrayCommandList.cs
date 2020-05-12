﻿using ACESim;
using ACESim.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace ACESimBase.Util.ArrayProcessing
{
    [Serializable]
    public partial class ArrayCommandList
    {

        #region Fields and settings

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
        public bool ReuseDestinations = false; // NOTE: Not currently working (must adapt to source code generation). If true, then we will not add a new ordered destination index for a destination location already used within code executed not in parallel. Instead, we will just increment the previous destination.
        public List<int> OrderedDestinationIndices;
        public double[] OrderedDestinations;
        public List<int>[] OrderedDestinationsInverted; // here, the outer array is the same size as the target array, and the inner list consists of the items to add up for that array index.
        public (int targetIndex, List<int> sourceIndices)[] OrderedDestinationsInvertedWithTarget;
        public Dictionary<int, int> ReusableOrderedDestinationIndices;
        public bool Parallelize;

        // If true, then when a command refers to an array index, 0 refers to FirstScratchIndex.
        public bool ScratchIndicesStartAt0 => UseOrderedSources && UseOrderedDestinations;
        public int FullArraySize => FirstScratchIndex + (Parallelize ? 0 : MaxArrayIndex);

        public Stack<int> PerDepthStartArrayIndices;
        int NextVirtualStackID = 0;

        bool RepeatIdenticalRanges = true; // instead of repeating identical sequences of commands, we run the same sequence twice
        public Stack<int?> RepeatingExistingCommandRangeStack;
        public bool RepeatingExistingCommandRange = false; // when this is true, we don't need to add new commands

        NWayTreeStorageInternal<ArrayCommandChunk> CommandTree;
        string CommandTreeString;
        List<byte> CurrentCommandTreeLocation = new List<byte>();
        NWayTreeStorageInternal<ArrayCommandChunk> CurrentNode => (NWayTreeStorageInternal<ArrayCommandChunk>) CommandTree.GetNode(CurrentCommandTreeLocation);
        ArrayCommandChunk CurrentCommandChunk => CurrentNode.StoredValue;

        StringBuilder CodeGenerationBuilder = new StringBuilder();
        HashSet<string> CompiledFunctions = new HashSet<string>();
        public int MinNumCommandsToCompile = 25;

        #endregion

        #region Construction and command tree creation

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

        // We are simulating a stack. When entering a new depth level, we remember the next array index. Then, when exiting this depth level, we revert to this array index. A consequence of this is that depth i + 1 can return values to depth <= i only by copying to array indices already set at this earlier depth. 

        public void IncrementDepth()
        {
            PerDepthStartArrayIndices.Push(NextArrayIndex);
        }

        public void DecrementDepth(bool completeCommandList = false)
        {
            var popResult = PerDepthStartArrayIndices.Pop();
            if (!PerDepthStartArrayIndices.Any() && completeCommandList)
            {
                CompleteCommandList();
            }
        }
        
        /// <summary>
        /// Starts a new command chunk. If an identical start command range is specified, then
        /// that range is recorded so that the commands do not need to be repeated. 
        /// </summary>
        /// <param name="runChildrenInParallel"></param>
        /// <param name="identicalStartCommandRange"></param>
        /// <param name="name"></param>
        public void StartCommandChunk(bool runChildrenInParallel, int? identicalStartCommandRange, string name = "")
        {
            if (RepeatIdenticalRanges && identicalStartCommandRange is int identical)
            {
                //Debug.WriteLine($"Repeating identical range (instead of {NextCommandIndex} using {identicalStartCommandRange})");
                RepeatingExistingCommandRangeStack.Push(identicalStartCommandRange);
                NextCommandIndex = identical;
                RepeatingExistingCommandRange = true;
            }
            var currentNode = CurrentNode;
            var currentNodeIsInParallel = CurrentNode?.StoredValue.ChildrenParallelizable ?? false;
            if (currentNodeIsInParallel)
                ReusableOrderedDestinationIndices = new Dictionary<int, int>();
            if (currentNode.StoredValue.LastChild == 255)
                throw new Exception("Too many tree branches");
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

        public void SetSkip(string chunkName, bool skip)
        {
            CommandTree.WalkTree(x =>
            {
                if (x.StoredValue.Name == chunkName)
                    x.StoredValue.Skip = skip;
                else if (x.Parent != null)
                    x.StoredValue.Skip = x.Parent.StoredValue.Skip;
            });
        }

        public void CompleteCommandList()
        {
            MaxCommandIndex = NextCommandIndex;
            while (CurrentCommandTreeLocation.Any())
                EndCommandChunk();
            CompleteCommandTree();
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

        private void CompleteCommandTree()
        {
            CommandTree.WalkTree(x => InsertMissingBranches((NWayTreeStorageInternal<ArrayCommandChunk>)x));
            CommandTree.WalkTree(null, x => SetupVirtualStack((NWayTreeStorageInternal<ArrayCommandChunk>)x)); // setting up bottom to top
            CommandTree.WalkTree(x => SetupVirtualStackRelationships((NWayTreeStorageInternal<ArrayCommandChunk>)x)); // must visit from top to bottom, since we may share the same virtual stack across multiple levels
            CommandTreeString = CommandTree.ToString();
            CompileCode();
        }

        private void SetupVirtualStack(NWayTreeStorageInternal<ArrayCommandChunk> node)
        {
            ArrayCommandChunk c = node.StoredValue;
            c.VirtualStack = new double[MaxArrayIndex + 1];
            c.VirtualStackID = NextVirtualStackID++;
            if (node.Branches == null || node.Branches.Length == 0)
            {
                c.FirstReadFromStack = new int?[MaxArrayIndex + 1];
                c.FirstSetInStack = new int?[MaxArrayIndex + 1];
                c.LastSetInStack = new int?[MaxArrayIndex + 1];
                c.LastUsed = new int?[MaxArrayIndex + 1];
                c.TranslationToLocalIndex = new int?[MaxArrayIndex + 1];
                (c.IndicesReadFromStack, c.IndicesInitiallySetInStack) = DetermineWhenIndicesFirstLastUsed(c.StartCommandRange, c.EndCommandRangeExclusive, c.FirstReadFromStack, c.FirstSetInStack, c.LastSetInStack, c.LastUsed, c.TranslationToLocalIndex);
            }
            else
            {
                DetermineSourcesUsedFromChildren(node);
            }
        }

        private static void DetermineSourcesUsedFromChildren(NWayTreeStorageInternal<ArrayCommandChunk> node)
        {
            ArrayCommandChunk c = node.StoredValue;
            HashSet<int> childrenSourceIndicesUsed = new HashSet<int>();
            foreach (var branch in node.Branches)
                foreach (int index in branch.StoredValue.IndicesReadFromStack)
                    childrenSourceIndicesUsed.Add(index);
            c.IndicesReadFromStack = childrenSourceIndicesUsed.OrderBy(x => x).ToArray();
            c.IndicesInitiallySetInStack = null; // indices are set only in children, so don't need noting here
        }

        private (int[] indicesReadFromStack, int[] indicesSetInStack) DetermineWhenIndicesFirstLastUsed(int startRange, int endRangeExclusive, int?[] firstReadFromStack, int?[] firstSetInStack, int?[] lastSetInStack, int?[] lastUsed, int?[] translationToLocalIndex)
        {
            HashSet<int> indicesUsed = new HashSet<int>();
            for (int commandIndex = startRange; commandIndex < endRangeExclusive; commandIndex++)
            {
                int virtualStackIndex = UnderlyingCommands[commandIndex].GetSourceIndexIfUsed();
                if (virtualStackIndex != -1)
                {
                    if (firstReadFromStack[virtualStackIndex] == null && firstSetInStack[virtualStackIndex] == null)
                        firstReadFromStack[virtualStackIndex] = commandIndex;
                    lastUsed[virtualStackIndex] = commandIndex;
                    indicesUsed.Add(virtualStackIndex);
                }
                virtualStackIndex = UnderlyingCommands[commandIndex].GetTargetIndexIfUsed();
                if (virtualStackIndex != -1)
                {
                    if (firstReadFromStack[virtualStackIndex] == null && firstSetInStack[virtualStackIndex] == null)
                    {
                        if (UnderlyingCommands[commandIndex].CommandType >= ArrayCommandType.MultiplyBy && UnderlyingCommands[commandIndex].CommandType <= ArrayCommandType.DecrementBy)
                            firstReadFromStack[virtualStackIndex] = commandIndex;
                        else
                            firstSetInStack[virtualStackIndex] = commandIndex;
                    }
                    lastSetInStack[virtualStackIndex] = commandIndex;
                    lastUsed[virtualStackIndex] = commandIndex;
                    indicesUsed.Add(virtualStackIndex);
                }
            }

            List<(int virtualStackIndex, int commandFirstUsed, int commandLastUsed)> useRanges = indicesUsed.Select(j => (j, firstReadFromStack[j] ?? firstSetInStack[j], lastUsed[j])).Where(x => x.Item2 != null || x.Item3 != null).Select(t => (t.Item1, (int)t.Item2, (int)t.Item3)).ToList();
            Dictionary<int, (HashSet<int> firstUses, HashSet<int> lastUses)> firstAndLastUsesForCommand = new Dictionary<int, (HashSet<int> firstUses, HashSet<int> lastUses)>();
            foreach (var useRange in useRanges)
            {
                if (!firstAndLastUsesForCommand.ContainsKey(useRange.commandFirstUsed))
                    firstAndLastUsesForCommand[useRange.commandFirstUsed] = (new HashSet<int>(), new HashSet<int>());
                if (!firstAndLastUsesForCommand.ContainsKey(useRange.commandLastUsed))
                    firstAndLastUsesForCommand[useRange.commandLastUsed] = (new HashSet<int>(), new HashSet<int>());
                firstAndLastUsesForCommand[useRange.commandFirstUsed].firstUses.Add(useRange.virtualStackIndex);
                firstAndLastUsesForCommand[useRange.commandLastUsed].lastUses.Add(useRange.virtualStackIndex);
            }

            int lastLocalIndexUsed = -1;
            Stack<int> availableLocals = new Stack<int>();
            for (int commandIndex = startRange; commandIndex < endRangeExclusive; commandIndex++)
            {
                if (firstAndLastUsesForCommand.ContainsKey(commandIndex))
                {
                    var firstUses = firstAndLastUsesForCommand[commandIndex].firstUses;
                    var lastUses = firstAndLastUsesForCommand[commandIndex].lastUses;
                    foreach (int virtualStackIndexFirstUsed in firstUses)
                    {
                        if (availableLocals.Any())
                        {
                            // we can recycle a local variable
                            translationToLocalIndex[virtualStackIndexFirstUsed] = availableLocals.Pop();
                        }
                        else
                            translationToLocalIndex[virtualStackIndexFirstUsed] = ++lastLocalIndexUsed;
                    }
                    foreach (int virtualStackIndexLastUsed in lastUses)
                    { // we're done with this local variable -- make it available
                        availableLocals.Push((int)translationToLocalIndex[virtualStackIndexLastUsed]);
                    }
                }
            }
            int[] indicesReadFromStack = Enumerable.Range(0, firstReadFromStack.Length).Where(x => firstReadFromStack[x] != null).ToArray();
            int[] indicesInitiallySetInStack = Enumerable.Range(0, firstSetInStack.Length).Where(x => firstSetInStack[x] != null).ToArray();
            return (indicesReadFromStack, indicesInitiallySetInStack);
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
                lastArrayIndex = Math.Max(lastArrayIndex, UnderlyingCommands[i].GetSourceIndexIfUsed());
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

        #region Command creation

        private void AddCommand(ArrayCommand command)
        {
            if (NextCommandIndex == 0 && command.CommandType != ArrayCommandType.Blank)
                InsertBlankCommand();
            if (RepeatingExistingCommandRange)
            {
                ArrayCommand existingCommand = UnderlyingCommands[NextCommandIndex];
                if (!command.Equals(existingCommand) && (!ReuseDestinations || command.CommandType != ArrayCommandType.ReusedDestination)) // note that goto may be specified later
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
                    AddCommand(new ArrayCommand(ArrayCommandType.IncrementBy, index, indexOfIncrement));
            }
            else
                AddCommand(new ArrayCommand(ArrayCommandType.IncrementBy, index, indexOfIncrement));
        }

        public void IncrementByProduct(int index, bool targetOriginal, int indexOfIncrementProduct1, int indexOfIncrementProduct2)
        {
            int spaceForProduct = CopyToNew(indexOfIncrementProduct1, false);
            MultiplyBy(spaceForProduct, indexOfIncrementProduct2);
            Increment(index, targetOriginal, spaceForProduct);
            // DEBUG NextArrayIndex--; // we've set aside an array index to be used for this command. But we no longer need it, so we can now allocate it to some other purpose (e.g., incrementing by another product)
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
            // DEBUG NextArrayIndex--; // we've set aside an array index to be used for this command. But we no longer need it, so we can now allocate it to some other purpose (e.g., Decrementing by another product)
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

        public void InsertIfCommand()
        {
            AddCommand(new ArrayCommand(ArrayCommandType.If, -1, -1));
        }

        public void InsertEndIfCommand()
        {
            AddCommand(new ArrayCommand(ArrayCommandType.EndIf, -1, -1));

        }

        /// <summary>
        ///  Inserts a blank command.
        /// </summary>
        /// <returns>The command index (not an array index, as with most comamnds)</returns>
        public int InsertBlankCommand()
        {
            int commandIndex = NextCommandIndex; // not the array index
            AddCommand(new ArrayCommand(ArrayCommandType.Blank, -1, -1));
            return commandIndex;
        }

        #endregion

        #region Code generation

        bool AutogenerateCode = true;
        [NonSerialized]
        Type AutogeneratedCodeType;
        public bool AutogeneratedCodeIsPrecompiled = false; // We can autogenerate and then just save to a c# file, so long as the game structure doesn't change. But we still need to initialize our game tree, since we use it to calculate our ordered sources. In that case, we don't need to autogenerate the code.
        bool CopyVirtualStackToLocalVariables = true; // A complication here is that this may cause the stack to become too large

        private void CompileCode()
        {
            if (!AutogenerateCode || !RepeatIdenticalRanges || !UseOrderedDestinations || !UseOrderedSources)
                return;

            Stopwatch s = new Stopwatch();
            s.Start();
            TabbedText.WriteLine($"Autogenerating and compiling code...");

            CodeGenerationBuilder.AppendLine($@"using System;

    namespace CommandTreeCodegen
    {{
    public static class AutogeneratedCode 
    {{");
            CommandTree.WalkTree(x => GenerateCode((NWayTreeStorageInternal<ArrayCommandChunk>)x)); // must call even if code is precompiled, to build a list of the precompiled function names
            CodeGenerationBuilder.AppendLine($@"}}
}}");
            string fullyQualifiedClassName = "CommandTreeCodegen.AutogeneratedCode";
            if (AutogeneratedCodeIsPrecompiled)
            {
                TabbedText.WriteLine($"Using precompiled code.");
                AutogeneratedCodeType = Type.GetType(fullyQualifiedClassName);
                return;
            }
            var codeString = CodeGenerationBuilder.ToString(); // TODO -- allow different names for different game versions, so that we can precompile all of them
            AutogeneratedCodeType = StringToCode.LoadCode(codeString, fullyQualifiedClassName);

            TabbedText.WriteLine($"...Code autogeneration complete after {s.ElapsedMilliseconds} milliseconds");
        }

        private bool ExecuteAutogeneratedCode(ArrayCommandChunk chunk)
        {
            int startCommandIndex = chunk.StartCommandRange;
            int endCommandIndexInclusive = chunk.EndCommandRangeExclusive - 1;
            string fnName = $"Execute{startCommandIndex}to{endCommandIndexInclusive}";
            if (!CompiledFunctions.Contains(fnName))
                return false;
            // NOTE: this is where we could add a Skip feature (by returning a value from InvokeAutogeneratedCode) -- but we also need to make it work in other circumstances
            InvokeAutogeneratedCode(chunk, fnName);
            return true;
        }

        private void InvokeAutogeneratedCode(ArrayCommandChunk chunk, string fnName)
        {
            var method = AutogeneratedCodeType.GetMethod(fnName);
            method.Invoke(null, new object[] { chunk.VirtualStack, OrderedSources, OrderedDestinations, chunk.StartSourceIndices, chunk.StartDestinationIndices });
        }



        private void GenerateCode(NWayTreeStorageInternal<ArrayCommandChunk> node)
        {
            if (node.Branches == null || !node.Branches.Any())
            {
                int startCommandIndex = node.StoredValue.StartCommandRange;
                int endCommandIndexInclusive = node.StoredValue.EndCommandRangeExclusive - 1;
                if (endCommandIndexInclusive - startCommandIndex + 1 >= MinNumCommandsToCompile)
                {
                    string fnName = $"Execute{startCommandIndex}to{endCommandIndexInclusive}";
                    if (!CompiledFunctions.Contains(fnName))
                    {
                        if (!AutogeneratedCodeIsPrecompiled)
                        {
                            CodeGenerationBuilder.AppendLine("");
                            CodeGenerationBuilder.AppendLine(GenerateSourceTextForChunk(node.StoredValue));
                            CodeGenerationBuilder.AppendLine("");
                        }
                        CompiledFunctions.Add(fnName);
                    }
                }
            }
        }

        private string GenerateSourceTextForChunk(ArrayCommandChunk c)
        {
            int startCommandIndex = c.StartCommandRange;
            int endCommandIndexInclusive = c.EndCommandRangeExclusive - 1;
            int virtualStackSize = c.VirtualStack.Length;

            StringBuilder b = new StringBuilder();
            // Note: We know what the ordered source and destination indices are for the first time this chunk is executed. But we are creating code that can be reused each time we have the same set of commands. Thus, we pass these in as parameters.
            b.AppendLine($@"public static void Execute{startCommandIndex}to{endCommandIndexInclusive}(double[] vs, double[] os, double[] od, int cosi, int codi)
{{
bool condition = true;
");


            // we limit the max number of local vars that we will create (if copying at all) to avoid stack overflow. later indices in the virtual stack have priority, because these are reused the most often.
            const int maxLocalVariables = 200;
            IEnumerable<int> localVariables = c.TranslationToLocalIndex.Where(x => x != null).Select(x => (int)x);
            int numLocalVariables = localVariables.Any() ? localVariables.Max() : 0;
            int minLocalVarNumber = Math.Max(numLocalVariables - maxLocalVariables, 0);
            // declare local variables
            if (CopyVirtualStackToLocalVariables)
            {
                // we don't just use virtual stack indices -- we translate to local variable indices, so that we can reuse where possible
                for (int i = minLocalVarNumber + 1; i <= numLocalVariables; i++)
                    b.AppendLine($"double i_{i} = 0;"); 
                b.AppendLine();
            }

            // when moving to next source or destination in the if block, we need to count the number of increments, so that when we close the if block, we can advance that number of spots.
            List<int> sourceIncrementsInIfBlock = new List<int>();
            List<int> destinationIncrementsInIfBlock = new List<int>();

            // generate code for commands
            int commandIndex = startCommandIndex;
            while (commandIndex <= endCommandIndexInclusive)
            {
                ArrayCommand command = UnderlyingCommands[commandIndex];
                int target = command.Index;
                int source = command.SourceIndex;
                int sourceIfIsIndex = command.GetSourceIndexIfUsed();
                int targetIfIsIndex = command.GetTargetIndexIfUsed();

                int? targetLastSetToStack = null;
                if (CopyVirtualStackToLocalVariables)
                {
                    int? sourceFirstReadFromStack = null;
                    int? targetFirstReadFromStack = null;
                    if (sourceIfIsIndex != -1)
                        sourceFirstReadFromStack = c.FirstReadFromStack[sourceIfIsIndex];
                    if (sourceIfIsIndex > -1 && c.TranslationToLocalIndex[sourceIfIsIndex] > minLocalVarNumber)
                        if (sourceFirstReadFromStack == commandIndex)
                            b.AppendLine($"i_{c.TranslationToLocalIndex[source]} = vs[{source}];");
                    if (targetIfIsIndex != -1)
                    {
                        targetFirstReadFromStack = c.FirstReadFromStack[targetIfIsIndex];
                        targetLastSetToStack = c.LastSetInStack[targetIfIsIndex];
                    }
                    if (targetIfIsIndex > -1 && c.TranslationToLocalIndex[target] > minLocalVarNumber)
                        if (targetFirstReadFromStack == commandIndex)
                            b.AppendLine($"i_{c.TranslationToLocalIndex[target]} = vs[{target}];");
                }

                string itemSourceString = $"vs[{source}]";
                string itemTargetString = $"vs[{target}]";
                if (CopyVirtualStackToLocalVariables)
                {
                    if (sourceIfIsIndex != -1 && c.TranslationToLocalIndex[source] > minLocalVarNumber)
                        itemSourceString = $"i_{c.TranslationToLocalIndex[source]}";
                    if (targetIfIsIndex != -1 && c.TranslationToLocalIndex[target] > minLocalVarNumber)
                        itemTargetString = $"i_{c.TranslationToLocalIndex[target]}";
                }
                switch (command.CommandType)
                {
                    case ArrayCommandType.Zero:
                        b.AppendLine($"{itemTargetString} = 0;");
                        break;
                    case ArrayCommandType.CopyTo:
                        b.AppendLine($"{itemTargetString} = {itemSourceString};");
                        break;
                    case ArrayCommandType.NextSource:
                        b.AppendLine($"{itemTargetString} = os[cosi++];");
                        for (int j = 0; j < sourceIncrementsInIfBlock.Count; j++)
                            sourceIncrementsInIfBlock[j]++;
                        break;
                    case ArrayCommandType.NextDestination:
                        b.AppendLine($"od[codi++] = {itemSourceString};");
                        for (int j = 0; j < destinationIncrementsInIfBlock.Count; j++)
                            destinationIncrementsInIfBlock[j]++;
                        break;
                    case ArrayCommandType.ReusedDestination:
                        // target here refers to the ordered destinations index rather than what was originally an index into the virtual stack
                        b.AppendLine($"od[{target}] += {itemSourceString};");
                        break;
                    case ArrayCommandType.MultiplyBy:
                        b.AppendLine($"{itemTargetString} *= {itemSourceString};");
                        break;
                    case ArrayCommandType.IncrementBy:
                        b.AppendLine($"{itemTargetString} += {itemSourceString};");
                        break;
                    case ArrayCommandType.DecrementBy:
                        b.AppendLine($"{itemTargetString} -= {itemSourceString};");
                        break;
                    case ArrayCommandType.EqualsOtherArrayIndex:
                        b.AppendLine($"condition = {itemTargetString} == {itemSourceString};");
                        break;
                    case ArrayCommandType.NotEqualsOtherArrayIndex:
                        b.AppendLine($"condition = {itemTargetString} != {itemSourceString};");
                        break;
                    case ArrayCommandType.GreaterThanOtherArrayIndex:
                        b.AppendLine($"condition = {itemTargetString} > {itemSourceString};");
                        break;
                    case ArrayCommandType.LessThanOtherArrayIndex:
                        b.AppendLine($"condition = {itemTargetString} < {itemSourceString};");
                        break;
                    // in next two, sourceindex represents a value, not an index to virtual stack
                    case ArrayCommandType.EqualsValue:
                        b.AppendLine($"condition = {itemTargetString} == (double) {source};");
                        break;
                    case ArrayCommandType.NotEqualsValue:
                        b.AppendLine($"condition = {itemTargetString} != (double) {source};");
                        break;
                    case ArrayCommandType.If:
                        // start counting source and destination increments that we'll miss if the code doesn't execute

                        sourceIncrementsInIfBlock.Add(0);
                        destinationIncrementsInIfBlock.Add(0);
                        b.AppendLine($@"if (condition)
{{");
                        break;
                    case ArrayCommandType.EndIf:
                        // here is where we adjust for the fast that source and destination increments were incremented in the if block. We need to advance to the same index as if the code had executed.
                        int sourceIncrements = sourceIncrementsInIfBlock.Any() ? sourceIncrementsInIfBlock.Last() : 0;
                        int destinationIncrements = destinationIncrementsInIfBlock.Any() ? destinationIncrementsInIfBlock.Last() : 0;
                        if (sourceIncrementsInIfBlock.Any())
                            sourceIncrementsInIfBlock.RemoveAt(sourceIncrementsInIfBlock.Count() - 1);
                        if (destinationIncrementsInIfBlock.Any())
                            destinationIncrementsInIfBlock.RemoveAt(destinationIncrementsInIfBlock.Count() - 1);
                        if (sourceIncrements == 0 && destinationIncrements == 0)
                            b.AppendLine("}");
                        else
                            b.AppendLine($@"}}
else
{{
    cosi += {sourceIncrements};
    codi += {destinationIncrements};
}}");
                        break;
                    case ArrayCommandType.Blank:
                        break;
                    default:
                        throw new NotImplementedException();
                }

                if (CopyVirtualStackToLocalVariables && targetLastSetToStack == commandIndex)
                {
                    if (target > -1 && c.TranslationToLocalIndex[target] > minLocalVarNumber)
                        b.AppendLine($"vs[{target}] = i_{c.TranslationToLocalIndex[target]};");
                    // can't do the following, because local var may be used again in another source (this is just where we set it for the last time): b.AppendLine($"i_{c.TranslationToLocalIndex[target]} = 0;");
                }
                commandIndex++;
            }

            b.AppendLine($"}}");

            return b.ToString();
        }

        #endregion

        #region Command execution

        public void ExecuteAll(double[] array, bool tracing)
        {
            PrepareOrderedSourcesAndDestinations(array);
            if (tracing && (Parallelize || RepeatIdenticalRanges || UseOrderedDestinations || UseOrderedSources)) 
                throw new Exception("Cannot trace unrolling with any of these options.");
            if (Parallelize || RepeatIdenticalRanges)
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
                    if (!commandChunk.Skip)
                    {
                        if (node.Branches == null || !node.Branches.Any())
                        {
                            ExecuteSectionOfCommands(commandChunk);
                        }
                        commandChunk.CopyIncrementsToParentIfNecessary();
                    }
                }, n =>
                {
                    var node = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                    return Parallelize && node.StoredValue.ChildrenParallelizable;
                });

            }
            else
            {
                array = ExecuteAllCommands(array);
            }
            //for (int i = 0; i < OrderedDestinations.Length; i++)
            //    System.Diagnostics.Debug.WriteLine($"{i}: {OrderedDestinations[i]}");
            //PrintCommandLog();

            CopyOrderedDestinations(array);
        }

        private double[] ExecuteAllCommands(double[] array)
        {
            Span<double> virtualStack = UseOrderedSources && UseOrderedDestinations ? new Span<double>(array).Slice(FirstScratchIndex) : new Span<double>(array);
            ExecuteSectionOfCommands(virtualStack, 0, MaxCommandIndex, 0, 0);
            return array;
        }

        private void ExecuteSectionOfCommands(ArrayCommandChunk commandChunk)
        {
            bool isCompiled = ExecuteAutogeneratedCode(commandChunk);
            if (!isCompiled)
            {
                ExecuteSectionOfCommands(new Span<double>(commandChunk.VirtualStack), commandChunk.StartCommandRange, commandChunk.EndCommandRangeExclusive - 1, commandChunk.StartSourceIndices, commandChunk.StartDestinationIndices);
            }
        }

        private void ExecuteSectionOfCommands(Span<double> virtualStack, int startCommandIndex, int endCommandIndexInclusive, int currentOrderedSourceIndex, int currentOrderedDestinationIndex)
        {
            bool conditionMet = false;
            int commandIndex = startCommandIndex;
            while (commandIndex <= endCommandIndexInclusive)
            {
                ArrayCommand command = UnderlyingCommands[commandIndex];
                //System.Diagnostics.Debug.WriteLine(*command);
                switch (command.CommandType)
                {
                    case ArrayCommandType.Zero:
                        virtualStack[command.Index] = 0;
                        break;
                    case ArrayCommandType.CopyTo:
                        virtualStack[command.Index] = virtualStack[command.SourceIndex];
                        break;
                    case ArrayCommandType.NextSource:
                        virtualStack[command.Index] = OrderedSources[currentOrderedSourceIndex++];
                        break;
                    case ArrayCommandType.NextDestination:
                        double value = virtualStack[command.SourceIndex];
                        OrderedDestinations[currentOrderedDestinationIndex++] = value;
                        break;
                    case ArrayCommandType.ReusedDestination:
                        value = virtualStack[command.SourceIndex];
                        int reusedDestination = command.Index;
                        OrderedDestinations[reusedDestination] += value;
                        break;
                    case ArrayCommandType.MultiplyBy:
                        virtualStack[command.Index] *= virtualStack[command.SourceIndex];
                        break;
                    case ArrayCommandType.IncrementBy:
                        virtualStack[command.Index] += virtualStack[command.SourceIndex];
                        break;
                    case ArrayCommandType.DecrementBy:
                        virtualStack[command.Index] -= virtualStack[command.SourceIndex];
                        break;
                    case ArrayCommandType.EqualsOtherArrayIndex:
                        conditionMet = virtualStack[command.Index] == virtualStack[command.SourceIndex];
                        break;
                    case ArrayCommandType.NotEqualsOtherArrayIndex:
                        conditionMet = virtualStack[command.Index] != virtualStack[command.SourceIndex];
                        break;
                    case ArrayCommandType.GreaterThanOtherArrayIndex:
                        conditionMet = virtualStack[command.Index] > virtualStack[command.SourceIndex];
                        break;
                    case ArrayCommandType.LessThanOtherArrayIndex:
                        conditionMet = virtualStack[command.Index] < virtualStack[command.SourceIndex];
                        break;
                    // in next two, sourceindex represents a value, not an array index
                    case ArrayCommandType.EqualsValue:
                        conditionMet = virtualStack[command.Index] == command.SourceIndex;
                        break;
                    case ArrayCommandType.NotEqualsValue:
                        conditionMet = virtualStack[command.Index] != command.SourceIndex;
                        break;
                    case ArrayCommandType.If:
                        if (!conditionMet)
                        {
                            int numIf = 1;
                            while (numIf > 0)
                            {
                                commandIndex++;
                                var commandType = UnderlyingCommands[commandIndex].CommandType;
                                if (commandType == ArrayCommandType.If)
                                    numIf++;
                                else if (commandType == ArrayCommandType.EndIf)
                                    numIf--;
                                else if (commandType == ArrayCommandType.NextSource)
                                    currentOrderedSourceIndex++;
                                else if (commandType == ArrayCommandType.NextDestination)
                                    currentOrderedDestinationIndex++;
                            }
                        }
                        break;
                    case ArrayCommandType.EndIf:
                        break;
                    case ArrayCommandType.Blank:
                        break;
                    default:
                        throw new NotImplementedException();
                }
                //LogCommand(commandIndex, virtualStack);
                commandIndex++;
            }
        }

        #endregion

        #region Logging

        [NonSerialized]
        ConcurrentDictionary<int, string> arrayValueAfterCommand = null;
        StringBuilder commandLog = new StringBuilder();
        private void LogCommand(int commandIndex, Span<double> array)
        {
            ArrayCommand command = UnderlyingCommands[commandIndex];
            if (command.CommandType == ArrayCommandType.If || command.CommandType == ArrayCommandType.EndIf || command.CommandType == ArrayCommandType.ReusedDestination || command.CommandType == ArrayCommandType.Blank)
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

        #endregion

        #region Copying ordered sources

        public void PrepareOrderedSourcesAndDestinations(double[] array)
        {
            int sourcesCount = OrderedSourceIndices.Count();
            int destinationsCount = OrderedDestinationIndices.Count();
            if (OrderedSources == null)
            {
                OrderedSources = new double[sourcesCount];
                OrderedDestinations = new double[destinationsCount];
            }
            Parallelizer.Go(Parallelize, 0, sourcesCount, n =>
            {
                OrderedSources[n] = array[OrderedSourceIndices[n]];
            });
            Array.Clear(OrderedDestinations, 0, destinationsCount);
        }

        public void CopyOrderedDestinations(double[] array)
        {
            int startOrderedDestinationIndex = 0;
            int endOrderedDestinationIndexExclusive = OrderedDestinationIndices.Count();
            if (Parallelize)
            {
                if (OrderedDestinationsInverted == null)
                {
                    OrderedDestinationsInverted = new List<int>[array.Length];
                    for (int currentOrderedDestinationIndex = startOrderedDestinationIndex; currentOrderedDestinationIndex < endOrderedDestinationIndexExclusive; currentOrderedDestinationIndex++)
                    {
                        int destinationIndex = OrderedDestinationIndices[currentOrderedDestinationIndex];
                        if (OrderedDestinationsInverted[destinationIndex] == null)
                            OrderedDestinationsInverted[destinationIndex] = new List<int>();
                        OrderedDestinationsInverted[destinationIndex].Add(currentOrderedDestinationIndex);
                    }
                    int nonNullCount = OrderedDestinationsInverted.Where(x => x != null).Count();
                    OrderedDestinationsInvertedWithTarget = new (int targetIndex, List<int> sourceIndices)[nonNullCount];
                    int totalCopied = 0;
                    for (int i = 0; i < array.Length; i++)
                    {
                        if (OrderedDestinationsInverted[i] != null)
                            OrderedDestinationsInvertedWithTarget[totalCopied++] = (i, OrderedDestinationsInverted[i]);
                    }
                }
                else if (OrderedDestinationsInverted.Length != array.Length)
                    throw new Exception();
                int numItemsInTargetArray = OrderedDestinationsInvertedWithTarget.Length;
                Parallelizer.Go(true, 0, numItemsInTargetArray, i =>
                {
                    var targetAndSourceIndices = OrderedDestinationsInvertedWithTarget[i];
                    List<int> indicesToCopy = targetAndSourceIndices.sourceIndices;
                    if (indicesToCopy != null)
                    {
                        double total = 0;
                        foreach (int indexToCopy in indicesToCopy)
                            total += OrderedDestinations[indexToCopy];
                        array[targetAndSourceIndices.targetIndex] = total;
                    }
                });
                // WARNING: We can't use a parallel for without interlocking, because we might affect the same destination multiple times. (We could using Interlocking.Add as in the code below, but that seems to take much longer.) This is also the same reason that we can't call this within each command segment. Alternatively, we could lock around the copying code.
                //Parallelizer.Go(Parallelize, startOrderedDestinationIndex, endOrderedDestinationIndexExclusive, currentOrderedDestinationIndex =>
                //{
                //    int destinationIndex = OrderedDestinationIndices[currentOrderedDestinationIndex];
                //    Interlocking.Add(ref array[destinationIndex], OrderedDestinations[currentOrderedDestinationIndex]);
                //}
                //);
            }
            else
            {
                
                for (int currentOrderedDestinationIndex = startOrderedDestinationIndex; currentOrderedDestinationIndex < endOrderedDestinationIndexExclusive; currentOrderedDestinationIndex++)
                {
                    int destinationIndex = OrderedDestinationIndices[currentOrderedDestinationIndex];
                    array[destinationIndex] += OrderedDestinations[currentOrderedDestinationIndex];
                }
            }
        }

        #endregion
    }
}
