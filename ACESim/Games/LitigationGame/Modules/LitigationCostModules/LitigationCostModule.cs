using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class LitigationCostModule : GameModule
    {

        public LitigationGame LitigationGame { get { return (LitigationGame)Game; } }
        public LitigationCostModuleProgress LitigationCostProgress { get { return (LitigationCostModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public override void ExecuteModule()
        {
            if (LitigationGame.DisputeContinues())
                RegisterDisputeExists((LitigationCostInputs) GameModuleInputs);
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            throw new Exception("This must be overridden. The overridden method should call SetGameAndStrategies.");
        }

        public virtual void RegisterDisputeExists(LitigationCostInputs lcInputs)
        {
            LitigationCostProgress.LitigationCostInputs = lcInputs;
        }

        public virtual void RegisterExtraInvestigationRound(bool isFirstInvestigationRound, bool isLastInvestigationRound, double portionOfRound = 1.0)
        {
        }

        public virtual void UpdatePartiesInformation(bool isFirstInvestigationRound)
        {
        }

        public virtual void RegisterSettlementFailure()
        {
        }

        public virtual void RegisterTrial()
        {
        }

        public virtual void RegisterRetrial()
        {
            RegisterTrial();
        }

        public virtual void ApplyAllLitigationCosts()
        {
            LitigationGame.AdjustmentsModule1.ActionWhenApplyingLitigationCosts();
            LitigationGame.AdjustmentsModule2.ActionWhenApplyingLitigationCosts();
            LitigationGame.LGP.PFinalWealth -= (double)LitigationGame.LitigationCostModule.LitigationCostProgress.PTotalExpenses;
            LitigationGame.LGP.DFinalWealth -= (double)LitigationGame.LitigationCostModule.LitigationCostProgress.DTotalExpenses;
        }

        public virtual void ApplyNonpecuniaryWealthEffects()
        {
            bool applyingEffects =
                    (LitigationGame.CurrentlyEvolvingModule is BargainingAggressivenessOverrideModule || LitigationGame.CurrentlyEvolvingModule is SimultaneousOfferBargainingModule) && // we assume that regret aversion does not affect drop/default decisions; we don't calculate any regret from not dropping or defaulting, and (maybe more importantly) we don't take into account that we will experience some negative utility from regret if we proceed onto bargaining; the point is simply that once bargaining, players experience regret aversion
                    (LitigationGame.LGP.DropInfo == null) &&
                    (LitigationGame.BargainingModule.BargainingProgress.SettlementBlockedThisBargainingRoundBecauseOfEvolution == null || !LitigationGame.BargainingModule.BargainingProgress.SettlementBlockedThisBargainingRoundBecauseOfEvolution.Any(x => x == true)); // if settlement is blocked because of evolution, then we don't want to calculate in regret aversion; instead, regret aversion should be relevant to calculating how aggressive players are in the bargaining range;
            if (applyingEffects)
            {
                if (LitigationGame.BargainingModule.BargainingProgress.SettlementExists == true)
                {
                    // Note that when evolving utility range bargaining module, we assume that settlement fails, so taste for settlement will NOT
                    // affect the bargaining range.
                    double pTasteForSettlementEffect = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim * LitigationGame.BargainingInputs.PTasteForSettlement;
                    double dTasteForSettlementEffect = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim * LitigationGame.BargainingInputs.DTasteForSettlement;
                    LitigationGame.LGP.PFinalWealth += pTasteForSettlementEffect;
                    LitigationGame.LGP.DFinalWealth += dTasteForSettlementEffect;
                }
                else if (LitigationGame.BargainingModule.BargainingProgress.SettlementExists == false)
                {
                    // apply regret aversion. For each party, see if the damages awarded is worse than the ultimate amount adjudicated, disregarding attorneys' fees. 
                    // If so, multiply the difference by the extent of regret aversion.
                    var bp = LitigationGame.BargainingModule.BargainingProgress;
                    double pBestOfferToD = 0, dBestOfferToP = 0;
                    if (bp.POfferList != null)
                        pBestOfferToD = (double)bp.POfferList.Where(x => x != null).Min() * LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;
                    if (bp.DOfferList != null)
                        dBestOfferToP = (double)bp.DOfferList.Where(x => x != null).Max() * LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;

                    if (LitigationGame.LGP.DamagesPaymentFromDToP > pBestOfferToD)
                        LitigationGame.LGP.DFinalWealth -= (LitigationGame.LGP.DamagesPaymentFromDToP - pBestOfferToD) * LitigationGame.BargainingInputs.DRegretAversion;
                    if (LitigationGame.LGP.DamagesPaymentFromDToP < dBestOfferToP)
                        LitigationGame.LGP.PFinalWealth -= (dBestOfferToP - LitigationGame.LGP.DamagesPaymentFromDToP) * LitigationGame.BargainingInputs.PRegretAversion;
                }
            }
        }

        public virtual double GetFinalWealth(bool plaintiff, bool useContingencyLawyerInsteadWhereApplicable = true)
        {
            if (plaintiff)
                return LitigationGame.LGP.PFinalWealth;
            else
                return LitigationGame.LGP.DFinalWealth;
        }

        public virtual double GetFinalWealthSubjectiveUtilityValue(bool plaintiff, bool useSocialWelfareMeasureIfOptionIsSet = true, bool useContingencyLawyerInsteadWhereApplicable = true)
        {
            if (useSocialWelfareMeasureIfOptionIsSet && LitigationGame.LitigationGameInputs.PlayersActToMaximizeSocialWelfare)
            {
                return 0 - LitigationGame.LGP.DisputeGeneratorModuleProgress.SocialLoss; // negative of social loss to make it a positive indication of social welfare
            }
            else
            {
                if (plaintiff)
                    return LitigationGame.Plaintiff.GetSubjectiveUtilityForWealthLevel(LitigationGame.LGP.PFinalWealth);
                else
                    return LitigationGame.Defendant.GetSubjectiveUtilityForWealthLevel(LitigationGame.LGP.DFinalWealth);
            }
        }

        public override OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            if (!forEvolution && secondActionGroup.Name.Contains("BeginningDropOrDefaultModule"))
                return OrderingConstraint.Before;
            if (forEvolution && secondActionGroup.Name.Contains("BeginningDropOrDefaultModule"))
                return OrderingConstraint.After;
            return null;
        }
    }
}
