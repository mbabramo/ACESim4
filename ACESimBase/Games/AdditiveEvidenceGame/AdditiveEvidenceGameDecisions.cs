using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public enum AdditiveEvidenceGameDecisions : byte
    {

        Chance_Plaintiff_Quality,
        Chance_Defendant_Quality,
        Chance_Plaintiff_Bias,
        Chance_Defendant_Bias,

        POffer,
        DOffer,

        Chance_Neither_Quality, 
        Chance_Neither_Bias,
    }
}
