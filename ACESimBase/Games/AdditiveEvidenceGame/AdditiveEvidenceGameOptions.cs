using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    [Serializable]
    public class AdditiveEvidenceGameOptions : GameOptions
    {
        public bool FirstRowOnly => false; // simplifies the reporting

        // Parameters regarding game structure

        // Whether we are constraining the players to linear bids, in which case they must select a slope and then points in various regions.
        public bool LinearBids;

        public bool TrialGuaranteed;
        public bool IncludePQuitDecision;
        public bool IncludeDQuitDecision;

        public double TrialCost;
        public bool FeeShifting;
        public bool FeeShiftingIsBasedOnMarginOfVictory;

        /// <summary>
        /// When based on margin of victory, judgment must exceed this amount (which thus should be greater than 1/2).
        /// When not based on margin of victory, the judgment that would be entered based only on information accessible to the winner must exceed this amount.
        /// </summary>
        public double FeeShiftingThreshold;

        // Parameters affecting relative importance of parties' information and relative importance of true merits and noise.

        // True merits q' is weighted sum of evidence known by both parties, know by one or the other party, or by neither party.
        // The noise Zn is weighted sum of non-evidentiary information known by both parties, know by one or the other party, or by neither party.
        // The judgment is q' * Alpha_Quality + Zn * Alpha_Bias.
        // Note: With linear bids, Alpha_Quality must be 0 or 1, because there is only 1 input to each party's decision. 

        /// <summary>
        /// The portion of the judgment that is attributable to evidence on actual quality (whether that evidence is held by one party, both or neither). 
        /// In the Dari-Mattiacci/Saraceno paper, Alpha_Quality should be 0.5, because the end result is effectively determined half by quality (known by both parties) and half by bias. 
        /// Evidence that is not related to quality is considered to be bias.
        /// </summary>
        public double Alpha_Quality;
        public double Alpha_Bias => 1.0 - Alpha_Quality;
        public double Alpha_Both_Quality;
        public double Alpha_Plaintiff_Quality;
        public double Alpha_Defendant_Quality;
        public double Alpha_Neither_Quality => Alpha_Neither_Quality_Helper();
        private double Alpha_Neither_Quality_Helper()
        {
            double calc = (1.0 - Alpha_Both_Quality - Alpha_Plaintiff_Quality - Alpha_Defendant_Quality);
            if (calc < 0)
                throw new Exception("Alphas don't add up");
            // If calc is positive, this remaining amount is the evidence that neither party has (but will still affect the judgment).
            return calc;
        }
        public double Alpha_Both_Bias;
        public double Alpha_Plaintiff_Bias;
        public double Alpha_Defendant_Bias;
        public double Alpha_Neither_Bias => Alpha_Neither_Bias_Helper();

        private double Alpha_Neither_Bias_Helper()
        {
            double calc = (1.0 - Alpha_Both_Bias - Alpha_Plaintiff_Bias - Alpha_Defendant_Bias);
            if (calc < 0)
                throw new Exception("Alphas don't add up");
            return calc;
        }

        // Parameters regarding shared information

        // The value of the evidence and/or non-evidentiary information shared by both parties is a parameter to the model. That is, we optimize given some assumptions about what these values are.
        // The evidence that one or neither party has, by contrast, is determined by chance.
        public double Evidence_Both_Quality;
        public double Evidence_Both_Bias;

        // Parameters regarding number of discrete levels and how they map onto continuous levels

        // NOTE: These are set in AdditiveEvidenceGameOptionsGenerator
        public byte NumQualityAndBiasLevels_PrivateInfo = 25;
        public byte NumQualityAndBiasLevels_NeitherInfo = 5;
        public byte NumOffers = 25;

        public double MinOffer = 0; // -0.25; // settlements beyond the range of (-0.25, 1.25) have not been observed, but we are allowing offers below 0 and above 1. This is important if fee-shifting is possible.
        public double OfferRange = 1.0; // 1.5; 

        public override string ToString()
        {
            return $@"Shared trial cost: {TrialCost}  Shared evidence: Quality {Evidence_Both_Quality} Bias {Evidence_Both_Bias}
Alpha_Quality {Alpha_Quality}: Both {Alpha_Both_Quality} P {Alpha_Plaintiff_Quality} D {Alpha_Defendant_Quality} Neither {Alpha_Neither_Quality}
Alpha_Bias {Alpha_Bias}: Both {Alpha_Both_Bias} P {Alpha_Plaintiff_Bias} D {Alpha_Defendant_Bias} Neither {Alpha_Neither_Bias}
FeeShifting {FeeShifting} {(FeeShifting ? $"Margin {FeeShiftingIsBasedOnMarginOfVictory} Threshold {FeeShiftingThreshold}" : "")}
NumOffers {NumOffers} MinOffer {MinOffer} OfferRange {OfferRange} 
NumQualityAndBiasLevels {NumQualityAndBiasLevels_PrivateInfo} (private) {NumQualityAndBiasLevels_NeitherInfo} (neither info)
LinearBids {LinearBids} TrialGuaranteed {TrialGuaranteed} IncludePQuit {IncludePQuitDecision} IncludeDQuit {IncludeDQuitDecision}";
        }
    }
}
