using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.LitigGame.Options
{

    

        public class PrecautionNegligenceOptionsGeneratorSettings
        {
            public bool UseSimplifiedPrecautionNegligenceGame = true; // DEBUG
            public bool CollapseDecisionsInSimplifiedPrecautionNegligenceGame = false;
            public bool PerfectAdjudication = false;
            public bool PerfectInformationToo = false;

            public byte ParameterForMultipleOptions_Simplified = 2;
            public byte ParameterForMultipleOptions = 3; // DEBUG 8
        }
}
