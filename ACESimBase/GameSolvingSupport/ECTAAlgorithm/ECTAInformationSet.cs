using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
	public class ECTAInformationSet // information set
	{
		/* given                    */
		public int player; // 0: chance player
		public int nmoves;
		public int move0;
		/* autoname possible                                                */
		public string name = ""; // name of iset
		/* will be generated                                                */
		public int seqin; // sequence leading to that iset
		/* for NF computation                                               */
		public int ncontin; // how many strategy-type continuations
		public int prefact; // multiplyer for later parallel isets
	}

}
