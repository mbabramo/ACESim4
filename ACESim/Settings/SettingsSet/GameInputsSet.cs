using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml.Serialization;

namespace ACESim
{
    [Serializable]
    public class GameInputsSet : SettingsSet
    {
        public GameInputsSet()
        {
        }

        public string GameName;

        public GameInputsSet(string settingsSetName, string theGameName)
            : base(settingsSetName)
        {
            GameName = theGameName;
        }
    }
}
