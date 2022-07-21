using Rationals;
using System;

namespace ACESimBase.GameSolvingSupport
{
    public interface IMaybeExact<T> : IComparable<T> where T : IMaybeExact<T>, new()
    {
        double AsDouble { get; }
        Rational AsRational { get; }
        IMaybeExact<T> Denominator { get; }
        bool IsExact { get; }
        IMaybeExact<T> Numerator { get; }

        IMaybeExact<T> DividedBy(IMaybeExact<T> b);
        int GetHashCode();
        bool IsEqualTo(IMaybeExact<T> b);
        bool IsGreaterThan(IMaybeExact<T> b);
        bool IsLessThan(IMaybeExact<T> b);
        bool IsCloseTo(IMaybeExact<T> b, IMaybeExact<T> absoluteDistance) => absoluteDistance.IsZero() ? IsEqualTo(b) : this.Minus(b).AbsoluteValue().IsLessThan(absoluteDistance);
        bool IsNegative();
        bool IsNotEqualTo(IMaybeExact<T> b);
        bool IsOne();
        bool IsPositive();
        bool IsZero();
        IMaybeExact<T> LeastCommonMultiple(IMaybeExact<T> b);
        IMaybeExact<T> Minus(IMaybeExact<T> b);
        IMaybeExact<T> Negated();
        IMaybeExact<T> AbsoluteValue() => IsNegative() ? this.Negated() : this;
        IMaybeExact<T> Plus(IMaybeExact<T> b);
        IMaybeExact<T> Times(IMaybeExact<T> b);
        IMaybeExact<T> NewValueFromInteger(int i);
        IMaybeExact<T> NewValueFromRational(Rational r);
        public static IMaybeExact<T> Zero() => IMaybeExact<T>.FromInteger(0);
        public static IMaybeExact<T> One() => IMaybeExact<T>.FromInteger(1);
        public static IMaybeExact<T> FromInteger(int i)
        {
            var x = new T();
            return x.NewValueFromInteger(i);
        }
        public static IMaybeExact<T> FromRational(Rational r)
        {
            var x = new T();
            return x.NewValueFromRational(r);
        }
    }
}