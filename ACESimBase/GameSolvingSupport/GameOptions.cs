using System;
using System.Collections.Generic;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class GameOptions
    {

        // This can be used to modify evolution settings for a particular game options in a game options set.
        public Action<EvolutionSettings> ModifyEvolutionSettings;

        public virtual void Simplify()
        {

        }
    }
}
