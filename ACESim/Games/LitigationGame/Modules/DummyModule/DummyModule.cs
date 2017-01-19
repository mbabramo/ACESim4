using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "DummyModule")]
    [Serializable]
    public class DummyModule : GameModule, ICodeBasedSettingGenerator
    {

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            DummyModule copy = new DummyModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = CumulativeDistributionModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public override void ExecuteModule()
        {
            // There is no decision actually being made here
        }

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            decisions.Add(GetDecision("Dummy" + options, "dum"));

            return new DummyModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "DummyModule" + options,
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                IgnoreWhenCountingProgress = true
            };
        }

        private static Decision GetDecision(string name, string abbreviation)
        {
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DummyDecisionRequiringNoOptimization = true, // IMPORTANT: This is a dummy decision designed just to trigger the cumulative distributions update
                DummyDecisionSkipAltogether = true,
                DynamicNumberOfInputs = true,
                UseOversampling = true,
                SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.1,
                InputAbbreviations = null,
                InputNames = null,
                StrategyBounds = new StrategyBounds()
                {
                    LowerBound = 0.0,
                    UpperBound = 1.0
                },
                Bipolar = false,
                StrategyGraphInfos = new List<StrategyGraphInfo>(),
                HighestIsBest = false,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = 99999,
                PreservePreviousVersionWhenOptimizing = false,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                ScoreRepresentsCorrectAnswer = false,
                TestInputs = null, // new List<double>() { 0.5, 0.07, 0.07 }, 
                TestOutputs = null // new List<double>() { 0.1, 0.2, 0.3, 0.4, 0.5 }
            };
        }

        public override OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            if (!forEvolution && actionGroupWithinThisModule.Name.Contains("DummyModuleBeginning"))
                return OrderingConstraint.Before;
            if (!forEvolution && actionGroupWithinThisModule.Name.Contains("DummyModuleEnd"))
                return OrderingConstraint.After;
            if (forEvolution && actionGroupWithinThisModule.Name.Contains("DummyModuleBeginning"))
                return OrderingConstraint.After;
            if (forEvolution && actionGroupWithinThisModule.Name.Contains("DummyModuleEnd"))
                return OrderingConstraint.Before;
            return null;
        }

    }
}
