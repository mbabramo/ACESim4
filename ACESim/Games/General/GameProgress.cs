﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class GameProgress : GameProgressReportable
    {
        public double DummyVariable = 1.0; // used by reporting module
        public IterationID IterationID;
        [FieldwiseComparisonSkip]
        public GameDefinition GameDefinition;
        public List<GameModuleProgress> GameModuleProgresses;
        public GameHistory GameHistory = new GameHistory();
        public bool GameComplete;
        public bool HaveAdvancedToFirstStep;
        public int? CurrentActionGroupNumber;
        public int? CurrentActionPointNumberWithinActionGroup;
        public bool PreparationForCurrentStepComplete;
        public int? HighestCumulativeDistributionUpdateIndexToBeginExecutingSoFar;
        public double? CutoffVariable;
        public bool CurrentlyEvolvingDecisionIndexReached;

        static ConcurrentQueue<GameProgress> RecycledGameProgressQueue = new ConcurrentQueue<GameProgress>();
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public virtual void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledGameProgressQueue.Enqueue(this);
            }
        }

        public static new GameProgress GetRecycledOrAllocate()
        {
            GameProgress recycled = null;
            RecycledGameProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new GameProgress();
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
            GameHistory.Initialize();
            GameComplete = false;
            HaveAdvancedToFirstStep = false;
            CurrentActionGroupNumber = null;
            CurrentActionPointNumberWithinActionGroup = null;
            PreparationForCurrentStepComplete = false;
            HighestCumulativeDistributionUpdateIndexToBeginExecutingSoFar = null;
            CutoffVariable = null;
            CurrentlyEvolvingDecisionIndexReached = false;
        }

        public virtual bool PassesSymmetryTest(GameProgress gameProgressWithSameInputsFlippedAndSwapped)
        {
            return true;
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
            HighestCumulativeDistributionUpdateIndexToBeginExecutingSoFar = null;
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

        internal void CopyFieldInfo(GameProgress copy)
        {
            copy.IterationID = IterationID;
            copy.GameDefinition = GameDefinition;
            copy.GameModuleProgresses = GameModuleProgresses == null ? null : ( GameModuleProgresses.Select(x => x == null ? null : x.DeepCopy()).ToList() );
            copy.GameComplete = this.GameComplete;
            copy.GameHistory = GameHistory.DeepCopy();
            copy.HaveAdvancedToFirstStep = this.HaveAdvancedToFirstStep;
            copy.CurrentActionGroupNumber = this.CurrentActionGroupNumber;
            copy.CurrentActionPointNumberWithinActionGroup = this.CurrentActionPointNumberWithinActionGroup;
            copy.PreparationForCurrentStepComplete = this.PreparationForCurrentStepComplete;
            copy.HighestCumulativeDistributionUpdateIndexToBeginExecutingSoFar = HighestCumulativeDistributionUpdateIndexToBeginExecutingSoFar;
            copy.CutoffVariable = CutoffVariable;
            copy.CurrentlyEvolvingDecisionIndexReached = CurrentlyEvolvingDecisionIndexReached;
        }

        public virtual void Report(SimulationInteraction interaction)
        {
            interaction.ReportOutputForIteration(this);
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

        public virtual List<double?> GetVariablesToTrackCumulativeDistributionsOf()
        {
            return null;
        }
    }
}
