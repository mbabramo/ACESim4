using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public unsafe class GameProgress : GameProgressReportable
    {
        public double DummyVariable = 1.0; // used by reporting module
        public IterationID IterationID;
        [FieldwiseComparisonSkip]
        public GameDefinition GameDefinition;
        public List<GameModuleProgress> GameModuleProgresses;
        public GameHistoryStorable GameHistoryStorable;
        public GameHistory GameHistory => GameHistoryStorable.ToRefStruct();
        public GameFullHistoryStorable GameFullHistoryStorable;
        public GameFullHistory GameFullHistory => GameFullHistoryStorable.ToRefStruct();
        public InformationSetLog InformationSetLog;
        public double PiChance; // probability chance would play to here
        public List<byte> ActionsToPlay = new List<byte>();
        /// <summary>
        /// A function that will choose an action to take for a particular decision, overriding other mechanisms. If it returns 0, the standard mechanisms will be used.
        /// </summary>
        public Func<Decision, GameProgress, byte> ActionOverrider = null;
        public bool GameComplete;
        public bool HaveAdvancedToFirstStep;
        public int? CurrentActionGroupNumber;
        public int? CurrentActionPointNumberWithinActionGroup;
        public bool PreparationForCurrentStepComplete;
        public bool IsFinalGamePath;
        public byte RandomNumbersUsed;
        public double Mixedness;

        public GameProgress()
        {
            GameHistory.Initialize();
            GameFullHistory.Initialize();
            InformationSetLog.Initialize();
        }

        public int ActionsToPlayIndex;
        public string ActionsToPlayString => String.Join(",", ActionsToPlay);
        public void SetActionsToPlay(List<byte> actionsToPlay)
        {
            ActionsToPlay = actionsToPlay;
            ActionsToPlayIndex = -1;
        }

        public bool ActionsToPlayCompleted => ActionsToPlayIndex + 2 > ActionsToPlay.Count();
        public void SetActionsToPlay(byte* actionsToPlay)
        {
            ActionsToPlay = new List<byte>();
            byte* a = actionsToPlay;
            while (*a != 255)
            {
                ActionsToPlay.Add(*a);
                a++;
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

        public IEnumerable<short> GetInformationSetHistoryItems_OverallIndices()
        {
            return GameFullHistory.ToStorable().GetInformationSetHistoryItems_OverallIndices(this);
        }

        public InformationSetHistory GetInformationSetHistory_OverallIndex(short index) => GameFullHistory.GetInformationSetHistory_OverallIndex(index, this);


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
            InformationSetLog.Initialize();
            DummyVariable = 1.0;
            IterationID = null; // torecycle
            GameDefinition = null;
            if (GameModuleProgresses != null)
                foreach (var gmp in GameModuleProgresses)
                    gmp.Recycle();
            GameModuleProgresses = null;
            GameHistory.Reinitialize();
            GameFullHistory.Initialize();
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

        /// <summary>
        /// Returns a deep copy
        /// </summary>
        /// <returns cref="GameProgress"></returns>
        public virtual GameProgress DeepCopy()
        {
            GameProgress copy = new GameProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal virtual void CopyFieldInfo(GameProgress copy)
        {
            copy.InformationSetLog = InformationSetLog;
            copy.IterationID = IterationID;
            copy.GameDefinition = GameDefinition;
            copy.GameModuleProgresses = GameModuleProgresses == null ? null : (GameModuleProgresses.Select(x => x?.DeepCopy()).ToList());
            copy.GameHistoryStorable = GameHistoryStorable.ToRefStruct().DeepCopy().ToStorable();
            copy.GameFullHistoryStorable = GameFullHistoryStorable;
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
            int numScenarios = gameDefinition.NumScenariosToInitialize;
            for (int s = 0; s < numScenarios; s++)
            {
                gameDefinition.ChangeOptionsBasedOnScenarioIndex(s, false);
                if (s > 0)
                    RecalculateGameOutcome();
                alternativeScenarios.Add(GetNonChancePlayerUtilities());
            }
            if (numScenarios > 0)
                gameDefinition.ChangeOptionsBasedOnScenarioIndex(0, false);
            return alternativeScenarios;
        }

        public virtual void RecalculateGameOutcome()
        {
        }


    }
}
