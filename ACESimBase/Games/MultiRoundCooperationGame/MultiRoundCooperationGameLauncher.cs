using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;

namespace ACESim
{
    public class MultiRoundCooperationGameLauncher : Launcher
    {
        public override GameDefinition GetGameDefinition() => new MultiRoundCooperationGameDefinition();

        public override List<GameOptions> GetOptionsSets()
        {
            return new List<GameOptions>() { GetSingleGameOptions().WithName("CoopReport") };
        }

        public override GameOptions GetSingleGameOptions()
        {
            return new GameOptions(); // no game-specific optinos
        }
    }
}
