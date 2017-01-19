using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class TrialModule : GameModule
    {

        public LitigationGame LitigationGame { get { return (LitigationGame)Game; } }
        public TrialModuleProgress TrialProgress { get { return (TrialModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public TrialInputs TrialInputs { get { return (TrialInputs)GameModuleInputs; } }
        public LitigationGameProgress LGP { get { return LitigationGame.LGP; } }

        public override void ExecuteModule()
        {
            if (Game.CurrentActionPointName == "Trial")
            {
                LGP.DisputeExists = LitigationGame.DisputeGeneratorModule.DGProgress.DisputeExists;
                if (LGP.DisputeExists)
                {
                    if (LitigationGame.LGP.DropInfo != null)
                    {
                        if (GameProgressLogger.LoggingOn)
                            GameProgressLogger.Log("Suit was dropped -- no trial or settlement.");
                        if (LitigationGame.LGP.DropInfo.DroppedByPlaintiff)
                        {
                            LGP.TrialOccurs = false;
                            LGP.DamagesPaymentFromDToP = 0;
                        }
                        else
                        {
                            LGP.TrialOccurs = false;
                            LGP.DamagesPaymentFromDToP = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;
                        }
                    }
                    else
                    {
                        if (GameProgressLogger.LoggingOn)
                            GameProgressLogger.Log("Suit was not dropped. Does settlement exist? " + LitigationGame.BargainingModule.BargainingProgress.SettlementExists);
                        if (LitigationGame.BaseProbabilityForecastingModule is IndependentEstimatesModule)
                        {
                            ((IndependentEstimatesModule)(LitigationGame.BaseProbabilityForecastingModule)).GetLatestEstimates(out TrialProgress.PEstimatePResultAtTrial, out TrialProgress.DEstimatePResultAtTrial);
                        }
                        LGP.TrialOccurs = !((bool)LitigationGame.BargainingModule.BargainingProgress.SettlementExists);
                        if (LGP.TrialOccurs)
                            HoldTrial();
                        else
                            EnforceSettlement();
                    }

                    LGP.PFinalWealth += LGP.DamagesPaymentFromDToP;
                    LGP.DFinalWealth -= LGP.DamagesPaymentFromDToP;

                    LitigationGame.LitigationCostModule.ApplyAllLitigationCosts();
                    LitigationGame.LitigationCostModule.ApplyNonpecuniaryWealthEffects();
                    CalculateCompensationAndDeterrenceErrors();
                }
                LitigationGame.DisputeGeneratorModule.CalculateSocialLoss();
            }
        }

        private void CalculateCompensationAndDeterrenceErrors()
        {
            double amountPShouldWinAndDShouldPay = LitigationGame.DisputeGeneratorModule.DGProgress.PShouldWin ? LitigationGame.DisputeGeneratorModule.DGProgress.BaseDamagesIfPWins : 0;
            double amountPWinsAfterExpenses = LGP.PFinalWealth - LGP.PWealthAfterDisputeGenerated;
            double amountDPaysAfterExpenses = LGP.DWealthAfterDisputeGenerated - LGP.DFinalWealth;
            LGP.PCompensationError = amountPWinsAfterExpenses - amountPShouldWinAndDShouldPay;
            LGP.DDeterrenceError = 0 - (amountPShouldWinAndDShouldPay - amountDPaysAfterExpenses); 
            LGP.PAbsCompensationError = Math.Abs(LGP.PCompensationError);
            LGP.DAbsDeterrenceError = Math.Abs(LGP.DDeterrenceError);
            LGP.AbsError = (LGP.PAbsCompensationError + LGP.DAbsDeterrenceError) / 2.0;
            if (LGP.DDeterrenceError < 0)
                LGP.UnderdeterrenceError = Math.Abs(LGP.DDeterrenceError);
            else
                LGP.OverdeterrenceError = LGP.DDeterrenceError;
            if (LGP.PCompensationError < 0)
                LGP.UndercompensationError = Math.Abs(LGP.PCompensationError);
            else
                LGP.OvercompensationError = LGP.PCompensationError;
            if (LitigationGame.DisputeGeneratorModule.DGProgress.PShouldWin)
            {
                LGP.PShouldWinCompensationError = LGP.PCompensationError;
                LGP.PShouldWinDeterrenceError = LGP.DDeterrenceError;
                LGP.DShouldWinCompensationError = null;
                LGP.DShouldWinDeterrenceError = null;
            }
            else
            {
                LGP.PShouldWinCompensationError = null;
                LGP.PShouldWinDeterrenceError = null;
                LGP.DShouldWinCompensationError = LGP.PCompensationError;
                LGP.DShouldWinDeterrenceError = LGP.DDeterrenceError;
            }
            LGP.ErrorSq = LGP.AbsError * LGP.AbsError;
            LGP.AdjError = LGP.AbsError * LitigationGame.DisputeGeneratorModule.DGProgress.AdjustedErrorWeight;
            LGP.DamagesOnlyError = Math.Abs(amountPShouldWinAndDShouldPay - LGP.DamagesPaymentFromDToP);
            LGP.PCompensationErrorSq = LGP.PCompensationError * LGP.PCompensationError;
            LGP.DDeterrenceErrorSq = LGP.DDeterrenceError * LGP.DDeterrenceError;
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            throw new Exception("This must be overridden. The overridden method should call SetGameAndStrategies.");
        }

        public virtual void EnforceSettlement()
        {
            LGP.DamagesPaymentFromDToP = (double)(LitigationGame.BargainingModule.BargainingProgress.SettlementProgress.CompleteSettlementAmount());
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Enforcing settlement at " + LGP.DamagesPaymentFromDToP);
        }

        public virtual void HoldTrial()
        {
            LitigationGame.AdjustmentsModule1.ActionBeforeTrial();
            LitigationGame.AdjustmentsModule2.ActionBeforeTrial();
        }

        public void AdjustDamagesAmounts(ref double ultimateDamagesIfPWins, ref double paymentFromPToDIfDWins)
        {
            LitigationGame.AdjustmentsModule1.AdjustDamagesAmounts(ref ultimateDamagesIfPWins, ref paymentFromPToDIfDWins);
            LitigationGame.AdjustmentsModule2.AdjustDamagesAmounts(ref ultimateDamagesIfPWins, ref paymentFromPToDIfDWins);
            if (TrialInputs.PlaintiffMustPayDamagesIfItLoses)
                paymentFromPToDIfDWins = ultimateDamagesIfPWins;
        }

        public override OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            if (!forEvolution && secondActionGroup.Name.Contains("EndDropOrDefault"))
                return OrderingConstraint.After;
            if (forEvolution && secondActionGroup.Name.Contains("EndDropOrDefault"))
                return OrderingConstraint.Before;
            return null;
        }
    }
}
