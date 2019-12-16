using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public static class AdditiveActionsGameActionsGenerator
    {
        public static Func<Decision, GameProgress, byte> PlaySpecifiedDecisions(byte chancePlaintiffQuality = 1, byte chanceDefendantQuality = 1, byte chancePlaintiffBias = 1, byte chanceDefendantBias=1, byte pOffer=1, byte dOffer=1, byte chanceNeitherQuality=1, byte chanceNeitherBias=1)
        {
            return (d, p) =>
            {
                switch ((AdditiveEvidenceGameDecisions)d.DecisionByteCode)
                {
                    case AdditiveEvidenceGameDecisions.Chance_Plaintiff_Quality:
                        return chancePlaintiffQuality;
                    case AdditiveEvidenceGameDecisions.Chance_Defendant_Quality:
                        return chanceDefendantQuality;
                    case AdditiveEvidenceGameDecisions.Chance_Plaintiff_Bias:
                        return chancePlaintiffBias;
                    case AdditiveEvidenceGameDecisions.Chance_Defendant_Bias:
                        return chanceDefendantBias;
                    case AdditiveEvidenceGameDecisions.POffer:
                        return pOffer;
                    case AdditiveEvidenceGameDecisions.DOffer:
                        return dOffer;
                    case AdditiveEvidenceGameDecisions.Chance_Neither_Quality:
                        return chanceNeitherQuality;
                    case AdditiveEvidenceGameDecisions.Chance_Neither_Bias:
                        return chanceNeitherBias;
                    default:
                        throw new NotSupportedException();
                }
            };
        }

        public static byte BothOffer3(Decision decision, GameProgress progress)
        {
            AdditiveEvidenceGameProgress p = (AdditiveEvidenceGameProgress)progress;
            if (decision.DecisionByteCode == (byte)AdditiveEvidenceGameDecisions.POffer || decision.DecisionByteCode == (byte)AdditiveEvidenceGameDecisions.DOffer)
                return 3;
            return 1;
        }
        public static byte POffers4_DOffers3(Decision decision, GameProgress progress)
        {
            AdditiveEvidenceGameProgress p = (AdditiveEvidenceGameProgress)progress;
            if (decision.DecisionByteCode == (byte)AdditiveEvidenceGameDecisions.POffer)
                return 4;
            if (decision.DecisionByteCode == (byte)AdditiveEvidenceGameDecisions.DOffer)
                return 3;
            return 0;
        }
        public static byte POffers2_DOffers5(Decision decision, GameProgress progress)
        {
            AdditiveEvidenceGameProgress p = (AdditiveEvidenceGameProgress)progress;
            if (decision.DecisionByteCode == (byte)AdditiveEvidenceGameDecisions.POffer)
                return 2;
            if (decision.DecisionByteCode == (byte)AdditiveEvidenceGameDecisions.DOffer)
                return 5;
            return 0;
        }
    }
}
