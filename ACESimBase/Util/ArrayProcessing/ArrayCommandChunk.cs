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

            public void CopyIncrementsToParentIfNecessary()
            {
                // ── fast‑exit guards ───────────────────────────────────────────────────────
                if (CopyIncrementsToParent == null || CopyIncrementsToParent.Length == 0)
                    return;
                if (ParentVirtualStack == null)
                {
                    Debug.WriteLine($"[INC‑SKIP] child={ID}  NO‑PARENT");
                    return;
                }

                bool sharing = ReferenceEquals(ParentVirtualStack, VirtualStack);
                if (sharing)
                {
                    // Having a list here is unexpected and could lead to double‑adds later.
                    Debug.WriteLine($"[INC‑WARN] child={ID} shares parent stack yet " +
                                    $"CopyIncrements is non‑empty → NO merge performed");
                    return;
                }

                // ── verbose, but extremely helpful when tracking duplicate merges ─────────
                string DumpValues(double[] stack, IEnumerable<int> idxs) =>
                    string.Join(", ", idxs.Select(i => $"{i}:{stack[i]}"));

                Debug.WriteLine($"[MERGE‑BEG] child={ID} → parentVS={ParentVirtualStackID}  " +
                                $"idxs=[{string.Join(",", CopyIncrementsToParent)}]");
                Debug.WriteLine($"           childVals  [{DumpValues(VirtualStack, CopyIncrementsToParent)}]");
                Debug.WriteLine($"           parentVals [{DumpValues(ParentVirtualStack, CopyIncrementsToParent)}]");

                // ── real work ─────────────────────────────────────────────────────────────
                foreach (int idx in CopyIncrementsToParent)
                {
                    double delta = VirtualStack[idx];
                    if (delta == 0) continue;

                    double before = ParentVirtualStack[idx];
                    Interlocking.Add(ref ParentVirtualStack[idx], delta);
                    double after = ParentVirtualStack[idx];

                    Debug.WriteLine($"   • idx={idx}  +={delta}   {before} → {after}");
                }

                Debug.WriteLine($"[MERGE‑END] parentVals [{DumpValues(ParentVirtualStack, CopyIncrementsToParent)}]");
            }

            public void ResetIncrementsForParent()
            {
                if (CopyIncrementsToParent == null || ParentVirtualStack == null)
                    return;
                if (ReferenceEquals(VirtualStack, ParentVirtualStack))
                    return;     // nothing to clear when stacks are shared

                Debug.WriteLine($"[RST‑BEG] slice={ID}  zeroing idxs=" +
                                $"[{string.Join(",", CopyIncrementsToParent)}]");

                foreach (int idx in CopyIncrementsToParent)
                    VirtualStack[idx] = 0;

                Debug.WriteLine($"[RST‑END] slice={ID} reset complete");
            }



        }
    }
}
