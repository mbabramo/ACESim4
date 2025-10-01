namespace ACESimBase.Util.ArrayProcessing
{
    /// <summary>Virtual-stack index.</summary>
    public readonly struct VsIndex
    {
        public int Value { get; }
        public VsIndex(int value) => Value = value;
        public override string ToString() => $"VS[{Value}]";
    }

    /// <summary>Original-source index.</summary>
    public readonly struct OsIndex
    {
        public int Value { get; }
        public OsIndex(int value) => Value = value;
        public override string ToString() => $"OS[{Value}]";
    }

    /// <summary>Ordered-destination index.</summary>
    public readonly struct OdIndex
    {
        public int Value { get; }
        public OdIndex(int value) => Value = value;
        public override string ToString() => $"OD[{Value}]";
    }
}
