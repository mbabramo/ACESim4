using System;
using System.Collections.Generic;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class GameOptions
    {

        public string Name;

        public GameOptions WithName(string name)
        {
            Name = name;
            return this;
        }

        // This can be used to modify evolution settings for a particular game options in a game options set.
        public Action<EvolutionSettings> ModifyEvolutionSettings;

        public bool InvertChanceDecisions { get; set; }

        /// <summary>
        /// If the launcher creates many different versions of game options, it can set the value of options here. This can make it easier to combine different game options into reports afterwards.
        /// </summary>
        public Dictionary<string, object> VariableSettings = new Dictionary<string, object>();

        public virtual void Simplify()
        {

        }
    }
}
