using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "PresetAggressivenessOverrideModule")]
    [Serializable]
    public class PresetAggressivenessOverrideModule : BargainingAggressivenessOverrideModule, ICodeBasedSettingGenerator
    {

        public PresetAggressivenessOverrideModuleInputs PresetAggressivenessOverrideModuleInputs { get { return (PresetAggressivenessOverrideModuleInputs)GameModuleInputs; } }
        public LitigationGame LitigationGame { get { return (LitigationGame)Game; } }

        public override void ExecuteModule()
        {

            if (LitigationGame.DisputeContinues())
            {

                if (Game.CurrentActionPointName == "ApplyPresetAggressiveness")
                {
                    LitigationGame.LGP.DAggressivenessOverride = LitigationGame.LGP.DAggressivenessOverrideFinal = PresetAggressivenessOverrideModuleInputs.DPresetAggressiveness;
                    LitigationGame.LGP.PAggressivenessOverride = LitigationGame.LGP.PAggressivenessOverrideFinal = PresetAggressivenessOverrideModuleInputs.PPresetAggressiveness;
                    AddLatestAggressivenessOverridesToList();
                }

            }
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            PresetAggressivenessOverrideModule copy = new PresetAggressivenessOverrideModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = BargainingAggressivenessOverrideModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            int aggressivenessModuleNumber = GetIntCodeGeneratorOption(options, "AggressModNumber");

            return new PresetAggressivenessOverrideModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "BargainingAggressivenessModule" + aggressivenessModuleNumber.ToString(),
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                GameModuleSettings = aggressivenessModuleNumber,
                ActionsAtBeginningOfModule = new List<string>() { "ApplyPresetAggressiveness" } ,
                Tags = new List<string>() { "Bargaining subclaim", "Bargaining round" }
            };
        }

    }
}
