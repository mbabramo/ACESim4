using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public struct LitigGameDisputeGeneratorActions
    {
        // we combine actions from different dispute generators here for convenience.
        public byte PrePrimaryChanceAction, PrimaryAction, PostPrimaryChanceAction, EngageInActivityAction, PrecautionLevelAction;
    }
}




