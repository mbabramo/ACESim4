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
        internal GameDefinition GameDefinition;
        internal List<GameModule> GameModules;
        internal ActionPoint CurrentActionPoint;
        internal int? MostRecentDecisionIndex;

        public ActionPoint CurrentDecisionPoint => CurrentActionPoint as ActionPoint;
        public ActionGroup CurrentActionGroup => CurrentActionPoint.ActionGroup;

        public byte? CurrentDecisionIndex { get { if (CurrentDecisionPoint == null) return null; return CurrentDecisionPoint.DecisionNumber; } }
        public byte? CurrentDecisionIndexWithinActionGroup { get { if (CurrentDecisionPoint == null) return null; return CurrentDecisionPoint.DecisionNumberWithinActionGroup; } }
        public byte? CurrentDecisionIndexWithinModule { get { if (CurrentDecisionPoint == null) return null; return CurrentDecisionPoint.DecisionNumberWithinModule; } }
        public byte? CurrentActionGroupExecutionIndex { get { if (CurrentActionPoint == null) return null; return CurrentActionPoint.ActionGroup.ActionGroupExecutionIndex; } }
        public byte? CurrentModuleIndex { get { if (CurrentActionPoint == null) return null; return CurrentActionPoint.ActionGroup.ModuleNumber; } }
        public Decision CurrentDecision { get { int? currentDecisionNumber = CurrentDecisionIndex; if (currentDecisionNumber == null) return null; return GameDefinition.DecisionsExecutionOrder[(int)currentDecisionNumber]; } }
        public byte CurrentPlayerNumber => CurrentDecision.PlayerNumber;
        public Strategy CurrentPlayerStrategy => Strategies[CurrentPlayerNumber];
        public GameModule CurrentModule => GameModules[(int) CurrentActionGroup.ModuleNumber];
        public string CurrentActionPointName { get { if (CurrentActionPoint == null) return null; return CurrentActionPoint.Name; } }
        

        internal bool PreparationPhase;

        internal bool DecisionNeeded;

        public bool TriggerReplay; // useful to try to find bugs

        public bool RecordReportInfo;

        public bool ChooseDefaultActionIfNoneChosen;

        public int? DecisionNumberWithinActionGroupForDecisionNumber(int decisionNumber)
        {
            return DecisionPointForDecisionNumber(decisionNumber).DecisionNumberWithinActionGroup;
        }

        public ActionPoint DecisionPointForDecisionNumber(int decisionNumber)
        {
            return GameDefinition.DecisionPointForDecisionNumber(decisionNumber);
        }

	    public virtual void PlaySetup(
            List<Strategy> strategies,
            GameProgress progress,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            bool restartFromBeginningOfGame
            )
        {
            if (restartFromBeginningOfGame && Strategies != null)
                progress = gameDefinition.GameFactory.CreateNewGameProgress(progress.IterationID);
            if (restartFromBeginningOfGame)
                progress.GameHistory.Reinitialize();

            this.Strategies = strategies;
            this.Progress = progress;
            this.Progress.GameDefinition = gameDefinition;
            this.GameDefinition = gameDefinition;
            this.RecordReportInfo = recordReportInfo;
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
                    originalModule.CreateInstanceAndInitializeProgress(this, Strategies, originalModule.GameModuleSettings, out GameModule theModule, out GameModuleProgress theProgress);
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
        public virtual void PrepareForOrMakeDecision()
        {
            if (Progress.GameComplete)
                return;
            Decision currentDecision = CurrentDecision;
            if (PreparationPhase)
            {
                DecisionNeeded = DecisionIsNeeded(currentDecision, Progress);
            }
            else if (DecisionNeeded)
            {
                byte action = ChooseAction();
                byte numPossibleActions = CurrentDecision.NumPossibleActions;
                if (Progress.IsFinalGamePath && action < numPossibleActions)
                    Progress.IsFinalGamePath = false;
                byte decisionIndex = (byte)CurrentDecisionIndex;
                byte playerNumber = CurrentPlayerNumber;
                UpdateGameHistory(ref Progress.GameHistory, GameDefinition, currentDecision, decisionIndex, action, Progress);
                // We update game progress now (note that this will not be called when traversing the tree -- that's why we don't do this within UpdateGameHistory)
                if (!currentDecision.Subdividable_IsSubdivision) // If it is a subdivision, we'll call this in Update
                    UpdateGameProgressFollowingAction(currentDecision.DecisionByteCode, action); 
                else if (currentDecision.Subdividable_IsSubdivision_Last)
                {
                    byte aggregatedAction = Progress.GameHistory.GetCacheItemAtIndex(GameHistory.Cache_SubdivisionAggregationIndex);
                    UpdateGameProgressFollowingAction(currentDecision.Subdividable_CorrespondingDecisionByteCode, aggregatedAction);
                }
            }
        }

        public static void UpdateGameHistory(ref GameHistory gameHistory, GameDefinition gameDefinition, Decision decision, byte decisionIndex, byte action, GameProgress gameProgress)
        {
            if (decision.Subdividable_IsSubdivision)
            {
                // For subdivision decisions, we use the game history cache to aggregate the decision result.
                byte aggregatedSoFar = decision.Subdividable_IsSubdivision_First ? (byte) 0 : gameHistory.GetCacheItemAtIndex(GameHistory.Cache_SubdivisionAggregationIndex);
                byte replacementAggregateValue = SubdivisionCalculations.GetAggregatedDecision(aggregatedSoFar, action, decision.Subdividable_NumOptionsPerBranch, decision.Subdividable_IsSubdivision_Last);
                gameHistory.SetCacheItemAtIndex(GameHistory.Cache_SubdivisionAggregationIndex, replacementAggregateValue);
                // Now, the player's information set. In HistoryPoint.GetGameStateForCurrentPlayer, we preface the information set tree for a decision (other than by the resolution player) by the decision index.
                // That's good, because it means that we won't confuse the information set for the subdivision decisions with the information sets for the aggregated decision, since each decision will have its own
                // decision index. But we still need to make sure that each subdivision decision gets the appropriate information set. Thus, after each subdivision decision, we need to add some information.
                // Because our information set traversal algorithms expect the action, that's what we'll add. But we won't do it for the LAST subdivision. Instead, we'll remove the subdivision decisions
                // from the information set. That way, the main decision will have a clear slate, and the subdivision decisions will not further clutter the information set tree.
                // For the last subdivision, we'll also need to do the same things that we would do for an ordinary nonsubdivision decision. This is because the original decision is NOT in
                // the decision list.
                if (!decision.Subdividable_IsSubdivision_Last)
                {
                    GameProgressLogger.Log(() => $"Adding subdivision action {action} to information set of {decision.PlayerNumber}");
                    gameHistory.AddToInformationSetAndLog(action, decisionIndex, decision.PlayerNumber, gameProgress);
                }
                else
                {
                    gameHistory.RemoveItemsInInformationSetAndLog(decision.PlayerNumber, decisionIndex, (byte) (decision.Subdividable_NumLevels - 1), gameProgress);
                    GameProgressLogger.Log(() => $"Adding overall decision action {replacementAggregateValue} from {decision.PlayerNumber} to {string.Join(",", decision.PlayersToInform)}");
                    gameHistory.AddToHistory(decision.Subdividable_CorrespondingDecisionByteCode, decisionIndex, decision.PlayerNumber, replacementAggregateValue, decision.AggregateNumPossibleActions, decision.PlayersToInform, false, decision.IncrementGameCacheItem, decision.StoreActionInGameCacheItem, decision.DeferNotificationOfPlayers, gameProgress);
                    gameDefinition.CustomInformationSetManipulation(decision, decisionIndex, action, ref gameHistory, gameProgress);
                }
            }
            else
            {
                gameHistory.AddToHistory(decision.DecisionByteCode, decisionIndex, decision.PlayerNumber, action, decision.NumPossibleActions, decision.PlayersToInform, false, decision.IncrementGameCacheItem, decision.StoreActionInGameCacheItem, decision.DeferNotificationOfPlayers, gameProgress);
                gameDefinition.CustomInformationSetManipulation(decision, decisionIndex, action, ref gameHistory, gameProgress);
            }
        }

        public virtual bool DecisionIsNeeded(Decision currentDecision, GameProgress gameProgress)
        {
            return !GameDefinition.SkipDecision(currentDecision, ref gameProgress.GameHistory);
        }

        public unsafe virtual byte ChooseAction()
        {
            byte actionToChoose;
            if (Progress.ActionsToPlay == null)
                Progress.ActionsToPlay = new List<byte>();
            if (Progress.ActionOverrider != null)
            {
                byte actionToAdd = Progress.ActionOverrider(CurrentDecision, Progress);
                if (actionToAdd != 0)
                {
                    GameProgressLogger.Log(() => $"Choosing overridden action {actionToAdd} for {CurrentDecision}");
                    return actionToAdd;
                }
            }
            bool anotherActionPlanned = Progress.ActionsToPlay_MoveNext();
            if (anotherActionPlanned)
            {
                actionToChoose = Progress.ActionsToPlay_CurrentAction;
                //Console.WriteLine($"Decision byte code {CurrentDecision.DecisionByteCode} (index {CurrentDecisionIndex}) ==> {actionToChoose}");
            }
            else if (ChooseDefaultActionIfNoneChosen)
            {
                actionToChoose =
                    1; // The history does not give us guidance, so we play the first available decision. When the game is complete, we can figure out the next possible game history and play that one (which may go to completion or not). 
                //Console.WriteLine($"Decision byte code {CurrentDecision.DecisionByteCode} (index {CurrentDecisionIndex}) ==> default {actionToChoose}");
            }
            else
            {
                actionToChoose = CurrentPlayerStrategy.ChooseActionBasedOnRandomNumber(Progress, Progress.IterationID.GetRandomNumberBasedOnIterationID((byte)CurrentDecisionIndex), CurrentDecision.NumPossibleActions);
                //Console.WriteLine($"Decision byte code {CurrentDecision.DecisionByteCode} (index {CurrentDecisionIndex}) ==> randomly chosen {actionToChoose}");
            }

            GameProgressLogger.Log(() => $"Choosing action {actionToChoose} for {CurrentDecision}");
            return actionToChoose;
        }

        public virtual void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
        }

        public virtual double ConvertActionToUniformDistributionDraw(int action)
        {
            // If we have 2 actions and we draw action #1, then this is equivalent to 0.25 (= 0.5/2); if we draw action #2, then we have 0.75 (= 1.5/2). If we have 3 actions, then the three actions are 1/6, 3/6, and 5/6.
            return EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1 /* make it zero-based */, CurrentDecision.AggregateNumPossibleActions);
        }

        public virtual double ConvertActionToNormalDistributionDraw(int action, double stdev)
        {
            return ConvertUniformDistributionDrawToNormalDraw(ConvertActionToUniformDistributionDraw(action), stdev);
        }

        public virtual double ConvertUniformDistributionDrawToNormalDraw(double uniformDistributionDraw, double stdev)
        {
            return InvNormal.Calculate(uniformDistributionDraw) * stdev;
        }

        public static int NumGamesPlayedAltogether;
        public static int NumGamesPlayedDuringEvolutionOfThisDecision;
        public static int? BreakAtNumGamesPlayedAltogether = null; 
        public static int? BreakAtNumGamesPlayedDuringEvolutionOfThisDecision = null;

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

        public void ContinuePathWithAction(byte actionToPlay)
        {
            if (!Progress.ActionsToPlay.Any())
                Progress.ActionsToPlayIndex = -1;
            Progress.ActionsToPlay.Add(actionToPlay);
            while (!Progress.ActionsToPlayCompleted)
                AdvanceToOrCompleteNextStep();
        }

        public void PlayPathAndStop(List<byte> actionsToPlay)
        {
            Progress.SetActionsToPlay(actionsToPlay);
            while (Progress.ActionsToPlayIndex < actionsToPlay.Count() - 1)
                AdvanceToOrCompleteNextStep();
        }

        /// <summary>
        /// Plays the game according to a particular decisionmaking path. If the path is incomplete, it plays the first possible action for each remaining decision.
        /// </summary>
        /// <param name="actionsToPlay"></param>
        /// <returns>The next path of decisions to play.</returns>
        public unsafe void PlayPathAndContinueWithDefaultAction(byte* actionsToPlay, ref byte* nextPath)
        {
            ChooseDefaultActionIfNoneChosen = true;
            Progress.SetActionsToPlay(actionsToPlay);
            Progress.IsFinalGamePath = true;
            PlayUntilComplete();
            if (nextPath == null || Progress.IsFinalGamePath)
            {
                nextPath = null;
                return;
            }
            Progress.GameFullHistory.GetNextDecisionPath(GameDefinition, nextPath);
            ChooseDefaultActionIfNoneChosen = false;
        }

        /// <summary>
        /// Continue to make decisions until the game is complete.
        /// </summary>
        public void PlayUntilComplete()
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
                GameProgressLogger.Log(() => "PLAY UNTIL COMPLETE");
                bool logEachGame = false;
                if (logEachGame)
                {
                    GameProgressLogger.LoggingOn = true;
                    GameProgressLogger.OutputLogMessages = true;
                    GameProgressLogger.Log("XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX");
                }
                GameProgressLogger.Log(() => "Iteration: " + Progress.IterationID.ToString());
                RegisterGamePlayed();
                while (!Progress.GameComplete)
                    AdvanceToOrCompleteNextStep();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception found in PlayUntilComplete: " + ex.Message);
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
                GameProgressLogger.Log(() => "PLAY UP TO: " + decisionNumber);
                RegisterGamePlayed();
                bool done = decisionNumber == -1;
                while (!done)
                {
                    done = CurrentDecisionIndex == decisionNumber && PreparationPhase;
                    if (!done)
                        AdvanceToOrCompleteNextStep();
                }
                CurrentlyPlayingUpToADecisionInsteadOfCompletingGame = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception found in PlayUpTo: " + ex.Message);
            }

        }

        public void AdvanceToOrCompleteNextStep()
        {
            // We track the information about step status in the Progress object, because
            // we may later continue a game that's progressed with a different Game object.
            Progress.AdvanceToNextPreparationPoint(GameDefinition.ExecutionOrder);

            HeisenbugTracker.CheckIn(); // this will only do something if our Heisenbug-testing module is enabled.

            SetStatusVariables();

            if (GameProgressLogger.LoggingOn)
            {
                string gamePointString = (CurrentActionPoint?.Name ?? "") + " " + " Preparation phase: " + PreparationPhase.ToString();
                GameProgressLogger.AddGameProgressStep(Progress, gamePointString);
                GameProgressLogger.Log(gamePointString);
            }

            PrepareForOrMakeDecision();

            if (Progress.IsFinalStep(GameDefinition.ExecutionOrder) || Progress.GameComplete)
            {
                FinalProcessing();
            }
        }

        public virtual void FinalProcessing()
        {
            Progress.GameComplete = true; // might have already been set as a way to trigger early final processing
            Progress.GameHistory.MarkComplete(Progress);
        }

        internal void SetStatusVariables()
        {
            PreparationPhase = !Progress.PreparationForCurrentStepComplete;
            this.CurrentActionPoint = Progress.CurrentActionGroupNumber == null ? null : GameDefinition.ExecutionOrder[(int)Progress.CurrentActionGroupNumber].ActionPoints[(int)Progress.CurrentActionPointNumberWithinActionGroup];
            if (this.CurrentDecisionIndex != null)
                this.MostRecentDecisionIndex = this.CurrentDecisionIndex;
        }
    }
}