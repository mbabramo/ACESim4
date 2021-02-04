using ACESimBase.GameSolvingSupport;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Numerics;
using System.Text;
using static ACESimBase.Util.CPrint;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public static class ExactValueOperations
    {
		public static bool AbbreviateExactValues = false; // DEBUG

		public static bool IsPositive(ExactValue a)
		{
			return a.GreaterThan(0);
		}
		public static bool IsNegative(ExactValue a)
		{
			return a.LessThan(0);
		}

		public static bool IsZero(ExactValue a)
		{
			return a.IsZero();
		}

		public static bool IsOne(ExactValue a)
		{
			return a.IsOne();
		}

		public static void ChangeSign(ref ExactValue a)
		{
			a.ChangeSign();
		}

		public static string ToStringForTable(this ExactValue x)
        {
			string s = x.ToString();
			return s;
        }

		public static ExactValue LeastCommonMultiple(ExactValue a, ExactValue b)
		/* a = least common multiple of a, b; b is preserved */
		{
			var result = a.LeastCommonMultiple(b);
			return result;
		}

		public static bool GreaterThan(ExactValue a, ExactValue b)
		{
			return a.GreaterThan(b);
		}


		public static ExactValue Multiply(ExactValue a, ExactValue b)
		{
			return a.Multiply(b);
		}


		public static ExactValue Divide(ExactValue a, ExactValue b)
        {
			return a.Divide(b);
        }
	}
}
