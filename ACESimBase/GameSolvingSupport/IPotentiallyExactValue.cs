using Rationals;

namespace ACESimBase.GameSolvingSupport
{
    public interface IPotentiallyExactValue<T> where T : IPotentiallyExactValue<T>
    {
        double AsDouble { get; }
        Rational AsRational { get; }
        IPotentiallyExactValue<T> CanonicalForm { get; }
        IPotentiallyExactValue<T> Denominator { get; }
        bool IsExact { get; }
        IPotentiallyExactValue<T> Numerator { get; }

        IPotentiallyExactValue<T> DividedBy(IPotentiallyExactValue<T> b);
        int GetHashCode();
        bool IsEqualTo(IPotentiallyExactValue<T> b);
        bool IsGreaterThan(IPotentiallyExactValue<T> b);
        bool IsLessThan(IPotentiallyExactValue<T> b);
        bool IsNegative();
        bool IsNotEqualTo(IPotentiallyExactValue<T> b);
        bool IsOne();
        bool IsPositive();
        bool IsZero();
        IPotentiallyExactValue<T> LeastCommonMultiple(IPotentiallyExactValue<T> b);
        IPotentiallyExactValue<T> Minus(IPotentiallyExactValue<T> b);
        IPotentiallyExactValue<T> Negated();
        IPotentiallyExactValue<T> Plus(IPotentiallyExactValue<T> b);
        IPotentiallyExactValue<T> Times(IPotentiallyExactValue<T> b);
        public static IPotentiallyExactValue<T> Zero() => IPotentiallyExactValue<T>.FromInteger(0);
        public static IPotentiallyExactValue<T> One() => IPotentiallyExactValue<T>.FromInteger(1);
        public static IPotentiallyExactValue<T> FromInteger(int i) => throw new System.Exception();
        public static IPotentiallyExactValue<T> FromRational(Rational r) => throw new System.Exception();
    }
}