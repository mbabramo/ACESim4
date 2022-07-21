using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public class ECTAOutcome<T> where T : IMaybeExact<T>, new()
    {
        public IMaybeExact<T>[] pay = new IMaybeExact<T>[] {  IMaybeExact<T>.Zero(),  IMaybeExact<T>.Zero() };
        public int nodeIndex;
    }
}
