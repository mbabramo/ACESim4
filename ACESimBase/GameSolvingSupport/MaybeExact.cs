using Rationals;

namespace ACESimBase.GameSolvingSupport
{
    public interface MaybeExact<T> where T : MaybeExact<T>, new()
    {
        double AsDouble { get; }
        Rational AsRational { get; }
        MaybeExact<T> Denominator { get; }
        bool IsExact { get; }
        MaybeExact<T> Numerator { get; }

        MaybeExact<T> DividedBy(MaybeExact<T> b);
        int GetHashCode();
        bool IsEqualTo(MaybeExact<T> b);
        bool IsGreaterThan(MaybeExact<T> b);
        bool IsLessThan(MaybeExact<T> b);
        bool IsCloseTo(MaybeExact<T> b, MaybeExact<T> absoluteDistance) => absoluteDistance.IsZero() ? IsEqualTo(b) : this.Minus(b).AbsoluteValue().IsLessThan(absoluteDistance);
        bool IsNegative();
        bool IsNotEqualTo(MaybeExact<T> b);
        bool IsOne();
        bool IsPositive();
        bool IsZero();
        MaybeExact<T> LeastCommonMultiple(MaybeExact<T> b);
        MaybeExact<T> Minus(MaybeExact<T> b);
        MaybeExact<T> Negated();
        MaybeExact<T> AbsoluteValue() => IsNegative() ? this.Negated() : this;
        MaybeExact<T> Plus(MaybeExact<T> b);
        MaybeExact<T> Times(MaybeExact<T> b);
        MaybeExact<T> NewValueFromInteger(int i);
        MaybeExact<T> NewValueFromRational(Rational r);
        public static MaybeExact<T> Zero() => MaybeExact<T>.FromInteger(0);
        public static MaybeExact<T> One() => MaybeExact<T>.FromInteger(1);
        public static MaybeExact<T> FromInteger(int i)
        {
            var x = new T();
            return x.NewValueFromInteger(i);
        }
        public static MaybeExact<T> FromRational(Rational r)
        {
            var x = new T();
            return x.NewValueFromRational(r);
        }
    }
}