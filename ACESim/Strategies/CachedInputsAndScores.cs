using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class CachedInputsAndScores
    {
        public List<double> Inputs;
        public double Weight;
        public double ScoreForFirstDecision;
        public double[] ScoresForSubsequentDecisions;
    }
}
