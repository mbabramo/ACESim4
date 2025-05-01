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
#if OUTPUT_HOISTING_INFO
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
        return $"ID{ID}: {Name}{(Name != null ? " " : "")}{EndCommandRangeExclusive - StartCommandRange} Commands:[{StartCommandRange},{EndCommandRangeExclusive})  Sources:[{StartSourceIndices},{EndSourceIndicesExclusive}) Destinations:[{StartDestinationIndices},{EndDestinationIndicesExclusive}) VirtualStack ID: {VirtualStackID} Contents: {virtualStackContents} Stackinfo: {stackInfo} {(ChildrenParallelizable ? "In parallel:" : "")}";
    }

    public void CopyParentVirtualStack()
    {
        // copy only when stacks are distinct and a parent exists – original guard
        if (ParentVirtualStack != VirtualStack && ParentVirtualStack != null)
        {
#if OUTPUT_HOISTING_INFO
            TabbedText.WriteLine(
                $"[CPS‑BEGIN] child={ID,4}  parentVS={ParentVirtualStackID,4}  "
                + $"indices=[{string.Join(",", IndicesReadFromStack)}]");
#endif

            foreach (int index in IndicesReadFromStack)
            {
                double before = VirtualStack[index];
                double src = ParentVirtualStack[index];

                VirtualStack[index] = src;            // ← original assignment

#if OUTPUT_HOISTING_INFO
                TabbedText.WriteLine($"   • idx={index}  {before} → {src}");
#endif
            }

#if OUTPUT_HOISTING_INFO
            TabbedText.WriteLine(
                $"[CPS‑END]   child={ID,4}  vs0={(VirtualStack.Length > 0 ? VirtualStack[0].ToString() : "∅")}");
#endif
        }
    }

#if OUTPUT_HOISTING_INFO
    private static void LogMerge(int childId, double before, double delta, double after,
                                    int idx, int parentVsId)
    {
        TabbedText.WriteLine(
            $"[MERGE] child={childId} idx={idx}  +={delta}   {before}→{after}  →pVS{parentVsId}");
    }
#endif

    /// <summary>
    /// Propagates every slot listed in <see cref="CopyIncrementsToParent"/> back
    /// to <see cref="ParentVirtualStack"/> **by simple assignment**
    /// </summary>
    public void CopyVirtualStackToParent()
    {
        /* fast‑exit #2 */
        if (ParentVirtualStack == null)
            return;

        /* shared stack → nothing to propagate */
        if (ReferenceEquals(ParentVirtualStack, VirtualStack))
            return;

        for (int index = 0; index < VirtualStack.Length; index++)
            ParentVirtualStack[index] = VirtualStack[index];
    }


    public override int GetHashCode()
    {
        return (ID, StartCommandRange, EndCommandRangeExclusive).GetHashCode();
    }


}
