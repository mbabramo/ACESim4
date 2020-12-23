using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
	public class ECTAPayVector
	{
		public Rational[] Values;

		public ECTAPayVector(Rational[] values = null)
		{
			Values = values ?? new Rational[2] { new Rational(), new Rational() };
		}
		public Rational this[int index]
		{
			get => Values[index];
			set
            {
				Values[index] = value;
            }
		}

		public static implicit operator ECTAPayVector(Rational[] d) => new ECTAPayVector(d);
		public static implicit operator Rational[](ECTAPayVector b) => b.Values;
	}
}
