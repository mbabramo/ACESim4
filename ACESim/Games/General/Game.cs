using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Diagnostics;

namespace ACESim
{
    /// <summary>
    /// This class provides the base methods for playing a game.
    /// </summary>
    public class Game
    {
        public GameProgress Progress;

        internal List<Strategy> Strategies;
        internal GameInputs GameInputs;
        internal StatCollectorArray RecordedInputs;
        internal GameDefinition GameDefinition;
        internal List<GameModule> GameModules;
        internal bool RecordReportInfo;
        internal int? RecordInputsForDecisionNumber;
        internal ActionPoint CurrentActionPoint;
        internal ActionPoint CurrentlyEvolvingActionPoint;
        public List<double> MostRecentRecordedInputs;
        Queue<List<double>> InputsOfFirstDecisionInEachGroupOfDecisionsToCacheNotIncludingMainGroup = new Queue<List<double>>();
        CachedInputsAndScores InputsAndScoresForCurrentGroupToCache = null;
        Strategy StrategyThatIsFirstInGroupOfDecisionsToCache = null;
        bool DisableFurtherCaching = false;


        public int? HighestCumulativeDistributionUpdateIndexToBeginExecutingSoFar { get { return Progress.HighestCumulativeDistributionUpdateIndexToBeginExecutingSoFar; } set { Progress.HighestCumulativeDistributionUpdateIndexToBeginExecutingSoFar = value; } }
        public int? HighestCumulativeDistributionUpdateIndexToFinishEvolvingSoFar { get { return Strategies[0].SimulationInteraction.HighestCumulativeDistributionUpdateIndexEvolved; } }
        public List<CumulativeDistribution> MostRecentCumulativeDistributions;

        public DecisionPoint CurrentDecisionPoint { get { return CurrentActionPoint as DecisionPoint; } }
        public ActionGroup CurrentActionGroup { get { return CurrentActionPoint.ActionGroup; } }
        public int? CurrentDecisionIndex { get { if (CurrentDecisionPoint == null) return null; return CurrentDecisionPoint.DecisionNumber; } }
        public int? MostRecentDecisionIndex { get; set; }
        public int? CurrentDecisionIndexWithinActionGroup { get { if (CurrentDecisionPoint == null) return null; return CurrentDecisionPoint.DecisionNumberWithinActionGroup; } }
        public int? CurrentDecisionIndexWithinModule { get { if (CurrentDecisionPoint == null) return null; return CurrentDecisionPoint.DecisionNumberWithinModule; } }
        public int? CurrentActionGroupExecutionIndex { get { if (CurrentActionPoint == null) return null; return CurrentActionPoint.ActionGroup.ActionGroupExecutionIndex; } }
        public int? CurrentModuleIndex { get { if (CurrentActionPoint == null) return null; return CurrentActionPoint.ActionGroup.ModuleNumber; } }
        public Decision CurrentDecision { get { int? currentDecisionNumber = CurrentDecisionIndex; if (currentDecisionNumber == null) return null; return GameDefinition.DecisionsExecutionOrder[(int)currentDecisionNumber]; } }
        public GameModule CurrentModule { get { return GameModules[(int) CurrentActionGroup.ModuleNumber]; } }
        public string CurrentActionPointName { get { if (CurrentActionPoint == null) return null; return CurrentActionPoint.Name; } }

        public DecisionPoint CurrentlyEvolvingDecisionPoint { get { return CurrentlyEvolvingActionPoint as DecisionPoint; } }
        public ActionGroup CurrentlyEvolvingActionGroup { get { if (CurrentlyEvolvingActionPoint == null) return null; return CurrentlyEvolvingActionPoint.ActionGroup; } }
        public int? CurrentlyEvolvingDecisionIndex { get { if (CurrentlyEvolvingDecisionPoint == null) return null; return CurrentlyEvolvingDecisionPoint.DecisionNumber; } }
        public int? CurrentlyEvolvingDecisionIndexWithinActionGroup { get { if (CurrentlyEvolvingDecisionPoint == null) return null; return CurrentlyEvolvingDecisionPoint.DecisionNumberWithinActionGroup; } }
        public int? CurrentlyEvolvingDecisionIndexWithinModule { get { if (CurrentlyEvolvingDecisionPoint == null) return null; return CurrentlyEvolvingDecisionPoint.DecisionNumberWithinModule; } }
        public int? CurrentlyEvolvingActionGroupExecutionIndex { get { if (CurrentlyEvolvingActionPoint == null) return null; return CurrentlyEvolvingActionPoint.ActionGroup.ActionGroupExecutionIndex; } }
        public int? CurrentlyEvolvingModuleIndex { get { if (CurrentlyEvolvingActionPoint == null) return null; return CurrentlyEvolvingActionPoint.ActionGroup.ModuleNumber; } }
        public Decision CurrentlyEvolvingDecision { get { int? currentlyEvolvingDecisionNumber = CurrentlyEvolvingDecisionIndex; if (currentlyEvolvingDecisionNumber == null) return null; return GameDefinition.DecisionsExecutionOrder[(int)currentlyEvolvingDecisionNumber]; } }
        public GameModule CurrentlyEvolvingModule { get { ActionGroup currentlyEvolvingActionGroup = CurrentlyEvolvingActionGroup; if (CurrentlyEvolvingActionGroup == null) return null; return GameModules[(int)currentlyEvolvingActionGroup.ModuleNumber]; } }
        public string CurrentlyEvolvingActionPointName { get { if (CurrentlyEvolvingActionPoint == null) return null; return CurrentlyEvolvingActionPoint.Name; } }

