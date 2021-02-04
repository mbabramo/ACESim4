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
        public static double Tolerance = 1E-10;

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

        public static MaybeExact<InexactValue> Zero() => InexactValue.FromInteger(0);
        public static MaybeExact<InexactValue> One() => InexactValue.FromInteger(1);
        public static MaybeExact<InexactValue> FromInteger(int i) => new InexactValue((Rational)i);
        public static MaybeExact<InexactValue> FromRational(Rational r) => new InexactValue(r);
        public static MaybeExact<InexactValue> FromDouble(double d) => new InexactValue(d);

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
            return Math.Abs(V) < Tolerance;
        }

        public bool IsOne()
        {
            return Math.Abs(V - 1.0)  < Tolerance;
        }

        public MaybeExact<InexactValue> Numerator => new InexactValue(V);
        public MaybeExact<InexactValue> Denominator => One();
        public double AsDouble => (double)V;
        public Rational AsRational => throw new NotImplementedException();
        public bool IsExact => false;

        public bool IsEqualTo(MaybeExact<InexactValue> b) => Math.Abs(V - b.AsDouble) < Tolerance;
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
