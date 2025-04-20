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
#if DEBUG
            internal bool _loggedInit;
            internal bool ChildrenParallelizableLogged
            {
                get => _loggedInit;
                set => _loggedInit = value;
            }
#endif

            public static int NextID = 0;
            public int ID;
            public bool ChildrenParallelizable;
            public bool RequiresPrivateStack;
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
            internal int[] CopyIncrementsToParent
            {
                get;
                set;
            }
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
                // copy only when stacks are distinct and a parent exists – original guard
                if (ParentVirtualStack != VirtualStack && ParentVirtualStack != null)
                {
                    TabbedText.WriteLine(
                        $"[CPS‑BEGIN] child={ID,4}  parentVS={ParentVirtualStackID,4}  "
                      + $"indices=[{string.Join(",", IndicesReadFromStack)}]");

                    foreach (int index in IndicesReadFromStack)
                    {
                        double before = VirtualStack[index];
                        double src = ParentVirtualStack[index];

                        VirtualStack[index] = src;            // ← original assignment

                        TabbedText.WriteLine($"   • idx={index}  {before} → {src}");
                    }

                    TabbedText.WriteLine(
                        $"[CPS‑END]   child={ID,4}  vs0={(VirtualStack.Length > 0 ? VirtualStack[0].ToString() : "∅")}");
                }
            }

#if DEBUG
            private static void LogMerge(int childId, double before, double delta, double after,
                                         int idx, int parentVsId)
            {
                TabbedText.WriteLine(
                    $"[MERGE] child={childId} idx={idx}  +={delta}   {before}→{after}  →pVS{parentVsId}");
            }
#endif

            public void CopyIncrementsToParentIfNecessary()
            {
                /* fast‑exit #1 */
                if (CopyIncrementsToParent == null || CopyIncrementsToParent.Length == 0)
                {
#if DEBUG
                    TabbedText.WriteLine($"[INC‑SKIP] child={ID}  empty‑list");
#endif
                    return;
                }

                /* fast‑exit #2 */
                if (ParentVirtualStack == null)
                {
#if DEBUG
                    TabbedText.WriteLine($"[INC‑SKIP] child={ID}  NO‑PARENT");
#endif
                    return;
                }

                bool sharing = ReferenceEquals(ParentVirtualStack, VirtualStack);
                if (sharing)
                {
#if DEBUG
                    TabbedText.WriteLine($"[INC‑WARN] child={ID} shares parent stack yet " +
                                    $"CopyIncrements is non‑empty → NO merge performed");
#endif
                    return;
                }

#if DEBUG
                string Dump(double[] s) => string.Join(", ", CopyIncrementsToParent.Select(i => $"{i}:{s[i]}"));
                TabbedText.WriteLine($"[MERGE‑BEG] child={ID} → parentVS={ParentVirtualStackID}  " +
                                $"idxs=[{string.Join("/", CopyIncrementsToParent)}]");
                TabbedText.WriteLine($"           childVals  [{Dump(VirtualStack)}]");
                TabbedText.WriteLine($"           parentVals [{Dump(ParentVirtualStack)}]");
#endif

                foreach (int idx in CopyIncrementsToParent)
                {
                    double before = ParentVirtualStack[idx];
                    double delta = VirtualStack[idx] - before;
                    if (delta == 0) continue;

                    ParentVirtualStack[idx] = before + delta;
#if DEBUG
                    TabbedText.WriteLine($"[MERGE] chunk={ID} idx={idx}  +={delta}   {before}→{before + delta}");
#endif
                }

#if DEBUG
                TabbedText.WriteLine($"[MERGE‑END] parentVals [{Dump(ParentVirtualStack)}]");
#endif
            }



            public void ResetIncrementsForParent()
            {
                if (CopyIncrementsToParent == null || ParentVirtualStack == null)
                    return;
                if (ReferenceEquals(VirtualStack, ParentVirtualStack))
                    return;    // shared stack – nothing to clear

                TabbedText.WriteLine(
                    $"[RST‑BEG] slice={ID,4}  zeroing idxs=[{string.Join(",", CopyIncrementsToParent)}]  "
                  + $"vs0={VirtualStack[0]}");

                foreach (int idx in CopyIncrementsToParent)
                    VirtualStack[idx] = 0;                     // ← original logic

                TabbedText.WriteLine($"[RST‑END] slice={ID,4}  vs0={VirtualStack[0]}");
            }



        }
    }
}
