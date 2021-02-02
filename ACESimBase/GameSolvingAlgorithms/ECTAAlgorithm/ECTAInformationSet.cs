using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
	public class ECTAInformationSet // information set
	{
		/* given                    */
		public int playerIndex; // 0: chance player
		public int numMoves;
		public int firstMoveIndex;
		/* autoname possible                                                */
		public string name = ""; // name of iset
		/* will be generated                                                */
		/// <summary>
		/// The sequence leading to the information set. This is represented by the index of the last move by the player who owns this information set. This will always be the same in a game of perfect recall.
		/// </summary>
		public int sequence; 
	}

}
