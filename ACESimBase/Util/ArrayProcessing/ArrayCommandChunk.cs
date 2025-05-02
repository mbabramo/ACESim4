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
    public byte LastChild;
    public int StartCommandRange, EndCommandRangeExclusive;
    public int StartSourceIndices, EndSourceIndicesExclusive;
    public int StartDestinationIndices, EndDestinationIndicesExclusive;
    public string CompiledCode;
    public string Name;
    public bool Skip; 
    public int SourcesInBody;
    public int DestinationsInBody;
    public int ExecId = int.MinValue;   // unassigned == int.MinValue 
    public double[] VirtualStack;

    public ArrayCommandChunk()
    {
        ID = NextID++;
    }

    public override string ToString()
    {
        return $"ID{ID}: {Name}{(Name != null ? " " : "")}{EndCommandRangeExclusive - StartCommandRange} Commands:[{StartCommandRange},{EndCommandRangeExclusive})";
    }

    public override int GetHashCode()
    {
        return (ID, StartCommandRange, EndCommandRangeExclusive).GetHashCode();
    }


}
