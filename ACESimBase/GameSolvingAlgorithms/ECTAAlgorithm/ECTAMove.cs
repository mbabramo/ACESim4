﻿using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public class ECTAMove
	{
		public int atiset = -1; // where this move emanates from
		public Rational behavprob = new Rational(); // behavior probability
		public Rational realprob = new Rational(); // realization probability
		public int redsfcol; // column of reduced sequence form
		/* for NF computation                                               */
		public int ncompat; // number of compatible partial strats
		public int offset; // number of partial strats for moves
		/* to the right of this at same iset    */
	}
}