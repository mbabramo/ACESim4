using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public static class AdditiveActionsGameActionsGenerator
    {
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
