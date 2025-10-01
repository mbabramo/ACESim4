using System;
using System.Diagnostics;

namespace ACESimBase.Util.ArrayProcessing
{
    /// <summary>Virtual-stack index.</summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct VsIndex : IEquatable<VsIndex>, IComparable<VsIndex>
    {
        public int Value { get; }
        public VsIndex(int value) => Value = value;
        public static VsIndex Invalid => new(-1);
        public static VsIndex Zero => new(0);
        public bool Equals(VsIndex other) => Value == other.Value;
        public override bool Equals(object obj) => obj is VsIndex v && Equals(v);
        public override int GetHashCode() => Value;
        public int CompareTo(VsIndex other) => Value.CompareTo(other.Value);
        public override string ToString() => $"VS[{Value}]";
        public static implicit operator int(VsIndex x) => x.Value;
        public static implicit operator VsIndex(int v) => new(v);
    }

    /// <summary>Original-source index.</summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct OsIndex : IEquatable<OsIndex>, IComparable<OsIndex>
    {
        public int Value { get; }
        public OsIndex(int value) => Value = value;
        public static OsIndex Invalid => new(-1);
        public static OsIndex Zero => new(0);
        public bool Equals(OsIndex other) => Value == other.Value;
        public override bool Equals(object obj) => obj is OsIndex v && Equals(v);
        public override int GetHashCode() => Value;
        public int CompareTo(OsIndex other) => Value.CompareTo(other.Value);
        public override string ToString() => $"OS[{Value}]";
        public static implicit operator int(OsIndex x) => x.Value;
        public static implicit operator OsIndex(int v) => new(v);
    }

    /// <summary>Original-destination index.</summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct OdIndex : IEquatable<OdIndex>, IComparable<OdIndex>
    {
        public int Value { get; }
        public OdIndex(int value) => Value = value;
        public static OdIndex Invalid => new(-1);
        public static OdIndex Zero => new(0);
        public bool Equals(OdIndex other) => Value == other.Value;
        public override bool Equals(object obj) => obj is OdIndex v && Equals(v);
        public override int GetHashCode() => Value;
        public int CompareTo(OdIndex other) => Value.CompareTo(other.Value);
        public override string ToString() => $"OD[{Value}]";
        public static implicit operator int(OdIndex x) => x.Value;
        public static implicit operator OdIndex(int v) => new(v);
    }
}
