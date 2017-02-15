using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// This provides information on one decision to be made in a simulation.
    /// </summary>
    [Serializable]
    public class Decision
    {
        /// <summary>
        /// The name of the decision (e.g., “Plaintiff settlement decision”).
        /// </summary>
        public String Name;

        /// <summary>
        /// An abbreviation for this name (e.g., “ps”).
        /// </summary>
        public String Abbreviation;

        /// <summary>
        /// A game-specific decision type code that can be used to provide information about the type of decision. For example, this can be
        /// used to determine whether the CurrentlyEvolvingDecision is of a particular type.
        /// </summary>
        [OptionalSetting]
        public string DecisionTypeCode;

        /// <summary>
        /// The bounds for the decision.
        /// </summary>
        public StrategyBounds StrategyBounds;

        /// <summary>
        /// True if the decision can only take the value -1 or 1.
        /// </summary>
        [OptionalSetting]
        public bool Bipolar;

        /// <summary>
        /// Are high scores best for this decision?
        /// </summary>
        public bool HighestIsBest;

        /// <summary>
        /// When this is set to 1 or more, the decision will be copied multiple times into the game definition. 
        /// </summary>
        [OptionalSetting]
        public int RepetitionsAfterFirst = 0;

        /// <summary>
        /// A file containing a version of the strategy to use before evolution.
        /// </summary>
        public string PreevolvedStrategyFilename;
        
        public List<string> InformationSetAbbreviations { get; set; }
    }

}
