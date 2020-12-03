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

        public override List<(string optionSetName, GameOptions options)> GetOptionsSets()
        {
            return new List<(string optionSetName, GameOptions options)>() { ("CoopReport", GetSingleGameOptions()) };
        }

        public override GameOptions GetSingleGameOptions()
        {
            return new GameOptions(); // no game-specific optinos
        }
    }
}
