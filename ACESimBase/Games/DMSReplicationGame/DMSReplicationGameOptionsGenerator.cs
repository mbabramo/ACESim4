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
            C = 0.2,
            Q = 0.5,
            T = 0
        };

        public static DMSReplicationGameOptions GetDMSReplicationGameOptions((double t, double c, double q) options) => new DMSReplicationGameOptions()
        {
            C = options.c,
            Q = options.q,
            T = options.t,
            Name = $"T{options.t},C{options.c},Q{options.q}"
        };
    }
}
