using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class BargainingAggressivenessOverrideModule : GameModule
    {
        public BargainingAggressivenessOverrideModuleProgress BargainingAggressivenessOverrideProgress { get { return (BargainingAggressivenessOverrideModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            throw new Exception("This must be overridden. The overridden method should call SetGameAndStrategies.");
        }

        public override void ExecuteModule()
        {
        }


        public void AddLatestAggressivenessOverridesToList()
        {
            LitigationGame LitigationGame = ((LitigationGame)Game);
            if (LitigationGame.LGP.PAggressivenessOverride != null && LitigationGame.LGP.DAggressivenessOverride != null)
            {
                LitigationGame.LGP.PAggressivenessOverrideList.Add((double)LitigationGame.LGP.PAggressivenessOverride);
                LitigationGame.LGP.DAggressivenessOverrideList.Add((double)LitigationGame.LGP.DAggressivenessOverride);
            }
        }

        public override OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            // this executes before bargaining module, unless bargaining module is split into BargainingModulePrepAndProject and BargainingModuleSettleAfterAggressiveness, in which case this is in the middle
            
            if (!forEvolution && secondActionGroup.Name.Contains("BargainingModulePrepAndProject") && actionGroupWithinThisModule.RepetitionTagString() == secondActionGroup.RepetitionTagString())
                return OrderingConstraint.After; 
            else if (!forEvolution && (secondActionGroup.Name.Contains("BargainingModule") || secondActionGroup.Name.Contains("BargainingModuleSettleAfterAggressiveness")) && actionGroupWithinThisModule.RepetitionTagString() == secondActionGroup.RepetitionTagString())
                return OrderingConstraint.Before;

            if (!forEvolution && secondActionGroup.Name.Contains("BeginningDropOrDefault"))
                return OrderingConstraint.After;

            // this evolves after bargaining (note that when we evolve UtilityRangeBargainingModule, the aggressiveness does not matter because we are blocking settlement, so aggressiveness should go later)
            // meanwhile, BargainingModulePrepAndProject and BargainingModuleSettleAfterAggressiveness order doesn't matter b/c only first of these contains evolvable decisions; if it did matter, we would specify that in the relevant bargaining module
            if (forEvolution && (secondActionGroup.Name.Contains("BargainingModule") || secondActionGroup.Name.Contains("BargainingModulePrepAndProject") || secondActionGroup.Name.Contains("BargainingModuleSettleAfterAggressiveness")) && actionGroupWithinThisModule.RepetitionTagString() == secondActionGroup.RepetitionTagString())
                return OrderingConstraint.After;
            if (forEvolution && secondActionGroup.Name.Contains("BeginningDropOrDefault"))
                return OrderingConstraint.Before;
            if (forEvolution && actionGroupWithinThisModule.Name.Contains("BargainingAggressivenessModule1") && secondActionGroup.Name.Contains("BargainingAggressivenessModule2") && actionGroupWithinThisModule.RepetitionTagString() == secondActionGroup.RepetitionTagString())
                return OrderingConstraint.Before; // aggressiveness module 1 should evolve before module 2 rather than in reverse (as would occur with backward induction)

            int? thisModuleBargainingRound = actionGroupWithinThisModule.GetRepetitionNumberForTag("Bargaining round");
            int? secondActionGroupBargainingRound = secondActionGroup.GetRepetitionNumberForTag("Bargaining round");
            bool evolveLaterBargainingRoundsFirst = false;
            if (forEvolution && actionGroupWithinThisModule.Name.Contains("BargainingAggressivenessModule") && secondActionGroup.Name.Contains("BargainingAggressivenessModule") && thisModuleBargainingRound != null && thisModuleBargainingRound > secondActionGroupBargainingRound)
                return evolveLaterBargainingRoundsFirst ? OrderingConstraint.Before : OrderingConstraint.After; 
            return null;
        }
    }
}
