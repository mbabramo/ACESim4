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
            DMS_WithFeeShifting,
            DMS_WithOptionNotToPlay,
            EvenStrength,
            Biasless_AsymmetryBasedOnQuality,
            Biasless_EvenStrength,
            Biasless_PHasInfo
        }

        static AdditiveEvidenceOptionSetChoices AdditiveEvidenceChoice => AdditiveEvidenceOptionSetChoices.DMS;

        public static AdditiveEvidenceGameOptions GetAdditiveEvidenceGameOptions() => AdditiveEvidenceChoice switch
        {
            AdditiveEvidenceOptionSetChoices.DMS => DariMattiacci_Saraceno(0.40, 0.18, false, false, 0.5, false),
            AdditiveEvidenceOptionSetChoices.DMS_WithFeeShifting => DariMattiacci_Saraceno(0.40, 0.15, true, false, 0.5, false),
            AdditiveEvidenceOptionSetChoices.DMS_WithOptionNotToPlay => DariMattiacci_Saraceno(0.90, 0.6, true, false, 0.7, true),
            //AdditiveEvidenceOptionSetChoices.DMS_WithOptionNotToPlay => DariMattiacci_Saraceno(0.05, 1.0, true, false, 0.5, false), // removing option not to play and including fee shifting  -- do we get negative settlements
            //AdditiveEvidenceOptionSetChoices.DMS_WithOptionNotToPlay => DariMattiacci_Saraceno(0.90, 40, true, false, 1.0, true), // very expensive with fee shifting to give incentive not to play, and at this level cases settle --> each party can't be sure whether the other one thinks it has a good hand, and so both parties are eager to settle to avoid the catastrophe of trial. 
            //AdditiveEvidenceOptionSetChoices.DMS_WithOptionNotToPlay => DariMattiacci_Saraceno(0.99, 37, true, false, 1.0, true), // very expensive with fee shifting to give incentive not to play, and at this level D plays a mixed strategy of dropping out about half the time (but since D has virtually no information, it doesn't correlate with D's info)
            AdditiveEvidenceOptionSetChoices.Biasless_AsymmetryBasedOnQuality => Biasless(0.6, 0.6, 0.15, false, false, 0.5, false),
            AdditiveEvidenceOptionSetChoices.Biasless_EvenStrength => Biasless(0.30, 0.5, 0.15, false, false, 1.0, false), // each party's information determines half of judgment, since no common quality info
            //AdditiveEvidenceOptionSetChoices.Biasless_EvenStrength => Biasless(0.05, 0.5, 1.0, true, false, 1.0, false), // very bad for plaintiff, and both parties know it -- settlements are slightly negative
            //AdditiveEvidenceOptionSetChoices.Biasless_EvenStrength => Biasless(0.6, 0.5, 0.15, false, false, 0.5, false),
            AdditiveEvidenceOptionSetChoices.Biasless_PHasInfo => Biasless(0.5, 1.0, 0.15, false, false, 0.5, false), // in this case, note that p's exact offer may be irrelevant, because D will always play same thing, so P will offer just lower than D's to settle or anywhere above D's to go to trial
            _ => throw new Exception()
        };

        // SUPERDEBUG
        public static byte NumOffers = 15; // having a good number here allows for more precise strategies
        public static byte NumQualityAndBiasLevels = 5; // this also contributes to precision (note, though, that this doesn't affect the number of levels of "both quality"

        public static AdditiveEvidenceGameOptions DariMattiacci_Saraceno(double quality, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold, bool withOptionNotToPlay)
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

            options.NumOffers = NumOffers;
            options.NumQualityAndBiasLevels = NumQualityAndBiasLevels;
            if (withOptionNotToPlay)
            {
                options.IncludePQuitDecision = true;
                options.IncludeDQuitDecision = true;
            }

            options.FeeShifting = feeShifting;
            options.FeeShiftingIsBasedOnMarginOfVictory = feeShiftingMarginOfVictory;
            options.FeeShiftingThreshold = feeShiftingThreshold;
            return options;
        }

        public static AdditiveEvidenceGameOptions EvenStrength(double quality, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold, bool withOptionNotToPlay)
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

            options.NumOffers = NumOffers;
            options.NumQualityAndBiasLevels = NumQualityAndBiasLevels;
            if (withOptionNotToPlay)
            {
                options.IncludePQuitDecision = true;
                options.IncludeDQuitDecision = true;
            }
            options.FeeShifting = feeShifting;
            options.FeeShiftingIsBasedOnMarginOfVictory = feeShiftingMarginOfVictory;
            options.FeeShiftingThreshold = feeShiftingThreshold;
            return options;
        }

        public static AdditiveEvidenceGameOptions Biasless(double quality, double pPortionOfPrivateInfo /* set to quality to be like the original DMS model in this respect */, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold, bool withOptionNotToPlay)
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

            options.NumOffers = NumOffers;
            options.NumQualityAndBiasLevels = NumQualityAndBiasLevels;
            if (withOptionNotToPlay)
            {
                options.IncludePQuitDecision = true;
                options.IncludeDQuitDecision = true;
            }
            options.FeeShifting = feeShifting;
            options.FeeShiftingIsBasedOnMarginOfVictory = feeShiftingMarginOfVictory;
            options.FeeShiftingThreshold = feeShiftingThreshold;
            return options;
        }
    }
}
