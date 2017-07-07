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
        /// The players to be informed of this decision. For a non-chance decision, this will generally include the player itself, so that the player can remember the decision.
        /// </summary>
        public List<byte> PlayersToInform;

        /// <summary>
        /// The number of discrete actions for this decision. (The actions will be numbered 1 .. NumberActions.)
        /// </summary>
        public byte NumPossibleActions;

        /// <summary>
        /// If this is a chance decision and this is true, then the probabilities of difference chance actions will be determined based on the game progress.
        /// </summary>
        public bool UnevenChanceActions;

        /// <summary>
        /// Whether the decision is bipolar (i.e., there are only two possible actions).
        /// </summary>
        public bool Bipolar => NumPossibleActions == 2;

        /// <summary>
        /// If non-null, the decision will always result in this action. (Not yet implemented)
        /// </summary>
        [OptionalSetting]
        public byte? AlwaysDoAction;

        /// <summary>
        /// Indicates whether the decision is always the final decision by a player.
        /// </summary>
        [OptionalSetting]
        public bool IsAlwaysPlayersLastDecision;

        /// <summary>
        /// Indicates whether it is possible that the decision will terminate the game. This is needed so that we can identify decisions that may require that the GameHistory be marked as complete.
        /// </summary>
        [OptionalSetting]
        public bool CanTerminateGame;

        /// <summary>
        /// A game-specific code, often used simply to list the decisions in order.
        /// </summary>
        [OptionalSetting]
        public byte DecisionByteCode;

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

        public Decision(string name, string abbreviation, byte playerNumber, List<byte> playersToInform, byte numActions, byte decisionByteCode = 0, string decisionTypeCode = null, int repetitionsAfterFirst = 0, string preevolvedStrategyFilename = null, List<string> informationSetAbbreviations = null, byte? alwaysDoAction = null, bool unevenChanceActions = false)
        {
            Name = name;
            Abbreviation = abbreviation;
            PlayerNumber = playerNumber;
            PlayersToInform = playersToInform;
            NumPossibleActions = numActions;
            DecisionByteCode = decisionByteCode;
            DecisionTypeCode = decisionTypeCode;
            RepetitionsAfterFirst = repetitionsAfterFirst;
            PreevolvedStrategyFilename = preevolvedStrategyFilename;
            InformationSetAbbreviations = informationSetAbbreviations;
            AlwaysDoAction = alwaysDoAction;
            UnevenChanceActions = unevenChanceActions;
        }
    }

}
