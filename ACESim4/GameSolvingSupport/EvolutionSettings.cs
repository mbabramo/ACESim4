using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class EvolutionSettings
    {
        public bool ParallelOptimization = false;
        public int MaxParallelDepth = 1;
        public GameApproximationAlgorithm Algorithm = GameApproximationAlgorithm.Probing;
        public int TotalAvgStrategySamplingCFRIterations = 100000;
        public int TotalProbingCFRIterations = 100000;
        public int TotalVanillaCFRIterations = 100000;
        public int? ReportEveryNIterations = 10000;
        public const int EffectivelyNever = 999999999;
        public int? BestResponseEveryMIterations = EffectivelyNever; // For now, don't do it. This takes most of the time when dealing with partial recall games.
        public Func<Decision, GameProgress, byte> AlternativeOverride;

        // The following apply to probing and average strategy sampling. The MCCFR algorithm is not guaranteed to visit all information sets. There is a trade-off, however. When we use epsilon policy exploration, whether for the player being optimized or for the opponent, we change the dynamics of the game. Perhaps, for example, it will make sense not to take a settlement that is valuable so long as there is some small chance that the opponent will engage in policy exploration and agree to a deal that is bad for the opponent. Similarly, a player's own earlier or later exploration can affect the player's own moves; if I might make a bad move later, then maybe I should play what otherwise would be suboptimally now. 
        public bool UseEpsilonOnPolicyForOpponent = true;
        public double FirstOpponentEpsilonValue = 0.5;
        public double LastOpponentEpsilonValue = 0.05;
        public int LastOpponentEpsilonIteration = 100000;
        public bool MaxOneEpsilonExploration = true; // If true, do no more than one epsilon exploration for either player, and after doing the epsilon exploration to do no further updating of later decisions. That way, the latest decisions can be optimized and we cna work backwards. (Implemented for now only with probing)

        // The following are for Abramowicz probing.
        internal bool RemoveOldRegrets;
        public List<double> EpsilonForPhases = new List<double>() { 0.05, 0.01, 0.001, 0, 0, 0, 0, 0, 0, 0};
        public List<double> WeightsForPhases = new List<double>() { 1, 2, 4, 8, 16, 32, 32, 32, 32, 32};

        public int NumRandomIterationsForReporting = 10000;
        public bool PrintGameTreeAfterReport = false;
        public bool PrintInformationSetsAfterReport = false;
        public bool PrintNonChanceInformationSetsOnly = true;
        public bool AlwaysUseAverageStrategyInReporting = true;
    }
}
