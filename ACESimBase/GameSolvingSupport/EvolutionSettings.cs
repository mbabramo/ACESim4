using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class EvolutionSettings
    {
        public bool AzureEnabled = false;
        public bool ParallelOptimization = false; // will be overridden by launcher
        public int MaxParallelDepth = 3; // will be overridden by launcher
        public GameApproximationAlgorithm Algorithm = GameApproximationAlgorithm.RegretMatching; // also will be overridden
        public int TotalAvgStrategySamplingCFRIterations = 100000;
        public int TotalProbingCFRIterations = 100000;
        public int TotalIterations = 100000;
        public int? ReportEveryNIterations = 1000;
        public int CorrelatedEquilibriumCalculationsEveryNIterations = 100000;
        public const int EffectivelyNever = 999999999;
        public int? BestResponseEveryMIterations = 100; // For partial recall games, this is very costly, so consider using EffectivelyNever.
        public bool UseAcceleratedBestResponse = false; // DEBUG
        DEBUG; // why isn't accelerated best response producing correct results with damages uncertainty? is it producing correct results in other situations?
        public bool CalculatePerturbedBestResponseRefinement = false;
        public double PerturbationForBestResponseCalculation = 0.001;
        public int? MiniReportEveryPIterations = 1000;
        public bool MeasureRegretMatchingChanges = false;
        public bool UseRandomPathsForReporting = true;
        public bool SerializeResults = true;
        public bool SerializeInformationSetDataOnly = true;
        public string SerializeResultsPrefix = "serstrat";
        public string SerializeResultsPrefixPlus(int scenario, int totalScenarios) => SerializeResultsPrefix + (totalScenarios > 0 ? scenario.ToString() : "");

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
        public bool AnalyzeInformationSets = false; 
        public List<int> RestrictToTheseInformationSets = null;
        public bool PrintNonChanceInformationSetsOnly = true;
        public List<ActionStrategies> ActionStrategiesToUseInReporting = new List<ActionStrategies>() { ActionStrategies.AverageStrategy };
        public int GameNumber = 0;
        internal int NumRandomIterationsForUtilityCalculation = 10000;
        internal bool SuppressReportDisplayOnScreen;

        public int? IterationsForWarmupScenario = 1; // applicable only to fictitious self-play

        public static bool PruneOnOpponentStrategy = true; // NOTE: In general sum games, this seems to cause difficulties, because some of the player's own information sets may not be visited, as a result of pruning on opponents' sets. 
        public static double PruneOnOpponentStrategyThreshold = 1E-4; // NOTE: This is the probability for this action, not the cumulative probability. 
        public static bool PredeterminePrunabilityBasedOnRelativeContributions = false; // if True, then we prune if and only if the action contributes negligibly at any later information set

        public bool CFRBR = false; // if true, opponent plays best response

        public bool DistributeChanceDecisions = true; // NOTE: This is currently very slow when using full game tree.
        public bool UnrollAlgorithm = true;

        // For Vanilla algorithm:
        // From Solving Imperfect Information Games with Discounted Regret Minimization -- optimal values (for situations in which pruning may be used)
        public bool UseDiscounting = false; // Note: This might be helpful sometimes for multiplicative weights
        public bool DiscountRegrets = false; // if true, Discounting_Alpha and Discounting_Beta are used -- note never currently used in MultiplicativeWeightsVanilla
        public const double Discounting_Alpha = 1.5; // multiply accumulated positive regrets by t^alpha / (t^alpha + 1)
        public const double Discounting_Beta = 0.5; // multiply accumulated negative regrets by t^alpha / (t^alpha + 1)
        public double Discounting_Gamma = 200;  // multiply contributions to average strategy by (t / t + 1)^gamma, which approaches 1 as t -> inf. Higher gamma means more discounting. If gamma equals 20, then we still get to 80% of the maximum in a mere 100 iterations. In other words, very early iterations are heavily discounted, but after a while, there is very little discounting.

        public double DiscountingTarget_ConstantAfterProportionOfIterations = 0.10; // set to 1.0 to make it so that discounting occurs all the time (albeit at lower rates pursuant to Gamma)

        public double Discounting_Gamma_ForIteration(int iteration)
        {
            if (!UseDiscounting)
                return 1.0;
            if (iteration > StopDiscountingAtIteration)
                iteration = StopDiscountingAtIteration;
            return Math.Pow((double)iteration / (double)(iteration + 1), Discounting_Gamma);
        }

        public double Discounting_Gamma_AsPctOfMax(int iteration) => Discounting_Gamma_ForIteration(iteration) / Discounting_Gamma_ForIteration(StopDiscountingAtIteration);
        public bool Discounting_DeriveGamma = true; // if true, gamma is derived so that at the specified proportion of iterations, the discount is the specified proportion of the discount that will exist at the maximum iteration
        public double DiscountingTarget_ProportionOfIterations = 0.25;
        public double DiscountingTarget_TargetDiscount = 0.001;
        public int StopDiscountingAtIteration => (int)(TotalIterations * DiscountingTarget_ConstantAfterProportionOfIterations);
        public void CalculateGamma()
        {
            if (!Discounting_DeriveGamma)
                return;
            // we want (pt/(pt+1))^gamma = d * (t/(t+1))^gamma. 
            double p = DiscountingTarget_ProportionOfIterations;
            double t = TotalIterations;

            Discounting_Gamma = Math.Log(DiscountingTarget_TargetDiscount) / (Math.Log(p * t / (p * t + 1)) - Math.Log(t / (t + 1)));
        }

        public bool GeneralizedVanillaAddTremble = false;

        public double MultiplicativeWeightsInitial = 0.5;
        public double MultiplicativeWeightsFinal = 0.5;
        public double MultiplicativeWeightsCurvature = 1.0;
        public double MultiplicativeWeightsEpsilon_BasedOnCurve(int iteration, int maxIteration) => MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(MultiplicativeWeightsInitial, MultiplicativeWeightsFinal, MultiplicativeWeightsCurvature, ((double)(iteration - 1)) / (double)maxIteration);

        public int SimulatedAnnealingEveryNIterations = EffectivelyNever;
        public double SimulatedAnnealingInitialAcceptance = 0.5;
        public double SimulatedAnnealingEventualAcceptance = 0.001;
        public double SimulatedAnnealingCurvature = 10.0;

        public bool SimulatedAnnealing_UseRandomWeights = false;
        public bool SimulatedAnnealing_UseRandomAverageStrategyAdjustment = false;

        public int SimulatedAnnealingSet(int iteration) => (iteration - 1) / SimulatedAnnealingEveryNIterations;
        public bool AcceptSimulatedAnnealingIfWorse(int iteration, int maxIteration)
        {
            double currentProbabilityOfAcceptance = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(SimulatedAnnealingInitialAcceptance, SimulatedAnnealingEventualAcceptance, SimulatedAnnealingCurvature, (double)iteration / (double)maxIteration);
            double r = new ConsistentRandomSequenceProducer(97).GetDoubleAtIndex(SimulatedAnnealingSet(iteration));
            return r < currentProbabilityOfAcceptance;
        }

        public double SimulatedAnnealing_RandomAverageStrategyAdjustment(int iteration, InformationSetNode informationSet)
        {
            double currentTotal = informationSet.BackupAverageStrategyAdjustmentsSum;
            if (currentTotal == 0)
                return 1.0;
            double r = new ConsistentRandomSequenceProducer(19).GetDoubleAtIndex(SimulatedAnnealingSet(iteration));
            double targetTotal = r * currentTotal;
            double targetPerIteration = targetTotal / (double) SimulatedAnnealingEveryNIterations;
            return targetPerIteration;
        }
        public double MultiplicativeWeightsEpsilon_SimulatedAnnealing(int iteration, int maxIteration) => new ConsistentRandomSequenceProducer(0).GetDoubleAtIndex(SimulatedAnnealingSet(iteration)); // a random number based on the simulated annealing group

        public double MultiplicativeWeightsEpsilon(int iteration, int maxIteration) => SimulatedAnnealingEveryNIterations == EffectivelyNever || !SimulatedAnnealing_UseRandomWeights ? MultiplicativeWeightsEpsilon_BasedOnCurve(iteration, maxIteration) : MultiplicativeWeightsEpsilon_SimulatedAnnealing(iteration, maxIteration);

        public void ChangeMultiplicativeWeightsEpsilon(bool moreAggressive)
        {

        }

        public double PerturbationInitial = 0.001; // should use with regret matching
        public double PerturbationFinal = 0.0;
        public double PerturbationCurvature = 5.0;
        public double Perturbation_BasedOnCurve(int iteration, int maxIteration) => MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(PerturbationInitial, PerturbationFinal, PerturbationCurvature, ((double)(iteration - 1)) / (double)maxIteration);

        public bool UseCFRPlusInRegretMatching = true; // if true, then cumulative regrets never fall below zero

        public bool RecordPastValues = false;
        public double RecordPastValues_AfterProportion = 0.75;
        public int RecordPastValues_TargetNumberToRecord = 1000;
        public int RecordPastValues_NumberToRecord => Math.Min(RecordPastValues_TargetNumberToRecord, TotalIterations - RecordPastValues_EarliestPossibleIteration + 1);

        public BitArray RecordPastValues_Iterations;
        public int RecordPastValues_EarliestPossibleIteration => (int)(RecordPastValues_AfterProportion * TotalIterations);

        public bool RecordPastValues_AtIteration(int iteration)
        {
            if (!RecordPastValues)
                return false;
            int earliestPossible = RecordPastValues_EarliestPossibleIteration;
            if (iteration < earliestPossible)
                return false;
            if (RecordPastValues_Iterations == null)
            { // choose iterations to record at random past proportion
                lock (this)
                {
                    if (RecordPastValues_Iterations == null)
                    {
                        int numEligibleIterations = TotalIterations - RecordPastValues_EarliestPossibleIteration + 1;
                        RecordPastValues_Iterations = new BitArray(numEligibleIterations);
                        int numSelected = 0;
                        ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(17);
                        int recordPastValues_NumberToRecord = RecordPastValues_NumberToRecord;
                        while (numSelected < recordPastValues_NumberToRecord)
                        {
                            int selection = r.NextInt(numEligibleIterations);
                            if (RecordPastValues_Iterations.Get(selection) == false)
                            {
                                RecordPastValues_Iterations.Set(selection, true);
                                numSelected++;
                            }
                        }
                    }
                }
            }
            return RecordPastValues_Iterations.Get(iteration - earliestPossible);
        }
    }
}
