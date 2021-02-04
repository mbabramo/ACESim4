using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public class ECTAOutcome<T> where T : MaybeExact<T>, new()
    {
        public MaybeExact<T>[] pay = new MaybeExact<T>[] {  MaybeExact<T>.Zero(),  MaybeExact<T>.Zero() };
        public int nodeIndex;
    }
}
