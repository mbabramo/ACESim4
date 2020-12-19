﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
	public class node
	{
		public bool terminal; // 0: decision node / 1: terminal node
		public int iset; // which information set
		public int father; // node closer to root
		public int reachedby; // move of edge from father
		public int outcome; // which outcome
		/* will be generated by  genseqin()                                 */
		public int[] defseq = new int[] { 0, 0, 0 }; // seq defd by node for each player
	}
}