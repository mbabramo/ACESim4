using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class GameProgress : GameProgressReportable, IDisposable
    {

        public byte? CurrentDecisionIndex;
        public bool PreparationPhase;

        public bool DecisionNeeded;

        public bool TriggerReplay; // useful to try to find bugs

        public bool RecordReportInfo;

        public bool ChooseDefaultActionIfNoneChosen;

        public long LastIterationNumberReceivingRandomNumber = -1;
        public double RandomNumberForIteration = -1;

        public double DummyVariable = 1.0; // used by reporting module
        public IterationID IterationID;
        [FieldwiseComparisonSkip]
        public GameDefinition GameDefinition;
        public List<GameModuleProgress> GameModuleProgresses;
        public GameHistoryStorable GameHistoryStorable;
        public GameHistory GameHistory => GameHistoryStorable.ShallowCopyToRefStruct();
        private GameFullHistory _GameFullHistoryStorable;

        public GameFullHistory GameFullHistory
        {
            get
            {
                if (FullHistoryRequired == false)
                    throw new Exception("Attempt to access full history when not stored.");
                return _GameFullHistoryStorable;
            }
            set
            {
                _GameFullHistoryStorable = value;
            }
        }
        public double PiChance; // probability chance would play to here
        public List<byte> ActionsToPlay = new List<byte>();
        /// <summary>
        /// A function that will choose an action to take for a particular decision, overriding other mechanisms. If it returns 0, the standard mechanisms will be used.
        /// </summary>
        public Func<Decision, GameProgress, byte> ActionOverrider = null;
        private bool _GameComplete;
        public bool GameComplete
        {
            get => _GameComplete;
            set
            {
                _GameComplete = value;
            }

        }
        public bool HaveAdvancedToFirstStep;
        public int? CurrentActionGroupNumber;
        public int? CurrentActionPointNumberWithinActionGroup;
        public bool PreparationForCurrentStepComplete;
        public bool IsFinalGamePath;
        public byte RandomNumbersUsed;
        public double Mixedness;

        public ActionPoint CurrentActionPoint;

        public int? MostRecentDecisionIndex;

        public bool CurrentlyPlayingUpToADecisionInsteadOfCompletingGame;

        public bool FullHistoryRequired;

        public GameProgress(bool fullHistoryRequired)
        {
            var gameHistory = new GameHistory();
            gameHistory.Initialize();
            GameHistoryStorable = gameHistory.DeepCopyToStorable();
            FullHistoryRequired = fullHistoryRequired;
            if (fullHistoryRequired)
                GameFullHistory = GameFullHistory.Initialize();
        }

        bool disposed = false;

        public void Dispose()
        {
            // Dispose of unmanaged resources.
            Dispose(true);
            // Suppress finalization.
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern.
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {

                // If using array pool on InformationSetLog: ArrayPool<byte>.Shared.Return(InformationSetLog.LogStorage);
                // Probably not worth the effort now that InformationSetLog is only part of GameFullHistory, which we 
                // ordinarily don't use.
            }

            disposed = true;
        }

        public int ActionsToPlayIndex;
        public string ActionsToPlayString => String.Join(",", ActionsToPlay);
        public void SetActionsToPlay(List<byte> actionsToPlay)
        {
            ActionsToPlay = actionsToPlay;
            ActionsToPlayIndex = -1;
        }

        public bool ActionsToPlayCompleted => ActionsToPlayIndex + 2 > ActionsToPlay.Count();

        public void SetActionsToPlay(Span<byte> actionsToPlay)
        {
            ActionsToPlay = new List<byte>();
            int i = 0;
            while (actionsToPlay[i] != 255)
            {
                ActionsToPlay.Add(actionsToPlay[i]);
                i++;
            }
            ActionsToPlayIndex = -1;
        }

        public byte ActionsToPlay_CurrentAction => ActionsToPlay[ActionsToPlayIndex];
        public bool ActionsToPlay_MoveNext()
        {
            if (ActionsToPlayCompleted)
                return false;
            ActionsToPlayIndex++;
            return true;
        }

        public List<byte> ActionsPlayed()
        {
            if (ActionsToPlayCompleted)
                return ActionsToPlay.ToList();
            List<byte> actionsPlayed = new List<byte>();
            for (byte b = 0; b < ActionsToPlayIndex; b++)
                actionsPlayed.Add(ActionsToPlay[b]);
            return actionsPlayed;
        }

        public int GetActionsPlayedHash(int hashVariant = 1)
        {
            unchecked
            {
                const int p = 16777619;
                int hash = (int)2166136261 * hashVariant;

                List<byte> data = ActionsPlayed();
                int dataLength = data.Count;
                for (int i = 0; i < dataLength; i++)
                    hash = (hash ^ data[i]) * p;

                hash += hash << 13;
                hash ^= hash >> 7;
                hash += hash << 3;
                hash ^= hash >> 17;
                hash += hash << 5;
                return hash;
            }
        }

        public IEnumerable<byte> GetDecisionIndicesCompleted()
        {
            return GameHistoryStorable.GetDecisionsEnumerable();
        }

        public bool IncludesDecisionIndex(byte decisionIndex) => GetDecisionIndicesCompleted().Contains(decisionIndex);

        static ConcurrentQueue<GameProgress> RecycledGameProgressQueue = new ConcurrentQueue<GameProgress>();
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        internal bool ReportingMode;

        public virtual void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledGameProgressQueue.Enqueue(this);
            }
        }

        public virtual void CleanAfterRecycling()
        {
            DummyVariable = 1.0;
            IterationID = null; // torecycle
            GameDefinition = null;
            if (GameModuleProgresses != null)
                foreach (var gmp in GameModuleProgresses)
                    gmp.Recycle();
            GameModuleProgresses = null;
            GameHistoryStorable = GameHistoryStorable = GameHistoryStorable.NewInitialized();
            if (FullHistoryRequired)
                GameFullHistory = GameFullHistory.Initialize();
            ActionsToPlay = null;
            ActionsToPlayIndex = -1;
            GameComplete = false;
            HaveAdvancedToFirstStep = false;
            CurrentActionGroupNumber = null;
            CurrentActionPointNumberWithinActionGroup = null;
            PreparationForCurrentStepComplete = false;
            IsFinalGamePath = true; // assume true until shown otherwise
            RandomNumbersUsed = 0;
        }
        

        public bool IsFinalStep(List<ActionGroup> executionGroupsInExecutionOrder)
        {
            return CurrentActionGroupNumber != null 
                && PreparationForCurrentStepComplete
                && CurrentActionGroupNumber == executionGroupsInExecutionOrder.Count - 1 
                && CurrentActionPointNumberWithinActionGroup == executionGroupsInExecutionOrder[executionGroupsInExecutionOrder.Count - 1].ActionPoints.Count - 1;
        }

        public void AdvanceToNextPreparationPoint(List<ActionGroup> executionGroupsInExecutionOrder)
        {
            if (!HaveAdvancedToFirstStep)
            {
                AdvanceToFirstStep();
                if (executionGroupsInExecutionOrder[(int)CurrentActionGroupNumber].ActionPoints.Count == 0)
                    AdvanceToNextActionPoint(executionGroupsInExecutionOrder);
                return; // do not change preparation status -- leave it at false
            }
            else
            {
                PreparationForCurrentStepComplete = !PreparationForCurrentStepComplete;
                // after a step has been prepared, we want to stay with that step to do the regular execution.
                // As a result, we shouldn't advance to the next step.
                // But if PreparationForCurrentStepComplete is false, that indicates that we completed a step, and it's time to advance to the next one.
                if (!PreparationForCurrentStepComplete) 
                    AdvanceToNextActionPoint(executionGroupsInExecutionOrder);
            }
        }

        private void AdvanceToFirstStep()
        {
            if (HaveAdvancedToFirstStep)
                throw new Exception("Tried to advance to first step two times. Make sure that there is a decision to play.");
            CurrentActionGroupNumber = 0;
            CurrentActionPointNumberWithinActionGroup = 0;
            HaveAdvancedToFirstStep = true;
            PreparationForCurrentStepComplete = false;
        }

        private void AdvanceToNextActionPoint(List<ActionGroup> executionGroupsInExecutionOrder)
        {
            CurrentActionPointNumberWithinActionGroup++;
            if (CurrentActionPointNumberWithinActionGroup == executionGroupsInExecutionOrder[(int)CurrentActionGroupNumber].ActionPoints.Count)
            {
                do
                {
                    CurrentActionGroupNumber++;
                }
                while (CurrentActionGroupNumber < executionGroupsInExecutionOrder.Count && executionGroupsInExecutionOrder[(int)CurrentActionGroupNumber].ActionPoints.Count == 0); // skip over empty execution groups
                if (CurrentActionGroupNumber == executionGroupsInExecutionOrder.Count)
                { // completely done
                    CurrentActionGroupNumber = null;
                    CurrentActionPointNumberWithinActionGroup = null;
                    PreparationForCurrentStepComplete = false;
                }
                else
                    CurrentActionPointNumberWithinActionGroup = 0;
            }
        }

        // We can split a GameProgress ex post for reporting if there are variables where multiple values are possible AFTER the game is complete.
        // For example, in a litigation game, we may never need to determine what would have occurred at trial if the case settles, but we might
        // still want to report on what would occur. If so, we can override this and implement a split.

        public virtual bool SplitExPostForReporting => false;

        public virtual List<GameProgress> CompleteSplitExPostForReporting()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<(GameProgress progress, double weight)> GetGameProgressIncludingAnySplits()
        {
            if (!SplitExPostForReporting)
            {
                yield return (this, 1.0);
                yield break;
            }
            var results = CompleteSplitExPostForReporting();
            double weight = 1.0 / ((double)results.Count());
            foreach (var result in results)
                yield return (result, weight);
        }

        /// <summary>
        /// Returns a deep copy
        /// </summary>
        /// <returns cref="GameProgress"></returns>
        public virtual GameProgress DeepCopy()
        {
            GameProgress copy = new GameProgress(FullHistoryRequired);

            CopyFieldInfo(copy);
            return copy;
        }

        internal virtual void CopyFieldInfo(GameProgress copy)
        {
            copy.CurrentActionPoint = CurrentActionPoint?.DeepCopy();
            copy.MostRecentDecisionIndex = MostRecentDecisionIndex;
            copy.CurrentlyPlayingUpToADecisionInsteadOfCompletingGame = CurrentlyPlayingUpToADecisionInsteadOfCompletingGame;
            copy.CurrentDecisionIndex = CurrentDecisionIndex;
            copy.PreparationPhase = PreparationPhase;
            copy.DecisionNeeded = DecisionNeeded;
            copy.TriggerReplay = TriggerReplay;
            copy.RecordReportInfo = RecordReportInfo;
            copy.ChooseDefaultActionIfNoneChosen = ChooseDefaultActionIfNoneChosen;
            copy.LastIterationNumberReceivingRandomNumber = LastIterationNumberReceivingRandomNumber;
            copy.RandomNumberForIteration = RandomNumberForIteration;
            copy.IterationID = IterationID;
            copy.GameDefinition = GameDefinition;
            copy.GameModuleProgresses = GameModuleProgresses == null ? null : (GameModuleProgresses.Select(x => x?.DeepCopy()).ToList());
            copy.GameHistoryStorable = GameHistoryStorable.ShallowCopyToRefStruct().DeepCopyToStorable();
            if (FullHistoryRequired)
                copy.GameFullHistory = GameFullHistory.DeepCopy();
            copy.ActionsToPlay = ActionsToPlay?.ToList(); 
            copy.ActionsToPlayIndex = ActionsToPlayIndex;
            copy.GameComplete = this.GameComplete;
            copy.HaveAdvancedToFirstStep = this.HaveAdvancedToFirstStep;
            copy.CurrentActionGroupNumber = this.CurrentActionGroupNumber;
            copy.CurrentActionPointNumberWithinActionGroup = this.CurrentActionPointNumberWithinActionGroup;
            copy.PreparationForCurrentStepComplete = this.PreparationForCurrentStepComplete;
            copy.IsFinalGamePath = this.IsFinalGamePath;
            copy.RandomNumbersUsed = this.RandomNumbersUsed;
            copy.Mixedness = this.Mixedness;
            copy.ReportingMode = this.ReportingMode;
            copy.FullHistoryRequired = this.FullHistoryRequired;
        }

        private object GetFieldValueForReportFromGameModuleProgress(string variableNameForReport, int? listIndex, out bool found)
        {
            foreach (GameModuleProgress gameModuleProgress in GameModuleProgresses)
            {
                if (gameModuleProgress != null)
                {
                    object result = gameModuleProgress.GetFieldValueForReport(variableNameForReport, listIndex, out found);
                    if (found)
                        return result;
                }
            }
            found = false;
            return null;
        }

        public override object GetFieldValueForReport(string variableNameForReport, int? listIndex, out bool found)
        {
            object result = base.GetFieldValueForReport(variableNameForReport, listIndex, out found);
            if (!found)
                result = GetFieldValueForReportFromGameModuleProgress(variableNameForReport, listIndex, out found);
            return result;
        }

        private object GetNonFieldValueForReportFromGameModuleProgress(string variableNameForReport, out bool found)
        {
            foreach (GameModuleProgress gameModuleProgress in GameModuleProgresses)
            {
                if (gameModuleProgress != null)
                {
                    object result = gameModuleProgress.GetNonFieldValueForReport(variableNameForReport, out found);
                    if (found)
                        return result;
                }
            }
            found = false;
            return null;
        }

        internal virtual object GetNonFieldValueForReportFromGameProgress(string variableNameForReport, out bool found)
        {
            found = false;
            return null;
        }

        public override object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
            // Note: This should not ordinarily be overridden. Override GetNonFieldValueForReportFromGameProgress instead.
            object result;
            result = GetNonFieldValueForReportFromGameProgress(variableNameForReport, out found);
            if (!found)
                result = GetNonFieldValueForReportFromGameModuleProgress(variableNameForReport, out found);
            return result;
        }
        
        public virtual double[] GetNonChancePlayerUtilities()
        {
            return new double[] { };
        }

        public virtual List<double[]> GetNonChancePlayerUtilities_IncludingAlternateScenarios(GameDefinition gameDefinition)
        {
            List<double[]> alternativeScenarios = new List<double[]>();
            int numInitializedScenarios = gameDefinition.NumScenariosToInitialize;
            for (int s = 0; s < numInitializedScenarios; s++)
            {
                bool warmupVersion = s >= gameDefinition.NumPostWarmupOptionSets;
                if (warmupVersion)
                    gameDefinition.ChangeOptionsBasedOnScenario(null, s - gameDefinition.NumPostWarmupOptionSets);
                else
                    gameDefinition.ChangeOptionsBasedOnScenario(s, null);
                if (s > 0)
                    RecalculateGameOutcome();
                alternativeScenarios.Add(GetNonChancePlayerUtilities());
            }
            gameDefinition.ChangeOptionsToCurrentScenario();
            return alternativeScenarios;
        }

        public virtual FloatSet GetCustomResult()
        {
            return new FloatSet();
        }

        public virtual List<FloatSet> GetCustomResult_IncludingAlternateScenarios(GameDefinition gameDefinition)
        {
            List<FloatSet> alternativeScenarios = new List<FloatSet>();
            int numScenarios = gameDefinition.NumScenariosToInitialize;
            for (int s = 0; s < numScenarios; s++)
            {
                bool warmupVersion = s >= gameDefinition.NumPostWarmupOptionSets;
                if (warmupVersion)
                    gameDefinition.ChangeOptionsBasedOnScenario(null, s - gameDefinition.NumPostWarmupOptionSets);
                else
                    gameDefinition.ChangeOptionsBasedOnScenario(s, null);
                if (s > 0)
                    RecalculateGameOutcome();
                alternativeScenarios.Add(GetCustomResult());
            }
            gameDefinition.ChangeOptionsToCurrentScenario();
            return alternativeScenarios;
        }

        public virtual void RecalculateGameOutcome()
        {
        }


    }
}
