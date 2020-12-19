using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public class move
	{
		public iset atiset; // where this move emanates from
		public Rat behavprob = new Rat(); // behavior probability
		public Rat realprob = new Rat(); // realization probability
		public int redsfcol; // column of reduced sequence form
		/* for NF computation                                               */
		public int ncompat; // number of compatible partial strats
		public int offset; // number of partial strats for moves
		/* to the right of this at same iset    */
	}
}
