using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "NeverGiveUpModule")]
    [Serializable]
    public class NeverGiveUpModule : DropOrDefaultModule, ICodeBasedSettingGenerator
    {
        public NeverGiveUpModuleProgress NeverGiveUpModuleProgress { get { return (NeverGiveUpModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public NeverGiveUpInputs NeverGiveUpInputs { get { return (NeverGiveUpInputs)GameModuleInputs; } } 


        public override void MakeDecisions()
        {
        }


        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            NeverGiveUpModule copy = new NeverGiveUpModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = NeverGiveUpModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public override object GenerateSetting(string options)
        {
            DropOrDefaultPeriod period = GetDropOrDefaultPeriodFromCodeGeneratorOptions(options);
            string pointString = period == DropOrDefaultPeriod.Beginning ? "Beginning" : (period == DropOrDefaultPeriod.Mid ? "Mid" : "End");

            List<Decision> decisions = new List<Decision>();


            return new NeverGiveUpModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "ActionBeforeDropOrDefault" },
                GameModuleName = pointString + "DropOrDefaultModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                Tags = (period == DropOrDefaultPeriod.Mid) ? new List<string>() { "Bargaining round", "Drop middle bargaining round" } : null
            };
        }

    }
}
