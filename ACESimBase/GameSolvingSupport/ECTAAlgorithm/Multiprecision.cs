using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.MultiprecisionStatic;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public class Multiprecision
    {
		public long[] Values;

		public Multiprecision(long[] values = null)
        {
			Values = values ?? new long[MultiprecisionStatic.MAX_DIGITS + 1];
        }

		public static implicit operator Multiprecision(long[] d) => new Multiprecision(d);
		public static implicit operator long[](Multiprecision b) => b.Values;
	}
}
