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
        public byte PrePrimaryChanceAction, PrimaryAction, PostPrimaryChanceAction;
    }
}
