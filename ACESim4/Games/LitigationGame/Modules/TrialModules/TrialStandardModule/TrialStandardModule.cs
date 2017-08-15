using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Diagnostics;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "TrialStandardModule")]
    [Serializable]
    public class TrialStandardModule : TrialModule, ICodeBasedSettingGenerator
    {
        public TrialStandardModuleProgress TrialStandardProgress { get { return (TrialStandardModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public TrialStandardInputs TrialStandardInputs { get { return (TrialStandardInputs)GameModuleInputs; } }

        public override void HoldTrial()
        {
            base.HoldTrial();
            if (TrialStandardInputs.UseProportionalResultsWhenEvolvingEarlierDecisions && LitigationGame.LitigationCostModule.GameModuleInputs is LitigationCostStandardInputs && ((LitigationCostStandardInputs)LitigationGame.LitigationCostModule.GameModuleInputs).MinimumMarginOfVictoryForLoserPays > 0)
                throw new NotImplementedException("Use of proportional results is not implemented when the minimum margin of victory is set to a number other than zero.");
            // We use proportional score calculation only when the decision being evolved executes before the current decision.
            LGP.ScoreCalculatedProportionatelyRelativeToProbabilityOfVictory = TrialStandardInputs.UseProportionalResultsWhenEvolvingEarlierDecisions && Game.CurrentlyEvolvingDecisionAlreadyExecuted;
            if (TrialStandardInputs.CaseResolvedBasedOnExogenouslySpecifiedProbability)
                HoldTrial_ExogenouslySpecifiedProbabilityOfVictory();
            else
                HoldTrial_JudgeEstimateOfEvidence();
        }

        private void HoldTrial_ExogenouslySpecifiedProbabilityOfVictory()
        {
            LGP.UltimateProbabilityOfPVictory = TrialStandardProgress.GetShiftedValue(LitigationGame.DisputeGeneratorModule.DGProgress.BaseProbabilityPWins);
            LGP.UltimateDamagesIfPWins = LitigationGame.DisputeGeneratorModule.DGProgress.BaseDamagesIfPWins;
            LGP.PWins = LGP.UltimateProbabilityOfPVictory > TrialStandardInputs.DisputeResolutionRandomSeedLiability;
            LGP.MarginOfVictory = Math.Abs(LitigationGame.DisputeGeneratorModule.DGProgress.BaseProbabilityPWins - TrialStandardInputs.DisputeResolutionRandomSeedLiability);
            LGP.DWins = !LGP.PWins;
            bool resolvedByPartialSettlement = EnforcePartialSettlementIfAny();
            if (!resolvedByPartialSettlement)
            {
                if (LGP.ScoreCalculatedProportionatelyRelativeToProbabilityOfVictory)
                {
                    double proportionalDamages = (double)LGP.UltimateProbabilityOfPVictory * (double)LGP.UltimateDamagesIfPWins;
                    LGP.DamagesPaymentFromDToP = proportionalDamages;
                }
                else
                {
                    if ((bool)LGP.PWins)
                        LGP.DamagesPaymentFromDToP = (double)LGP.UltimateDamagesIfPWins;
                }
            }
            LitigationGame.LitigationCostModule.RegisterTrial();
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Damages payment from D to P " + LGP.DamagesPaymentFromDToP);
        }

        public void HoldTrial_JudgeEstimateOfEvidence()
        {
            bool useUniformLiabilityDistribution = true; 
            CumulativeDistribution liabilityDistribution;
            if (useUniformLiabilityDistribution)
                liabilityDistribution = GetUniformCumulativeDistribution.Get();
            else
                liabilityDistribution = LitigationGame.MostRecentCumulativeDistributions[0];
            if (liabilityDistribution == null)
                throw new Exception("Internal exception: Could not find liability distribution.");
            double liabilityRandSeedShiftedBasedOnEffort = TrialStandardProgress.GetShiftedValue(TrialStandardInputs.DisputeResolutionRandomSeedLiability);
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Trial random seed liability " + TrialStandardInputs.DisputeResolutionRandomSeedLiability + " shifted based on effort " + liabilityRandSeedShiftedBasedOnEffort);
            double judgeBias = LitigationGame.BaseProbabilityForecastingInputs.BiasAffectingEntireLegalSystem + LitigationGame.BaseProbabilityForecastingInputs.BiasAffectingJudge;
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Bias affecting legal system " + LitigationGame.BaseProbabilityForecastingInputs.BiasAffectingEntireLegalSystem + " + affecting judge " + LitigationGame.BaseProbabilityForecastingInputs.BiasAffectingJudge + " = judge bias " + judgeBias);
            double liabilityEstimate;
            ValueFromSignalEstimatorBasedOnSingleSignal.DoEstimate(liabilityDistribution, LGP.DisputeGeneratorModuleProgress.EvidentiaryStrengthLiability, TrialStandardInputs.JudgeNoiseLevelLiability, judgeBias, true, out liabilityEstimate, liabilityRandSeedShiftedBasedOnEffort, null);

            LGP.PWins = liabilityEstimate > 0.5;
            LGP.UltimateProbabilityOfPVictory = liabilityEstimate; // Note that here we don't adjust the liability estimate by the probability, since we just calculated in liabilityEstimate the probability that the liability distribution is over 0.5, given the judge's information
            LGP.MarginOfVictory = Math.Abs(liabilityEstimate - 0.5);
            LGP.DWins = !LGP.PWins;

            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Trial judge liability estimate: " + liabilityEstimate + " p wins? " + LGP.PWins);

            CumulativeDistribution damagesDistribution = LitigationGame.MostRecentCumulativeDistributions[1];
            double damagesRandSeedShiftedBasedOnEffort = TrialStandardProgress.GetShiftedValue(TrialStandardInputs.DisputeResolutionRandomSeedDamages);
            double damagesEstimateAsPctOfClaim;
            ValueFromSignalEstimatorBasedOnSingleSignal.DoEstimate(damagesDistribution, LGP.DisputeGeneratorModuleProgress.BaseDamagesIfPWinsAsPctOfClaim, TrialStandardInputs.JudgeNoiseLevelDamages, judgeBias, false, out damagesEstimateAsPctOfClaim, damagesRandSeedShiftedBasedOnEffort);
            LGP.UltimateDamagesIfPWins = damagesEstimateAsPctOfClaim * LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;

            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Ultimate damages if p wins: " + LGP.UltimateDamagesIfPWins);

            bool resolvedByPartialSettlement = EnforcePartialSettlementIfAny();
            if (!resolvedByPartialSettlement)
            {

                double adjustedUltimateDamagesIfPWins = (double)LGP.UltimateDamagesIfPWins;
                double paymentFromPToDIfDWins = 0;
                AdjustDamagesAmounts(ref adjustedUltimateDamagesIfPWins, ref paymentFromPToDIfDWins);
                if (LGP.ScoreCalculatedProportionatelyRelativeToProbabilityOfVictory)
                {
                    LGP.DamagesPaymentFromDToP = adjustedUltimateDamagesIfPWins * (double)LGP.UltimateProbabilityOfPVictory - paymentFromPToDIfDWins * (1.0 - (double)LGP.UltimateProbabilityOfPVictory);
                    
                    if (GameProgressLogger.LoggingOn)
                        GameProgressLogger.Log("Proportional damages since damages calculated proportionately: " + LGP.DamagesPaymentFromDToP);
                }
                else
                {
                    if ((bool)LGP.PWins)
                    {
                        LGP.DamagesPaymentFromDToP = adjustedUltimateDamagesIfPWins;
                    }
                    else if ((bool)LGP.DWins)
                    {
                        LGP.DamagesPaymentFromDToP = 0 - paymentFromPToDIfDWins;
                    }
                }
            }
            
            LitigationGame.LitigationCostModule.RegisterTrial();
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Damages payment from D to P " + LGP.DamagesPaymentFromDToP);
        }


        private bool EnforcePartialSettlementIfAny()
        {
            bool resolvedByPartialSettlement = false; // assume for now
            if (LitigationGame.BargainingModule is UtilityRangeBargainingModule)
            {
                UtilityRangeBargainingModule urbm = (UtilityRangeBargainingModule)LitigationGame.BargainingModule;
                if (urbm.UtilityRangeBargainingModuleSettings.PartialSettlementEnforced && urbm.BargainingProgress.SettlementProgress is ProbabilityAndMagnitudeSettlementProgress)
                {
                    ProbabilityAndMagnitudeSettlementProgress settProg = (ProbabilityAndMagnitudeSettlementProgress)urbm.BargainingProgress.SettlementProgress;

                    // check for incompatibilities
                    if (settProg.AgreedUponDamagesProportion != null && settProg.AgreedUponProbability != null && !settProg.BlockSettlementDuringOptimization)
                        throw new Exception("Internal error: Should not resolve partial settlement when full settlement reached.");
                    if (LGP.ScoreCalculatedProportionatelyRelativeToProbabilityOfVictory)
                        throw new Exception("Cannot use partial settlement with proportional scoring.");
                    double adjustedUltimateDamagesIfPWins = (double)LGP.UltimateDamagesIfPWins;
                    double paymentFromPToDIfDWins = 0;
                    AdjustDamagesAmounts(ref adjustedUltimateDamagesIfPWins, ref paymentFromPToDIfDWins);
                    if (adjustedUltimateDamagesIfPWins != LGP.UltimateDamagesIfPWins || paymentFromPToDIfDWins != 0)
                        throw new Exception("Cannot use partial settlement with damages adjustments.");

                    resolvedByPartialSettlement = true; // change assumption for now
                    if (settProg.AgreedUponDamagesProportion != null) // settled on damages but not probability, so we either pay damages or not, depending on what happens in trial
                        LGP.DamagesPaymentFromDToP = LGP.PWins == true ? (double)settProg.AgreedUponDamagesProportion : 0;
                    else if (settProg.AgreedUponProbability != null)
                    {
                        LGP.DamagesPaymentFromDToP = (double)(settProg.AgreedUponProbability * LGP.UltimateDamagesIfPWins);
                        LGP.PWins = LGP.UltimateProbabilityOfPVictory > 0.5; // we'll call this a "win" but we're going to resolve proportionately.
                    }
                    else
                        resolvedByPartialSettlement = false;
                }
            }
            if (resolvedByPartialSettlement)
                TrialProgress.PartialSettlementEnforced = true;
            return resolvedByPartialSettlement;
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            TrialStandardModule copy = new TrialStandardModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = TrialStandardModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }


        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            return new TrialStandardModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "Trial" },
                GameModuleName = "TrialModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { }
            };
        }


    }
}
