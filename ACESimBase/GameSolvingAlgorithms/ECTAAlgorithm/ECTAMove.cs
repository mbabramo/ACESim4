using ACESimBase.GameSolvingSupport.ExactValues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public class ECTAMove<T> where T : IMaybeExact<T>, new()
	{
		public int priorInformationSet = -1; // where this move emanates from
		public IMaybeExact<T> behavioralProbability =  IMaybeExact<T>.Zero(); // behavior probability
		public IMaybeExact<T> realizationProbability =  IMaybeExact<T>.Zero(); // realization probability
		public int redsfcol; // column of reduced sequence form
		/* for NF computation                                               */
		public int ncompat; // number of compatible partial strats
		public int offset; // number of partial strats for moves
		/* to the right of this at same iset    */
	}
}
