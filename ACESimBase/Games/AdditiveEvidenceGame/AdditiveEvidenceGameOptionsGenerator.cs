using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public class AdditiveEvidenceGameOptionsGenerator
    {
        public static byte NumOffers = 10; // having a good number here allows for more precise strategies // 5/5/3 -> 39 seconds. 6/5/3 -> 2:10, 7/... -> 4:56 8/... -> 4:29 9/... -> 7:53 10/... -> 9:50
        public static byte NumQualityAndBiasLevels_PrivateInfo = 10; // we don't need quite as much here, since it's information that doesn't intersect between players
        public static byte NumQualityAndBiasLevels_NeitherInfo = 1; // still less needed here

        public enum AdditiveEvidenceOptionSetChoices
        {
            DMS,
            EqualStrengthInfo,
            AsymmetricInfo,
            NoPInfo,
            NoisyWithAsymmetricInfoOnBias,
            Temporary,
        }

        // Note: Go to Launcher to change multiple option sets settings.

        static AdditiveEvidenceOptionSetChoices AdditiveEvidenceChoice => AdditiveEvidenceOptionSetChoices.NoPInfo; 

        public static AdditiveEvidenceGameOptions GetAdditiveEvidenceGameOptions() => AdditiveEvidenceChoice switch
        {
            AdditiveEvidenceOptionSetChoices.DMS => DariMattiacci_Saraceno_Original(0.6, 0.15, true, false, 0.5, false, false),
            AdditiveEvidenceOptionSetChoices.EqualStrengthInfo => EqualStrengthInfo(0.6, 0.15, true, false, 0.5, false, false),
            AdditiveEvidenceOptionSetChoices.AsymmetricInfo => AsymmetricInfo(1.0, 0.65, 0.8, 0.15, true, false, 0.25, false, false),
            AdditiveEvidenceOptionSetChoices.NoPInfo => AsymmetricInfo(1.0, 0, 0.8, 0.15, true, false, 0.25, false, false),
            AdditiveEvidenceOptionSetChoices.NoisyWithAsymmetricInfoOnBias => NoiseAndAsymmetricInfo(0.50, 1.0, 0.75, 0.8, 0.15, true, false, 0.25, false, false),
            AdditiveEvidenceOptionSetChoices.Temporary => DariMattiacci_Saraceno_Original(0.8, 0, true, false, 0.01, true, true)
            ,
            _ => throw new NotImplementedException()
        };
        public static AdditiveEvidenceGameOptions DariMattiacci_Saraceno_Original(double quality, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold, bool withOptionNotToPlay, bool winnerTakesAll = false)
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
            options.NumQualityAndBiasLevels_PrivateInfo = NumQualityAndBiasLevels_PrivateInfo;
            options.NumQualityAndBiasLevels_NeitherInfo = NumQualityAndBiasLevels_NeitherInfo;
            if (withOptionNotToPlay)
            {
                options.IncludePQuitDecision = true;
                options.IncludeDQuitDecision = true;
                // change the offer range, so that neither party can count on always getting a minimal offer
                // if plaintiff makes an offer > 1 or defendant makes an offer < 0, then the other party will not be allowed to match
                options.MinOffer = 0 - 1.0 / (double)options.NumOffers;
                options.OfferRange = 1.0 + 2.0 / (double)options.NumOffers;
                options.NumOffers += 2;
            }

            options.FeeShifting = feeShifting;
            options.FeeShiftingIsBasedOnMarginOfVictory = feeShiftingMarginOfVictory;
            options.FeeShiftingThreshold = feeShiftingThreshold;

            if (winnerTakesAll)
            {
                options.WinnerTakesAll = true;
            }
            return options;
        }

        public static AdditiveEvidenceGameOptions EqualStrengthInfo(double quality, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold, bool withOptionNotToPlay, bool winnerTakesAll)
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
            // Because quality is fixed, parties don't need to estimate that. They do have estimates on bias. 
            options.Alpha_Plaintiff_Bias = 0.5; // even strength on bias
            options.Alpha_Defendant_Bias = 1.0 - options.Alpha_Plaintiff_Bias;
            // nothing to neither or both with respect to bias

            options.TrialCost = costs;

            options.NumOffers = NumOffers;
            options.NumQualityAndBiasLevels_PrivateInfo = NumQualityAndBiasLevels_PrivateInfo;
            options.NumQualityAndBiasLevels_NeitherInfo = NumQualityAndBiasLevels_NeitherInfo;
            if (withOptionNotToPlay)
            {
                options.IncludePQuitDecision = true;
                options.IncludeDQuitDecision = true;
            }
            options.FeeShifting = feeShifting;
            options.FeeShiftingIsBasedOnMarginOfVictory = feeShiftingMarginOfVictory;
            options.FeeShiftingThreshold = feeShiftingThreshold; 
            if (winnerTakesAll)
            {
                options.WinnerTakesAll = true;
            }
            return options;
        }

        public static AdditiveEvidenceGameOptions AsymmetricInfo(double alphaBothQuality, double pPortionOfPrivateInfo, double sharedQualityInfo, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold, bool withOptionNotToPlay, bool winnerTakesAll) => NoiseAndAsymmetricInfo(0, alphaBothQuality, pPortionOfPrivateInfo, sharedQualityInfo, costs, feeShifting, feeShiftingMarginOfVictory, feeShiftingThreshold, withOptionNotToPlay, winnerTakesAll);
        
        public static AdditiveEvidenceGameOptions NoiseAndAsymmetricInfo(double noisiness, double alphaBothQuality, double pPortionOfPrivateInfo, double sharedQualityInfo, double costs, bool feeShifting, bool feeShiftingMarginOfVictory, double feeShiftingThreshold, bool withOptionNotToPlay, bool winnerTakesAll)
        {
            var options = new AdditiveEvidenceGameOptions()
            {
                Evidence_Both_Quality = sharedQualityInfo,
                Alpha_Quality = 0.5,
                Alpha_Both_Quality = alphaBothQuality, // if parallel to above, this should be 1.0, meaning that all info on quality is shared. If less than 1.0, then the parties will have private information on quality as well as on what we are calling bias
                Alpha_Plaintiff_Quality = (1 - alphaBothQuality) * pPortionOfPrivateInfo,
                Alpha_Defendant_Quality = (1 - alphaBothQuality) * (1.0 - pPortionOfPrivateInfo),
                // so Neither_Quality is set automatically to 0
                Alpha_Both_Bias = 0.0, // no shared info on bias
                Alpha_Plaintiff_Bias = (1.0 - noisiness) * pPortionOfPrivateInfo,
                Alpha_Defendant_Bias = (1.0 - noisiness) * (1.0 - pPortionOfPrivateInfo),
                // thus, the noisiness applies to the bias component only, and it represents information that is irrelevant to the merits, that affects the judgment, and no one knows -- essentially, judicial uncertainty.
            };
            // nothing to neither or both with respect to bias

            options.TrialCost = costs;

            options.NumOffers = NumOffers;
            options.NumQualityAndBiasLevels_PrivateInfo = NumQualityAndBiasLevels_PrivateInfo;
            options.NumQualityAndBiasLevels_NeitherInfo = NumQualityAndBiasLevels_NeitherInfo;
            if (withOptionNotToPlay)
            {
                options.IncludePQuitDecision = true;
                options.IncludeDQuitDecision = true;
            }
            options.FeeShifting = feeShifting;
            options.FeeShiftingIsBasedOnMarginOfVictory = feeShiftingMarginOfVictory;
            options.FeeShiftingThreshold = feeShiftingThreshold; 
            if (winnerTakesAll)
            {
                options.WinnerTakesAll = true;
            }
            return options;
        }
    }
}
