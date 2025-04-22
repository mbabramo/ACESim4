using ACESim.Util;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Reporting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ACESimBase.Util.ArrayProcessing;

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

    /// <summary>
    /// Propagates every slot listed in <see cref="CopyIncrementsToParent"/> back
    /// to <see cref="ParentVirtualStack"/> **by simple assignment** instead of the
    /// old “delta‑add” logic.  
    ///
    /// Rationale
    /// ---------
    /// * The old implementation tried to compute <c>delta = child - parent</c> and
    ///   then <c>parent += delta</c>.  When a child chunk itself contains *nested*
    ///   children the parent’s slot may already include earlier partial results,
    ///   so the delta can be mis‑computed and end up “undoing” work that was done
    ///   deeper in the hierarchy (that’s the fuzz‑test failure you’re seeing).  
    /// * Assigning the *final* value produced by the child chunk is both simpler
    ///   and correct: each chunk is responsible for producing the definitive
    ///   value for its covered command range, so we can just copy it upward.
    /// </summary>
    public void CopyIncrementsToParentIfNecessary()
    {
        /* fast‑exit #1 */
        if (CopyIncrementsToParent == null || CopyIncrementsToParent.Length == 0)
            return;

        /* fast‑exit #2 */
        if (ParentVirtualStack == null)
            return;

        /* shared stack → nothing to propagate */
        if (ReferenceEquals(ParentVirtualStack, VirtualStack))
            return;

#if DEBUG
        string Dump(double[] s) =>
            string.Join(", ", CopyIncrementsToParent.Select(i => $"{i}:{s[i]}"));

        TabbedText.WriteLine($"[MERGE‑BEG] child={ID} → parentVS={ParentVirtualStackID}  " +
                             $"idxs=[{string.Join("/", CopyIncrementsToParent)}]");
        TabbedText.WriteLine($"           childVals  [{Dump(VirtualStack)}]");
        TabbedText.WriteLine($"           parentVals [{Dump(ParentVirtualStack)}]");
#endif

        /* ---------- NEW LOGIC: plain assignment, no delta arithmetic ---------- */
        foreach (int idx in CopyIncrementsToParent)
            ParentVirtualStack[idx] = VirtualStack[idx];

#if DEBUG
        TabbedText.WriteLine($"[MERGE‑END] parentVals [{Dump(ParentVirtualStack)}]");
#endif
    }


    /// <summary>
    /// After <see cref="CopyIncrementsToParentIfNecessary"/> has merged the
    /// child‑chunk’s accumulated deltas into <c>ParentVirtualStack</c>, bring the
    /// *child* stack back in sync with the parent so the next execution starts
    /// from the same baseline.
    ///
    /// Old implementation wrote zeroes; that breaks any subsequent reads because
    /// those indices are no longer part of <see cref="IndicesReadFromStack"/> and
    /// therefore will not be refreshed.  Instead we copy the parent’s value.
    /// </summary>
    public void ResetIncrementsForParent()
    {
        // Fast‑exits
        if (CopyIncrementsToParent is null || CopyIncrementsToParent.Length == 0)
            return;
        if (ParentVirtualStack is null)
            return;
        if (ReferenceEquals(VirtualStack, ParentVirtualStack))
            return;   // shared stack – nothing to do

#if DEBUG
        TabbedText.WriteLine(
            $"[RST‑BEG] slice={ID,4}  syncing idxs=[{string.Join(",", CopyIncrementsToParent)}]");
#endif

        // Sync every slot that was merged back to the parent
        foreach (int idx in CopyIncrementsToParent)
            VirtualStack[idx] = ParentVirtualStack[idx];

#if DEBUG
        TabbedText.WriteLine($"[RST‑END] slice={ID,4}");
#endif
    }


    public override int GetHashCode()
    {
        return (ID, StartCommandRange, EndCommandRangeExclusive).GetHashCode();
    }


}
