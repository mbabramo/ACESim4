using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
	public class Payvec
	{
		public Rational[] Values;

		public Payvec(Rational[] values = null)
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

		public static implicit operator Payvec(Rational[] d) => new Payvec(d);
		public static implicit operator Rational[](Payvec b) => b.Values;
	}
}
