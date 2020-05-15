using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ACESim
{
    /// <summary>
    /// This class provides the base methods for playing a game.
    /// </summary>
    public class Game
    {
        public readonly GameProgress Progress;
        public readonly List<Strategy> Strategies;
        public readonly GameDefinition GameDefinition;
        public readonly List<GameModule> GameModules;

        public Game(
            List<Strategy> strategies,
            GameProgress progress,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            bool restartFromBeginningOfGame,
            bool fullHistoryRequired
            )
        {
            if (restartFromBeginningOfGame && (progress == null || Strategies != null))
                progress = gameDefinition.GameFactory.CreateNewGameProgress(fullHistoryRequired, progress?.IterationID ?? new IterationID());
            if (restartFromBeginningOfGame)
            {
                progress.GameHistoryStorable = GameHistoryStorable.NewInitialized();
            }

            this.Strategies = strategies;
            this.Progress = progress;
            this.Progress.GameDefinition = gameDefinition;
            this.GameDefinition = gameDefinition;
            this.RecordReportInfo = recordReportInfo;
            this.GameModules = SetUpGameModules();
            Initialize();
        }

        public ActionPoint CurrentActionPoint
        {
            get => Progress.CurrentActionPoint;
            set => Progress.CurrentActionPoint = value;
        }
        public ActionGroup CurrentActionGroup => CurrentActionPoint.ActionGroup;

        public int? MostRecentDecisionIndex
        {
            get => Progress.MostRecentDecisionIndex;
            set => Progress.MostRecentDecisionIndex = value;
        }

        public bool CurrentlyPlayingUpToADecisionInsteadOfCompletingGame
        {
            get => Progress.CurrentlyPlayingUpToADecisionInsteadOfCompletingGame;
            set => Progress.CurrentlyPlayingUpToADecisionInsteadOfCompletingGame = value;
        }

        public byte? CurrentDecisionIndex
        {
            get => Progress.CurrentDecisionIndex;
            set => Progress.CurrentDecisionIndex = value;
        }
        public bool PreparationPhase
        {
            get => Progress.PreparationPhase;
            set => Progress.PreparationPhase = value;
        }

        public bool DecisionNeeded
        {
            get => Progress.DecisionNeeded;
            set => Progress.DecisionNeeded = value;
        }

        public bool TriggerReplay
        {
            get => Progress.TriggerReplay;
            set => Progress.TriggerReplay = value;
        }

        public bool RecordReportInfo
        {
            get => Progress.RecordReportInfo;
            set => Progress.RecordReportInfo = value;
        }

        public bool ChooseDefaultActionIfNoneChosen
        {
            get => Progress.ChooseDefaultActionIfNoneChosen;
            set => Progress.ChooseDefaultActionIfNoneChosen = value;
        }

        public long LastIterationNumberReceivingRandomNumber
        {
            get => Progress.LastIterationNumberReceivingRandomNumber;
            set => Progress.LastIterationNumberReceivingRandomNumber = value;
        }

        public double RandomNumberForIteration
        {
            get => Progress.RandomNumberForIteration;
            set => Progress.RandomNumberForIteration = value;
        }

        public Decision CurrentDecision
        {
            get
            {
                if (CurrentDecisionIndex == null)
                    return null;
                return GameDefinition.DecisionsExecutionOrder[(int)CurrentDecisionIndex];
            }
        }

        public byte CurrentPlayerNumber => CurrentDecision.PlayerIndex;
        public Strategy CurrentPlayerStrategy => Strategies[CurrentPlayerNumber];
        public GameModule CurrentModule => GameModules[(int) CurrentActionGroup.ModuleNumber];
        public string CurrentActionPointName { get { if (CurrentActionPoint == null) return null; return CurrentActionPoint.Name; } }
        


        public int? DecisionNumberWithinActionGroupForDecisionNumber(int decisionNumber)
        {
            return DecisionPointForDecisionNumber(decisionNumber).DecisionNumberWithinActionGroup;
        }

        public ActionPoint DecisionPointForDecisionNumber(int decisionNumber)
        {
            return GameDefinition.DecisionPointForDecisionNumber(decisionNumber);
        }

        

        public virtual void Initialize()
        {
            
        }

        internal virtual List<GameModule> SetUpGameModules()
        {
            List<GameModule> gameModules = null;
            if (GameDefinition.GameModules != null)
            {
                bool mustAddGameModuleProgresses = Progress.GameModuleProgresses == null;
                if (mustAddGameModuleProgresses)
                    Progress.GameModuleProgresses = new List<GameModuleProgress>(); // allocate list but individual CreateInstanceAndInitializeProgress routines will create objects
                gameModules = new List<GameModule>();
                foreach (var originalModule in GameDefinition.GameModules)
                {
                    originalModule.CreateInstanceAndInitializeProgress(this, Strategies, originalModule.GameModuleSettings, out GameModule theModule, out GameModuleProgress theProgress);
                    gameModules.Add(theModule);
                    if (mustAddGameModuleProgresses)
                        Progress.GameModuleProgresses.Add(theProgress);
                }
            }
            return gameModules;
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
                MakeDecision(currentDecision);
            }
        }

        private void MakeDecision(Decision currentDecision)
        {
            byte action = ChooseAction();
            byte numPossibleActions = CurrentDecision.NumPossibleActions;
            if (Progress.IsFinalGamePath && action < numPossibleActions)
                Progress.IsFinalGamePath = false;
            byte decisionIndex = (byte)CurrentDecisionIndex;
            byte playerNumber = CurrentPlayerNumber;

            // Note: We need to update the game history, but in Progress it's not stored as a ref struct. So we convert it to a ref struct and then convert it back.
            // NOTE: this only works if we can be sure that Progress is not shared across threads (otherwise, we get an error when GameHistory is used on different threads)
            var history = Progress.GameHistory;
            UpdateGameHistory(ref history, GameDefinition, currentDecision, decisionIndex, action, Progress);
            Progress.GameHistoryStorable.UpdateFromShallowCopy(history);

            // We update game progress now (note that this will not be called when traversing the tree -- that's why we don't do this within UpdateGameHistory)
            UpdateGameProgressFollowingAction(currentDecision.DecisionByteCode, action);
        }

        public static void UpdateGameHistory(ref GameHistory gameHistory, GameDefinition gameDefinition, Decision decision, byte decisionIndex, byte action, GameProgress gameProgress)
        {
            gameHistory.AddToHistory(decision.DecisionByteCode, decisionIndex, decision.PlayerIndex, action, decision.NumPossibleActions, decision.PlayersToInform, decision.PlayersToInformOfOccurrenceOnly, decision.IncrementGameCacheItem, decision.StoreActionInGameCacheItem, gameProgress, decision.DeferNotificationOfPlayers, false);
            if (decision.RequiresCustomInformationSetManipulation)
                gameDefinition.CustomInformationSetManipulation(decision, decisionIndex, action, ref gameHistory, gameProgress);
        }

        public virtual bool DecisionIsNeeded(Decision currentDecision, GameProgress gameProgress)
        {
            var history = gameProgress.GameHistory;
            return !GameDefinition.SkipDecision(currentDecision, in history);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual byte ChooseAction()
        {
            byte actionToChoose;
            if (Progress.ActionsToPlay == null)
                ResetActionsToPlay();
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
                //TabbedText.WriteLine($"Decision byte code {CurrentDecision.DecisionByteCode} (index {CurrentDecisionIndex}) ==> {actionToChoose}");
            }
            else if (ChooseDefaultActionIfNoneChosen)
            {
                actionToChoose =
                    1; // The history does not give us guidance, so we play the first available decision. When the game is complete, we can figure out the next possible game history and play that one (which may go to completion or not). 
                //TabbedText.WriteLine($"Decision byte code {CurrentDecision.DecisionByteCode} (index {CurrentDecisionIndex}) ==> default {actionToChoose}");
            }
            else
            {
                actionToChoose = ChooseActionRandomly();
            }

            GameProgressLogger.Log(() => $"Choosing action {actionToChoose} for {CurrentDecision}");
            return actionToChoose;
        }

        private byte ChooseActionRandomly()
        {
            byte actionToChoose;
            double randomNumberForIterationThisTime;
            long iterationNumber = Progress.IterationID.GetIterationNumber();
            if (iterationNumber == LastIterationNumberReceivingRandomNumber)
                randomNumberForIterationThisTime = RandomNumberForIteration;
            else
            {
                randomNumberForIterationThisTime = RandomNumberForIteration = Progress.IterationID.GetRandomNumberBasedOnIterationID((byte)(253));
                LastIterationNumberReceivingRandomNumber = iterationNumber;
            }
            actionToChoose = CurrentPlayerStrategy.ChooseActionBasedOnRandomNumber(Progress, Progress.IterationID.GetRandomNumberBasedOnIterationID((byte)CurrentDecisionIndex), // must be different for every decision in the game
                randomNumberForIterationThisTime, // must be same for every decision in the game
                CurrentDecision.NumPossibleActions);

            //TabbedText.WriteLine($"Decision byte code {CurrentDecision.DecisionByteCode} (index {CurrentDecisionIndex}) ==> randomly chosen {actionToChoose}");
            return actionToChoose;
        }

        private void ResetActionsToPlay()
        {
            Progress.ActionsToPlay = new List<byte>();
        }

        public virtual void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
        }

        public virtual double ConvertActionToUniformDistributionDraw(int action, bool includeEndpoints) => ConvertActionToUniformDistributionDraw(action, CurrentDecision.NumPossibleActions, includeEndpoints);

        public static double ConvertActionToUniformDistributionDraw(int action, int numPossibleActions, bool includeEndpoints)
        {
            if (numPossibleActions == 1)
                return 0.5;
            // Not including endpoints: If we have 2 actions and we draw action #1, then this is equivalent to 0.25 (= 0.5/2); if we draw action #2, then we have 0.75 (= 1.5/2). If we have 3 actions, then the three actions are 1/6, 3/6, and 5/6.
            return EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1 /* make it zero-based */, numPossibleActions, includeEndpoints);
        }

        public virtual double ConvertActionToNormalDistributionDraw(int action, double stdev)
        {
            return ConvertUniformDistributionDrawToNormalDraw(ConvertActionToUniformDistributionDraw(action, false), stdev);
        }

        public virtual double ConvertUniformDistributionDrawToNormalDraw(double uniformDistributionDraw, double stdev)
        {
            return InvNormal.Calculate(uniformDistributionDraw) * stdev;
        }

        public static int NumGamesPlayedAltogether;
        public static int? BreakAtNumGamesPlayedAltogether = null; 

        public void RegisterGamePlayed()
        {
            Interlocked.Increment(ref NumGamesPlayedAltogether);
            if (NumGamesPlayedAltogether == BreakAtNumGamesPlayedAltogether)
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
            if (Progress.GameComplete)
                throw new Exception("Game is already complete.");
            if (!Progress.ActionsToPlay.Any())
                Progress.ActionsToPlayIndex = -1;
            Progress.ActionsToPlay.Add(actionToPlay);
            while (!Progress.ActionsToPlayCompleted)
                AdvanceToOrCompleteNextStep();
        }

        public void PlayPathAndStop(List<byte> actionsToPlay)
        {
            Progress.SetActionsToPlay(actionsToPlay);
            while (!Progress.GameComplete && (!Progress.HaveAdvancedToFirstStep || Progress.ActionsToPlayIndex < actionsToPlay.Count() - 1))
                AdvanceToOrCompleteNextStep();
        }

        /// <summary>
        /// Plays the game according to a particular decisionmaking path. If the path is incomplete, it plays the first possible action for each remaining decision.
        /// </summary>
        /// <param name="actionsToPlay"></param>
        /// <returns>The next path of decisions to play.</returns>
        public void PlayPathAndContinueWithDefaultAction(Span<byte> actionsToPlay, ref Span<byte> nextPath)
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
                // NOTE: These can also be enabled in GameProgressLogger. This will not turn off logging if turned on.
                bool logEachGame = false; 
                bool detailedLogging = false;
                if (logEachGame)
                {
                    GameProgressLogger.LoggingOn = true;
                    GameProgressLogger.OutputLogMessages = true;
                    GameProgressLogger.Log("XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX");
                }
                if (detailedLogging)
                    GameProgressLogger.DetailedLogging = true;
                GameProgressLogger.Log(() => "Iteration: " + Progress.IterationID.ToString());
                RegisterGamePlayed();
                while (!Progress.GameComplete)
                    AdvanceToOrCompleteNextStep();
            }
            catch (Exception ex)
            {
                TabbedText.WriteLine("Exception found in PlayUntilComplete: " + ex.Message);
            }
        }

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
                TabbedText.WriteLine("Exception found in PlayUpTo: " + ex.Message);
            }
        }

        public void AdvanceToOrCompleteNextStep()
        {
            // We track the information about step status in the Progress object, because
            // we may later continue a game that's progressed with a different Game object.
            Progress.AdvanceToNextPreparationPoint(GameDefinition.ExecutionOrder);

            HeisenbugTracker.CheckIn(); // this will only do something if our Heisenbug-testing module is enabled.

            SetStatusVariables();

            if (GameProgressLogger.LoggingOn && GameProgressLogger.DetailedLogging)
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
            var gameHistory = Progress.GameHistory;
            gameHistory.MarkComplete(Progress);
            Progress.GameHistoryStorable.UpdateFromShallowCopy(gameHistory);
        }

        internal void SetStatusVariables()
        {
            PreparationPhase = !Progress.PreparationForCurrentStepComplete;
            this.CurrentActionPoint = Progress.CurrentActionGroupNumber == null ? null : GameDefinition.ExecutionOrder[(int)Progress.CurrentActionGroupNumber].ActionPoints[(int)Progress.CurrentActionPointNumberWithinActionGroup];
            this.CurrentDecisionIndex =  CurrentActionPoint?.DecisionNumber;
            if (this.CurrentDecisionIndex != null)
                this.MostRecentDecisionIndex = this.CurrentDecisionIndex;
        }
    }
}