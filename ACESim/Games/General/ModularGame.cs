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
            GameDefinition gameDefinition,
            bool recordReportInfo)
        {
            base.PlaySetup(strategies, progress, gameInputs, gameDefinition, recordReportInfo);
            CopyInputsToSpecificModules(gameInputs as ModularGameInputsSet);
        }

        public void CopyInputsToSpecificModules(ModularGameInputsSet mgi)
        {
            InputsSet = mgi;
            for (int m = 0; m < mgi.GameModulesInputs.Count; m++)
                GameModules[m].GameModuleInputs = mgi.GameModulesInputs[m];
        }

        public override void PrepareForOrMakeDecision()
        {
            if (CurrentDecisionIndex != null || !PreparationPhase) // make sure that nondecision execution points execute only once
                CurrentModule.ExecuteModule();
        }
    }
}