        public bool CurrentlyEvolvingDecisionAlreadyExecuted { get { return CurrentlyEvolvingActionGroupExecutionIndex < CurrentActionGroupExecutionIndex || (CurrentlyEvolvingActionGroupExecutionIndex == CurrentActionGroupExecutionIndex && CurrentlyEvolvingDecisionIndexWithinActionGroup < CurrentDecisionIndexWithinActionGroup); } }
        public bool CurrentlyEvolvingCurrentlyExecutingModule { get { GameModule currentlyEvolvingModule = CurrentlyEvolvingModule; return currentlyEvolvingModule != null && currentlyEvolvingModule == CurrentModule; } }
        public bool CurrentlyEvolvingCurrentlyExecutingActionGroup { get { int? actionGroupNumber = CurrentActionGroupExecutionIndex; return actionGroupNumber != null && actionGroupNumber == CurrentlyEvolvingActionGroupExecutionIndex; } }
        public bool CurrentlyEvolvingCurrentlyExecutingDecision { get { int? currentDecisionNumber = CurrentDecisionIndex; return (currentDecisionNumber != null && CurrentlyEvolvingDecisionIndex == currentDecisionNumber); } }

        internal bool PreparationPhase;
        internal bool CurrentlyEvolving;
        internal double WeightOfScoreInWeightedAverage;

        public bool TriggerReplay; // useful to try to find bugs

        public int? DecisionNumberWithinActionGroupForDecisionNumber(int decisionNumber)
        {
            return DecisionPointForDecisionNumber(decisionNumber).DecisionNumberWithinActionGroup;
        }

        public DecisionPoint DecisionPointForDecisionNumber(int decisionNumber)
        {
            return GameDefinition.DecisionPointForDecisionNumber(decisionNumber);
        }

	    public virtual void PlaySetup(
            List<Strategy> strategies, 
            GameProgress progress, 
            GameInputs gameInputs, 
            StatCollectorArray recordedInputs,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            double weightOfScoreInWeightedAverage)
        {
            if (RestartFromBeginningOfGame && Strategies != null)
            {
                IGameFactory gameFactory = Strategies[0].SimulationInteraction.CurrentExecutionInformation.GameFactory;
                progress = gameFactory.CreateNewGameProgress(progress.IterationID);
            }

            this.Strategies = strategies;
            this.Progress = progress;
            this.Progress.GameDefinition = gameDefinition;
            this.GameInputs = gameInputs;
            this.RecordedInputs = recordedInputs;
            this.GameDefinition = gameDefinition;
            this.RecordReportInfo = recordReportInfo;
            this.WeightOfScoreInWeightedAverage = weightOfScoreInWeightedAverage;
            SetUpGameModules();
        }

        internal virtual void SetUpGameModules()
        {
            if (GameDefinition.GameModules != null)
            {
                bool mustAddGameModuleProgresses = Progress.GameModuleProgresses == null;
                if (mustAddGameModuleProgresses)
                    Progress.GameModuleProgresses = new List<GameModuleProgress>(); // allocate list but individual CreateInstanceAndInitializeProgress routines will create objects
                GameModules = new List<GameModule>();
                foreach (var originalModule in GameDefinition.GameModules)
                {
                    GameModule theModule;
                    GameModuleProgress theProgress;
                    originalModule.CreateInstanceAndInitializeProgress(this, Strategies, originalModule.GameModuleSettings, out theModule, out theProgress);
                    GameModules.Add(theModule);
                    if (mustAddGameModuleProgresses)
                        Progress.GameModuleProgresses.Add(theProgress);
                }
            }
        }

