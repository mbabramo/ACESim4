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
		public int[] Values;

		public Multiprecision(int[] values = null)
        {
			Values = values ?? new int[MultiprecisionStatic.MAX_DIGITS + 1];
        }

		public static implicit operator Multiprecision(int[] d) => new Multiprecision(d);
		public static implicit operator int[](Multiprecision b) => b.Values;
	}
}
