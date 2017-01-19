using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class LitigationGameProgress : GameProgress
    {
        public LitigationGameInputs Inputs;
        public bool DisputeExists;
        public DropInfo DropInfo;
        public bool TrialOccurs;
        public bool? PWins;
        public bool? DWins;
        public double? PAggressivenessOverride;
        public double? DAggressivenessOverride;
        public double? PAggressivenessOverrideFinal;
        public double? DAggressivenessOverrideFinal;
        public List<double> PAggressivenessOverrideList = new List<double>();
        public List<double> DAggressivenessOverrideList = new List<double>();
        public bool CaseTagged; // we can tag a case during development so that we can focus on a subset of cases in the report
        public bool CaseTagged2; // we can tag a case during development so that we can focus on a subset of cases in the report
        public bool ScoreCalculatedProportionatelyRelativeToProbabilityOfVictory;
        public double? UltimateProbabilityOfPVictory;
        public double? UltimateDamagesIfPWins;
        public double? MarginOfVictory;
        public double DamagesPaymentFromDToP = 0;
        public double FeeShiftingPositiveIfFromP = 0;
        public double PWealthAfterDisputeGenerated;
        public double DWealthAfterDisputeGenerated;
        public double PFinalWealth;
        public double DFinalWealth;
        public double PCompensationError;
        public double DDeterrenceError;
        public double UnderdeterrenceError;
        public double OverdeterrenceError;
        public double UndercompensationError;
        public double OvercompensationError;
        public double? PShouldWinCompensationError;
        public double? PShouldWinDeterrenceError;
        public double? DShouldWinCompensationError;
        public double? DShouldWinDeterrenceError;
        public double PAbsCompensationError;
        public double DAbsDeterrenceError;
        public double AbsError;
        public double AdjError;
        public double DamagesOnlyError;
        public double PCompensationErrorSq;
        public double DDeterrenceErrorSq;
        public double ErrorSq;
        public double? CaseAttribute1; // can be anything (to make it easy to create reports)
        public double? CaseAttribute2; // same
        public double? CaseAttribute3; // same
        public double? CaseAttribute4; // same
        public double? CaseAttribute5; // same
        public double? CaseAttribute6; // same
        public double? CaseAttribute7; // same
        public double? CaseAttribute8; // same
        public string CaseAttributeString; // same

        public DisputeGeneratorModuleProgress DisputeGeneratorModuleProgress;
        public LitigationCostModuleProgress LitigationCostModuleProgress;
        public ValueAndErrorForecastingModuleProgress BaseProbabilityForecastingModuleProgress;
        public ValueAndErrorForecastingModuleProgress BaseDamagesForecastingModuleProgress;
        public DropOrDefaultModuleProgress BeginningDropOrDefaultModuleProgress;
        public BargainingModuleProgress BargainingModuleProgress;
        public DropOrDefaultModuleProgress MidDropOrDefaultModuleProgress;
        public DropOrDefaultModuleProgress EndDropOrDefaultModuleProgress;
        public TrialModuleProgress TrialModuleProgress;

        public override GameProgress DeepCopy()
        {
            LitigationGameProgress copy = new LitigationGameProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(LitigationGameProgress copy)
        {
            copy.Inputs = Inputs;
            copy.DisputeExists = DisputeExists;
            copy.DropInfo = DropInfo;
            copy.TrialOccurs = TrialOccurs;
            copy.PWins = PWins;
            copy.DWins = DWins;
            copy.PAggressivenessOverride = PAggressivenessOverride;
            copy.DAggressivenessOverride = DAggressivenessOverride;
            copy.PAggressivenessOverrideFinal = PAggressivenessOverrideFinal;
            copy.DAggressivenessOverrideFinal = DAggressivenessOverrideFinal;
            copy.PAggressivenessOverrideList = PAggressivenessOverrideList.ToList();
            copy.DAggressivenessOverrideList = DAggressivenessOverrideList.ToList();
            copy.CaseTagged = CaseTagged;
            copy.CaseTagged2 = CaseTagged2;
            copy.ScoreCalculatedProportionatelyRelativeToProbabilityOfVictory = ScoreCalculatedProportionatelyRelativeToProbabilityOfVictory;
            copy.UltimateProbabilityOfPVictory = UltimateProbabilityOfPVictory;
            copy.UltimateDamagesIfPWins = UltimateDamagesIfPWins;
            copy.MarginOfVictory = MarginOfVictory;
            copy.DamagesPaymentFromDToP = DamagesPaymentFromDToP;
            copy.FeeShiftingPositiveIfFromP = FeeShiftingPositiveIfFromP;
            copy.PWealthAfterDisputeGenerated = PWealthAfterDisputeGenerated;
            copy.DWealthAfterDisputeGenerated = DWealthAfterDisputeGenerated;
            copy.PFinalWealth = PFinalWealth;
            copy.DFinalWealth = DFinalWealth;
            copy.PCompensationError = PCompensationError;
            copy.DDeterrenceError = DDeterrenceError;
            copy.UnderdeterrenceError = UnderdeterrenceError;
            copy.OverdeterrenceError = OverdeterrenceError;
            copy.UndercompensationError = UndercompensationError;
            copy.OvercompensationError = OvercompensationError;
            copy.PAbsCompensationError = PAbsCompensationError;
            copy.DAbsDeterrenceError = DAbsDeterrenceError;
            copy.PCompensationErrorSq = PCompensationErrorSq;
            copy.DDeterrenceErrorSq = DDeterrenceErrorSq;
            copy.PShouldWinCompensationError = PShouldWinCompensationError;
            copy.PShouldWinDeterrenceError = PShouldWinDeterrenceError;
            copy.DShouldWinCompensationError = DShouldWinCompensationError;
            copy.DShouldWinDeterrenceError = DShouldWinDeterrenceError;
            copy.AbsError = AbsError;
            copy.AdjError = AdjError;
            copy.ErrorSq = ErrorSq;
            copy.DamagesOnlyError = DamagesOnlyError;
            copy.DisputeGeneratorModuleProgress = DisputeGeneratorModuleProgress;
            copy.LitigationCostModuleProgress = LitigationCostModuleProgress;
            copy.BaseProbabilityForecastingModuleProgress = BaseProbabilityForecastingModuleProgress;
            copy.BaseDamagesForecastingModuleProgress = BaseDamagesForecastingModuleProgress;
            copy.BeginningDropOrDefaultModuleProgress = BeginningDropOrDefaultModuleProgress;
            copy.BargainingModuleProgress = BargainingModuleProgress;
            copy.MidDropOrDefaultModuleProgress = MidDropOrDefaultModuleProgress;
            copy.EndDropOrDefaultModuleProgress = EndDropOrDefaultModuleProgress;
            copy.TrialModuleProgress = TrialModuleProgress;
            copy.CaseAttribute1 = CaseAttribute1;
            copy.CaseAttribute2 = CaseAttribute2;
            copy.CaseAttribute3 = CaseAttribute3;
            copy.CaseAttribute4 = CaseAttribute4;
            copy.CaseAttribute5 = CaseAttribute5;
            copy.CaseAttribute6 = CaseAttribute6;
            copy.CaseAttribute7 = CaseAttribute7;
            copy.CaseAttribute8 = CaseAttribute8;
            copy.CaseAttributeString = CaseAttributeString;

            base.CopyFieldInfo(copy);
        }
        public UtilityMaximizer InputsPlaintiff { get { return (Inputs.GameModulesInputs[GameDefinition.GameModuleNumbersGameReliesOn[(int) LitigationGame.ModuleNumbers.LitigationCost]] as LitigationCostInputs).Plaintiff; } }
        public UtilityMaximizer InputsDefendant { get { return (Inputs.GameModulesInputs[GameDefinition.GameModuleNumbersGameReliesOn[(int)LitigationGame.ModuleNumbers.LitigationCost]] as LitigationCostInputs).Defendant; } }

        public override List<double?> GetVariablesToTrackCumulativeDistributionsOf()
        {
            if (DisputeGeneratorModuleProgress.DisputeGeneratorInitiated && DisputeContinues())
                return new List<double?>() { GetEvidentiaryStrengthLiabilityIfSet(), GetBaseDamagesIfPWinsPctIfSet() };
            else
                return new List<double?>() { null, null };
        }

        public override bool PassesSymmetryTest(GameProgress gameProgressWithSameInputsFlippedAndSwapped)
        {
            LitigationGameProgress flipped = ((LitigationGameProgress)gameProgressWithSameInputsFlippedAndSwapped);
            bool pPaysDamagesToDIfLoses = (this.Inputs.GameModulesInputs.First(x => x is TrialInputs) as TrialInputs).PlaintiffMustPayDamagesIfItLoses;
            if (pPaysDamagesToDIfLoses)
            {
                double damagesPaymentHere = DamagesPaymentFromDToP;
                double damagesPaymentFlipped = flipped.DamagesPaymentFromDToP;
                bool closeEnough = Math.Abs(damagesPaymentHere + damagesPaymentFlipped) < 0.1;
                return closeEnough;
            }
            else
            {
                double damagesClaimIfHalfSuccessful = (double)DisputeGeneratorModuleProgress.DamagesClaim / 2.0;
                double resultHereRelativeToHalfSuccess = DamagesPaymentFromDToP - damagesClaimIfHalfSuccessful;
                double halfSuccessRelativeToFlippedResult = damagesClaimIfHalfSuccessful - flipped.DamagesPaymentFromDToP;
                bool closeEnough = Math.Abs(resultHereRelativeToHalfSuccess - halfSuccessRelativeToFlippedResult) < 0.1;
                return closeEnough;
            }
        }

        public bool DisputeContinues()
        {
            return !DisputeGeneratorModuleProgress.DisputeGeneratorInitiated || 
                (
                DisputeGeneratorModuleProgress.DisputeExists && 
                DropInfo == null
                && !(BargainingModuleProgress.SettlementExists == true)
                );
        }

        public double? GetEvidentiaryStrengthLiabilityIfSet()
        {
            if (DisputeGeneratorModuleProgress == null)
                return null;
            return DisputeGeneratorModuleProgress.EvidentiaryStrengthLiability;
        }

        public double? GetBaseDamagesIfPWinsPctIfSet()
        {
            if (DisputeGeneratorModuleProgress == null)
                return null;
            return DisputeGeneratorModuleProgress.BaseDamagesIfPWinsAsPctOfClaim;
        }

        internal override object GetNonFieldValueForReportFromGameProgress(string variableNameForReport, out bool found)
        {
            found = true; // assume for now
            switch (variableNameForReport)
            {
                case "PInitialWealth":
                    return InputsPlaintiff.InitialWealth;

                case "DInitialWealth":
                    return InputsDefendant.InitialWealth;

                case "PNetWealth":
                    return PFinalWealth - InputsPlaintiff.InitialWealth;

                case "DNetWealth":
                    double dNetWealth = DFinalWealth - InputsDefendant.InitialWealth;
                    return dNetWealth;

                case "TotalNetWealth":
                    return PFinalWealth - InputsPlaintiff.InitialWealth + DFinalWealth - InputsDefendant.InitialWealth;

                case "PWorseOff":
                    return PFinalWealth < InputsPlaintiff.InitialWealth ? 100.0 : 0.0;

                case "PNetUtility":
                    return InputsPlaintiff.GetSubjectiveUtilityForWealthLevel(PFinalWealth) - InputsPlaintiff.GetSubjectiveUtilityForWealthLevel(InputsPlaintiff.InitialWealth);

                case "DNetUtility":
                    double dNetUtility = InputsDefendant.GetSubjectiveUtilityForWealthLevel(DFinalWealth) - InputsDefendant.GetSubjectiveUtilityForWealthLevel(InputsDefendant.InitialWealth);
                    return dNetUtility;

                case "TotalNetUtility":
                    return InputsPlaintiff.GetSubjectiveUtilityForWealthLevel(PFinalWealth) - InputsPlaintiff.GetSubjectiveUtilityForWealthLevel(InputsPlaintiff.InitialWealth) + InputsDefendant.GetSubjectiveUtilityForWealthLevel(DFinalWealth) - InputsDefendant.GetSubjectiveUtilityForWealthLevel(InputsDefendant.InitialWealth);

                case "PNetUtilityLitigOnly":
                    return InputsPlaintiff.GetSubjectiveUtilityForWealthLevel(PFinalWealth) - InputsPlaintiff.GetSubjectiveUtilityForWealthLevel(PWealthAfterDisputeGenerated);

                case "DNetUtilityLitigOnly":
                    return InputsDefendant.GetSubjectiveUtilityForWealthLevel(DFinalWealth) - InputsDefendant.GetSubjectiveUtilityForWealthLevel(DWealthAfterDisputeGenerated);

                case "TotalNetUtilityLitigOnly":
                    return InputsPlaintiff.GetSubjectiveUtilityForWealthLevel(PFinalWealth) - InputsPlaintiff.GetSubjectiveUtilityForWealthLevel(PWealthAfterDisputeGenerated) + InputsDefendant.GetSubjectiveUtilityForWealthLevel(DFinalWealth) - InputsDefendant.GetSubjectiveUtilityForWealthLevel(DWealthAfterDisputeGenerated);

                case "PWinsIfTrialPct":
                    if (PWins == null)
                    {
                        found = true;
                        return null;
                    }
                    return TrialOccurs ? ((bool) PWins ? (double?)100.0 : (double?)0.0) : (double?) null;

                case "DWinsIfTrialPct":
                    if (DWins == null)
                    {
                        found = true;
                        return null;
                    }
                    return TrialOccurs ? ((bool) DWins ? (double?)100.0 : (double?)0.0) : (double?)null;

                default:
                    found = false;
                    return null;
            }
        }
    }
}
