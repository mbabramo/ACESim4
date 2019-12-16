using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public class AdditiveEvidenceGameOptionsGenerator
    {
        public enum AdditiveEvidenceOptionSetChoices
        {
            EvenStrength,
            PStrong,
            DStrong
        }

        static AdditiveEvidenceOptionSetChoices AdditiveEvidenceChoice => AdditiveEvidenceOptionSetChoices.EvenStrength;

        public static AdditiveEvidenceGameOptions GetAdditiveEvidenceGameOptions() => AdditiveEvidenceChoice switch
        {
            AdditiveEvidenceOptionSetChoices.EvenStrength => Usual(0.5),
            AdditiveEvidenceOptionSetChoices.PStrong => Usual(0.65), // note that these are between 1/3 and 2/3, as in the mathematical model
            AdditiveEvidenceOptionSetChoices.DStrong => Usual(0.35),
            _ => throw new Exception()
        };

        public static AdditiveEvidenceGameOptions Usual(double quality, bool overrideQualityLimits = false)
        {
            if (!overrideQualityLimits && (quality < 1.0 / 3.0 || quality > 2.0 / 3.0))
                throw new Exception("Quality out of range");
            var options = new AdditiveEvidenceGameOptions()
            {
                Alpha_Quality = 0.5,
                Alpha_Both_Quality = 1.0,
                Alpha_Plaintiff_Quality = 0,
                Alpha_Defendant_Quality = 0,
                Alpha_Both_Bias = 0.0,
            };
            options.Alpha_Plaintiff_Bias = options.Evidence_Both_Quality = quality; // this is the key (strange) assumption -- the proportion of the evidence about bias that is in the plaintiff's possession is equal to the strength of the case
            options.Alpha_Defendant_Bias = 1.0 - options.Alpha_Plaintiff_Bias;
            return options;
        }
    }
}
