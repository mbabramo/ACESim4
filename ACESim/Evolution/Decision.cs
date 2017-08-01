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
        /// If true, the decision itself is hidden; the other players will know only that the decision occurred.
        /// </summary>
        public bool InformOnlyThatDecisionOccurred;

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

        /// <summary>
        /// If true, then information will never be automatically added to information sets. 
        /// </summary>
        [OptionalSetting]
        public bool CustomInformationSetManipulationOnly;

        /// <summary>
        /// This can be used to store some additional information about a decision.
        /// </summary>
        [OptionalSetting]
        public byte CustomByte;

        /// <summary>
        /// This may be set for continuous actions, where a single decision should be broken up into multiple nodes. For example, if the choices are numbers 1-128, the first decision might be to choose between 1 and 64, the second between 65 and 128, etc. 
        /// </summary>
        [OptionalSetting]
        public bool Subdividable;

        /// <summary>
        /// The number of options per branch. For example, this would be set to 2 for a binary division.
        /// </summary>
        public byte Subdividable_NumOptionsPerBranch;

        /// <summary>
        /// The number of levels to be used for subdividing. For example, if the actions are 1-128 and there are two options per branch, this should be set to 2.
        /// </summary>
        public byte Subdividable_NumLevels;

        /// <summary>
        /// The number of options per branch raised to the number of levels.
        /// </summary>
        public byte Subdividable_OriginalNumPossibleActions;

        /// <summary>
        /// For a subdividable decision, this represents the decision byte code to be used for each level of the substitutable decision. For the subdivision itself, this represents the decision byte code of the subdivided decision.
        /// </summary>
        public byte Subdividable_CorrespondingDecisionByteCode;

        /// <summary>
        /// When a decision is subdividable, it is duplicated into multiple subdivision decisions.
        /// </summary>
        [OptionalSetting]
        public bool Subdividable_IsSubdivision;


        /// <summary>
        /// Indicates for a subdivision whether this is the first subdivision. If so, a stub will be inserted in the party's own information set.
        /// </summary>
        [OptionalSetting]
        public bool Subdividable_IsSubdivision_First;

        /// <summary>
        /// Indicates for a subdivision whether this is the last subdivision. If this is true, then after the player makes its move, all of the items accumulating for each subdivision level in the information set will be removed and replaced by the aggregated decision.
        /// </summary>
        [OptionalSetting]
        public bool Subdividable_IsSubdivision_Last;

        public Decision()
        {

        }

        public Decision(string name, string abbreviation, byte playerNumber, List<byte> playersToInform, byte numActions, byte decisionByteCode = 0, string decisionTypeCode = null, int repetitionsAfterFirst = 0, string preevolvedStrategyFilename = null, List<string> informationSetAbbreviations = null, byte? alwaysDoAction = null, bool unevenChanceActions = false, bool informOnlyThatDecisionOccurred = false)
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
            InformOnlyThatDecisionOccurred = informOnlyThatDecisionOccurred;
        }

        public Decision Clone()
        {
            Decision d = new Decision(Name, Abbreviation, PlayerNumber, PlayersToInform?.ToList() ?? new List<byte>(), NumPossibleActions, DecisionByteCode, DecisionTypeCode, RepetitionsAfterFirst, PreevolvedStrategyFilename, InformationSetAbbreviations, AlwaysDoAction, UnevenChanceActions, InformOnlyThatDecisionOccurred) { IsAlwaysPlayersLastDecision = IsAlwaysPlayersLastDecision, CanTerminateGame = CanTerminateGame, CustomInformationSetManipulationOnly = CustomInformationSetManipulationOnly, CustomByte = CustomByte, Subdividable = Subdividable, Subdividable_NumLevels = Subdividable_NumLevels, Subdividable_NumOptionsPerBranch = Subdividable_NumOptionsPerBranch, Subdividable_CorrespondingDecisionByteCode = Subdividable_CorrespondingDecisionByteCode, Subdividable_IsSubdivision = Subdividable_IsSubdivision, Subdividable_IsSubdivision_Last = Subdividable_IsSubdivision_Last, Subdividable_IsSubdivision_First = Subdividable_IsSubdivision_First, Subdividable_OriginalNumPossibleActions = Subdividable_OriginalNumPossibleActions };
            return d;
        }

        public void AddDecisionOrSubdivisions(List<Decision> currentDecisionList)
        {
            if (!Subdividable)
                currentDecisionList.Add(this);
            else
                currentDecisionList.AddRange(ConvertToSubdivisionDecisions());
        }

        public List<Decision> ConvertToSubdivisionDecisions()
        {
            if (!Subdividable)
                throw new Exception("Only subdividable decisions can be converted");
            int totalActions = (int)Math.Pow(Subdividable_NumOptionsPerBranch, Subdividable_NumLevels);
            if (totalActions != NumPossibleActions)
                throw new Exception("Subdivision not set up correctly.");
            List<Decision> decisions = new List<Decision>();
            for (int i = 0; i < Subdividable_NumLevels; i++)
            {
                Decision subdivisionDecision = Clone();
                subdivisionDecision.DecisionByteCode = Subdividable_CorrespondingDecisionByteCode;
                subdivisionDecision.Subdividable_CorrespondingDecisionByteCode = DecisionByteCode;
                subdivisionDecision.Subdividable = false;
                subdivisionDecision.Subdividable_OriginalNumPossibleActions = NumPossibleActions;
                subdivisionDecision.NumPossibleActions = Subdividable_NumOptionsPerBranch;
                subdivisionDecision.Subdividable_IsSubdivision = true;
                subdivisionDecision.CustomInformationSetManipulationOnly = false;
                if (i == 0)
                    subdivisionDecision.Subdividable_IsSubdivision_First = true;
                else if (i == Subdividable_NumLevels - 1)
                    subdivisionDecision.Subdividable_IsSubdivision_Last = true;
                decisions.Add(subdivisionDecision);
            }
            return decisions;
        }
    }

}
