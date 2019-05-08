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
        public byte[] PlayersToInform;

        /// <summary>
        /// If any players are listed here, then they are informed only that the action has occurred.
        /// </summary>
        public byte[] PlayersToInformOfOccurrenceOnly;

        /// <summary>
        /// If true, then players will be informed only after the next decision in the game. This is useful when two players "simultaneously" make decisions, i.e., the second player should not know of the first player's decision until after the second player makes a decision.
        /// </summary>
        public bool DeferNotificationOfPlayers;

        /// <summary>
        /// If true, the CustomInformationSetManipulation method of the GameDefinition will be called. Otherwise, information sets and cache items will be manipulated solely on the basis of the above.
        /// </summary>
        public bool RequiresCustomInformationSetManipulation;

        /// <summary>
        /// If true, then the decision step can be reversed, via the ReverseDecision mechanism or an overload.
        /// </summary>
        public bool IsReversible;

        /// <summary>
        /// If non-null, then the game history cache item specified will be incremented immediately after the decision. This makes it possible to keep track of how many times a decision or set of decisions have been made.
        /// </summary>
        public byte[] IncrementGameCacheItem;

        /// <summary>
        /// If non-null, then the game history cache item specified will be set to the action chosen.
        /// </summary>
        public byte? StoreActionInGameCacheItem;

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
        public byte? AlwaysDoAction;

        /// <summary>
        /// Indicates whether the decision is always the final decision by a player.
        /// </summary>
        public bool IsAlwaysPlayersLastDecision;

        /// <summary>
        /// Indicates whether it is possible that the decision will terminate the game. This is needed so that we can identify decisions that may require that the GameHistory be marked as complete.
        /// </summary>
        public bool CanTerminateGame;

        /// <summary>
        /// Indicates that a decision always terminates the game. This allows for a more efficient probing strategy to be used.
        /// </summary>
        public bool AlwaysTerminatesGame;

        /// <summary>
        /// A game-specific code, often used simply to list the decisions in order.
        /// </summary>
        public byte DecisionByteCode;

        /// <summary>
        /// A game-specific decision type code that can be used to provide information about the type of decision. For example, this can be
        /// used to determine whether the CurrentlyEvolvingDecision is of a particular type.
        /// </summary>
        public string DecisionTypeCode;

        /// <summary>
        /// When this is set to 1 or more, the decision will be copied multiple times into the game definition. 
        /// </summary>
        public int RepetitionsAfterFirst = 0;

        /// <summary>
        /// A file containing a version of the strategy to use before evolution.
        /// </summary>
        public string PreevolvedStrategyFilename;

        /// <summary>
        /// Abbreviations for the items that will be in the information set at the time of this decision.
        /// </summary>
        public List<string> InformationSetAbbreviations;

        /// <summary>
        /// This can be used to store some additional information about a decision.
        /// </summary>
        public byte CustomByte;

        /// <summary>
        /// If true and this is a chance node, then explorative probing will probe all possibilities. 
        /// </summary>
        public bool CriticalNode;

        /// <summary>
        /// This may be set for continuous actions, where a single decision should be broken up into multiple nodes. For example, if the choices are numbers 1-128, the first decision might be to choose between 1 and 64, the second between 65 and 128, etc. Note that the automatically generated subdivisions will have Subdividable == false.
        /// </summary>
        public bool Subdividable;

        /// <summary>
        /// The number of options per branch. For example, this would be set to 2 for a binary division. Currently, only 2 is supported.
        /// </summary>
        public byte Subdividable_NumOptionsPerBranch;

        /// <summary>
        /// The number of levels to be used for subdividing. For example, if the actions are 1-128 and there are two options per branch, this should be set to 7.
        /// </summary>
        public byte Subdividable_NumLevels;

        /// <summary>
        /// The number of options per branch raised to the number of levels. If Subdividable_NumOptionsPerBranch is 2 and Subdividable_NumLevels is 7, then this should be 128.
        /// </summary>
        public byte Subdividable_AggregateNumPossibleActions;


        public byte AggregateNumPossibleActions => Subdividable_IsSubdivision ? Subdividable_AggregateNumPossibleActions : NumPossibleActions;

        /// <summary>
        /// When a decision is subdividable, it is duplicated into multiple subdivision decisions. The subdivision decision components will have this set to true.
        /// </summary>
        public bool Subdividable_IsSubdivision;

        /// <summary>
        /// For a subdividable decision, this represents the decision byte code to be used for each level of the substitutable decision. For the subdivision itself, this represents the decision byte code of the subdivided decision.
        /// </summary>
        public byte Subdividable_CorrespondingDecisionByteCode;


        /// <summary>
        /// Indicates for a subdivision the subdivision level. Level 1 is the first and most important level (e.g., if we're choosing among 128 levels, the action will be a 1 for values 1-64 and a 2 for values 65-128).
        /// </summary>
        public byte Subdividable_IsSubdivision_Level;

        /// <summary>
        /// Indicates for a subdivision whether this is the first subdivision. If so, a stub will be inserted in the party's own information set.
        /// </summary>
        public bool Subdividable_IsSubdivision_First;

        /// <summary>
        /// Indicates for a subdivision whether this is the last subdivision. If this is true, then after the player makes its move, all of the items accumulating for each subdivision level in the information set will be removed and replaced by the aggregated decision.
        /// </summary>
        public bool Subdividable_IsSubdivision_Last;

        /// <summary>
        /// Indicates that when using an unrolling algorithm, different versions of the decision should be run in parallel.
        /// </summary>
        public bool Unroll_Parallelize;

        /// <summary>
        /// Indicates that when unrolling, the subsequent set of commands will be identical regardless of the action value taken. In other words, this should be true if the structure of the game remains the same for any action, even though of course the optimal decisions will depend on the action taken.
        /// </summary>
        public bool Unroll_Parallelize_Identical;

        public Decision()
        {

        }

        public Decision(string name, string abbreviation, byte playerNumber, byte[] playersToInform, byte numActions, byte decisionByteCode = 0, string decisionTypeCode = null, int repetitionsAfterFirst = 0, string preevolvedStrategyFilename = null, List<string> informationSetAbbreviations = null, byte? alwaysDoAction = null, bool unevenChanceActions = false, bool criticalNode = false)
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
            CriticalNode = criticalNode;
        }

        public Decision Clone()
        {
            Decision d = new Decision(Name, Abbreviation, PlayerNumber, PlayersToInform?.ToArray() ?? new byte[] {}, NumPossibleActions, DecisionByteCode, DecisionTypeCode, RepetitionsAfterFirst, PreevolvedStrategyFilename, InformationSetAbbreviations, AlwaysDoAction, UnevenChanceActions, CriticalNode) { IsAlwaysPlayersLastDecision = IsAlwaysPlayersLastDecision, CanTerminateGame = CanTerminateGame, IncrementGameCacheItem = IncrementGameCacheItem, CustomByte = CustomByte, Subdividable = Subdividable, Subdividable_NumLevels = Subdividable_NumLevels, Subdividable_NumOptionsPerBranch = Subdividable_NumOptionsPerBranch, Subdividable_CorrespondingDecisionByteCode = Subdividable_CorrespondingDecisionByteCode, Subdividable_IsSubdivision = Subdividable_IsSubdivision, Subdividable_IsSubdivision_Last = Subdividable_IsSubdivision_Last, Subdividable_IsSubdivision_First = Subdividable_IsSubdivision_First, Subdividable_AggregateNumPossibleActions = Subdividable_AggregateNumPossibleActions, DeferNotificationOfPlayers = DeferNotificationOfPlayers, StoreActionInGameCacheItem = StoreActionInGameCacheItem, Subdividable_IsSubdivision_Level = Subdividable_IsSubdivision_Level, };
            return d;
        }

        public override string ToString()
        {
            return
                $"{Name} ({Abbreviation}) Player {PlayerNumber} ByteCode {DecisionByteCode} CustomByte {CustomByte} Subdivision? {Subdividable_IsSubdivision} UnevenChanceActions {UnevenChanceActions} AlwaysDoAction {AlwaysDoAction}";
        }

        public void AddDecisionOrSubdivisions(List<Decision> currentDecisionList)
        {
            if (!Subdividable)
                currentDecisionList.Add(this);
            else
                currentDecisionList.AddRange(ConvertToSubdivisionDecisions());
            // NOTE: We do not add the original decision after the subdivisions are added. Instead, the Game will arrange for progress to be added based on the original decision after the last subdivision executes, even though it hasn't been added.
        }

        public List<Decision> ConvertToSubdivisionDecisions()
        {
            if (!Subdividable)
                throw new Exception("Only subdividable decisions can be converted");
            int totalActions = (int)Math.Pow(Subdividable_NumOptionsPerBranch, Subdividable_NumLevels);
            if (totalActions != NumPossibleActions || totalActions > 254)
                throw new Exception("Subdivision not set up correctly.");
            List<Decision> decisions = new List<Decision>();
            for (int i = 0; i < Subdividable_NumLevels; i++)
            {
                Decision subdivisionDecision = Clone();
                subdivisionDecision.Name += $" (Subdivision Level {i + 1})";
                subdivisionDecision.Abbreviation += $"SL{i + 1}";
                subdivisionDecision.DecisionByteCode = Subdividable_CorrespondingDecisionByteCode;
                subdivisionDecision.Subdividable_CorrespondingDecisionByteCode = DecisionByteCode;
                subdivisionDecision.Subdividable = false;
                subdivisionDecision.Subdividable_AggregateNumPossibleActions = NumPossibleActions;
                subdivisionDecision.NumPossibleActions = Subdividable_NumOptionsPerBranch;
                subdivisionDecision.Subdividable_IsSubdivision = true;
                subdivisionDecision.PlayersToInform = PlayersToInform?.ToArray(); // this will be applied only when simulating the last decision
                subdivisionDecision.PlayersToInformOfOccurrenceOnly = PlayersToInformOfOccurrenceOnly?.ToArray();
                if (i == 0)
                    subdivisionDecision.Subdividable_IsSubdivision_First = true;
                else if (i == Subdividable_NumLevels - 1)
                    subdivisionDecision.Subdividable_IsSubdivision_Last = true;
                subdivisionDecision.Subdividable_IsSubdivision_Level = (byte) (i + 1);
                subdivisionDecision.CanTerminateGame = subdivisionDecision.Subdividable_IsSubdivision_Last;
                decisions.Add(subdivisionDecision);
            }
            return decisions;
        }
    }

}
