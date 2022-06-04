﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public enum AdditiveEvidenceGameDecisions : byte
    {

        Chance_Plaintiff_Quality,
        Chance_Defendant_Quality,
        Chance_Plaintiff_Bias,
        Chance_Plaintiff_Bias_Reduction,
        Chance_Defendant_Bias,
        Chance_Defendant_Bias_Reduction,

        PQuit,
        DQuit,

        POffer,
        DOffer,
        // If we have piecewise linear, then instead of an offer, each player must announce a min value for the range (which the player receives on the bases of the chance bias number) and a slope for that range.
        P_Slope,
        P_MinValueForRange,
        P_TruncationPortion,
        D_Slope,
        D_MinValueForRange,
        D_TruncationPortion,

        Chance_Neither_Quality, 
        Chance_Neither_Bias,
    }
}
