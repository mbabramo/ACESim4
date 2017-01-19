using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "SkipAggressivenessOverrideModule")]
    [Serializable]
    public class SkipAggressivenessOverrideModule : BargainingAggressivenessOverrideModule, ICodeBasedSettingGenerator
    {


        public override void ExecuteModule()
        {
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            SkipAggressivenessOverrideModule copy = new SkipAggressivenessOverrideModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = BargainingAggressivenessOverrideModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            int aggressivenessModuleNumber = GetIntCodeGeneratorOption(options, "AggressModNumber");

            return new SkipAggressivenessOverrideModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "BargainingAggressivenessModule" + aggressivenessModuleNumber.ToString(),
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                GameModuleSettings = aggressivenessModuleNumber,
                Tags = new List<string>() { "Bargaining subclaim", "Bargaining round" }
            };
        }

    }
}
