using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public struct ExactValue
    {
		public static bool AbbreviateExactValues = false;

        private Rational V;

		public ExactValue(Rational v)
		{
			V = v;
		}

		public ExactValue(BigInteger v)
        {
			V = v;
        }

		public ExactValue(int v)
        {
			V = v;
        }

		public static ExactValue Zero() => new ExactValue(0);
		public static ExactValue One() => new ExactValue(1);
		public static ExactValue FromInteger(int i) => new ExactValue(i);

		public static implicit operator ExactValue(BigInteger b) => new ExactValue(b);
		public static implicit operator ExactValue(int b) => new ExactValue(b);

		public static implicit operator ExactValue(Rational b) => new ExactValue(b);

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
			return V.IsZero;
		}

		public bool IsOne()
		{
			return V.IsOne;
		}

		public void ChangeSign()
		{
			V = -V;
		}

		public void ChangeToCanonicalForm()
        {
			V = V.CanonicalForm;
		}

		public ExactValue CanonicalForm => V.CanonicalForm;

		public ExactValue Numerator => V.Numerator;
		public ExactValue Denominator => V.Denominator;
		public double AsDouble => (double)V;
		public Rational AsRational => V;
		public bool IsExact => true;

		public bool Equality(ExactValue b) => V == b.V;

		public override string ToString()
		{
			string s;

			if (V.Denominator == 1)
			{
				s = V.ToString();
				if (s.Length > 8 && AbbreviateExactValues)
					return V.ToString("E5");
				else
					return s;
			}

			if (V.Denominator == 1)
				s = V.Numerator.ToString();
			else if (V.Denominator == 0)
				s = $"{V.Numerator}/{V.Denominator}"; // show illegal div by 0
			else
				s = V.ToString();
			return s;
		}

		public ExactValue LeastCommonMultiple(ExactValue b)
		/* a = least common multiple of a, b; b is preserved */
		{
			if (V.Denominator != 1 || b.V.Denominator != 1)
				throw new Exception("LeastCommonMultiple operation not available.");
			var result = Multiply(b).Divide(BigInteger.GreatestCommonDivisor(V.Numerator, b.V.Numerator));
			return result.CanonicalForm;
		}

		public bool GreaterThan(ExactValue b)
		{
			return V > b.V;
		}

		public bool LessThan(ExactValue b)
		{
			return V < b.V;
		}

		public ExactValue Add(ExactValue b)
		{
			return (V - b.V).CanonicalForm;
		}

		public ExactValue Subtract(ExactValue b)
		{
			return (V - b.V).CanonicalForm;
		}

		public ExactValue Negated() => Zero().Subtract(this);

		public ExactValue Multiply(ExactValue b)
		{
			return (V * b.V).CanonicalForm;
		}

		public ExactValue Divide(ExactValue b)
		{
			return (V / b.V).CanonicalForm;
		}

		public static ExactValue operator +(ExactValue a) => a;
		public static ExactValue operator -(ExactValue a) => a.Negated();

        public override bool Equals(object obj)
        {
			return this == (ExactValue)obj;
        }

        public override int GetHashCode()
        {
            return V.GetHashCode();
        }

        public static bool operator ==(ExactValue a, ExactValue b)
			=> a.Equality(b);
		public static bool operator !=(ExactValue a, ExactValue b)
			=> !a.Equality(b);

		public static ExactValue operator +(ExactValue a, ExactValue b)
			=> a.Add(b);

		public static ExactValue operator -(ExactValue a, ExactValue b)
			=> a.Subtract(b);

		public static ExactValue operator *(ExactValue a, ExactValue b)
			=> a.Multiply(b);

		public static ExactValue operator /(ExactValue a, ExactValue b)
			=> a.Divide(b);
	}
}
