using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public readonly struct InexactValue : MaybeExact<InexactValue>
    {
        public static double Tolerance = 1E-50;

        private readonly double V;

        public InexactValue(int i)
        {
            V = (double)i;
        }

        public InexactValue(Rational r)
        {
            V = (double) r;
        }

        public InexactValue(double d)
        {
            V = d;
        }

        public MaybeExact<InexactValue> NewValueFromInteger(int i) => new InexactValue(i);
        public MaybeExact<InexactValue> NewValueFromRational(Rational r) => new InexactValue(r);
        public static MaybeExact<InexactValue> FromInteger(int i) => new InexactValue((Rational)i);
        public static MaybeExact<InexactValue> FromRational(Rational r) => new InexactValue(r);
        public static MaybeExact<InexactValue> FromDouble(double d) => new InexactValue(d);

        static MaybeExact<InexactValue> _Zero = MaybeExact<InexactValue>.FromInteger(0);
        static MaybeExact<InexactValue> _One = MaybeExact<InexactValue>.FromInteger(1);
        public static MaybeExact<InexactValue> Zero() => _Zero;
        public static MaybeExact<InexactValue> One() => _One;

        public bool IsPositive()
        {
            return V > 0;
        }
        public bool IsNegative()
        {
            return V < 0;
        }

        public bool IsZero()
        {
            return IsEqualTo(_Zero);
        }

        public bool IsOne()
        {
            return IsEqualTo(_One);
        }

        public MaybeExact<InexactValue> Numerator => new InexactValue(V);
        public MaybeExact<InexactValue> Denominator => One();
        public double AsDouble => IsZero() ? 0 : (IsOne() ? 1.0 : (double)V);
        public Rational AsRational => throw new NotImplementedException();
        public bool IsExact => false;

        public bool IsEqualTo(MaybeExact<InexactValue> b)
        {
            //Math.Abs(V - bVal) < Tolerance;
            double bVal = ((InexactValue)b).V;
            if (bVal == 0 && V == 0)
                return true;
            if (bVal == 0)
                return false;
            double absRatioMinus1 = bVal == 0 ? 1 : Math.Abs(V / bVal - 1.0);
            return absRatioMinus1 < Tolerance;
        }
        public bool IsNotEqualTo(MaybeExact<InexactValue> b) => !IsEqualTo(b);

        public override string ToString()
        {
            string s;
            s = V.ToString();
            if (s.Length > 8 && ExactValue.AbbreviateValues)
                return V.ToString("E5");
            else
                return s;
        }

        public MaybeExact<InexactValue> LeastCommonMultiple(MaybeExact<InexactValue> b)
        /* since we don't work only in integers with this type, we just return the larger number */
        {
            if (b.AsDouble > V)
                return b;
            return this;
        }

        public bool IsGreaterThan(MaybeExact<InexactValue> b)
        {
            return V > b.AsDouble && !IsEqualTo(b); // make sure it is not within tolerance
        }

        public bool IsLessThan(MaybeExact<InexactValue> b)
        {
            return V < b.AsDouble && !IsEqualTo(b); // make sure it is not within tolerance
        }

        public MaybeExact<InexactValue> Plus(MaybeExact<InexactValue> b)
        {
            return FromDouble(V + b.AsDouble);
        }

        public MaybeExact<InexactValue> Minus(MaybeExact<InexactValue> b)
        {
            return FromDouble(V - b.AsDouble);
        }

        public MaybeExact<InexactValue> Negated() => Zero().Minus(this);

        public MaybeExact<InexactValue> Times(MaybeExact<InexactValue> b)
        {
            return FromDouble(V * b.AsDouble);
        }

        public MaybeExact<InexactValue> DividedBy(MaybeExact<InexactValue> b)
        {
            return FromDouble(V / b.AsDouble);
        }

        public override int GetHashCode()
        {
            return V.GetHashCode();
        }
    }
}
