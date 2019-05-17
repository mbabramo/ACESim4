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
        public GameApproximationAlgorithm Algorithm = GameApproximationAlgorithm.Vanilla;
        public int TotalAvgStrategySamplingCFRIterations = 100000;
        public int TotalProbingCFRIterations = 100000;
        public int TotalVanillaCFRIterations = 100000;
        public int? ReportEveryNIterations = 100;
        public const int EffectivelyNever = 999999999;
        public int? BestResponseEveryMIterations = 100; // For partial recall games, this is very costly, so consider using EffectivelyNever.
        public int? MiniReportEveryPIterations = 1000;
        public bool MeasureRegretMatchingChanges = false;
        public bool UseRandomPathsForReporting = true;
        public bool SerializeResults = false;

        // The following apply to  average strategy sampling. The MCCFR algorithm is not guaranteed to visit all information sets. There is a trade-off, however. When we use epsilon policy exploration, whether for the player being optimized or for the opponent, we change the dynamics of the game. Perhaps, for example, it will make sense not to take a settlement that is valuable so long as there is some small chance that the opponent will engage in policy exploration and agree to a deal that is bad for the opponent. Similarly, a player's own earlier or later exploration can affect the player's own moves; if I might make a bad move later, then maybe I should play what otherwise would be suboptimally now. 
        public bool UseEpsilonOnPolicyForOpponent = true;
        public double FirstOpponentEpsilonValue = 0.5;
        public double LastOpponentEpsilonValue = 0.05;
        public int LastOpponentEpsilonIteration = 100000;

        // The following are for exploratory probing.
        public bool PlayerBeingOptimizedExploresOnOwnIterations = false;
        public double EpsilonForMainPlayer = 0.5;
        public double EpsilonForOpponentWhenExploring = 0.05;
        public int MinBackupRegretsTrigger = 5;
        public int TriggerIncreaseOverTime = 45;

        public bool GenerateReportsByPlaying;
        public int NumRandomIterationsForSummaryTable = 10000;
        public bool PrintGameTree = false;
        public bool PrintInformationSets = false;
        public bool AnalyzeInformationSets = true; // DEBUG
        public List<int> RestrictToTheseInformationSets = null;
        public bool PrintNonChanceInformationSetsOnly = true;
        public List<ActionStrategies> ActionStrategiesToUseInReporting = new List<ActionStrategies>() { ActionStrategies.AverageStrategy };
        public int GameNumber = 0;
        internal int NumRandomIterationsForUtilityCalculation = 10000;
        internal bool SuppressReportPrinting;

        // For Vanilla algorithm:
        // From Solving Imperfect Information Games with Discounted Regret Minimization -- optimal values (for situations in which pruning may be used)
        public bool UseRegretAndStrategyDiscounting = false;
        public const double Discounting_Alpha = 1.5; // multiply accumulated positive regrets by t^alpha / (t^alpha + 1)
        public const double Discounting_Beta = 0.5; // multiply accumulated negative regrets by t^alpha / (t^alpha + 1)
        public double Discounting_Gamma = 200;  // multiply contributions to average strategy by (t / t + 1)^gamma, which approaches 1 as t -> inf. Higher gamma means more discounting. If gamma equals 20, then we still get to 80% of the maximum in a mere 100 iterations. In other words, very early iterations are heavily discounted, but after a while, there is very little discounting.
        public bool Discounting_DeriveGamma = true; // if true, gamma is derived so that at the specified proportion of iterations, the discount is the specified proportion of the discount that will exist at the maximum iteration
        public double DiscountingTarget_ProportionOfIterations = 0.25;
        public double DiscountingTarget_TargetDiscount = 0.1;
        public void CalculateGamma()
        {
            if (!Discounting_DeriveGamma)
                return;
            // we want (pt/(pt+1))^gamma = d * (t/(t+1))^gamma. 
            double p = DiscountingTarget_ProportionOfIterations;
            double t = TotalVanillaCFRIterations;

            Discounting_Gamma = Math.Log(DiscountingTarget_TargetDiscount) / (Math.Log(p * t / (p * t + 1)) - Math.Log(t / (t + 1)));
        }

        public bool RecordPastValues = true; // DEBUG -- necessary for coarse correlated equilibrium
        public int RecordPastValuesEveryN = 10;


        public const bool PruneOnOpponentStrategy = false; // NOTE: Not currently supported in hedge vanilla when unrolling algorithm. Also, not using the methods that allow us to do this on own strategy.
        public const double PruneOnOpponentStrategyThreshold = 1E-8;

        public bool DistributeChanceDecisions = true; 
        public bool UnrollAlgorithm = true; 
    }
}
