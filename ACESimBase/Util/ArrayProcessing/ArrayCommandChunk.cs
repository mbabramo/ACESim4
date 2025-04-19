using ACESim;
using ACESim.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ACESimBase.Util.ArrayProcessing
{
    public partial class ArrayCommandList
    {
        [Serializable]
        public class ArrayCommandChunk
        {
            public static int NextID = 0;
            public int ID;
            public bool ChildrenParallelizable;
            public byte LastChild;
            public int StartCommandRange, EndCommandRangeExclusive;
            public int StartSourceIndices, EndSourceIndicesExclusive;
            public int StartDestinationIndices, EndDestinationIndicesExclusive;
            public double[] VirtualStack;
            public int VirtualStackID;
            // The following indicate when indices in the virtual stack are first and last used. If the first use is an assignment, then FirstSetInStack will be non-null for the index; if the first use is a read, then FirstReadFromStack will be non-null for the index. LastSetInStack will be non-null if the index is written to.
            public int?[] FirstReadFromStack, FirstSetInStack, LastSetInStack, LastUsed;
            /// <summary>
            /// The index within the virtual stack where a command with the specified index
            /// </summary>
            public int?[] TranslationToLocalIndex;
            public int[] IndicesReadFromStack;
            public int[] IndicesInitiallySetInStack;
            public double[] ParentVirtualStack;
            public int ParentVirtualStackID;
            public string CompiledCode;
            public string Name;
            internal int[] CopyIncrementsToParent;
            public bool Skip; 
            public int SourcesInBody;
            public int DestinationsInBody;
            public int ExecId = int.MinValue;   // unassigned == int.MinValue 

            public ArrayCommandChunk()
            {
                ID = NextID++;
            }


            public override string ToString()
            {
                string copyIncrementsToParent = CopyIncrementsToParent == null ? "" : String.Join(",", CopyIncrementsToParent);
                string virtualStackContents = VirtualStack?.ToSignificantFigures(4);
                string stackInfo = "";
                if (FirstReadFromStack != null)
                {
                    for (int i = 0; i < FirstReadFromStack.Length; i++)
                    {
                        int? firstRead = FirstReadFromStack[i];
                        int? firstSet = FirstSetInStack[i];
                        int? lastSet = LastSetInStack[i];
                        int? lastUsed = LastUsed[i];
                        int? translationToLocalIndex = TranslationToLocalIndex[i];
                        stackInfo += $"{(firstRead == null ? "" : $"{firstRead}R")}{(firstSet == null ? "" : $"{firstSet}S")}-{(lastUsed == null ? "" : $"{lastUsed}U")}{(lastSet == null ? "" : $"{lastSet}S")},";

                    }
                }
                return $"ID{ID}: {Name}{(Name != null ? " " : "")}{EndCommandRangeExclusive - StartCommandRange} Commands:[{StartCommandRange},{EndCommandRangeExclusive})  Sources:[{StartSourceIndices},{EndSourceIndicesExclusive}) Destinations:[{StartDestinationIndices},{EndDestinationIndicesExclusive}) CopyIncrements: {copyIncrementsToParent} VirtualStack ID: {VirtualStackID} Contents: {virtualStackContents} Stackinfo: {stackInfo} {(ChildrenParallelizable ? "In parallel:" : "")}";
            }

            public void CopyParentVirtualStack()
            {
                if (ParentVirtualStack != VirtualStack && ParentVirtualStack != null)
                {
                    //Debug.Write($"Copying stack from {ParentVirtualStackID} to {VirtualStackID}: "); 
                    foreach (int index in IndicesReadFromStack)
                    {
                        VirtualStack[index] = ParentVirtualStack[index];
                        //Debug.Write($"{index}:{VirtualStack[index]}, "); 
                    }
                    //Debug.WriteLine(""); 
                    //int stackSize = Math.Min(VirtualStack.Length, ParentVirtualStack.Length);
                    //for (int i = 0; i < stackSize; i++)
                    //    VirtualStack[i] = ParentVirtualStack[i];
                }
            }

            public void ResetIncrementsForParent()
            {
                if (CopyIncrementsToParent == null || ParentVirtualStack == null)
                    return;
                if (ReferenceEquals(VirtualStack, ParentVirtualStack))
                    return;

#if DEBUG
                Debug.WriteLine($"[RST‑BEF] slice {ID} has CopyInc = " +
                    $"{string.Join(',', CopyIncrementsToParent ?? Array.Empty<int>())}");
#endif

                foreach (int idx in CopyIncrementsToParent)
                    VirtualStack[idx] = 0;

#if DEBUG
                Debug.WriteLine($"[RST‑AFT] slice {ID} reset complete");
#endif
            }


            public void CopyIncrementsToParentIfNecessary()
            {
                // nothing to do?
                if (CopyIncrementsToParent == null || CopyIncrementsToParent.Length == 0)
                    return;

                if (ParentVirtualStack == null)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        $"[INC] childID={ID,4}  NO‑PARENT; list len={CopyIncrementsToParent.Length}");
#endif
                    return;                                 // should never happen
                }
                if (CopyIncrementsToParent != null)
                {
                    Debug.WriteLine($"[MERGE] slice {ID}  -> parentVS[{ParentVirtualStackID}]  " +
                                    $"indices={string.Join(",", CopyIncrementsToParent)}"); // DEBUG
                }

                bool sharing = ReferenceEquals(ParentVirtualStack, VirtualStack);

#if DEBUG
                System.Diagnostics.Debug.WriteLine(
                    $"[INC] childID={ID,4}  share={(sharing ? "YES" : "NO ")}  " +
                    $"copy {{{string.Join(",", CopyIncrementsToParent)}}}");
#endif

                // If we *share* the parent stack there is nothing to merge.
                // Having a non‑empty list in that case is a logic error worth flagging.
                if (sharing)
                {
                    Debug.WriteLine($"[INC‑SKIP] slice {ID} shares parent – merge skipped"); // DEBUG
                    return;
                }

                /* real merge for private‑stack children */
                foreach (int idx in CopyIncrementsToParent)
                {
                    double val = VirtualStack[idx];
                    if (val == 0) continue;                 // skip zeros (cheap)

                    // atomic add – implementation provided elsewhere in your codebase
                    Interlocking.Add(ref ParentVirtualStack[idx], val);
                }
            }

        }
    }
}
