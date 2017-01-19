using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class GameDefinitionSettingsSet : SettingsSet
    {
        public GameDefinitionSettingsSet()
        {
        }

        public GameDefinitionSettingsSet(string theName)
            : base(theName)
        {
            // Do nothing.
        }
    }
}
