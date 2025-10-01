using System;
using System.Collections.Generic;
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
        public static explicit operator int(VsIndex x) => x.Value;
        public static explicit operator VsIndex(int v) => new(v);
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
        public static explicit operator int(OsIndex x) => x.Value;
        public static explicit operator OsIndex(int v) => new(v);
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
        public static explicit operator int(OdIndex x) => x.Value;
        public static explicit operator OdIndex(int v) => new(v);
    }

    public static class IndexExtensions
    {
        // “Make” typed indices from ints
        public static VsIndex Vs(this int v) => new VsIndex(v);
        public static OsIndex Os(this int v) => new OsIndex(v);
        public static OdIndex Od(this int v) => new OdIndex(v);

        // Extract raw values
        public static int Val(this VsIndex v) => v.Value;
        public static int Val(this OsIndex v) => v.Value;
        public static int Val(this OdIndex v) => v.Value;
    }

    public static class ListIndexExtensions
    {
        public static void Add(this List<OsIndex> list, int v) => list.Add(new OsIndex(v));
        public static void Add(this List<OdIndex> list, int v) => list.Add(new OdIndex(v));
        public static void Add(this List<VsIndex> list, int v) => list.Add(new VsIndex(v));
    }
}
