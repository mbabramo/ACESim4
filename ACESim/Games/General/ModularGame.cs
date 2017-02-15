using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    public class ModularGame : Game
    {
        ModularGameInputsSet InputsSet;

        public GameModule GetModule(int indexOfModuleInListOfModulesGameReliesOn)
        {
            return GameModules[GetModuleIndexFromIndexOfModuleInListOfModulesGameReliesOn(indexOfModuleInListOfModulesGameReliesOn)];
        }

        public int GetModuleIndexFromIndexOfModuleInListOfModulesGameReliesOn(int indexOfModuleInListOfModulesGameReliesOn)
        {
            return GameDefinition.GameModuleNumbersGameReliesOn[indexOfModuleInListOfModulesGameReliesOn];
        }

        public Decision GetCurrentDecision()
        {
            return GameDefinition.DecisionsExecutionOrder[(int) CurrentDecisionIndex];
        }

        public override void PlaySetup(
            List<Strategy> strategies,
            GameProgress progress,
            GameInputs gameInputs,
            StatCollectorArray recordedInputs,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            double weightOfObservation)
        {
            base.PlaySetup(strategies, progress, gameInputs, recordedInputs, gameDefinition, recordReportInfo, weightOfObservation);
            CopyInputsToSpecificModules(gameInputs as ModularGameInputsSet);
        }

        public override void FinalProcessing()
        {
            Progress.GameComplete = true;
            Progress.GameHistory.MarkComplete();
        }

        public void CopyInputsToSpecificModules(ModularGameInputsSet mgi)
        {
            InputsSet = mgi;
            for (int m = 0; m < mgi.GameModulesInputs.Count; m++)
                GameModules[m].GameModuleInputs = mgi.GameModulesInputs[m];
        }

        public override void PrepareForOrMakeCurrentDecision()
        {
            if (CurrentDecisionIndex != null || !PreparationPhase) // make sure that nondecision execution points execute only once
                CurrentModule.ExecuteModule();
        }
    }
}