        /// <summary>
        /// This calls either the method that allows preparation for a particular decision or requires execution of that decision.
        /// The subclass can either override this method, or it can override PrepareForCurrentDecision and CompleteCurrentDecision,
        /// depending on whether the code is best organized by separating the prepartion steps from the execution steps.
        /// If game play is completed, then Progress.Complete should be set to true. 
        /// </summary>
        public virtual void PrepareForOrMakeCurrentDecision()
        {
            if (PreparationPhase)
                PrepareForCurrentDecision();
            else
                MakeCurrentDecision();
        }

        /// <summary>
        /// This should be handled entirely by the subclass if PrepareForOrMakeCurrentDecision is not overridden.
        /// In this method, the subclass makes the decision indicated by CurrentDecisionNumber.
        /// </summary>
        public virtual void MakeCurrentDecision()
        {
            // Entirely subclass
        }

        public virtual void PrepareForCurrentDecision()
        {
            // Entirely subclass
        }

        public static int NumGamesPlayedAltogether;
        public static int NumGamesPlayedDuringEvolutionOfThisDecision;
        public static int? BreakAtNumGamesPlayedAltogether = null; 
        public static int? BreakAtNumGamesPlayedDuringEvolutionOfThisDecision = null;
        public static bool RestartFromBeginningOfGame = false; // Change this to true in development to see the game from the beginning; it's also automatically changed when breaking at a particular point

        public void RegisterGamePlayed()
        {
            Interlocked.Increment(ref NumGamesPlayedAltogether);
            Interlocked.Increment(ref NumGamesPlayedDuringEvolutionOfThisDecision);
            if (NumGamesPlayedDuringEvolutionOfThisDecision == BreakAtNumGamesPlayedDuringEvolutionOfThisDecision || NumGamesPlayedAltogether == BreakAtNumGamesPlayedAltogether)
            {
                try
                {
                    throw new Exception("Breaking at specified point. This exception will be caught so that you can trace problem identified.");
                }
                catch
                {
                }
            }
        }

        public static long? LoggingOnTrigger = null;

