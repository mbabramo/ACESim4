using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
	public class ECTAPayVector
	{
		public ExactValue[] Values;

		public ECTAPayVector(ExactValue[] values = null)
		{
			Values = values ?? new ExactValue[2] { new ExactValue(), new ExactValue() };
		}
		public ExactValue this[int index]
		{
			get => Values[index];
			set
            {
				Values[index] = value;
            }
		}

		public static implicit operator ECTAPayVector(ExactValue[] d) => new ECTAPayVector(d);
		public static implicit operator ExactValue[](ECTAPayVector b) => b.Values;
	}
}
