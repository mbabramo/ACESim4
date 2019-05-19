﻿using ACESim.Util;
using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;

namespace ACESimBase.Util.ArrayProcessing
{
    public partial class ArrayCommandList
    {
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
            public int?[] FirstReadFromStack, FirstSetInStack, LastSetInStack, LastUsed, TranslationToLocalIndex;
            public int[] IndicesReadFromStack;
            public int[] IndicesInitiallySetInStack;
            public double[] ParentVirtualStack;
            public int ParentVirtualStackID;
            public string CompiledCode;
            internal string Name;
            internal int[] CopyIncrementsToParent;
            public bool Skip;

            public ArrayCommandChunk()
            {
                ID = NextID++;
            }


            public override string ToString()
            {
                return $"ID{ID}: {Name}{(Name != null ? " " : "")}{EndCommandRangeExclusive - StartCommandRange} Commands:[{StartCommandRange},{EndCommandRangeExclusive})  Sources:[{StartSourceIndices},{EndSourceIndicesExclusive}) Destinations:[{StartDestinationIndices},{EndDestinationIndicesExclusive}) VirtualStackID {VirtualStackID} {(ChildrenParallelizable ? "In parallel:" : "")}";
            }

            public void CopyParentVirtualStack()
            {
                if (ParentVirtualStack != VirtualStack && ParentVirtualStack != null)
                {
                    //System.Diagnostics.Debug.WriteLine($"Copying stack from {ParentVirtualStackID} to {VirtualStackID}");
                    foreach (int index in IndicesReadFromStack)
                        VirtualStack[index] = ParentVirtualStack[index];
                    //int stackSize = Math.Min(VirtualStack.Length, ParentVirtualStack.Length);
                    //for (int i = 0; i < stackSize; i++)
                    //    VirtualStack[i] = ParentVirtualStack[i];
                }
            }

            public void ResetIncrementsForParent()
            {
                if (ParentVirtualStack != VirtualStack && ParentVirtualStack != null && CopyIncrementsToParent != null)
                {
                    foreach (int index in CopyIncrementsToParent)
                    {
                        VirtualStack[index] = 0;
                    }
                }
            }

            public void CopyIncrementsToParentIfNecessary()
            {
                if (ParentVirtualStack != VirtualStack && ParentVirtualStack != null && CopyIncrementsToParent != null)
                {
                    //Debug.WriteLine($"Copying increments for chunk {ID} {String.Join(",", CopyIncrementsToParent.Select(x => VirtualStack[x]))}");
                    foreach (int index in CopyIncrementsToParent)
                    {
                        double value = VirtualStack[index];
                        if (value != 0)
                            Interlocking.Add(ref ParentVirtualStack[index], value);
                    }
                }
            }
        }
    }
}
