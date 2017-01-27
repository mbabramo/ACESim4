using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// This provides information on one decision to be made in a simulation.
    /// </summary>
    [Serializable]
    public class Decision
    {
        /// <summary>
        /// The name of the decision (e.g., “Plaintiff settlement decision”).
        /// </summary>
        public String Name;

        /// <summary>
        /// An abbreviation for this name (e.g., “ps”).
        /// </summary>
        public String Abbreviation;

        /// <summary>
        /// A game-specific decision type code that can be used to provide information about the type of decision. For example, this can be
        /// used to determine whether the CurrentlyEvolvingDecision is of a particular type.
        /// </summary>
        [OptionalSetting]
        public string DecisionTypeCode;

        /// <summary>
        /// The names of the inputs (e.g., “litigation costs,” etc.), if these are preset
        /// </summary>
        public List<string> InputNames;

        /// <summary>
        /// Abbreviations for the inputs (e.g., “x”), if these are preset
        /// </summary>
        public List<string> InputAbbreviations;

        /// <summary>
        /// If this is set, the input names and abbreviations, as well as the input groups, are ignored. This allows the number of inputs to vary.
        /// </summary>
        [OptionalSetting]
        public bool DynamicNumberOfInputs = false;

        /// <summary>
        /// If true, then after the strategy is optimized, when there is only a single input (post-filtering), a lookup table is created for faster calculation of the strategy.
        /// </summary>
        [OptionalSetting]
        public bool ConvertOneDimensionalDataToLookupTable = false;

        /// <summary>
        /// If a lookup table is used, the number of points in the lookup table.
        /// </summary>
        [OptionalSetting]
        public int NumberPointsInLookupTable = 1000;

        /// <summary>
        /// If the following is true, information from optimization of the last decision will be copied rather than recreated.
        /// </summary>
        [OptionalSetting]
        public bool InputsAndOccurrencesAlwaysSameAsPreviousDecision;

        /// <summary>
        /// If true, then during the presmoothing step, we do not eliminate ineligible points. This can be useful to avoid errors from having too few poitns. (Experimental: Using oversampling is a better option.)
        /// </summary>
        [OptionalSetting]
        public bool NeverEliminateIneligiblePoints;

        /// <summary>
        /// If true, an OversamplingPlan will be developed so that we concentrate only on the portions of the input seeds range that tend to lead to this decision occurring. Weights will be produced so that oversampled observations can be devalued relative to those that produce the decision without oversampling.
        /// </summary>
        [OptionalSetting]
        public bool UseOversampling;

        [OptionalSetting]
        public bool OversamplingWillAlwaysBeSameAsPreviousDecision;

        /// <summary>
        /// When using oversampling, we may also use success replication. This is when we find (using oversampling) a sample of decisions that successfully
        /// reach the decision being optimized. We figure out which input seeds can be changed without affecting success. Then, when generating new
        /// iterations, we copy the input seeds from this sample that guaranteed success, but replace the ones that can be changed without affecting
        /// success. Once success replication is triggered for one strategy, it will automatically be used for any subsequent strategy that is a subset.
        /// </summary>
        [OptionalSetting]
        public double SuccessReplicationIfSuccessAttemptRatioIsBelowThis;

        /// <summary>
        /// If true, then the decision will be executed for each repetition in which it appears, but the subsequent repetitions after the first
        /// will simply repeat the first repetition.
        /// </summary>
        [OptionalSetting]
        public bool EvolveOnlyFirstRepetitionInExecutionOrder;

        /// <summary>
        /// If true, then the decision will be executed for each repetition in which it appears, but the repetitions before the last
        /// will simply repeat the last repetition (which may be the first to evolve).
        /// </summary>
        [OptionalSetting]
        public bool EvolveOnlyLastRepetitionInExecutionOrder;

        /// <summary>
        /// When a score represents the correct answer (i.e., we are only making a projection decision), and the inputs are exactly
        /// the same as a previous decision, we can save time by recording scores for multiple decisions simultaneously.
        /// This indicates the number of decisions FOLLOWING this one that we should also record scores for.
        /// </summary>
        [InternallyDefinedSetting]
        public int SubsequentDecisionsToRecordScoresFor;

        /// <summary>
        /// If a previous decision has SubsequentDecisionsToRecordScoresFor > 0, and this is one of the decisions that decision
        /// will record a score for, then this indicates how many decisions must be counted back to get the scores for this
        /// decision.
        /// </summary>
        [InternallyDefinedSetting]
        public int? ScoresRecordedByDecisionNPrevious;

        /// <summary>
        /// After recording decisions with the same inputs, we can cache decisions that have different inputs from this one.
        /// A group represents a consecutive set of decisions with a corresponding set of inputs.
        /// </summary>
        [InternallyDefinedSetting]
        public int NumberGroupsOfDecisionsToCache;

        /// <summary>
        /// The number of decisions to either record or cache after this one.
        /// </summary>
        [InternallyDefinedSetting]
        public int NumberDecisionsToEitherRecordOrCacheBeyondThisOne;

        /// <summary>
        /// True if we are caching decisions (beyond the current group) and this decision is first in a group of decisions to cache.
        /// </summary>
        [InternallyDefinedSetting]
        public bool DecisionIsFirstInGroupOfDecisionsToCache;

        /// <summary>
        /// True if we are caching decisions (beyond the current group) and this decision is last in a group of decisions to cache.
        /// </summary>
        [InternallyDefinedSetting]
        public bool DecisionIsLastInGroupOfDecisionsToCache;

        /// <summary>
        /// If true, this decision will not be cached (but it could still be prescored, if applicable).
        /// This should be set if the inputs for this decision cannot be determined until after an earlier decision's optimization is complete, where that earlier decision
        /// would be caching this decision.
        /// </summary>
        [InternallyDefinedSetting]
        public bool DisableCachingForThisDecision;

        /// <summary>
        /// If true, this decision will not be prescored.
        /// </summary>
        [InternallyDefinedSetting]
        public bool DisablePrescoringForThisDecision;

        /// <summary>
        /// If true, any evolution of a strategy corresponding to this decision will be repeated the specified number of times (individually, rather
        /// than as part of a group).
        /// </summary>
        [OptionalSetting]
        public int? RepeatEvolutionNTimes;

        /// <summary>
        /// Groups of inputs, so that separate strategies will be developed for each group. If omitted,
        /// then single strategy with all inputs will be developed.
        /// </summary>
        [OptionalSetting]
        public List<InputGroup> InputGroups;

        /// <summary>
        /// The bounds for the decision.
        /// </summary>
        public StrategyBounds StrategyBounds;

        /// <summary>
        /// Instead of clustering based on sample inputs, sample based on the strategy bounds. This should be chosen only if the inputs are one-dimensional, and only when using OptimizePointsAndSmooth.
        /// </summary>
        public bool ProduceClusteringPointsEvenlyFrom0To1;

        /// <summary>
        /// True if the decision can only take the value -1 or 1.
        /// </summary>
        [OptionalSetting]
        public bool Bipolar;

        /// <summary>
        /// True if the decision is a zero-dimensional pure cutoff, with the strategy optimization determining a value to compare to a cutoff variable. 
        /// A Cutoff decision should, when evolving during game preparation, set the CutoffVariable value of GameProgress.
        /// A Cutoff decision must be zero-dimensional.
        /// </summary>
        [OptionalSetting]
        public bool Cutoff;

        /// <summary>
        /// If this is a cutoff decision, then true would indicate that the relevant action (e.g., dropping a case) is taking to the left of the cutoff value.
        /// </summary>
        [OptionalSetting]
        public bool CutoffPositiveOneIsPlayedToLeft;

        /// <summary>
        /// If the decision is bipolar, then the algorithm will do a much larger number of iterations to look for more iterations whose decision inputs
        /// fall within the ambit of those whose scores are relatively close.
        /// </summary>
        [OptionalSetting]
        public bool ImproveOptimizationOfCloseCasesForBipolarDecision;

        [OptionalSetting]
        public int ImproveOptimizationOfCloseCasesForBipolarDecisionMultiplier;

        [OptionalSetting]
        public double ImproveOptimizationOfCloseCasesForBipolarDecisionProportionToScrutinize;

        /// <summary>
        /// This indicates whether the score is not really an indication of how good the decision is, but instead represents the best decision.
        /// If the decision is a forecast, and the variable that is being forecast is known to the simulation (at least some of the time), then this should be true.
        /// Then, instead of a score being the error in the forecast, the score should simply be the the correct forecast. This will save time in
        /// the optimization of the decision, because we can simply average the scores, instead of trying a range of different values until we get
        /// the closest one.
        /// </summary>
        [OptionalSetting]
        public bool ScoreRepresentsCorrectAnswer;

        /// <summary>
        /// In optimizing a zero-dimensional decision, usually we try to minimize or maximize. Instead, we can try to get the value as close as posible to some target. Then, 
        /// we will be minimizing the square of (average value - target). Note that just minimizing the average of square of (value - target) produces a different result.
        /// </summary>
        [OptionalSetting]
        public double ZeroDimensionalTargetValue;

        /// <summary>
        /// If true, then the Calculate method will always return 0, but oversampling analysis will still take place.
        /// </summary>
        [OptionalSetting]
        public bool DummyDecisionRequiringNoOptimization;

        [OptionalSetting]
        public bool UpdateCumulativeDistributionsWhenOptimizing;

        /// <summary>
        /// If true, strategy development will be skipped altogether.
        /// </summary>
        [OptionalSetting]
        public bool DummyDecisionSkipAltogether;

        /// <summary>
        /// If non-zero, the number of iterations that otherwise would be used will be multiplied by this number by the OptimizePointsAndSmooth class.
        /// </summary>
        [OptionalSetting]
        public double IterationsMultiplier;

        /// <summary>
        /// If non-zero, this number of iterations overrides the default (and the IterationsMultiplier setting is ignored).
        /// </summary>
        [OptionalSetting]
        public long? IterationsOverride;

        /// <summary>
        /// If not null, then this overrides the number of smoothing points for smoothing. This is useful when an excessive number of smoothing points causes for a particular decision
        /// excessively small samples. If there is high volatility in the unsmoothed results, then the averaging that smoothing accomplishes may be imperfect, because the average 
        /// optimal result across smoothing points need not be exactly equal to the optimal result taking all the iterations corresponding to those smoothing points together.
        /// </summary>
        [OptionalSetting]
        public int? SmoothingPointsOverride;

        /// <summary>
        /// Information on graphs to produce based on the strategy as it is evolving or has already evolved.
        /// </summary>
        [OptionalSetting]
        public List<StrategyGraphInfo> StrategyGraphInfos;

        /// <summary>
        /// Are high scores best for this decision?
        /// </summary>
        public bool HighestIsBest;

        /// <summary>
        /// The maximum number of times to evolve this. This is useful when some decisions must be evolved repeatedly, while others are evolved only once.
        /// Set it to a very high number if we don't want to limit repetitions.
        /// </summary>
        public int MaxEvolveRepetitions;

        /// <summary>
        /// Use of this feature is not currently recommended. It phases out default behavior of a module over repetitions of a module.
        /// The better feature to implement might be to phase out default behavior over evolution steps.
        /// If greater than 1, then the default behavior will receive some weight over the remaining specified repetitions of the module.
        /// For example, if 3, then first time, default behavior will receive weight of 1.0, second time weight of 0.67, third time 0.33, and thereafter 0.0.
        /// </summary>
        [OptionalSetting]
        public int PhaseOutDefaultBehaviorOverRepetitions;

        /// <summary>
        /// This is a setting related to randomization of input seeds. Setting this to true ensures that the game inputs will be different for this decision than for other decisions for which it is not true.
        /// </summary>
        [OptionalSetting]
        public bool UseAlternativeGameInputs;


        /// <summary>
        /// When this is set to more than 1, the decision will be copied multiple times into the game definition, and each
        /// version of the decision will be evolved separately. (Note: This 
        /// </summary>
        [OptionalSetting]
        public int RepetitionsAfterFirst = 0;

        /// <summary>
        /// When this is set to true, the previous version of the strategy corresponding to the decision will be saved when the decision is
        /// being optimized. This is useful if there are multiple agents using the same strategy, if we are not copying the decision a 
        /// separate time into the game definition for each agent. For example, we can evolve the optimal strategy for one player in an
        /// auction, while having all other players use the old strategy, hoping eventually to have convergence.
        /// </summary>
        [OptionalSetting]
        public bool PreservePreviousVersionWhenOptimizing;

        /// <summary>
        /// When true, the previous version of the strategy (or the default behavior before evolution) will be used when evolving another decision 
        /// within the same module. This makes it possible to ensure that the order of the decisions evolved within that module will be irrelevant.
        /// NOTE: Right now, we're not using this, instead always using the previous decision unless AlwaysUseLatestVersion is set.
        /// </summary>
        [OptionalSetting]
        public bool UsePreviousVersionWhenOptimizingOtherDecisionInModule;

        /// <summary>
        /// When true, we always will average in the previous version of the strategy. This helps avoid situations where we get alternating decisionmaking
        /// from one repetition to the next.
        /// </summary>
        [OptionalSetting]
        public bool AverageInPreviousVersion;

        /// <summary>
        /// When true, 
        /// </summary>
        [OptionalSetting]
        public bool AlwaysUseLatestVersion;

        /// <summary>
        /// This is useful in development when, after evolving a number of decisions, we want to keep the evolved version of one
        /// while exploring the evolution of others. By default, ACESim will look for a file with the InitializeStrategiesFile-## name,
        /// where ## is the DecisionNumber, if UseInitializeStrategiesFile is set, and then it will use it if this flag is set.
        /// Alternatively, use PreevolvedStrategyFilename.
        /// </summary>
        [OptionalSetting]
        public bool SkipThisDecisionWhenEvolvingIfAlreadyEvolved;

        /// <summary>
        /// If this is not the empty string, then ACESim will load this before evolution, regardless of whether the UseInitializeStrategiesFile
        /// option is set. The strategy will continue evolution, unless SkipThisDecisionWhenEvolvingIfAlreadyEvolved is set.
        /// </summary>
        [OptionalSetting]
        public string PreevolvedStrategyFilename = "";

        /// <summary>
        /// This is useful in development when, after evolving a number of decisions, we want to focus on one.
        /// We should set DoNotEvolveByDefault in EvolutionSettings. All the decisions should have previously evolved (and stored in the Strategies
        /// subdirectory) for this to work, using the same game definition so that the decision number is the same
        /// </summary>
        [OptionalSetting]
        public bool EvolveThisDecisionEvenWhenSkippingByDefault;

        /// <summary>
        /// This can be used to pair two decisions, each taking zero inputs, where the goal is to find an equilibrium between the decisions.
        /// </summary>
        [OptionalSetting]
        public bool MustBeInEquilibriumWithNextDecision;

        /// <summary>
        /// This can be used to pair two decisions, each taking zero inputs, where the goal is to find an equilibrium between the decisions.
        /// </summary>
        [OptionalSetting]
        public bool MustBeInEquilibriumWithPreviousDecision;

        /// <summary>
        /// If more than two decisions must be in equilibrium with one another, this setting can be used.
        /// </summary>
        [OptionalSetting]
        public bool MustBeInEquilibriumWithPreviousAndNextDecision;

        /// <summary>
        /// The simple method involves simply cycling through the decisions and optimizing them repeatedly.
        /// </summary>
        [OptionalSetting]
        public bool UseSimpleMethodForDeterminingEquilibrium;

        /// <summary>
        /// Fast convergence means that we will repeatedly optimize each function only a little bit rather than perfectly, since there is no reason
        /// to perfect optimization when the strategies are still changing rapidly in response to one another.
        /// </summary>
        [OptionalSetting]
        public bool UseFastConvergenceWithSimpleEquilibrium;

        /// <summary>
        /// Instead of relying on a set number of repetitions, we can abort fast convergence when we get to a specified level of precision.
        /// </summary>
        [OptionalSetting]
        public bool AbortFastConvergenceIfPreciseEnough;

        /// <summary>
        /// When using fast convergence, if this is non null, fast convergence will continue until we reach this level of precision, measured as
        /// a proportion of the strategy bounds, unless AbortFastConvergenceIfPreciseEnough is false.
        /// </summary>
        [OptionalSetting]
        public double? PrecisionForFastConvergence;

        /// <summary>
        /// How many times should each decision be optimized if the simple method is selected? This applies only if we are not using fast convergence.
        /// </summary>
        [OptionalSetting]
        public int RepetitionsForSimpleMethodForDeterminingEquilibrium;

        /// <summary>
        /// In development, you can specify a set of inputs where you would like information about what score is produced for each
        /// of one or more outputs. The test is based on the smoothing point closest to the test input.
        /// </summary>
        [OptionalSetting]
        public List<double> TestInputs;


        /// <summary>
        /// To simultaneously test different inputs lists, use this instead of TestInputs.
        /// </summary>
        [OptionalSetting]
        public List<List<double>> TestInputsList;

        [OptionalSetting]
        public bool SkipIdentifyClosestPointOnSubsequentRepetitions;

        /// <summary>
        /// The outputs to test separately using the set of inputs in TestInputs.
        /// </summary>
        [OptionalSetting]
        public List<double> TestOutputs;

        /// <summary>
        /// If a decision is evolved by avoiding narrowing of results (e.g., by assuming that a case will not settle), then this will be set to true,
        /// so that update cumulative distributions can be put immediately prior.
        /// </summary>
        [OptionalSetting]
        public bool DecisionCounterfactuallyAvoidsNarrowingOfResults;

        [OptionalSetting]
        public bool AutomaticallyGeneratePlotRegardlessOfGeneralSetting;

        [OptionalSetting]
        public bool DontAutomaticallyGeneratePlotRegardlessOfGeneralSetting;

        [OptionalSetting]
        public double? XAxisMinOverrideForPlot;

        [OptionalSetting]
        public double? XAxisMaxOverrideForPlot;

        [OptionalSetting]
        public double? YAxisMinOverrideForPlot;

        [OptionalSetting]
        public double? YAxisMaxOverrideForPlot;


        [OptionalSetting]
        public string XAxisLabelForPlot;

        [OptionalSetting]
        public string YAxisLabelForPlot;

        /// <summary>
        /// If this is defined, the following action will execute following the development of the strategy for the decision.
        /// </summary>
        [InternallyDefinedSetting]
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public Action<Strategy> ActionToTakeFollowingStrategyDevelopment;

        public virtual bool ConductEvolutionThisEvolveStep(int evolveStep, int totalEvolveSteps, int? repetitionOfModule, int? repetitionOfDecision)
        {
            return true; // note that this will only be called when the decision is not part of a game module
        }

        public virtual bool SkipAltogetherThisEvolveStep(int evolveStep, int totalEvolveSteps, int? repetitionOfModule, int? repetitionOfDecision)
        {
            return false; // note that this will only be called when the decision is not part of a game module
        }

        public void AddToStrategyGraphs(string baseOutputDirectory, bool newEvolveStep, bool evolutionIsComplete, Strategy strategy, ActionGroup actionGroup)
        {
            if (StrategyGraphInfos == null)
                StrategyGraphInfos = new List<StrategyGraphInfo>();
            foreach (var strategyGraphInfo in StrategyGraphInfos)
            {
                if ( (strategyGraphInfo.ReportAfterEachEvolutionStep && newEvolveStep) || evolutionIsComplete)
                    strategyGraphInfo.AddToReport(baseOutputDirectory, (newEvolveStep && !evolutionIsComplete) || (evolutionIsComplete && !strategyGraphInfo.ReportAfterEachEvolutionStep), this, strategy, actionGroup.RepetitionTagStringLongForm());
            }
            if (evolutionIsComplete && StrategyGraphInfos.Any())
                SaveStrategyGraphs();
        }

        internal void SaveStrategyGraphs()
        {
            foreach (var strategyGraphInfo in StrategyGraphInfos)
            {
                strategyGraphInfo.SaveReport();
            }
        }
    }

}
