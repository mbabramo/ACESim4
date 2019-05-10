using ACESim.Util;
using System;
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
            public double[] ParentVirtualStack;
            public int ParentVirtualStackID;
            internal string Name;
            internal int[] CopyIncrementsToParent;

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
                    System.Diagnostics.Debug.WriteLine($"Copying stack from {ParentVirtualStackID} to {VirtualStackID}");
                    int stackSize = Math.Min(VirtualStack.Length, ParentVirtualStack.Length);
                    for (int i = 0; i < stackSize; i++)
                        VirtualStack[i] = ParentVirtualStack[i];
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
                    Debug.WriteLine($"Copying increments for chunk {ID} {String.Join(",", CopyIncrementsToParent.Select(x => VirtualStack[x]))}"); // DEBUG
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
