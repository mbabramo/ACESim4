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
        public override string MasterReportNameForDistributedProcessing => "DMS008"; // Note: Overridden in subclass.

        public override GameDefinition GetGameDefinition() => new DMSReplicationGameDefinition();

        public override GameOptions GetDefaultSingleGameOptions() => DMSReplicationGameOptionsGenerator.GetDMSReplicationGameOptions();

        public override List<GameOptions> GetOptionsSets()
        {
            // These are the verified one-segment equilibria that should be replicable.
            var optionsToTest = new (double t, double c, double q)[] { (0,0.1,0.4), (0,0.1,0.5), (0,0.1,0.6), (0,0.2,0.4), (0,0.2,0.5), (0,0.2,0.6), (0,0.3,0.4), (0,0.3,0.5), (0,0.3,0.6), (0.1,0.1,0.4), (0.1,0.1,0.5), (0.1,0.1,0.6), (0.1,0.2,0.4), (0.1,0.2,0.5), (0.1,0.2,0.6), (0.1,0.3,0.4), (0.1,0.3,0.5), (0.1,0.3,0.6), (0.2,0.1,0.4), (0.2,0.1,0.5), (0.2,0.1,0.6), (0.2,0.2,0.4), (0.2,0.2,0.5), (0.2,0.2,0.6), (0.2,0.3,0.4), (0.2,0.3,0.5), (0.2,0.3,0.6), (0.3,0.1,0.4), (0.3,0.1,0.5), (0.3,0.1,0.6), (0.3,0.2,0.4), (0.3,0.2,0.5), (0.3,0.2,0.6), (0.3,0.3,0.4), (0.3,0.3,0.5), (0.3,0.3,0.6), (0.4,0.1,0.4), (0.4,0.1,0.5), (0.4,0.1,0.6), (0.4,0.2,0.4), (0.4,0.2,0.5), (0.4,0.2,0.6), (0.4,0.3,0.4), (0.4,0.3,0.5), (0.4,0.3,0.6), (0.5,0.1,0.4), (0.5,0.1,0.5), (0.5,0.1,0.6), (0.5,0.2,0.5), (0.5,0.3,0.5), (0.6,0.1,0.4), (0.6,0.1,0.5), (0.6,0.1,0.6), (0.6,0.3,0.4), (0.6,0.3,0.6), (0.7,0.1,0.4), (0.7,0.1,0.5), (0.7,0.1,0.6), (0.8,0.1,0.4), (0.8,0.1,0.5), (0.8,0.1,0.6), (0.9,0.1,0.4), (0.9,0.1,0.5), (0.9,0.1,0.6), (1,0.1,0.4), (1,0.1,0.5), (1,0.1,0.6),
 };
            return optionsToTest.Select(opt => (GameOptions) DMSReplicationGameOptionsGenerator.GetDMSReplicationGameOptions(opt)).ToList();
        }
    }
}
