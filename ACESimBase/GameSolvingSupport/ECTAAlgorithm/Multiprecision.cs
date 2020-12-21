using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.MultiprecisionStatic;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    // DEBUG: Should be able to eliminate this.
    public class Multiprecision
    {
		public BigInteger BigInt;

		public Multiprecision(BigInteger? values = null)
        {
            BigInt = values ?? new BigInteger();
        }

		public static implicit operator Multiprecision(BigInteger d) => new Multiprecision(d);
		public static implicit operator BigInteger(Multiprecision b) => b.BigInt;

        public override string ToString()
        {
            return BigInt.ToString();
        }
    }
}
