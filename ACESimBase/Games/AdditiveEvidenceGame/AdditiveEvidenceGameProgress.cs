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

        // Linear bids
        // Each player announces a minimum and a max, so the player's offer equals the minimum + (Max - min) * (party's information - 1).
        // Note that we're assuming here that the party has only one type of information.

        public byte P_LinearBid_Min;
        public byte P_LinearBid_Max;
        public byte D_LinearBid_Min;
        public byte D_LinearBid_Max;
        private double LinearBidProportion(byte action) => EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumOffers, false);
        
        public byte P_LinearBid_Input => AdditiveEvidenceGameOptions.Alpha_Bias > 0 && AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias > 0 ? Chance_Plaintiff_Bias : Chance_Plaintiff_Quality;
        public byte D_LinearBid_Input => AdditiveEvidenceGameOptions.Alpha_Bias > 0 && AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias > 0 ? Chance_Defendant_Bias : Chance_Defendant_Quality;
        public double P_LinearBid_Continuous => (1.0 - LinearBidProportion(P_LinearBid_Input)) * ContinuousOffer(P_LinearBid_Min) + LinearBidProportion(P_LinearBid_Input) * ContinuousOffer(P_LinearBid_Max);
        public double D_LinearBid_Continuous => (1.0 - LinearBidProportion(D_LinearBid_Input)) * ContinuousOffer(D_LinearBid_Min) + LinearBidProportion(D_LinearBid_Input) * ContinuousOffer(D_LinearBid_Max);


        public byte Chance_Plaintiff_Quality;
        public byte Chance_Defendant_Quality;
        public byte Chance_Plaintiff_Bias;
        public byte Chance_Defendant_Bias;

        public bool PQuits;
        public bool DQuits;

        public double Chance_Plaintiff_Quality_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Plaintiff_Quality - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_PrivateInfo, false);
        public double Chance_Defendant_Quality_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Defendant_Quality - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_PrivateInfo, false);
        public double Chance_Plaintiff_Bias_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Plaintiff_Bias - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_PrivateInfo, false);
        public double Chance_Defendant_Bias_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Defendant_Bias - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_PrivateInfo, false);

        public byte POffer;
        public byte DOffer;

        public byte GetDiscreteOffer(bool plaintiff)
        {
            if (!AdditiveEvidenceGameOptions.LinearBids)
                return plaintiff ? POffer : DOffer;
            // When using linear bids, we want to find the discrete offer value nearest the continuous value -- this will allow for comparability between our linear bids and discrete models
            double actualOffer = plaintiff ? P_LinearBid_Continuous : D_LinearBid_Continuous;
            byte closest = Enumerable.Range(1, AdditiveEvidenceGameOptions.NumOffers).Select(a => (byte)a).Select(a => (a, Math.Abs(actualOffer - ContinuousOffer(a)))).OrderBy(x => x.Item2).First().Item1;
            return closest;
        }

        private double ContinuousOffer(byte offerAction) => AdditiveEvidenceGameOptions.MinOffer + AdditiveEvidenceGameOptions.OfferRange * EquallySpaced.GetLocationOfEquallySpacedPoint(offerAction - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumOffers, false);
        public double POfferContinuousIfMade => AdditiveEvidenceGameOptions.LinearBids ? P_LinearBid_Continuous : ContinuousOffer(POffer);
        public double DOfferContinuousIfMade => AdditiveEvidenceGameOptions.LinearBids ? D_LinearBid_Continuous : ContinuousOffer(DOffer);

        public double? POfferContinuousOrNull => SomeoneQuits ? (double?)null : POfferContinuousIfMade;
        public double? DOfferContinuousOrNull => SomeoneQuits ? (double?)null : DOfferContinuousIfMade;

        public byte Chance_Neither_Quality;
        public byte Chance_Neither_Bias;
        public double Chance_Neither_Quality_Continuous_IfDetermined => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Neither_Quality - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_PrivateInfo, false);
        public double Chance_Neither_Bias_Continuous_IfDetermined => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Neither_Bias - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_PrivateInfo, false);
        public double? Chance_Neither_Quality_Continuous_OrNull => (TrialOccurs ? Chance_Neither_Quality_Continuous_IfDetermined : (double?)null);
        public double? Chance_Neither_Bias_Continuous_OrNull => (TrialOccurs ? Chance_Neither_Bias_Continuous_IfDetermined : (double?)null);

        public double? SettlementValue => SettlementOccurs ? (POfferContinuousIfMade + DOfferContinuousIfMade) / 2.0 : (double?) null;
        public bool SomeoneQuits => PQuits || DQuits;
        public bool SettlementOccurs => !SomeoneQuits && POfferContinuousIfMade <= DOfferContinuousIfMade;
        public bool TrialOccurs => !SomeoneQuits && !SettlementOccurs;
        static double dOr0(double n, double d) => d == 0 ? 0 : n / d; // avoid division by zero
        public double QualitySum => AdditiveEvidenceGameOptions.Alpha_Both_Quality * AdditiveEvidenceGameOptions.Evidence_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality * Chance_Plaintiff_Quality_Continuous + AdditiveEvidenceGameOptions.Alpha_Defendant_Quality * Chance_Defendant_Quality_Continuous + AdditiveEvidenceGameOptions.Alpha_Neither_Quality * Chance_Neither_Quality_Continuous_IfDetermined;
        public double QualitySum_PInfoOnly => dOr0((AdditiveEvidenceGameOptions.Alpha_Both_Quality * AdditiveEvidenceGameOptions.Evidence_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality * Chance_Plaintiff_Quality_Continuous), (AdditiveEvidenceGameOptions.Alpha_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality));
        public double QualitySum_DInfoOnly => dOr0((AdditiveEvidenceGameOptions.Alpha_Both_Quality * AdditiveEvidenceGameOptions.Evidence_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Defendant_Quality * Chance_Defendant_Quality_Continuous), (AdditiveEvidenceGameOptions.Alpha_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Defendant_Quality));
        public double BiasSum => AdditiveEvidenceGameOptions.Alpha_Both_Bias * AdditiveEvidenceGameOptions.Evidence_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias * Chance_Plaintiff_Bias_Continuous + AdditiveEvidenceGameOptions.Alpha_Defendant_Bias * Chance_Defendant_Bias_Continuous + AdditiveEvidenceGameOptions.Alpha_Neither_Bias * Chance_Neither_Bias_Continuous_IfDetermined;
        public double BiasSum_PInfoOnly => dOr0((AdditiveEvidenceGameOptions.Alpha_Both_Bias * AdditiveEvidenceGameOptions.Evidence_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias * Chance_Plaintiff_Bias_Continuous), (AdditiveEvidenceGameOptions.Alpha_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias));
        public double BiasSum_DInfoOnly => dOr0((AdditiveEvidenceGameOptions.Alpha_Both_Bias * AdditiveEvidenceGameOptions.Evidence_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Defendant_Bias * Chance_Defendant_Bias_Continuous), (AdditiveEvidenceGameOptions.Alpha_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Defendant_Bias));

        public double TrialValuePreShiftingIfOccurs => AdditiveEvidenceGameOptions.Alpha_Quality * QualitySum + AdditiveEvidenceGameOptions.Alpha_Bias * BiasSum;
        public double AnticipatedTrialValue_PInfo => AdditiveEvidenceGameOptions.Alpha_Quality * QualitySum_PInfoOnly + AdditiveEvidenceGameOptions.Alpha_Bias * BiasSum_PInfoOnly;
        public double AnticipatedTrialValue_DInfo => AdditiveEvidenceGameOptions.Alpha_Quality * QualitySum_DInfoOnly + AdditiveEvidenceGameOptions.Alpha_Bias * BiasSum_DInfoOnly;
        public double? TrialValuePreShifting => TrialOccurs ? TrialValuePreShiftingIfOccurs : (double?)null;
        public double PTrialEffect_IfOccurs => TrialValuePreShiftingIfOccurs - (1.0 - DsProportionOfCostIfTrial()) * AdditiveEvidenceGameOptions.TrialCost;
        public double DTrialEffect_IfOccurs => 1.0 - TrialValuePreShiftingIfOccurs - (DsProportionOfCostIfTrial()) * AdditiveEvidenceGameOptions.TrialCost; // remember, this is a damages game, so defendant receives (1 - what is awarded to plaintiff)
        public double? PTrialEffect => TrialOccurs ? PTrialEffect_IfOccurs : (double?) null;
        public double? DTrialEffect => TrialOccurs ? DTrialEffect_IfOccurs : (double?) null;

        public double? PResultFromQuitting => PQuits ? 0 : (DQuits ? 1.0 : (double?) null);
        public double? DResultFromQuitting => DQuits ? 0 : (PQuits ? 1.0 : (double?)null);

        public double ResolutionValue => PResultFromQuitting ?? (TrialOccurs ? (double) TrialValuePreShiftingIfOccurs : (double) SettlementValue);

        public bool ShiftingOccurs => TrialOccurs && DsProportionOfCost != 0.5;
        public bool ShiftingOccursIfTrial => DsProportionOfCostIfTrial() != 0.5;
        public double ShiftingValueIfTrial => (DsProportionOfCostIfTrial() - 0.5) * AdditiveEvidenceGameOptions.TrialCost; // e.g., with full shifting of the burden to defendant, defendant pays half of the trial cost (i.e., an amount equal to plaintiff's fees) to the plaintiff; if full shifting of the burden to the plaintiff, then we end up with a negative amount
        public double TrialValueWithShiftingIfOccurs => TrialValuePreShiftingIfOccurs + ShiftingValueIfTrial; // e.g., suppose P wins 0.70, plus has own costs of 0.4 shifted. Then we count this as P receiving 1.1. In this case, D receives -0.1, since D gets 0.30 and pays 0.4. These figures omit each party's payment of own fees. 

        public double ResolutionValueIncludingShiftedAmount => PResultFromQuitting ?? (TrialOccurs ? (double)TrialValueWithShiftingIfOccurs : (double)SettlementValue);


        public double DsProportionOfCost => DsProportionOfCost_Helper();
        private double DsProportionOfCost_Helper()
        {
            if (SettlementOccurs || SomeoneQuits)
                return 0.5;
            return DsProportionOfCostIfTrial();

        }

        public double DsProportionOfCostIfTrial()
        {
            if (AdditiveEvidenceGameOptions.FeeShifting == false)
                return 0.5;
            if (AdditiveEvidenceGameOptions.FeeShiftingIsBasedOnMarginOfVictory)
            {
                // Note that with a threshold above 0.5, we always get fee shifting. We could multiply by 2 to adjust for that, but then it would be different in other ways from the regular fee shifting. 
                if (TrialValuePreShiftingIfOccurs > 0.5 && TrialValuePreShiftingIfOccurs > 1.0 - AdditiveEvidenceGameOptions.FeeShiftingThreshold)
                    return 1.0;
                else if (TrialValuePreShiftingIfOccurs <= 0.5 && TrialValuePreShiftingIfOccurs < AdditiveEvidenceGameOptions.FeeShiftingThreshold) // note we use less than or equal since we may end up with a TrialValue exactly equal to 0.5, and so we count this as a defendant win
                    return 0;
                return 0.5;
            }
            else
            {
                // the regular fee shifting depends only on information available to the player, and it comes into effect only if the player wins.
                // Note that 0 is the American rule and 1 is the British rule. 
                bool considerShiftingToDefendant = TrialValuePreShiftingIfOccurs > 0.5; // if p has more than 0.5, d may have to pay
                if (considerShiftingToDefendant)
                    return AnticipatedTrialValue_PInfo > 1.0 - AdditiveEvidenceGameOptions.FeeShiftingThreshold ? 1.0 : 0.5;
                else
                    return AnticipatedTrialValue_DInfo < AdditiveEvidenceGameOptions.FeeShiftingThreshold ? 0.0 : 0.5;
            }
        }

        public double Accuracy => Math.Abs(QualitySum - ResolutionValueIncludingShiftedAmount); // this EXCLUDES self-borne costs
        public double AccuracySquared => Accuracy * Accuracy;
        public double Accuracy_ForPlaintiff => Math.Abs(PWelfare - QualitySum);
        public double Accuracy_ForDefendant => Math.Abs(DWelfare - (1.0 - QualitySum));

        public double PWelfare => PResultFromQuitting ?? (SettlementOccurs ? (double)SettlementValue : PTrialEffect_IfOccurs);
        public double DWelfare => DResultFromQuitting ?? (SettlementOccurs ? (double)(1.0 - SettlementValue) : DTrialEffect_IfOccurs);

        public override string ToString()
        {
            string offers;
            if (AdditiveEvidenceGameOptions.LinearBids)
                offers = $"P_LinearBid_Min {P_LinearBid_Min} P_LinearBid_Max {P_LinearBid_Max} => {P_LinearBid_Continuous}; D_LinearBid_Min {D_LinearBid_Min} D_LinearBid_Max {D_LinearBid_Max} => {D_LinearBid_Continuous}";
            else
                offers = $"POffer {POffer} DOffer {DOffer}";
            return
                $@"Chance_Plaintiff_Quality {Chance_Plaintiff_Quality} Chance_Defendant_Quality {Chance_Defendant_Quality} Chance_Plaintiff_Bias {Chance_Plaintiff_Bias} Chance_Defendant_Bias {Chance_Defendant_Bias}  Chance_Neither_Quality {Chance_Neither_Quality} Chance_Neither_Bias {Chance_Neither_Bias}
{offers}
QualitySum {QualitySum} QualitySum_PInfoOnly {QualitySum_PInfoOnly} QualitySum_DInfoOnly {QualitySum_DInfoOnly} BiasSum {BiasSum} BiasSum_PInfoOnly {BiasSum_PInfoOnly} BiasSum_DInfoOnly {BiasSum_DInfoOnly}
PQuits {PQuits} DQuits {DQuits}
SettlementValue {SettlementValue} SettlementOccurs {SettlementOccurs} TrialOccurs {TrialOccurs} TrialValuePreShifting {TrialValuePreShifting} SettlementOrJudgment {ResolutionValue} DsProportionOfCost {DsProportionOfCost}
ShiftingOccurs {ShiftingOccurs}  PTrialEffect {PTrialEffect} DTrialEffect {DTrialEffect} PWelfare {PWelfare} DWelfare {DWelfare}
AccuracyIgnoringCosts {Accuracy} Accuracy_ForPlaintiff {Accuracy_ForPlaintiff} Accuracy_ForDefendant {Accuracy_ForDefendant} PWelfare {PWelfare} DWelfare {DWelfare}";
        }

        public override GameProgress DeepCopy()
        {
            AdditiveEvidenceGameProgress copy = new AdditiveEvidenceGameProgress();

            // copy.GameComplete = this.GameComplete;
            base.CopyFieldInfo(copy);
            copy.P_LinearBid_Min = P_LinearBid_Min;
            copy.P_LinearBid_Max = P_LinearBid_Max;
            copy.D_LinearBid_Min = D_LinearBid_Min;
            copy.D_LinearBid_Max = D_LinearBid_Max;
            copy.Chance_Plaintiff_Quality = Chance_Plaintiff_Quality;
            copy.Chance_Defendant_Quality = Chance_Defendant_Quality;
            copy.Chance_Plaintiff_Bias = Chance_Plaintiff_Bias;
            copy.Chance_Defendant_Bias = Chance_Defendant_Bias;
            copy.PQuits = PQuits;
            copy.DQuits = DQuits;
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
                (float) AccuracySquared,
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
