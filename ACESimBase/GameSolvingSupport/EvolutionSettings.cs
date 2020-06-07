using ACESimBase;
using ACESimBase.GameSolvingSupport;
using ACESimBase.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class EvolutionSettings
    {
        public bool DistributeChanceDecisions = true;
        public bool UnrollAlgorithm = true; 
        public bool AzureEnabled = false;
        // Note: Many of the below are overridden by launcher.
        public int TotalAvgStrategySamplingCFRIterations = 100000;
        public int TotalProbingCFRIterations = 100000;
        public int TotalIterations = 100000;
        public GameApproximationAlgorithm Algorithm = GameApproximationAlgorithm.DeepCFR; // also will be overridden by Launcher
        public double BestResponseTarget => Algorithm switch
        {
            GameApproximationAlgorithm.FictitiousPlay => -1, // 0.00001,
            GameApproximationAlgorithm.BestResponseDynamics => -1, // 0.00001,
            _ => -1 // 0.005
        }; // will end early if this target is reached
        public bool CreateInformationSetCharts = false; 
        public int? ReportEveryNIterations = 1000;
        public int? SuppressReportBeforeIteration = null;

        public bool UseAcceleratedBestResponse = true; 
        public const int EffectivelyNever = 999999999;
        public int? BestResponseEveryMIterations = 100; // For partial recall games, this is very costly, so consider using EffectivelyNever.
        public int? SuppressBestResponseBeforeIteration = null;
        public bool RememberBestResponseExploitability = true;
        public bool UseCurrentStrategyForBestResponse = true; // requires accelerated best response
        public bool CalculatePerturbedBestResponseRefinement = false;
        public double PerturbationForBestResponseCalculation = 0.001;
        public int? MiniReportEveryPIterations = 1000;
        public bool MeasureRegretMatchingChanges = false;
        public bool UseRandomPathsForReporting = true;
        public bool RoundOffLowProbabilitiesBeforeReporting = true;
        public bool RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = true; 
        public double RoundOffThreshold = 0.005;
        public bool SerializeResults = false;
        public bool SerializeInformationSetDataOnly = true;
        public string SerializeResultsPrefix = "serstrat";
        public bool ParallelOptimization = true; // will be overridden by launcher
        public bool DynamicSetParallel = false; // will be overridden by launcher
        public int MaxParallelDepth = 3; // will be overridden by launcher
        public string SerializeResultsPrefixPlus(int scenario, int totalScenarios) => SerializeResultsPrefix + (totalScenarios > 0 ? scenario.ToString() : "");

        // CORRELATED EQ SETTINGS -- MUST ALSO SET IN GAME DEFINITION
        public bool ConstructCorrelatedEquilibrium = false;
        public bool ConstructCorrelatedEquilibriumMultipleTimesExPost = true;
        public int MaxNumCorrelatedEquilibriaToConstruct = 125;
        public bool CheckCorrelatedEquilibriumIncompatibilitiesAlongWay = false;
        public int ReduceCorrelatedEquilibriumEveryNScenariosIfCheckingAlongWay = EffectivelyNever;
        public int CorrelatedEquilibriumCalculationsEveryNIterations = 100000; // Maybe obsolete -- old approach to CE

        public bool BestResponseDynamics = false;

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
        public bool PrintGameTree = false; // Note: Value here overridden in launcher
        public bool PrintInformationSets = false; // Note: Value here overridden in launcher
        public bool AnalyzeInformationSets = false; 
        public List<int> RestrictToTheseInformationSets = null;
        public bool PrintNonChanceInformationSetsOnly = true;
        public List<ActionStrategies> ActionStrategiesToUseInReporting = new List<ActionStrategies>() { ActionStrategies.CurrentProbability }; // CORRELATED EQ SETTING
        public int GameNumber = 0; // does not copy over to Launcher
        internal int NumRandomIterationsForUtilityCalculation = 10000;
        internal bool SuppressReportDisplayOnScreen;

        public static bool PruneOnOpponentStrategy = true; // NOTE: In general sum games, this seemed to cause difficulties at one point, because some of the player's own information sets may not be visited, as a result of pruning on opponents' sets. Also, note that this cannot be used with symmetric games.
        public static double PruneOnOpponentStrategyThreshold = 1E-4; // NOTE: This is the probability for this action, not the cumulative probability. Meanwhile, this is not looked at directly during the generalized tree walk. This value affects only the updating of the information sets, by setting pruned probability to the smallest value possible (a positive value about 1). Pruning will then happen in turn in the tree walk.
        public static bool PredeterminePrunabilityBasedOnRelativeContributions = false; // if True, then we prune if and only if the action contributes negligibly at any later information set

        public bool CFRBR = false; // if true, opponent plays best response
        public bool CFR_OpponentPlaysAverageStrategy = false; // typically, the opponent plays the current strategy in CFR. With this variant, the opponent plays the average strategy. This sometimes gives mildly better results, but not consistent performance improvements.
        public bool CFR_OpponentSampling = false; // if true, then we sample only 1 action at each opponent information set (but this does not apply to chance actions, as currently implemented)

        // DEEPCFR SETTINGS
        // Note: DeepCFR iterations are set in Launcher, same as vanilla iterations.
        public DeepCFRMultiModelMode DeepCFR_MultiModelMode = DeepCFRMultiModelMode.DecisionSpecific;
        public int DeepCFR_BaseReservoirCapacity = 1_000; // DEBUG // the base reservoir capacity -- if generating observations through game progress tree, we multiply this by the number of possible decisions
        public bool DeepCFR_UseGameProgressTreeToGenerateObservations = true;
        public int DeepCFR_NumProbesPerGameProgressTreeObservation = 1;
        public bool DeepCFR_MultiplyProbesForEachIdenticalIteration = true;
        public int DeepCFR_NumProbesPerGameProgressTreeObservation_Exploitability = 10;
        public bool DeepCFR_MultiplyProbesForEachIdenticalIteration_Exploitability = true;
        public bool DeepCFR_UseWeightedData = true;
        public bool DeepCFR_SeparateObservationsForIdenticalGameProgressTreeItems = true; // relevant only if UseWeightedData is false; with separate observations, the number of observations is proportional to their frequency in the regression; this takes longer but produces more accurate results
        public int DeepCFR_MaximumTotalObservationsPerIteration = 100_000; // when not using gameprogresstree, after this number of observations, we stop looking for more observations, even if we haven't gotten enough to fill as many iterations as desired in one or more reservoirs (in which case, we rely more on earlier observations)

        public bool DeepCFR_PCA_PerformPrincipalComponentAnalysis = true;
        public double DeepCFR_PCA_Precision = 1E-5;
        public int DeepCFR_PCA_FirstIterationToSaveGenotypes = 5; // DEBUG
        public int DeepCFR_PCA_SaveGenotypeEveryNIterationsAfterFirst = 1;
        public int[] DeepCFR_PCA_NumVariationsPerPrincipalComponent = new int[] { 4, 3, 2 }; // NOTE: Not currently being used; this can be used to generate non-random permutations of principal components.
        public int DeepCFR_PCA_NumPrincipalComponents = 3;
        public bool DeepCFR_PCA_BuildModelToPredictUtilitiesBasedOnPrincipalComponents = true;
        public int DeepCFR_PCA_NumUtilitiesToCalculateToBuildModel = 250; // DEBUG 1_000;
        public int DeepCFR_PCA_NumGamesToPlayToEstimateEachUtilityWhileBuildingModel = 100; // DEBUG 1_000;
        public int DeepCFR_PCA_NumStrategyChoicesPerPlayer = 1_000;

        public RegressionTechniques RegressionTechnique = RegressionTechniques.FastTree;
        public bool DeepCFR_ProbeAllActions = true;
        public int DeepCFR_NeuralNetwork_Epochs = 1_000;
        public int DeepCFR_NeuralNetwork_HiddenLayers = 3;
        public int DeepCFR_NeuralNetwork_NeuronsPerHiddenLayer = 150;
        public double DeepCFR_Epsilon_OffPolicyProbabilityForProbe = 0.05;
        public double DeepCFR_DiscountRate = 0.98;
        public bool DeepCFR_ExploitabilityProxy = false;
        public int DeepCFR_GamesForExploitabilityProxy = 10;
        public bool DeepCFR_ApproximateBestResponse = false;
        public bool DeepCFR_ApproximateBestResponse_BackwardInduction = true;
        public double DeepCFR_ApproximateBestResponse_BackwardInduction_CapacityMultiplier = 10; // if the reservoir capacity is higher for the best response, we can get a more precise measure of best response -- what matters to ensure a good result is the total number and also that there be something of a multiple
        public bool DeepCFR_ApproximateBestResponse_BackwardInduction_AlwaysPickHighestRegret = true;
        public int DeepCFR_ApproximateBestResponseIterations = 1;
        public int DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation = 10_000; 
        /// <summary>
        // With this option, we are not doing true regret matching. We are forecasting average utility for each action, 
        // rather than average regrets. The difference is that when averaging regrets, we look at relative utilities in 
        // each iteration to calculate regrets. Thus, the actions that then have high probabilities (based on previous
        // accumulated regrets) set the baseline for that iteration. If we're calculating utilities rather than regrets,
        // all we can do is just pick the best option, so we do the same thing as is dictated by the ALwaysChooseBestOption option. 
        /// </summary>
        public static bool DeepCFR_PredictUtilitiesNotRegrets = false;
        public Func<(IRegression regression, bool requiresNormalization)> RegressionFactory() => RegressionTechnique switch
        {
            RegressionTechniques.NeuralNetworkNetRegression => () => (new NeuralNetworkNetRegression(DeepCFR_NeuralNetwork_Epochs, DeepCFR_NeuralNetwork_HiddenLayers, DeepCFR_NeuralNetwork_NeuronsPerHiddenLayer), true),
            _ => () => (new MLNetRegression(RegressionTechnique), false)
        };

        // For Vanilla algorithm:
        // From Solving Imperfect Information Games with Discounted Regret Minimization -- optimal values (for situations in which pruning may be used)
        public bool UseContinuousRegretsDiscounting = true; // an alternative to discounting regrets with standard discounting approach
        public double ContinuousRegretsDiscountPerIteration => UseContinuousRegretsDiscounting ? 0.99 : 1.0; 
        public bool UseStandardDiscounting = false; // Note: This might be especially helpful sometimes for multiplicative weights
        public bool DiscountRegrets = false; // if true, Discounting_Alpha and Discounting_Beta are used -- note never currently used in MultiplicativeWeightsVanilla
        public const double Discounting_Alpha = 1.5; // multiply accumulated positive regrets by t^alpha / (t^alpha + 1)
        public const double Discounting_Beta = 0.5; // multiply accumulated negative regrets by t^alpha / (t^alpha + 1)
        public double Discounting_Gamma = 2000; // multiply contributions to average strategy by (t / t + 1)^gamma, for which ratio between iterations -> 1 as t -> inf. Higher gamma means more discounting. If gamma equals 20, then we still get to 80% of the maximum in a mere 100 iterations. In other words, very early iterations are heavily discounted, but after a while, there is very little discounting.

        public double DiscountingTarget_ConstantAfterProportionOfIterations = 0.10; // set to 1.0 to make it so that discounting occurs all the time (albeit at lower rates pursuant to Gamma)

        public double Discounting_Gamma_ForIteration(int iteration)
        {
            if (!UseStandardDiscounting)
                return 1.0;
            iteration = EffectiveIteration(iteration);
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

        public double MultiplicativeWeightsEpsilon_BasedOnCurve(int iteration, int maxIteration)
        {
            (iteration, maxIteration) = EffectiveIterationAndMaxIteration(iteration, maxIteration);
            var result = MultiplicativeWeightsEpsilon_BasedOnCurve_Helper(iteration, maxIteration);
            if (double.IsNaN(result))
                throw new Exception();
            return result;
        }
        public double MultiplicativeWeightsEpsilon_BasedOnCurve_Helper(int iteration, int maxIteration) => MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(MultiplicativeWeightsInitial, MultiplicativeWeightsFinal, MultiplicativeWeightsCurvature, ((double)(iteration - 1)) / (double)maxIteration);

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
        public double PerturbationFinal = 0; 
        public double PerturbationCurvature = 5.0;
        public double Perturbation_BasedOnCurve(int iteration, int maxIteration)
        {
            (iteration, maxIteration) = EffectiveIterationAndMaxIteration(iteration, maxIteration);
            var result = Perturbation_BasedOnCurve_Helper(iteration, maxIteration);
            if (double.IsNaN(result))
                throw new Exception();
            return result;
        }
        private double Perturbation_BasedOnCurve_Helper(int iteration, int maxIteration) => MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(PerturbationInitial, PerturbationFinal, PerturbationCurvature, ((double)(iteration - 1)) / (double)maxIteration);

        public bool UseCFRPlusInRegretMatching = false; // if true, then cumulative regrets never fall below zero

        public bool RecordPastValues = true; // must specify true for constructing correlated equilibrium
        public bool RecordPastValues_AtEndOfScenarioOnly = true;
        public int RecordPastValues_TargetNumberToRecord = 100;
        public int? RecordPastValues_AtIterationMultiples = 5_000; 
        public bool RecordPastValues_ResetAtIterationMultiples = false; 
        public int EffectiveIteration(int iteration) => (RecordPastValues && !RecordPastValues_AtEndOfScenarioOnly && RecordPastValues_AtIterationMultiples is int multiples && RecordPastValues_ResetAtIterationMultiples) ? iteration % multiples + 1 
                : iteration;
        public (int effectiveIteration, int effectiveMaxIteration) EffectiveIterationAndMaxIteration(int iteration, int maxIteration) => (RecordPastValues && !RecordPastValues_AtEndOfScenarioOnly && RecordPastValues_AtIterationMultiples is int multiples && RecordPastValues_ResetAtIterationMultiples) ? (iteration % multiples + 1, multiples) : (iteration, maxIteration);

        public bool IsIterationResetPoint(int iteration) => RecordPastValues && !RecordPastValues_AtEndOfScenarioOnly && RecordPastValues_AtIterationMultiples is int multiples && iteration % multiples == 0 && RecordPastValues_ResetAtIterationMultiples;

        /// <summary>
        /// The proportion of iterations at which to start randomly selecting past values. This will be used only if RecordPastValues_AtIterationMultiples is null.
        /// </summary>
        public double RecordPastValues_AfterProportion = 0.75;
        public int RecordPastValues_NumberToRecord => Math.Min(RecordPastValues_TargetNumberToRecord, TotalIterations - RecordPastValues_EarliestPossibleIteration + 1);

        public BitArray RecordPastValues_Iterations;
        public int RecordPastValues_EarliestPossibleIteration => (int)(RecordPastValues_AfterProportion * TotalIterations);

        public bool RecordPastValues_AtIteration(int iteration)
        {
            if (!RecordPastValues || RecordPastValues_AtEndOfScenarioOnly)
                return false;
            if (RecordPastValues_AtIterationMultiples is int multiples)
            {
                return iteration % multiples == 0;
            }
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