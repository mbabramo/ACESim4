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

        public bool LinearBids;
        public bool IncludePQuitDecision;
        public bool IncludeDQuitDecision;

        // The evidence shared by both parties is a parameter. The evidence that one or neither party has is determined by chance.
        public double Evidence_Both_Quality;
        public double Evidence_Both_Bias;

        /// <summary>
        /// The portion of the judgment that is attributable to evidence on actual quality.
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

        public byte NumQualityAndBiasLevels_PrivateInfo = 25;
        public byte NumQualityAndBiasLevels_NeitherInfo = 5;
        public byte NumOffers = 25;

        public double MinOffer = -0.25; // settlements beyond the range of (-0.25, 1.25) have not been observed
        public double OfferRange = 1.5; 

        public double TrialCost;
        public bool FeeShifting;
        public bool FeeShiftingIsBasedOnMarginOfVictory;

        /// <summary>
        /// When based on margin of victory, judgment must exceed this amount (which thus should be greater than 1/2).
        /// When not based on margin of victory, the judgment that would be entered based only on information accessible to the winner must exceed this amount.
        /// </summary>
        public double FeeShiftingThreshold;

        public override string ToString()
        {
            return $@"Shared trial cost: {TrialCost}  Shared evidence: Quality {Evidence_Both_Quality} Bias {Evidence_Both_Bias}
Alpha_Quality {Alpha_Quality}: Both {Alpha_Both_Quality} P {Alpha_Plaintiff_Quality} D {Alpha_Defendant_Quality} Neither {Alpha_Neither_Quality}
Alpha_Bias {Alpha_Bias}: Both {Alpha_Both_Bias} P {Alpha_Plaintiff_Bias} D {Alpha_Defendant_Bias} Neither {Alpha_Neither_Bias}
FeeShifting {FeeShifting} {(FeeShifting ? $"Margin {FeeShiftingIsBasedOnMarginOfVictory} Threshold {FeeShiftingThreshold}" : "")}
NumOffers {NumOffers} MinOffer {MinOffer} OfferRange {OfferRange} NumQualityAndBiasLevels {NumQualityAndBiasLevels_PrivateInfo}
LinearBids {LinearBids} IncludePQuit {IncludePQuitDecision} IncludeDQuit {IncludeDQuitDecision}";
        }
    }
}
