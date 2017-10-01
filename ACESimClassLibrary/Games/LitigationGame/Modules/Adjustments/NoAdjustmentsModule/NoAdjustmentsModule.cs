using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "NoAdjustmentsModule")]
    [Serializable]
    public class NoAdjustmentsModule : AdjustmentsModule, ICodeBasedSettingGenerator
    {


        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            NoAdjustmentsModule copy = new NoAdjustmentsModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = AdjustmentsModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            int adjustmentsModuleNumber = GetIntCodeGeneratorOption(options, "AdjustModNumber");

            return new NoAdjustmentsModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "AdjustmentsModule" + adjustmentsModuleNumber,
                GameModuleNamesThisModuleReliesOn = new List<string>() { }
            };
        }

    }
}
