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
        /// The player responsible for this decision.
        /// </summary>
        public byte PlayerNumber;

        /// <summary>
        /// The number of discrete actions for this decision. (The actions will be numbered 1 .. NumberActions.)
        /// </summary>
        public int NumActions;

        /// <summary>
        /// Whether the decision is bipolar (i.e., there are only two possible actions).
        /// </summary>
        public bool Bipolar => NumActions == 2;

        /// <summary>
        /// A game-specific decision type code that can be used to provide information about the type of decision. For example, this can be
        /// used to determine whether the CurrentlyEvolvingDecision is of a particular type.
        /// </summary>
        [OptionalSetting]
        public string DecisionTypeCode;

        /// <summary>
        /// When this is set to 1 or more, the decision will be copied multiple times into the game definition. 
        /// </summary>
        [OptionalSetting]
        public int RepetitionsAfterFirst = 0;

        /// <summary>
        /// A file containing a version of the strategy to use before evolution.
        /// </summary>
        [OptionalSetting]
        public string PreevolvedStrategyFilename;

        /// <summary>
        /// Abbreviations for the items that will be in the information set at the time of this decision.
        /// </summary>
        [OptionalSetting]
        public List<string> InformationSetAbbreviations;

        public Decision()
        {

        }

        public Decision(string name, string abbreviation, byte playerNumber, byte numActions, string decisionTypeCode = null, int repetitionsAfterFirst = 0, string preevolvedStrategyFilename = null, List<string> informationSetAbbreviations = null)
        {
            Name = name;
            Abbreviation = abbreviation;
            PlayerNumber = playerNumber;
            NumActions = numActions;
            DecisionTypeCode = decisionTypeCode;
            RepetitionsAfterFirst = repetitionsAfterFirst;
            PreevolvedStrategyFilename = preevolvedStrategyFilename;
            InformationSetAbbreviations = informationSetAbbreviations;
        }
    }

}
