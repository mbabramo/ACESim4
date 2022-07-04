using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.DMSReplicationGame
{
    public class DMSReplicationGameLauncher : Launcher
    {
        public override string MasterReportNameForDistributedProcessing => "DMS003"; // Note: Overridden in subclass.

        public override GameDefinition GetGameDefinition() => new DMSReplicationGameDefinition();

        public override GameOptions GetDefaultSingleGameOptions() => DMSReplicationGameOptionsGenerator.GetDMSReplicationGameOptions();

        public override List<GameOptions> GetOptionsSets()
        {
            throw new NotImplementedException();
        }
    }
}
