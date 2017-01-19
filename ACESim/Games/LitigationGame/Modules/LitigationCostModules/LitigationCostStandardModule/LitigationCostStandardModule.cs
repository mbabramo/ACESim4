using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Diagnostics;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "LitigationCostStandardModule")]
    [Serializable]
    public class LitigationCostStandardModule : LitigationCostModule, ICodeBasedSettingGenerator
    {
        public LitigationCostStandardModuleProgress LCSProgress { get { return (LitigationCostStandardModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public LitigationCostStandardInputs LitigationCostStandardInputs { get { return (LitigationCostStandardInputs)GameModuleInputs; } set { GameModuleInputs = value; } }

        public override void RegisterDisputeExists(LitigationCostInputs lcInputs)
        {
            LitigationCostProgress.LitigationCostInputs = LitigationCostStandardInputs = (LitigationCostStandardInputs)lcInputs;
            LCSProgress.PAnticipatedTrialExpenses = LitigationCostStandardInputs.CommonTrialExpenses + LitigationCostStandardInputs.PAdditionalTrialExpenses;
            LCSProgress.DAnticipatedTrialExpenses = LitigationCostStandardInputs.CommonTrialExpenses + LitigationCostStandardInputs.DAdditionalTrialExpenses;
            LCSProgress.PInvestigationExpenses += LitigationCostStandardInputs.PInvestigationExpensesIfDispute;
            LCSProgress.DInvestigationExpenses += LitigationCostStandardInputs.DInvestigationExpensesIfDispute;
            LCSProgress.CalculateTotalExpensesForReporting();
        }

        public override void RegisterExtraInvestigationRound(bool isFirstInvestigationRound, bool isLastInvestigationRound, double portionOfRound = 1.0)
        {
            double pExpenses = LitigationCostStandardInputs.PMarginalInvestigationExpensesAfterEachMiddleBargainingRound;
            double dExpenses = LitigationCostStandardInputs.DMarginalInvestigationExpensesAfterEachMiddleBargainingRound;
            if (isLastInvestigationRound)
            { // last takes precedence over first if there is only one
                pExpenses = LitigationCostStandardInputs.PMarginalInvestigationExpensesAfterLastBargainingRound;
                dExpenses = LitigationCostStandardInputs.DMarginalInvestigationExpensesAfterLastBargainingRound;
            }
            else if (isFirstInvestigationRound)
            {
                pExpenses = LitigationCostStandardInputs.PMarginalInvestigationExpensesAfterFirstBargainingRound;
                dExpenses = LitigationCostStandardInputs.DMarginalInvestigationExpensesAfterFirstBargainingRound;
            }
            pExpenses *= portionOfRound;
            dExpenses *= portionOfRound;
            LCSProgress.PInvestigationExpenses += pExpenses;
            LCSProgress.DInvestigationExpenses += dExpenses;
            LCSProgress.NumberInvestigativeRounds++;
            LCSProgress.CalculateTotalExpensesForReporting();
        }

        public override void UpdatePartiesInformation(bool isFirstInvestigationRound)
        {
            if (LitigationCostStandardInputs.PartiesInformationImprovesOverTime)
            { // improve the parties' information
                if (isFirstInvestigationRound)
                {
                    LitigationGame.BaseDamagesForecastingModule.UpdateCombinedForecasts(LitigationCostStandardInputs.NoiseLevelOfPlaintiffFirstIndependentInformation, LitigationCostStandardInputs.NoiseLevelOfDefendantFirstIndependentInformation);
                    LitigationGame.BaseProbabilityForecastingModule.UpdateCombinedForecasts(LitigationCostStandardInputs.NoiseLevelOfPlaintiffFirstIndependentInformation, LitigationCostStandardInputs.NoiseLevelOfDefendantFirstIndependentInformation);
                }
                else
                {
                    LitigationGame.BaseDamagesForecastingModule.UpdateCombinedForecasts(LitigationCostStandardInputs.NoiseLevelOfPlaintiffIndependentInformation, LitigationCostStandardInputs.NoiseLevelOfDefendantIndependentInformation);
                    LitigationGame.BaseProbabilityForecastingModule.UpdateCombinedForecasts(LitigationCostStandardInputs.NoiseLevelOfPlaintiffIndependentInformation, LitigationCostStandardInputs.NoiseLevelOfDefendantIndependentInformation);
                }
            }
        }

        public override void RegisterSettlementFailure()
        {
            LCSProgress.PInvestigationExpenses += LitigationCostStandardInputs.PSettlementFailureCostPerBargainingRound;
            LCSProgress.DInvestigationExpenses += LitigationCostStandardInputs.DSettlementFailureCostPerBargainingRound;
            LCSProgress.CalculateTotalExpensesForReporting();
        }

        public override void RegisterTrial()
        {
            RegisterTrialForParticularMultiplierOfExpenses(1.0);
        }

        internal virtual void RegisterTrialForParticularMultiplierOfExpenses(double multiplier)
        {
            LCSProgress.PTrialExpenses += multiplier * (LitigationCostStandardInputs.CommonTrialExpenses + LitigationCostStandardInputs.PAdditionalTrialExpenses);
            LCSProgress.DTrialExpenses += multiplier * (LitigationCostStandardInputs.CommonTrialExpenses + LitigationCostStandardInputs.DAdditionalTrialExpenses);
            LCSProgress.CalculateTotalExpensesForReporting();
        }

        public override void RegisterRetrial()
        {
            RegisterTrial();
        }

        public override void ApplyAllLitigationCosts()
        {
            LitigationGame.AdjustmentsModule1.ActionWhenApplyingLitigationCosts();
            LitigationGame.AdjustmentsModule2.ActionWhenApplyingLitigationCosts();
            if (!LitigationCostStandardInputs.LoserPaysRule)
            {
                AssignEachPartyItsOwnCosts(); 
                if (LitigationGame.LGP.TrialOccurs)
                    ApplyTrialTaxes();
            }
            else
            {
                if (LitigationGame.LGP.TrialOccurs)
                {
                    if (!LitigationGame.LGP.ScoreCalculatedProportionatelyRelativeToProbabilityOfVictory && LitigationGame.LGP.MarginOfVictory /* never negative */ > LitigationCostStandardInputs.MinimumMarginOfVictoryForLoserPays)
                        /* basic non-proportional fee shifting */
                        AssignLoserWinnersCosts((bool)LitigationGame.LGP.PWins ? 1.0 : 0.0, LitigationCostStandardInputs.LimitLoserPaysToOwnExpenses, LitigationCostStandardInputs.LoserPaysMultiple);
                    else if (LitigationGame.LGP.ScoreCalculatedProportionatelyRelativeToProbabilityOfVictory)
                        /* proportional fee shifting */
                        AssignLoserWinnersCosts((double) LitigationGame.LGP.UltimateProbabilityOfPVictory, LitigationCostStandardInputs.LimitLoserPaysToOwnExpenses, LitigationCostStandardInputs.LoserPaysMultiple);
                    else
                        AssignEachPartyItsOwnCosts();

                    ApplyTrialTaxes();
                }
                else
                {
                    bool lawsuitAbandoned = false;
                    double paymentByDProportion;
                    if (LitigationGame.LGP.DropInfo != null)
                    {
                        if (LitigationGame.LGP.DropInfo.DroppedByPlaintiff)
                        {
                            lawsuitAbandoned = true;
                            paymentByDProportion = 0.0;
                        }
                        else
                        {
                            lawsuitAbandoned = true;
                            paymentByDProportion = 1.0;
                        }
                    }
                    else
                        paymentByDProportion = (double)LitigationGame.BargainingModule.BargainingProgress.SettlementProgress.OverallSuccessOfPlaintiff();
                    if (lawsuitAbandoned && !LitigationCostStandardInputs.ApplyLoserPaysWhenCasesAbandoned)
                        AssignEachPartyItsOwnCosts();
                    else
                    {
                        bool settlementResultIsEquivalentToPLoss = paymentByDProportion < 0.5;
                        double losersShare = settlementResultIsEquivalentToPLoss ? paymentByDProportion : 1.0 - paymentByDProportion;
                        if (losersShare < LitigationCostStandardInputs.ApplyLoserPaysToSettlementsForLessThanThisProportionOfDamages)
                        {
                            AssignLoserWinnersCosts(settlementResultIsEquivalentToPLoss ? 0.0 : 1.0, LitigationCostStandardInputs.LimitLoserPaysToOwnExpenses, LitigationCostStandardInputs.LoserPaysMultiple);
                        }
                        else
                            AssignEachPartyItsOwnCosts();
                    }
                }
            }
            PayContingencyFeeLawyerIfNecessary();
            LCSProgress.CalculateTotalExpensesForReporting();
            if (GameProgressLogger.LoggingOn)
            {
                GameProgressLogger.Log("Final wealth for P and D " + LitigationGame.LGP.PFinalWealth + ", " + LitigationGame.LGP.DFinalWealth);
                GameProgressLogger.Log("Final utility for P and D " + GetFinalWealthSubjectiveUtilityValue(true) + ", " + GetFinalWealthSubjectiveUtilityValue(false));
                
            }
        }

        private void ApplyTrialTaxes()
        {
            LitigationGame.LGP.PFinalWealth -= LitigationCostStandardInputs.TrialTaxEachParty;
            LitigationGame.LGP.DFinalWealth -= LitigationCostStandardInputs.TrialTaxEachParty;
            if (LitigationGame.LGP.ScoreCalculatedProportionatelyRelativeToProbabilityOfVictory)
            {
                LitigationGame.LGP.PFinalWealth -= (1.0 - (double)LitigationGame.LGP.UltimateProbabilityOfPVictory) * LitigationCostStandardInputs.TrialTaxLoser;
                LitigationGame.LGP.DFinalWealth -= (double)LitigationGame.LGP.UltimateProbabilityOfPVictory * (LitigationCostStandardInputs.TrialTaxLoser + LitigationCostStandardInputs.TrialTaxDLoser);
            }
            else
            {
                if (LitigationGame.LGP.PWins == true)
                    LitigationGame.LGP.DFinalWealth -= LitigationCostStandardInputs.TrialTaxLoser + LitigationCostStandardInputs.TrialTaxDLoser;
                else
                    LitigationGame.LGP.PFinalWealth -= LitigationCostStandardInputs.TrialTaxLoser;
            }
        }

        public void AssignEachPartyItsOwnCosts()
        {
            double pTotalExpenses = (double)LitigationGame.LitigationCostModule.LitigationCostProgress.PTotalExpenses;
            double dTotalExpenses = (double)LitigationGame.LitigationCostModule.LitigationCostProgress.DTotalExpenses;
            if (LitigationCostStandardInputs.UseContingencyFees)
                LCSProgress.PlaintiffLawyerNetUtility -= pTotalExpenses;
            else
                LitigationGame.LGP.PFinalWealth -= pTotalExpenses;
            LitigationGame.LGP.DFinalWealth -= dTotalExpenses;
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Assigning parties own costs: p " + pTotalExpenses + " d " + dTotalExpenses);
        }

        private void AssignLoserWinnersCosts(double probabilityPlaintiffWin, bool limitToLosersCosts, double multiple)
        {
            double pTotalExpenses = (double)LitigationGame.LitigationCostModule.LitigationCostProgress.PTotalExpenses;
            double dTotalExpenses = (double)LitigationGame.LitigationCostModule.LitigationCostProgress.DTotalExpenses;
            if (probabilityPlaintiffWin > 0)
            {
                double amountToReassign = limitToLosersCosts ? Math.Min(pTotalExpenses, dTotalExpenses) : pTotalExpenses;
                amountToReassign *= multiple * probabilityPlaintiffWin;
                LitigationGame.LGP.FeeShiftingPositiveIfFromP = amountToReassign;
                pTotalExpenses -= amountToReassign; // plaintiff wins, so its costs are paid for
                dTotalExpenses += amountToReassign; // defendant must pay them
            }
            if (probabilityPlaintiffWin < 1.0)
            {
                double amountToReassign = limitToLosersCosts ? Math.Min(pTotalExpenses, dTotalExpenses) : dTotalExpenses;
                LitigationGame.LGP.FeeShiftingPositiveIfFromP = 0 - amountToReassign;
                amountToReassign *= multiple * (1.0 - probabilityPlaintiffWin);
                pTotalExpenses += amountToReassign; // defendant wins, so plaintiff must bear at least some of these costs
                dTotalExpenses -= amountToReassign;
            }
            if (LitigationCostStandardInputs.UseContingencyFees)
                LCSProgress.PlaintiffLawyerNetUtility -= pTotalExpenses;
            else
                LitigationGame.LGP.PFinalWealth -= pTotalExpenses;
            LitigationGame.LGP.DFinalWealth -= dTotalExpenses;

            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("After assigning loser costs expenses: p " + pTotalExpenses + " d " + dTotalExpenses);
        }

        public void PayContingencyFeeLawyerIfNecessary()
        {
            if (LitigationCostStandardInputs.UseContingencyFees)
            {
                double payment = LitigationGame.LGP.DamagesPaymentFromDToP  * LitigationCostStandardInputs.ContingencyFeeRate;
                LCSProgress.PlaintiffLawyerNetUtility += payment; // lawyer was charged for expenses but now receives payment
                LitigationGame.LGP.PFinalWealth -= payment; // plaintiff was not charged for expenses

                if (GameProgressLogger.LoggingOn)
                    GameProgressLogger.Log("P wealth decreases by contingency payment to lawyer of " + payment);
            }
        }

        public override double GetFinalWealth(bool plaintiff, bool useContingencyLawyerInsteadWhereApplicable = true)
        {
            if (plaintiff && useContingencyLawyerInsteadWhereApplicable && LitigationCostStandardInputs.UseContingencyFees)
                return LCSProgress.PlaintiffLawyerNetUtility;
            return base.GetFinalWealth(plaintiff, useContingencyLawyerInsteadWhereApplicable);
        }

        public override double GetFinalWealthSubjectiveUtilityValue(bool plaintiff, bool useSocialWelfareMeasureIfOptionIsSet = true, bool useContingencyLawyerInsteadWhereApplicable = true)
        {
            if (plaintiff && useContingencyLawyerInsteadWhereApplicable && LitigationCostStandardInputs.UseContingencyFees && !(LitigationGame.LitigationGameInputs.PlayersActToMaximizeSocialWelfare && useSocialWelfareMeasureIfOptionIsSet))
                return LCSProgress.PlaintiffLawyerNetUtility;
            return base.GetFinalWealthSubjectiveUtilityValue(plaintiff, useSocialWelfareMeasureIfOptionIsSet, useContingencyLawyerInsteadWhereApplicable);
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            LitigationCostStandardModule copy = new LitigationCostStandardModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = LitigationCostStandardModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            return new LitigationCostStandardModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "LitigationCostStandard" },
                GameModuleName = "LitigationCostModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { }
            };
        }

       
    }
}
