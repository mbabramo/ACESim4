using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class DisputeGeneratorModule : GameModule
    {
        public LitigationGame LitigationGame { get { return (LitigationGame)Game; } }

        public DisputeGeneratorModuleProgress DGProgress { get { return (DisputeGeneratorModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public DisputeGeneratorInputs DisputeGeneratorInputs { get { return (DisputeGeneratorInputs)GameModuleInputs; } }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            throw new Exception("This must be overridden. The overridden method should call SetGameAndStrategies.");
        }

        public override void ExecuteModule()
        {
            DGProgress.DisputeGeneratorInitiated = true;
            Process(DisputeGeneratorInputs);
            LitigationGame.LGP.PWealthAfterDisputeGenerated = LitigationGame.Plaintiff.InitialWealth + LitigationGame.DisputeGeneratorModule.DGProgress.PrelitigationWelfareEffectOnP;
            LitigationGame.LGP.DWealthAfterDisputeGenerated = LitigationGame.Defendant.InitialWealth + LitigationGame.DisputeGeneratorModule.DGProgress.PrelitigationWelfareEffectOnD;
            LitigationGame.LGP.PFinalWealth = LitigationGame.LGP.PWealthAfterDisputeGenerated;
            LitigationGame.LGP.DFinalWealth = LitigationGame.LGP.DWealthAfterDisputeGenerated;
        }

        public virtual void Process(DisputeGeneratorInputs moduleInputs)
        {
        }

        public virtual void CalculateSocialLoss()
        {
        }

        public override OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            if (!forEvolution && (secondActionGroup.Name.Contains("ProbabilityForecastingModule") || secondActionGroup.Name.Contains("DamagesForecastingModule")))
                return OrderingConstraint.Before;
            if (forEvolution && (secondActionGroup.Name.Contains("ProbabilityForecastingModule") || secondActionGroup.Name.Contains("DamagesForecastingModule")))
                return OrderingConstraint.After;
            return null;
        }
    }
}
