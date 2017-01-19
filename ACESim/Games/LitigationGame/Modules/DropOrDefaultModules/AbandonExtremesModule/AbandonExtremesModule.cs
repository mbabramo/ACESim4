using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "AbandonExtremesModule")]
    [Serializable]
    public class AbandonExtremesModule : DropOrDefaultModule, ICodeBasedSettingGenerator
    {
        public AbandonExtremesModuleProgress AbandonExtremesModuleProgress { get { return (AbandonExtremesModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public AbandonExtremesInputs AbandonExtremesInputs { get { return (AbandonExtremesInputs)GameModuleInputs; } }


        public override void ExecuteModule()
        {
            if (Game.CurrentActionPointName == "ActionBeforeDropOrDefault")
            {
                DetermineDropOrDefaultPeriod();
                MakeDecisions(); // Do it now because there are no formal decisions in this module
            }
        }

        public override void MakeDecisions()
        {
            if (LitigationGame.DisputeContinues() && !(Game.CurrentlyEvolvingDecisionPoint != null && Game.CurrentlyEvolvingDecisionPoint.Name == "PWinsProbability"))
            {
                bool pLower = LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.PEstimatePResult < 0.1;
                bool dLower = LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.DEstimateDResult < 0.1;
                if (pLower && !dLower)
                {
                    AbandonExtremesModuleProgress.PDropsCase = true;
                    LitigationGame.LGP.DropInfo = new DropInfo() { DroppedByPlaintiff = true, DropOrDefaultPeriod = DropOrDefaultProgress.DropOrDefaultPeriod };
                }
                else if (dLower && !pLower)
                {
                    AbandonExtremesModuleProgress.DDefaultsCase = true;
                    LitigationGame.LGP.DropInfo = new DropInfo() { DroppedByPlaintiff = false, DropOrDefaultPeriod = DropOrDefaultProgress.DropOrDefaultPeriod };
                }
            }
        }


        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            AbandonExtremesModule copy = new AbandonExtremesModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = AbandonExtremesModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public override object GenerateSetting(string options)
        {
            DropOrDefaultPeriod period = GetDropOrDefaultPeriodFromCodeGeneratorOptions(options);
            string pointString = period == DropOrDefaultPeriod.Beginning ? "Beginning" : (period == DropOrDefaultPeriod.Mid ? "Mid" : "End");
            List<Decision> decisions = new List<Decision>();

            return new AbandonExtremesModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "ActionBeforeDropOrDefault" },
                GameModuleName = pointString + "DropOrDefaultModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                UpdateCumulativeDistributionsAfterSingleActionGroup = false, /* currently updating only after dispute generation */
                Tags = (period == DropOrDefaultPeriod.Mid) ? new List<string>() { "Bargaining round", "Drop middle bargaining round" } : null
            };
        }

    }
}
