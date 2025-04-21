using ACESimBase.Util.Reporting;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util.Collections
{
    public readonly struct FloatSet
    {
        public readonly float F1, F2, F3, F4;

        public FloatSet(float f1, float f2, float f3, float f4) => (F1, F2, F3, F4) = (f1, f2, f3, f4);

        public override string ToString() => $"{F1.ToSignificantFigures(3)}, {F2.ToSignificantFigures(3)}, {F3.ToSignificantFigures(3)}, {F4.ToSignificantFigures(3)}";

        public FloatSet Plus(FloatSet other) => new FloatSet(F1 + other.F1, F2 + other.F2, F3 + other.F3, F4 + other.F4);
        public FloatSet Times(float m) => new FloatSet(m * F1, m * F2, m * F3, m * F4);

        public double[] AsDoubleArray() => new double[] { F1, F2, F3, F4 };
    }
}
