using System;
using System.Collections.Generic;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class GameOptions
    {
        public string GroupName;
        public string Name;

        public GameOptions WithName(string name)
        {
            Name = name;
            return this;
        }

        // This can be used to modify evolution settings for a particular game options in a game options set.
        public Action<EvolutionSettings> ModifyEvolutionSettings;

        public bool CollapseChanceDecisions { get; set; }

        public bool InitializeToMostRecentEquilibrium; // This is for the SequenceForm solver. If true, then the initial strategy is set to the most recent equilibrium, which will then be altered to be strictly mixed. If false, then the initial strategy is generated as usual. This is in GameDefinition because we might want to do this for only some games.

        /// <summary>
        /// If the launcher creates many different versions of game options, it can set the value of options here. This can make it easier to combine different game options into reports afterwards.
        /// </summary>
        public Dictionary<string, object> VariableSettings = new Dictionary<string, object>();

        public virtual void Simplify()
        {

        }
    }
}
