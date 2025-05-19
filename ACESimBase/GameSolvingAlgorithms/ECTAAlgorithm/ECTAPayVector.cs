using ACESimBase.GameSolvingSupport.ExactValues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public class ECTAPayVector<T> where T : IMaybeExact<T>, new()
	{
		public IMaybeExact<T>[] Values;

		public ECTAPayVector(IMaybeExact<T>[] values = null)
		{
			Values = values ?? new IMaybeExact<T>[2] {  IMaybeExact<T>.Zero(),  IMaybeExact<T>.Zero() };
		}
		public IMaybeExact<T> this[int index]
		{
			get => Values[index];
			set
            {
				Values[index] = value;
            }
		}

		public static implicit operator ECTAPayVector<T>(IMaybeExact<T>[] d) => new ECTAPayVector<T>(d);
		public static implicit operator IMaybeExact<T>[](ECTAPayVector<T> b) => b.Values;
	}
}
