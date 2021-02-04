using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
	public class ECTAPayVector<T> where T : MaybeExact<T>, new()
	{
		public MaybeExact<T>[] Values;

		public ECTAPayVector(MaybeExact<T>[] values = null)
		{
			Values = values ?? new MaybeExact<T>[2] {  MaybeExact<T>.Zero(),  MaybeExact<T>.Zero() };
		}
		public MaybeExact<T> this[int index]
		{
			get => Values[index];
			set
            {
				Values[index] = value;
            }
		}

		public static implicit operator ECTAPayVector<T>(MaybeExact<T>[] d) => new ECTAPayVector<T>(d);
		public static implicit operator MaybeExact<T>[](ECTAPayVector<T> b) => b.Values;
	}
}
