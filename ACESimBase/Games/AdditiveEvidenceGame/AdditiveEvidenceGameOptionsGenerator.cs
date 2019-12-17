using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public class AdditiveEvidenceGameOptionsGenerator
    {
        public enum AdditiveEvidenceOptionSetChoices
        {
            DMS,
            EvenStrength,
            Biasless_AsymmetryBasedOnQuality,
            Biasless_EvenStrength
        }

        static AdditiveEvidenceOptionSetChoices AdditiveEvidenceChoice => AdditiveEvidenceOptionSetChoices.DMS;

        public static AdditiveEvidenceGameOptions GetAdditiveEvidenceGameOptions() => AdditiveEvidenceChoice switch
        {
            AdditiveEvidenceOptionSetChoices.DMS => DariMattiacci_Saraceno(0.60, 0.15, false, false, 0.5),
            AdditiveEvidenceOptionSetChoices.Biasless_AsymmetryBasedOnQuality => Biasless(0.6, 0.6, 0.15, false, false, 0.5),
            AdditiveEvidenceOptionSetChoices.Biasless_EvenStrength => Biasless(0.6, 0.5, 0.15, false, false, 0.5),
            _ => throw new Exception()
        };

        public static byte numOffers = 25; // having a good number here allows for more precise strategies
        public static byte numQualityAndBiasLevels = 25; // this is what will be across on each minigraph, so it's good to have a relatively high number

        public static AdditiveEvidenceGameOptions DariMattiacci_Saraceno(double quality, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold)
        {
            var options = new AdditiveEvidenceGameOptions()
            {
                Alpha_Quality = 0.5,
                Alpha_Both_Quality = 1.0,
                Alpha_Plaintiff_Quality = 0,
                Alpha_Defendant_Quality = 0,
                // so Neither_Quality is set automatically to 0
                Alpha_Both_Bias = 0.0,
            };
            options.Alpha_Plaintiff_Bias = options.Evidence_Both_Quality = quality; // this is the key (strange) assumption -- the proportion of the evidence about bias that is in the plaintiff's possession is equal to the strength of the case
            options.Alpha_Defendant_Bias = 1.0 - options.Alpha_Plaintiff_Bias;
            // nothing to neither or both with respect to bias

            options.TrialCost = costs;

            options.NumOffers = numOffers;
            options.NumQualityAndBiasLevels = numQualityAndBiasLevels;
            return options;
        }

        public static AdditiveEvidenceGameOptions EvenStrength(double quality, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold)
        {
            var options = new AdditiveEvidenceGameOptions()
            {
                Evidence_Both_Quality = quality,
                Alpha_Quality = 0.5,
                Alpha_Both_Quality = 1.0,
                Alpha_Plaintiff_Quality = 0,
                Alpha_Defendant_Quality = 0,
                // so Neither_Quality is set automatically to 0
                Alpha_Both_Bias = 0.0,
            };
            options.Alpha_Plaintiff_Bias = 0.5; // even strength
            options.Alpha_Defendant_Bias = 1.0 - options.Alpha_Plaintiff_Bias;
            // nothing to neither or both with respect to bias

            options.TrialCost = costs;

            options.NumOffers = numOffers;
            options.NumQualityAndBiasLevels = numQualityAndBiasLevels;
            return options;
        }

        public static AdditiveEvidenceGameOptions Biasless(double quality, double pPortionOfPrivateInfo /* set to quality to be like the original DMS model in this respect */, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold)
        {
            var options = new AdditiveEvidenceGameOptions()
            {
                Evidence_Both_Quality = quality,
                Alpha_Quality = 1.0, // there is no separate bias
                Alpha_Both_Quality = 0.5, // So, the actual quality is now still worth 1/2 in effect. 
                Alpha_Plaintiff_Quality = 0.5 * pPortionOfPrivateInfo, // we are apportioning the remaining 1/2 of quality between the plaintiff and defendant based on the parameter -- this can be set to quality to simulate the DMS model (so that we can see what the effect is of everything being quality rather than having a split between quailty and bias)
                Alpha_Defendant_Quality = 0.5 * (1.0 - pPortionOfPrivateInfo),
                // so Neither_Quality is set automatically to 0
                // bias is irrelevant
                Alpha_Both_Bias = 0.0,
                Alpha_Plaintiff_Bias = 0.0,
                Alpha_Defendant_Bias = 0.0,
            };
            // nothing to neither or both with respect to bias

            options.TrialCost = costs;

            options.NumOffers = numOffers;
            options.NumQualityAndBiasLevels = numQualityAndBiasLevels;
            return options;
        }
    }
}
