using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    [Serializable]
    public class AdditiveEvidenceGameProgress : GameProgress, ISignalOfferReportGameProgress
    {

        public AdditiveEvidenceGameProgress(bool fullHistoryRequired) : base(fullHistoryRequired)
        {

        }
        public AdditiveEvidenceGameDefinition AdditiveEvidenceGameDefinition => (AdditiveEvidenceGameDefinition)GameDefinition;
        public AdditiveEvidenceGameOptions AdditiveEvidenceGameOptions => AdditiveEvidenceGameDefinition.Options;


        public byte P_LinearBid_Input => AdditiveEvidenceGameOptions.Alpha_Bias > 0 && AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias > 0 ? Chance_Plaintiff_Bias : Chance_Plaintiff_Quality;
        public byte D_LinearBid_Input => AdditiveEvidenceGameOptions.Alpha_Bias > 0 && AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias > 0 ? Chance_Defendant_Bias : Chance_Defendant_Quality;

        public bool PFiles => true;
        public bool DAnswers => true;

        // For piecewise linear:

        public DMSCalc _PiecewiseLinearCalcs = null;
        public DMSCalc PiecewiseLinearCalcs
        {
            get
            {
                if (PiecewiseLinearActive)
                {
                    return AdditiveEvidenceGameDefinition.PiecewiseLinearCalcs;
                }
                return null;
            }
        }
        public bool PiecewiseLinearActive => AdditiveEvidenceGameOptions.PiecewiseLinearBids;
        public double PSlope, DSlope, PMinValueForRange, DMinValueForRange, PTruncationPortion, DTruncationPortion;

        public double PiecewiseLinearPBidFromSignal(double signal) => PiecewiseLinearCalcs.GetPiecewiseLinearBidTruncated(signal, true, PMinValueForRange, PSlope, PTruncationPortion);
        public double PiecewiseLinearPBid => PiecewiseLinearPBidFromSignal(Chance_Plaintiff_Bias_Continuous);
        public double PiecewiseLinearDBidFromSignal(double signal) => PiecewiseLinearCalcs.GetPiecewiseLinearBidTruncated(signal, false, DMinValueForRange, DSlope, DTruncationPortion);
        public double PiecewiseLinearDBid => PiecewiseLinearDBidFromSignal(Chance_Defendant_Bias_Continuous);

        // Chance information (where each party has individual information, as joint information is set as a game parameter)

        public byte Chance_Plaintiff_Quality;
        public byte Chance_Defendant_Quality;
        public byte Chance_Plaintiff_Bias;
        public byte Chance_Defendant_Bias;
        // with piecewise linear, the plaintiff/defendant receive only partial information about the bias and then produce an entire schedule of responses
        public byte Chance_Plaintiff_Bias_Reduction;
        public byte Chance_Defendant_Bias_Reduction;

        public double Chance_Plaintiff_Quality_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Plaintiff_Quality - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_PrivateInfo, false);
        public double Chance_Defendant_Quality_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Defendant_Quality - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_PrivateInfo, false);
        public double Chance_Plaintiff_Bias_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Plaintiff_Bias - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_PrivateInfo, false);
        public double Chance_Defendant_Bias_Continuous => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Defendant_Bias - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_PrivateInfo, false);

        // Party decisions on offers (whether or not based on linear bid) and quitting)

        public byte POffer { get; set; }

        public byte DOffer { get; set; }

        public bool PQuits;
        public bool DQuits;

        public double PFirstOffer => PiecewiseLinearActive ? PiecewiseLinearPBid : ContinuousOffer(GetDiscreteOffer(true));
        public double DFirstOffer => PiecewiseLinearActive ? PiecewiseLinearDBid : ContinuousOffer(GetDiscreteOffer(false));
        public List<double> POffers => new List<double>() { POffer };

        public List<double> DOffers => new List<double>() { POffer };

        public byte GetDiscreteOffer(bool plaintiff)
        {
            return plaintiff ? POffer : DOffer;
        }

        private double ContinuousOffer(byte offerAction) => AdditiveEvidenceGameOptions.MinOffer + AdditiveEvidenceGameOptions.OfferRange * EquallySpaced.GetLocationOfEquallySpacedPoint(offerAction - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumOffers, false);
        public double POfferContinuousIfMade
        {
            get
            {
                if (!PiecewiseLinearActive)
                    return ContinuousOffer(POffer);
                return PiecewiseLinearPBid;
            }
        }
        public double DOfferContinuousIfMade
        {
            get
            {
                if (!PiecewiseLinearActive)
                    return ContinuousOffer(DOffer);
                return PiecewiseLinearDBid;
            }
        }

        public double? POfferContinuousOrNull => SomeoneQuits ? (double?)null : POfferContinuousIfMade;
        public double? DOfferContinuousOrNull => SomeoneQuits ? (double?)null : DOfferContinuousIfMade;

        public byte Chance_Neither_Quality;
        public byte Chance_Neither_Bias;
        public double Chance_Neither_Quality_Continuous_IfDetermined => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Neither_Quality - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_NeitherInfo, false);
        public double Chance_Neither_Bias_Continuous_IfDetermined => EquallySpaced.GetLocationOfEquallySpacedPoint(Chance_Neither_Bias - 1 /* make it zero-based */, AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_NeitherInfo, false);
        public double? Chance_Neither_Quality_Continuous_OrNull => (TrialOccurs ? Chance_Neither_Quality_Continuous_IfDetermined : (double?)null);
        public double? Chance_Neither_Bias_Continuous_OrNull => (TrialOccurs ? Chance_Neither_Bias_Continuous_IfDetermined : (double?)null);

        public double? SettlementValue => SettlementOccurs ? (POfferContinuousIfMade + DOfferContinuousIfMade) / 2.0 : (double?) null;
        public bool SomeoneQuits => !AdditiveEvidenceGameOptions.TrialGuaranteed && (PQuits || DQuits);
        public bool SettlementOccurs => !SomeoneQuits && !AdditiveEvidenceGameOptions.TrialGuaranteed && POfferContinuousIfMade <= DOfferContinuousIfMade;
        public bool TrialOccurs => !SomeoneQuits && !SettlementOccurs;
        static double dOr0(double n, double d) => d == 0 ? 0 : n / d; // avoid division by zero
        public double QualitySum => AdditiveEvidenceGameOptions.Alpha_Both_Quality * AdditiveEvidenceGameOptions.Evidence_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality * Chance_Plaintiff_Quality_Continuous + AdditiveEvidenceGameOptions.Alpha_Defendant_Quality * Chance_Defendant_Quality_Continuous + AdditiveEvidenceGameOptions.Alpha_Neither_Quality * Chance_Neither_Quality_Continuous_IfDetermined;
        public double QualitySum_PInfoOnly => dOr0((AdditiveEvidenceGameOptions.Alpha_Both_Quality * AdditiveEvidenceGameOptions.Evidence_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality * Chance_Plaintiff_Quality_Continuous), QualitySum_PInfoOnly_Denominator);
        public double QualitySum_PInfoOnly_Denominator => (AdditiveEvidenceGameOptions.Alpha_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality);
        public double QualitySum_DInfoOnly => dOr0((AdditiveEvidenceGameOptions.Alpha_Both_Quality * AdditiveEvidenceGameOptions.Evidence_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Defendant_Quality * Chance_Defendant_Quality_Continuous), QualitySum_DInfoOnly_Denominator);
        public double QualitySum_DInfoOnly_Denominator => (AdditiveEvidenceGameOptions.Alpha_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Defendant_Quality);
        public double BiasSum => AdditiveEvidenceGameOptions.Alpha_Both_Bias * AdditiveEvidenceGameOptions.Evidence_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias * Chance_Plaintiff_Bias_Continuous + AdditiveEvidenceGameOptions.Alpha_Defendant_Bias * Chance_Defendant_Bias_Continuous + AdditiveEvidenceGameOptions.Alpha_Neither_Bias * Chance_Neither_Bias_Continuous_IfDetermined;
        public double BiasSum_PInfoOnly => dOr0((AdditiveEvidenceGameOptions.Alpha_Both_Bias * AdditiveEvidenceGameOptions.Evidence_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias * Chance_Plaintiff_Bias_Continuous), BiasSum_PInfoOnly_Denominator);
        public double BiasSum_PInfoOnly_Denominator => (AdditiveEvidenceGameOptions.Alpha_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias);
        public double BiasSum_DInfoOnly => dOr0((AdditiveEvidenceGameOptions.Alpha_Both_Bias * AdditiveEvidenceGameOptions.Evidence_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Defendant_Bias * Chance_Defendant_Bias_Continuous), BiasSum_DInfoOnly_Denominator);
        public double BiasSum_DInfoOnly_Denominator => (AdditiveEvidenceGameOptions.Alpha_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Defendant_Bias);

        public double PBestGuess => AdditiveEvidenceGameOptions.Alpha_Quality * (AdditiveEvidenceGameOptions.Alpha_Both_Quality * AdditiveEvidenceGameOptions.Evidence_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality * Chance_Plaintiff_Quality_Continuous + AdditiveEvidenceGameOptions.Alpha_Defendant_Quality * 0.5 /* best guess of defendant's evidence */ + AdditiveEvidenceGameOptions.Alpha_Neither_Quality * 0.5) + AdditiveEvidenceGameOptions.Alpha_Bias * (AdditiveEvidenceGameOptions.Alpha_Both_Bias * AdditiveEvidenceGameOptions.Evidence_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias * Chance_Plaintiff_Bias_Continuous + AdditiveEvidenceGameOptions.Alpha_Defendant_Bias * 0.5 + AdditiveEvidenceGameOptions.Alpha_Neither_Bias * 0.5);

        public double DBestGuess => AdditiveEvidenceGameOptions.Alpha_Quality * (AdditiveEvidenceGameOptions.Alpha_Both_Quality * AdditiveEvidenceGameOptions.Evidence_Both_Quality + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality * 0.5 + AdditiveEvidenceGameOptions.Alpha_Defendant_Quality * Chance_Defendant_Quality_Continuous + AdditiveEvidenceGameOptions.Alpha_Neither_Quality * 0.5) + AdditiveEvidenceGameOptions.Alpha_Bias * (AdditiveEvidenceGameOptions.Alpha_Both_Bias * AdditiveEvidenceGameOptions.Evidence_Both_Bias + AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias * 0.5 + AdditiveEvidenceGameOptions.Alpha_Defendant_Bias * Chance_Defendant_Bias_Continuous + AdditiveEvidenceGameOptions.Alpha_Neither_Bias * 0.5);

        public double TrialValuePreShiftingIfOccurs => AdditiveEvidenceGameOptions.Alpha_Quality * QualitySum + AdditiveEvidenceGameOptions.Alpha_Bias * BiasSum;
        public double AnticipatedTrialValue_PInfo
        {
            get
            {
                if (BiasSum_PInfoOnly_Denominator == 0) 
                    return QualitySum_PInfoOnly;
                if (QualitySum_PInfoOnly_Denominator == 0)
                    return BiasSum_PInfoOnly;
                return AdditiveEvidenceGameOptions.Alpha_Quality* QualitySum_PInfoOnly + AdditiveEvidenceGameOptions.Alpha_Bias * BiasSum_PInfoOnly;
            }
        }

        public double AnticipatedTrialValue_DInfo
        {
            get
            {
                if (BiasSum_DInfoOnly_Denominator == 0)
                    return QualitySum_DInfoOnly;
                if (QualitySum_DInfoOnly_Denominator == 0)
                    return BiasSum_DInfoOnly;
                return AdditiveEvidenceGameOptions.Alpha_Quality* QualitySum_DInfoOnly + AdditiveEvidenceGameOptions.Alpha_Bias * BiasSum_DInfoOnly;
            }
        }

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
                double? trialValuePreShifting = TrialValuePreShifting;
                if (0.5 - 1E-12 < trialValuePreShifting && trialValuePreShifting < 0.5 + 1E-12)
                    return 0.5;
                bool considerShiftingToDefendant = trialValuePreShifting > 0.5; // if p has more than 0.5, d may have to pay
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
            AdditiveEvidenceGameProgress copy = new AdditiveEvidenceGameProgress(FullHistoryRequired);

            // copy.GameComplete = this.GameComplete;
            base.CopyFieldInfo(copy);
            copy.Chance_Plaintiff_Quality = Chance_Plaintiff_Quality;
            copy.Chance_Defendant_Quality = Chance_Defendant_Quality;
            copy.Chance_Plaintiff_Bias = Chance_Plaintiff_Bias;
            copy.Chance_Defendant_Bias = Chance_Defendant_Bias;
            copy.Chance_Plaintiff_Bias_Reduction = Chance_Plaintiff_Bias_Reduction;
            copy.Chance_Defendant_Bias_Reduction = Chance_Defendant_Bias_Reduction;
            copy.PQuits = PQuits;
            copy.DQuits = DQuits;
            copy.POffer = POffer;
            copy.DOffer = DOffer;
            copy.Chance_Neither_Quality = Chance_Neither_Quality;
            copy.Chance_Neither_Bias = Chance_Neither_Bias;
            copy.PSlope = PSlope;
            copy.DSlope = DSlope;
            copy.PMinValueForRange = PMinValueForRange;
            copy.DMinValueForRange = DMinValueForRange;
            copy.PTruncationPortion = PTruncationPortion;
            copy.DTruncationPortion = DTruncationPortion;

            return copy;
        }

        public override bool SplitExPostForReporting => Chance_Neither_Bias == 0; // haven't determined it, but we still want to report on it

        public override List<GameProgress> CompleteSplitExPostForReporting()
        {
            List<GameProgress> results = new List<GameProgress>();
            for (byte i = 1; i <= AdditiveEvidenceGameOptions.NumQualityAndBiasLevels_NeitherInfo; i++)
            {
                var copy = DeepCopy();
                ((AdditiveEvidenceGameProgress)copy).Chance_Neither_Bias = i;
                results.Add(copy);
            }
            return results;
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
