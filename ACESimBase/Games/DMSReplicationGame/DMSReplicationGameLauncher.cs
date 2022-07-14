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
        public override string MasterReportNameForDistributedProcessing => "DMS004"; // Note: Overridden in subclass.

        public override GameDefinition GetGameDefinition() => new DMSReplicationGameDefinition();

        public override GameOptions GetDefaultSingleGameOptions() => DMSReplicationGameOptionsGenerator.GetDMSReplicationGameOptions();

        public override List<GameOptions> GetOptionsSets()
        {
            // These are the verified one-segment equilibria that should be replicable.
            var optionsToTest = new (double t, double c, double q)[] { (0, 0, 0.45), (0, 0, 0.55), (0, 0.1, 0.35), (0, 0.1, 0.45), (0, 0.1, 0.55), (0, 0.1, 0.65), (0, 0.2, 0.35), (0, 0.2, 0.55), (0, 0.2, 0.65), (0, 0.3, 0.45), (0, 0.3, 0.55), (0, 0.3, 0.65), (0.1, 0, 0.35), (0.1, 0, 0.45), (0.1, 0, 0.55), (0.1, 0.1, 0.35), (0.1, 0.1, 0.45), (0.1, 0.1, 0.55), (0.1, 0.1, 0.65), (0.1, 0.2, 0.35), (0.1, 0.2, 0.45), (0.1, 0.2, 0.55), (0.1, 0.2, 0.65), (0.1, 0.3, 0.35), (0.1, 0.3, 0.55), (0.1, 0.3, 0.65), (0.2, 0, 0.35), (0.2, 0, 0.45), (0.2, 0, 0.55), (0.2, 0, 0.65), (0.2, 0.1, 0.35), (0.2, 0.1, 0.45), (0.2, 0.1, 0.55), (0.2, 0.2, 0.45), (0.2, 0.2, 0.55), (0.2, 0.3, 0.45), (0.2, 0.3, 0.55), (0.2, 0.3, 0.65), (0.3, 0, 0.35), (0.3, 0, 0.45), (0.3, 0, 0.55), (0.3, 0, 0.65), (0.3, 0.1, 0.45), (0.3, 0.1, 0.65), (0.3, 0.2, 0.35), (0.3, 0.2, 0.45), (0.3, 0.2, 0.55), (0.3, 0.2, 0.65), (0.3, 0.3, 0.35), (0.3, 0.3, 0.45), (0.3, 0.3, 0.55), (0.3, 0.3, 0.65), (0.4, 0, 0.35), (0.4, 0, 0.45), (0.4, 0, 0.55), (0.4, 0.1, 0.45), (0.4, 0.1, 0.55), (0.4, 0.2, 0.45), (0.4, 0.2, 0.55), (0.5, 0, 0.35), (0.5, 0, 0.45), (0.5, 0.1, 0.35), (0.5, 0.1, 0.45), (0.6, 0.1, 0.35), (0.6, 0.3, 0.35), (1, 0, 0.35), (1, 0, 0.45), (1, 0, 0.55), };
            return optionsToTest.Select(opt => (GameOptions) DMSReplicationGameOptionsGenerator.GetDMSReplicationGameOptions(opt)).ToList();
        }
    }
}
