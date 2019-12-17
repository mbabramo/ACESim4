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
            Biasless_AsymmetryBasedOnQuality,
            Biasless_EqualPrivateInfo
        }

        static AdditiveEvidenceOptionSetChoices AdditiveEvidenceChoice => AdditiveEvidenceOptionSetChoices.Biasless_EqualPrivateInfo;

        public static AdditiveEvidenceGameOptions GetAdditiveEvidenceGameOptions() => AdditiveEvidenceChoice switch
        {
            AdditiveEvidenceOptionSetChoices.DMS => DariMattiacci_Saraceno(0.60, 0.15, false, false, 0.5),
            AdditiveEvidenceOptionSetChoices.Biasless_AsymmetryBasedOnQuality => Biasless(0.6, 0.6, 0.15, false, false, 0.5),
            AdditiveEvidenceOptionSetChoices.Biasless_EqualPrivateInfo => Biasless(0.5, 0.5, 0.15, false, false, 0.5),
            _ => throw new Exception()
        };

        public static AdditiveEvidenceGameOptions DariMattiacci_Saraceno(double quality, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold, bool overrideQualityLimits = false)
        {
            if (!overrideQualityLimits && (quality < 1.0 / 3.0 || quality > 2.0 / 3.0))
                throw new Exception("Quality out of range");
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

            options.NumOffers = 25;
            options.NumQualityAndBiasLevels = 10;
            return options;
        }

        public static AdditiveEvidenceGameOptions Biasless(double quality, double pPortionOfPrivateInfo /* set to quality to be like the original DMS model in this respect */, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold, bool overrideQualityLimits = false)
        {
            if (!overrideQualityLimits && (quality < 1.0 / 3.0 || quality > 2.0 / 3.0))
                throw new Exception("Quality out of range");
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

            options.NumOffers = 25;
            options.NumQualityAndBiasLevels = 10;
            return options;
        }
    }
}
