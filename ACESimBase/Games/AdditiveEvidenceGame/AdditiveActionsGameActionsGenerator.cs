using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public static class AdditiveActionsGameActionsGenerator
    {
        public static Func<Decision, GameProgress, byte> PlaySpecifiedDecisions(byte chancePlaintiffQuality = 1, byte chanceDefendantQuality = 1, byte chancePlaintiffBias = 1, byte chanceDefendantBias=1, byte pOffer=1, byte dOffer=1, byte chanceNeitherQuality=1, byte chanceNeitherBias=1, bool pQuit = false, bool dQuit = false, byte pSlope = 2, byte dSlope = 2, byte pMinValForRange = 3, byte dMinValForRange = 2, byte pTruncationPortion = 2, byte dTruncationPortion = 2)
        {
            return (d, p) =>
            {
                switch ((AdditiveEvidenceGameDecisions)d.DecisionByteCode)
                {
                    case AdditiveEvidenceGameDecisions.PQuit:
                        return pQuit ? (byte)1 : (byte)2;
                    case AdditiveEvidenceGameDecisions.DQuit:
                        return dQuit ? (byte)1 : (byte)2;
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
    }
}
