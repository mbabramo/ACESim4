using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.DMSReplicationGame
{
    internal class DMSReplicationGameOptionsGenerator
    {
        public static DMSReplicationGameOptions GetDMSReplicationGameOptions() => new DMSReplicationGameOptions()
        {
            C = 0.1,
            Q = 0.5,
            T = 0
        };
    }
}
