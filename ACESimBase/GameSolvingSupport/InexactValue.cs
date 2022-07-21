using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public readonly struct InexactValue : IMaybeExact<InexactValue>
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

        public IMaybeExact<InexactValue> NewValueFromInteger(int i) => new InexactValue(i);
        public IMaybeExact<InexactValue> NewValueFromRational(Rational r) => new InexactValue(r);
        public static IMaybeExact<InexactValue> FromInteger(int i) => new InexactValue((Rational)i);
        public static IMaybeExact<InexactValue> FromRational(Rational r) => new InexactValue(r);
        public static IMaybeExact<InexactValue> FromDouble(double d) => new InexactValue(d);

        static IMaybeExact<InexactValue> _Zero = IMaybeExact<InexactValue>.FromInteger(0);
        static IMaybeExact<InexactValue> _One = IMaybeExact<InexactValue>.FromInteger(1);
        public static IMaybeExact<InexactValue> Zero() => _Zero;
        public static IMaybeExact<InexactValue> One() => _One;

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

        public IMaybeExact<InexactValue> Numerator => new InexactValue(V);
        public IMaybeExact<InexactValue> Denominator => One();
        public double AsDouble => IsZero() ? 0 : (IsOne() ? 1.0 : (double)V);
        public Rational AsRational => throw new NotImplementedException();
        public bool IsExact => false;

        public bool IsEqualTo(IMaybeExact<InexactValue> b)
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
        public bool IsNotEqualTo(IMaybeExact<InexactValue> b) => !IsEqualTo(b);

        public override string ToString()
        {
            string s;
            s = V.ToString();
            if (s.Length > 8 && ExactValue.AbbreviateValues)
                return V.ToString("E5");
            else
                return s;
        }

        public IMaybeExact<InexactValue> LeastCommonMultiple(IMaybeExact<InexactValue> b)
        /* since we don't work only in integers with this type, we just return the larger number */
        {
            if (b.AsDouble > V)
                return b;
            return this;
        }

        public bool IsGreaterThan(IMaybeExact<InexactValue> b)
        {
            return V > b.AsDouble && !IsEqualTo(b); // make sure it is not within tolerance
        }

        public bool IsLessThan(IMaybeExact<InexactValue> b)
        {
            return V < b.AsDouble && !IsEqualTo(b); // make sure it is not within tolerance
        }

        public IMaybeExact<InexactValue> Plus(IMaybeExact<InexactValue> b)
        {
            return FromDouble(V + b.AsDouble);
        }

        public IMaybeExact<InexactValue> Minus(IMaybeExact<InexactValue> b)
        {
            return FromDouble(V - b.AsDouble);
        }

        public IMaybeExact<InexactValue> Negated() => Zero().Minus(this);

        public IMaybeExact<InexactValue> Times(IMaybeExact<InexactValue> b)
        {
            return FromDouble(V * b.AsDouble);
        }

        public IMaybeExact<InexactValue> DividedBy(IMaybeExact<InexactValue> b)
        {
            return FromDouble(V / b.AsDouble);
        }

        public override int GetHashCode()
        {
            return V.GetHashCode();
        }
        
        public int CompareTo(InexactValue other)
        {
            return V.CompareTo(other.V);
        }
    }
}
