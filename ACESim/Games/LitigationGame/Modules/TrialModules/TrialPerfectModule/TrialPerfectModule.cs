using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "TrialPerfectModule")]
    [Serializable]
    public class TrialPerfectModule : TrialModule, ICodeBasedSettingGenerator
    {
        public TrialPerfectModuleProgress TrialPerfectProgress { get { return (TrialPerfectModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public TrialPerfectInputs TrialPerfectInputs { get { return (TrialPerfectInputs)GameModuleInputs; } }

        public override void HoldTrial()
        {
            base.HoldTrial();
            LGP.PWins = LitigationGame.DisputeGeneratorModule.DGProgress.PShouldWin;
            LGP.MarginOfVictory = 0.5;
            LGP.DWins = !LGP.PWins;
            if ((bool)LGP.PWins)
            {
                double damagesAwarded = LitigationGame.DisputeGeneratorModule.DGProgress.BaseDamagesIfPWins; // welfare effect will be negative because of injury so we reverse the sign
                LGP.DamagesPaymentFromDToP = damagesAwarded;
            }
            LitigationGame.LitigationCostModule.RegisterTrial();
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            TrialPerfectModule copy = new TrialPerfectModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = TrialPerfectModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            return new TrialPerfectModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "Trial" },
                GameModuleName = "TrialModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { }
            };
        }


    }
}
