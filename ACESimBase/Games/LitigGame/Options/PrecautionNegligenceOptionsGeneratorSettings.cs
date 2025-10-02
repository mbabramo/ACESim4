using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.LitigGame.Options
{
    public class PrecautionNegligenceOptionsGeneratorSettings
    {
        public bool UseSimplifiedPrecautionNegligenceGame = false;
        public byte ParameterForMultipleOptions_Simplified = 2;
        public byte ParameterForMultipleOptions = 2; // DEBUG

        public bool CollapseDecisionsInSimplifiedPrecautionNegligenceGame = true; // DEBUG
        public bool PerfectAdjudication = false;
        public bool PerfectInformationToo = false;

        public byte NumPotentialBargainingRounds = 2; // DEBUG
        public bool AllowQuitting = false;
        public bool PredeterminedAbandonAndDefaults = false; // should be false when doing more than one bargaining round OR when using CFR.

    }
}
