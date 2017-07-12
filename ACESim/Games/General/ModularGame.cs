using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    public class ModularGame : Game
    {
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
            GameDefinition gameDefinition,
            bool recordReportInfo,
            bool restartFromBeginningOfGame)
        {
            base.PlaySetup(strategies, progress, gameDefinition, recordReportInfo, restartFromBeginningOfGame);
        }

        public override void PrepareForOrMakeDecision()
        {
            if (CurrentDecisionIndex != null || !PreparationPhase) // make sure that nondecision execution points execute only once
                CurrentModule.ExecuteModule();
        }
    }
}
