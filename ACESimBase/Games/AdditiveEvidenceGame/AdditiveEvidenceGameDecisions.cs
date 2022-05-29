using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public enum AdditiveEvidenceGameDecisions : byte
    {
        // If we have piecewise linear, then each player must announce a slope and up to three values for the lowest value within the range, corresponding to up to three ranges. 
        P_Slope,
        P_PiecewiseLinear_1,
        P_PiecewiseLinear_2,
        P_PiecewiseLinear_3,
        D_Slope,
        D_PiecewiseLinear_1,
        D_PiecewiseLinear_2,
        D_PiecewiseLinear_3,

        Chance_Plaintiff_Quality,
        Chance_Defendant_Quality,
        Chance_Plaintiff_Bias,
        Chance_Defendant_Bias,

        PQuit,
        DQuit,

        POffer,
        DOffer,

        Chance_Neither_Quality, 
        Chance_Neither_Bias,
    }
}
