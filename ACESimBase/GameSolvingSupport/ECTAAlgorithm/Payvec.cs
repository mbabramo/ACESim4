using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
	public class Payvec
	{
		public Rat[] Values;

		public Payvec(Rat[] values = null)
		{
			Values = values ?? new Rat[2] { new Rat(), new Rat() };
		}
		public Rat this[int index]
		{
			get => Values[index];
			set
            {
				Values[index] = value;
            }
		}

		public static implicit operator Payvec(Rat[] d) => new Payvec(d);
		public static implicit operator Rat[](Payvec b) => b.Values;
	}
}