        /// <summary>
        /// Continue to make decisions until the game is complete.
        /// </summary>
        public void PlayUntilComplete(int? recordInputsForDecision, int? currentlyEvolvingDecision, bool recycleAfterPlay = true)
        {
            try
            {
                if (LoggingOnTrigger != null)
                {
                    if (Progress.IterationID.IterationNumber == LoggingOnTrigger)
                    {
                        GameProgressLogger.LoggingOn = true;
                        GameProgressLogger.OutputLogMessages = true;
                    }
                    else
                    {
                        GameProgressLogger.LoggingOn = false;
                        GameProgressLogger.OutputLogMessages = false;
                    }
                }
                if (GameProgressLogger.LoggingOn)
                    GameProgressLogger.Log("PLAY UNTIL COMPLETE");
                bool logEachGame = false;
                if (logEachGame)
                {
                    GameProgressLogger.LoggingOn = true;
                    GameProgressLogger.OutputLogMessages = true;
                    GameProgressLogger.Log("XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX");
                }
                if (GameProgressLogger.LoggingOn)
                    GameProgressLogger.Log("Iteration: " + Progress.IterationID.ToString());
                RegisterGamePlayed();
                this.RecordInputsForDecisionNumber = recordInputsForDecision;
                while (!Progress.GameComplete)
                    AdvanceToAndCompleteNextStep(currentlyEvolvingDecision);
                if (recycleAfterPlay)
                {
                    Progress.Recycle();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception found in PlayUntilComplete: " + ex.Message);
            }
        }

        public bool CurrentlyPlayingUpToADecisionInsteadOfCompletingGame = false;
        /// <summary>
        /// Repeatedly play the game up to the decision number, calling the Game subclass to prepare for or play each decision.
        /// </summary>
        /// <param name="decisionNumber"></param>
        public void PlayUpTo(int decisionNumber, int? currentlyEvolvingDecision)
        {
            try
            {
                CurrentlyPlayingUpToADecisionInsteadOfCompletingGame = true;
                if (GameProgressLogger.LoggingOn)
                    GameProgressLogger.Log("PLAY UP TO: " + decisionNumber);
                RegisterGamePlayed();
                this.RecordInputsForDecisionNumber = decisionNumber;
                bool done = decisionNumber == -1;
                while (!done)
                {
                    done = CurrentDecisionIndex == decisionNumber && PreparationPhase;
                    if (!done)
                        AdvanceToAndCompleteNextStep(currentlyEvolvingDecision);
                }
                CurrentlyPlayingUpToADecisionInsteadOfCompletingGame = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception found in PlayUpTo: " + ex.Message);
            }

        }

        private void AdvanceToAndCompleteNextStep(int? currentlyEvolvingDecision)
        {
            // We track the information about step status in the Progress object, because
            // we may later continue a game that's progressed with a different Game object.
            Progress.AdvanceToNextPreparationPoint(GameDefinition.ExecutionOrder);

            HeisenbugTracker.CheckIn(); // this will only do something if our Heisenbug-testing module is enabled.

            SetStatusVariables(currentlyEvolvingDecision);

            if (GameProgressLogger.LoggingOn)
            {
                string gamePointString = CurrentActionPoint.Name + " " + " Preparation phase: " + PreparationPhase.ToString();
                GameProgressLogger.AddGameProgressStep(Progress, gamePointString);
                GameProgressLogger.Log(gamePointString);
            }

            string gamePointString2 = CurrentActionPoint.Name + " " + " Preparation phase: " + PreparationPhase.ToString() + "\n";

            MostRecentCumulativeDistributions = GetMostRecentCumulativeDistributions();

            PrepareForOrMakeCurrentDecision();

            if (Progress.IsFinalStep(GameDefinition.ExecutionOrder))
                FinalProcessing();
        }


        public List<CumulativeDistribution> GetMostRecentCumulativeDistributions()
        {
            if (HighestCumulativeDistributionUpdateIndexToFinishEvolvingSoFar == null || HighestCumulativeDistributionUpdateIndexToBeginExecutingSoFar == null) //  || !FirstDecisionHasBegunEvolving)
                return null;
            int updateIndex = (int)HighestCumulativeDistributionUpdateIndexToBeginExecutingSoFar;
            if (updateIndex > HighestCumulativeDistributionUpdateIndexToFinishEvolvingSoFar)
                updateIndex = (int)HighestCumulativeDistributionUpdateIndexToFinishEvolvingSoFar;

            int decisionNumForUpdateIndex = GameDefinition.DecisionIndexForEachCumulativeDistributionsUpdate[updateIndex];
            List<CumulativeDistribution> ds = Strategies[decisionNumForUpdateIndex].CumulativeDistributions.ToList();
            return ds;
        }


        public virtual void FinalProcessing()
        {
        }

        internal void SetStatusVariables(int? currentlyEvolvingDecision)
        {
            PreparationPhase = !Progress.PreparationForCurrentStepComplete;
            this.CurrentActionPoint = Progress.CurrentActionGroupNumber == null ? null : GameDefinition.ExecutionOrder[(int)Progress.CurrentActionGroupNumber].ActionPoints[(int)Progress.CurrentActionPointNumberWithinActionGroup];
            if (this.CurrentDecisionIndex != null)
                this.MostRecentDecisionIndex = this.CurrentDecisionIndex;
            if (currentlyEvolvingDecision == null)
                this.CurrentlyEvolvingActionPoint = null;
            else
                this.CurrentlyEvolvingActionPoint = DecisionPointForDecisionNumber((int)currentlyEvolvingDecision);
            this.CurrentlyEvolving = currentlyEvolvingDecision != null;
        }
    
        /// <summary>
        /// The subclass will create the inputs based on the gameSettings.
        /// 
        /// This is game subclass-specific. These are the numbers that will be
        /// passed into the strategy. So, in a litigation-subclass, the inputs that
        /// getDecisionInputs returns might be the litigant's estimate of the
        /// probability of winning, the litigant's wealth, etc. 
        /// </summary>
        protected virtual List<double> GetDecisionInputs()
        {
            List<double> inputs = new List<double>();

            // subclass should retain the following logic. If GetDecisionInputs is not used, this must be accomplished somewhere for the decision being evolved.
            RecordInputsIfNecessary(inputs);

            return inputs;
        }

        internal bool RecordInputsIfNecessary(List<double> inputs)
        {
            bool inputsJustRecorded = false;
            if (CurrentDecisionIndex == RecordInputsForDecisionNumber && PreparationPhase)
            {
                if (RecordedInputs != null)
                {
                    inputsJustRecorded = true;
                    RecordedInputs.Add(inputs.ToArray());
                }
                MostRecentRecordedInputs = inputs;
            }
            if (CurrentlyEvolvingDecisionIndex != null && CurrentDecisionIndex != null && CurrentDecisionIndex > CurrentlyEvolvingDecisionIndex && CurrentDecisionIndex <= CurrentlyEvolvingDecisionIndex + Strategies[(int)CurrentlyEvolvingDecisionIndex].Decision.NumberDecisionsToEitherRecordOrCacheBeyondThisOne && Strategies[(int)CurrentDecisionIndex].Decision.DecisionIsFirstInGroupOfDecisionsToCache)
                InputsOfFirstDecisionInEachGroupOfDecisionsToCacheNotIncludingMainGroup.Enqueue(inputs.ToList());
            return inputsJustRecorded;
        }

        /// <summary>
        /// Scores the strategy represented by decisionNumber.
        /// </summary>
        /// <param name="decisionNumber"></param>
        public virtual void Score(int decisionNumber, double score)
        {
            int currentlyEvolvingDecisionIndex = (int)CurrentlyEvolvingDecisionIndex;
            Strategy currentlyEvolvingStrategy = Strategies[currentlyEvolvingDecisionIndex];
            if (CurrentlyEvolvingDecisionIndex == decisionNumber)
                currentlyEvolvingStrategy.AddScore(score, WeightOfScoreInWeightedAverage);
            else
            { // could be a score for a subsequent decision
                int numberDecisionsPastDecision = decisionNumber - currentlyEvolvingDecisionIndex;
                if (numberDecisionsPastDecision <= currentlyEvolvingStrategy.Decision.SubsequentDecisionsToRecordScoresFor)
                {
                    Strategies[decisionNumber - numberDecisionsPastDecision].AddScore(score, WeightOfScoreInWeightedAverage, numberDecisionsPastDecision);
                }
                else if (numberDecisionsPastDecision <= currentlyEvolvingStrategy.Decision.NumberDecisionsToEitherRecordOrCacheBeyondThisOne)
                {
                    Strategy strategyForScoreBeingRecorded = Strategies[decisionNumber];
                    if (strategyForScoreBeingRecorded.StrategyStillToEvolveThisEvolveStep && !DisableFurtherCaching)
                    { // otherwise, don't cache it
                        if (strategyForScoreBeingRecorded.Decision.DecisionIsFirstInGroupOfDecisionsToCache)
                        {
                            int subsequentDecisionsToRecordScoresFor = strategyForScoreBeingRecorded.Decision.SubsequentDecisionsToRecordScoresFor;
                            InputsAndScoresForCurrentGroupToCache = new CachedInputsAndScores() { Inputs = InputsOfFirstDecisionInEachGroupOfDecisionsToCacheNotIncludingMainGroup.Dequeue(), Weight = WeightOfScoreInWeightedAverage, ScoreForFirstDecision = score, ScoresForSubsequentDecisions = (subsequentDecisionsToRecordScoresFor == 0 ? null : new double[subsequentDecisionsToRecordScoresFor]) };
                            StrategyThatIsFirstInGroupOfDecisionsToCache = strategyForScoreBeingRecorded;
                        }
                        else
                            InputsAndScoresForCurrentGroupToCache.ScoresForSubsequentDecisions[(int)strategyForScoreBeingRecorded.Decision.ScoresRecordedByDecisionNPrevious - 1] = score;
                        if (strategyForScoreBeingRecorded.Decision.DecisionIsLastInGroupOfDecisionsToCache)
                        { // cache the values in the first of the group
                            ConcurrentDictionary<IterationID, CachedInputsAndScores> cache = StrategyThatIsFirstInGroupOfDecisionsToCache.CacheFromPreviousOptimization;
                            if (cache != null)
                                cache.TryAdd(Progress.IterationID, InputsAndScoresForCurrentGroupToCache);
                        }
                    }
                    else if (strategyForScoreBeingRecorded.Decision.DecisionIsFirstInGroupOfDecisionsToCache)
                        DisableFurtherCaching = true; // the current algorithm won't work for subsequent decisions to cache if the first in a group of decisions to cache isn't cached
                }
            }
        }

        public virtual void ScoreCurrentModule()
        {
            if (CurrentlyEvolvingModule != null)
                CurrentlyEvolvingModule.Score();
        }

        /// <summary>
        /// Calls GetDecisionInputs and Calculate, and it updates the 
        /// decisionCompleted field of GameSettings 
        /// </summary>
        public double MakeDecision(List<double> inputs = null)
        {
            if (inputs == null)
                inputs = GetDecisionInputs();
            double calculation = Calculate(inputs);
            if (double.IsNaN(calculation) || double.IsInfinity(calculation))
                return 0; // this should ordinarily lead to the strategy getting a bad score
            return calculation;
        }

        /// <summary>
        /// Calls Calculate method on the strategy corresponding to the decisionNumber. 
        /// </summary>
        protected double Calculate(List<double> inputs)
        {
            return Strategies[(int) CurrentDecisionIndex].Calculate(inputs);
        }
    }
}