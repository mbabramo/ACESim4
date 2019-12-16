using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public class AdditiveEvidenceGameOptionsGenerator
    {
        public enum AdditiveEvidenceOptionSetChoices
        {
            Base,
        }

        static AdditiveEvidenceOptionSetChoices AdditiveEvidenceChoice => AdditiveEvidenceOptionSetChoices.Base;

        public static AdditiveEvidenceGameOptions GetAdditiveEvidenceGameOptions() => AdditiveEvidenceChoice switch
        {
            AdditiveEvidenceOptionSetChoices.Base => DariMattiacci_Saraceno(0.5, 0.15, false, false, 0.5),
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

            options.NumOffers = 20;
            options.NumQualityAndBiasLevels = 20;
            return options;
        }
    }
}
