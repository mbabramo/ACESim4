using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public class ECTAMove<T> where T : MaybeExact<T>, new()
	{
		public int priorInformationSet = -1; // where this move emanates from
		public MaybeExact<T> behavioralProbability =  MaybeExact<T>.Zero(); // behavior probability
		public MaybeExact<T> realizationProbability =  MaybeExact<T>.Zero(); // realization probability
		public int redsfcol; // column of reduced sequence form
		/* for NF computation                                               */
		public int ncompat; // number of compatible partial strats
		public int offset; // number of partial strats for moves
		/* to the right of this at same iset    */
	}
}
