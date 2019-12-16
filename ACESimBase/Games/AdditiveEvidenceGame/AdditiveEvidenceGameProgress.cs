using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    [Serializable]
    public class AdditiveEvidenceGameProgress : GameProgress
    {
        public AdditiveEvidenceGameDefinition AdditiveEvidenceGameDefinition => (AdditiveEvidenceGameDefinition)GameDefinition;
        public AdditiveEvidenceGameOptions AdditiveEvidenceGameOptions => AdditiveEvidenceGameDefinition.Options;

        public byte Chance_Plaintiff_Quality;
        public byte Chance_Defendant_Quality;
        public byte Chance_Plaintiff_Bias;
        public byte Chance_Defendant_Bias;
        public double Chance_Plaintiff_Quality_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Plaintiff_Quality - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels, false);
        public double Chance_Defendant_Quality_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Defendant_Quality - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels, false);
        public double Chance_Plaintiff_Bias_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Plaintiff_Bias - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels, false);
        public double Chance_Defendant_Bias_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Defendant_Bias - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels, false);

        public byte POffer;
        public byte DOffer;
        public double POfferContinuous => EquallySpaced.GetLocationOfEquallySpacedPoint(POffer - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumOffers, false);
        public double DOfferContinuous => EquallySpaced.GetLocationOfEquallySpacedPoint(DOffer - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumOffers, false);

        public byte Chance_Neither_Quality;
        public byte Chance_Neither_Bias;
        public double Chance_Neither_Quality_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Neither_Quality - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels, false);
        public double Chance_Neither_Bias_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Neither_Bias - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels, false);

        public double? SettlementValue => SettlementOccurs ? (POfferContinuous + DOfferContinuous) / 2.0 : (double?) null;
        public bool SettlementOccurs => POffer <= DOffer;
        public bool TrialOccurs => !SettlementOccurs;
        public double QualitySum => AdditiveEvidenceGameOptions.Alpha_Both_Quality * AdditiveEvidenceGameOptions.Evidence_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality * Chance_Plaintiff_Quality_Continuous + AdditiveEvidenceGameOptions.Alpha_Defendant_Quality * Chance_Defendant_Quality_Continuous + AdditiveEvidenceGameOptions.Alpha_Neither_Quality * Chance_Neither_Quality_Continuous;
        public double QualitySum_PInfoOnly => (AdditiveEvidenceGameOptions.Alpha_Both_Quality * AdditiveEvidenceGameOptions.Evidence_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality * Chance_Plaintiff_Quality_Continuous) / (AdditiveEvidenceGameOptions.Alpha_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality);
        public double QualitySum_DInfoOnly => (AdditiveEvidenceGameOptions.Alpha_Both_Quality * AdditiveEvidenceGameOptions.Evidence_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Defendant_Quality * Chance_Defendant_Quality_Continuous) / (AdditiveEvidenceGameOptions.Alpha_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Defendant_Quality);
        public double BiasSum => AdditiveEvidenceGameOptions.Alpha_Both_Bias * AdditiveEvidenceGameOptions.Evidence_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias * Chance_Plaintiff_Bias_Continuous + AdditiveEvidenceGameOptions.Alpha_Defendant_Bias * Chance_Defendant_Bias_Continuous + AdditiveEvidenceGameOptions.Alpha_Neither_Bias * Chance_Neither_Bias_Continuous;
        public double BiasSum_PInfoOnly => (AdditiveEvidenceGameOptions.Alpha_Both_Bias * AdditiveEvidenceGameOptions.Evidence_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias * Chance_Plaintiff_Bias_Continuous) / (AdditiveEvidenceGameOptions.Alpha_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias);
        public double BiasSum_DInfoOnly => (AdditiveEvidenceGameOptions.Alpha_Both_Bias * AdditiveEvidenceGameOptions.Evidence_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Defendant_Bias * Chance_Defendant_Bias_Continuous) / (AdditiveEvidenceGameOptions.Alpha_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Defendant_Bias);

        public double TrialValueIfOccurs => AdditiveEvidenceGameOptions.Alpha_Quality * QualitySum + AdditiveEvidenceGameOptions.Alpha_Bias * BiasSum;
        public double AnticipatedTrialValue_PInfo => AdditiveEvidenceGameOptions.Alpha_Quality * QualitySum_PInfoOnly + AdditiveEvidenceGameOptions.Alpha_Bias * BiasSum_PInfoOnly;
        public double AnticipatedTrialValue_DInfo => AdditiveEvidenceGameOptions.Alpha_Quality * QualitySum_DInfoOnly + AdditiveEvidenceGameOptions.Alpha_Bias * BiasSum_DInfoOnly;
        public double? TrialValuePreShifting => TrialOccurs ? TrialValueIfOccurs : (double?)null;
        public double? PTrialEffect => TrialOccurs ? TrialValueIfOccurs - (1.0 - DsProportionOfCost) * AdditiveEvidenceGameOptions.TrialCost : (double?) null;
        public double? DTrialEffect => TrialOccurs ? 0 - TrialValueIfOccurs - (DsProportionOfCost) * AdditiveEvidenceGameOptions.TrialCost : (double?) null;

        public double SettlementOrJudgment => TrialOccurs ? (double) TrialValueIfOccurs : (double) SettlementValue;

        public bool ShiftingOccurs => DsProportionOfCost != 0.5;

        public double DsProportionOfCost => DsProportionOfCost_Helper();
        private double DsProportionOfCost_Helper()
        {
            if (AdditiveEvidenceGameOptions.FeeShifting == false || SettlementOccurs)
                return 0.5;
            if (AdditiveEvidenceGameOptions.FeeShiftingIsBasedOnMarginOfVictory)
            {
                // Note that with a threshold above 0.5, we always get fee shifting. We could multiply by 2 to adjust for that, but then it would be different in other ways from the regular fee shifting. 
                if (TrialValueIfOccurs > 0.5 && TrialValueIfOccurs > 1.0 - AdditiveEvidenceGameOptions.FeeShiftingThreshold)
                    return 1.0;
                else if (TrialValueIfOccurs <= 0.5 && TrialValueIfOccurs < AdditiveEvidenceGameOptions.FeeShiftingThreshold) // note we use less than or equal since we may end up with a TrialValue exactly equal to 0.5, and so we count this as a defendant win
                    return 0;
                return 0.5;
            }
            else
            {
                // the regular fee shifting depends only on information available to the player, and it comes into effect only if the player wins.
                // Note that 0 is the American rule and 1 is the British rule. 
                bool considerShiftingToDefendant = TrialValueIfOccurs > 0.5;
                if (considerShiftingToDefendant)
                    return AnticipatedTrialValue_PInfo > 1.0 - AdditiveEvidenceGameOptions.FeeShiftingThreshold ? 1.0 : 0.5;
                else
                    return AnticipatedTrialValue_DInfo < AdditiveEvidenceGameOptions.FeeShiftingThreshold ? 0.0 : 0.5;
            }

        }


        public double AccuracyIgnoringCosts => Math.Abs(PWelfare - SettlementOrJudgment);
        public double Accuracy_ForPlaintiff => Math.Abs(PWelfare - QualitySum);
        public double Accuracy_ForDefendant => Math.Abs(DWelfare + QualitySum);

        public double PWelfare => SettlementOccurs ? (double)SettlementValue : (double)PTrialEffect;
        public double DWelfare => SettlementOccurs ? (double)-SettlementValue : (double)DTrialEffect;

        public override string ToString()
        {
            return
                $@"Chance_Plaintiff_Quality {Chance_Plaintiff_Quality} Chance_Defendant_Quality {Chance_Defendant_Quality} Chance_Plaintiff_Bias {Chance_Plaintiff_Bias} Chance_Defendant_Bias {Chance_Defendant_Bias} POffer {POffer} DOffer {DOffer} Chance_Neither_Quality {Chance_Neither_Quality} Chance_Neither_Bias {Chance_Neither_Bias}
QualitySum {QualitySum} QualitySum_PInfoOnly {QualitySum_PInfoOnly} QualitySum_DInfoOnly {QualitySum_DInfoOnly} BiasSum {BiasSum} BiasSum_PInfoOnly {BiasSum_PInfoOnly} BiasSum_DInfoOnly {BiasSum_DInfoOnly}
SettlementValue {SettlementValue} SettlementOccurs {SettlementOccurs} TrialOccurs {TrialOccurs} TrialValuePreShifting {TrialValuePreShifting} SettlementOrJudgment {SettlementOrJudgment} DsProportionOfCost {DsProportionOfCost}
ShiftingOccurs {ShiftingOccurs}  PTrialEffect {PTrialEffect} DTrialEffect {DTrialEffect} PWelfare {PWelfare} DWelfare {DWelfare}
AccuracyIgnoringCosts {AccuracyIgnoringCosts} Accuracy_ForPlaintiff {Accuracy_ForPlaintiff} Accuracy_ForDefendant {Accuracy_ForDefendant} PWelfare {PWelfare} DWelfare {DWelfare}";
        }

        public override GameProgress DeepCopy()
        {
            AdditiveEvidenceGameProgress copy = new AdditiveEvidenceGameProgress();

            // copy.GameComplete = this.GameComplete;
            base.CopyFieldInfo(copy);
            copy.Chance_Plaintiff_Quality = Chance_Plaintiff_Quality;
            copy.Chance_Defendant_Quality = Chance_Defendant_Quality;
            copy.Chance_Plaintiff_Bias = Chance_Plaintiff_Bias;
            copy.Chance_Defendant_Bias = Chance_Defendant_Bias;
            copy.POffer = POffer;
            copy.DOffer = DOffer;
            copy.Chance_Neither_Quality = Chance_Neither_Quality;
            copy.Chance_Neither_Bias = Chance_Neither_Bias;

            return copy;
        }

        public override double[] GetNonChancePlayerUtilities()
        {
            return new double[] { PWelfare, DWelfare };
        }

        public override FloatSet GetCustomResult()
        {
            return new FloatSet(
                TrialOccurs ? 1.0f : 0,
                (float) AccuracyIgnoringCosts,
                (float) PWelfare,
                (float) DWelfare
                );
        }

        public void CalculateGameOutcome()
        {
            // nothing to do
        }

        public override void RecalculateGameOutcome()
        {
            // nothing to do
        }
    }
}
