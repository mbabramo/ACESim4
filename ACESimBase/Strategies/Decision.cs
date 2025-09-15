using ACESimBase.GameSolvingSupport.Symmetry;
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
        /// True if this is a decision of a chance player
        /// </summary>
        public bool IsChance;

        /// <summary>
        /// The player responsible for this decision.
        /// </summary>
        public byte PlayerIndex;

        /// <summary>
        /// The players to be informed of this decision. For a non-chance decision, this will generally include the player itself, so that the player can remember the decision.
        /// </summary>
        public byte[] PlayersToInform;

        /// <summary>
        /// If any players are listed here, then they are informed only that the action has occurred.
        /// </summary>
        public byte[] PlayersToInformOfOccurrenceOnly;

        /// <summary>
        /// If true, then players will be informed only after the next decision in the game. This is useful when two players "simultaneously" make decisions, i.e., the second player should not know of the first player's decision until after the second player makes a decision. We don't currently include the capability of deferring notifications longer than this.
        /// </summary>
        public bool DeferNotificationOfPlayers;

        /// <summary>
        /// If true, the CustomInformationSetManipulation method of the GameDefinition will be called. Otherwise, information sets and cache items will be manipulated solely on the basis of the above.
        /// </summary>
        public bool RequiresCustomInformationSetManipulation;

        /// <summary>
        /// If true, then the decision step can be reversed, via the ReverseDecision mechanism or an overload.
        /// At present, decisions are irreversible if we are deferring notification of players.
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
        /// Indicates whether a decision represents a continuous action, e.g. how much to bet in a round of poker. 
        /// </summary>
        public bool IsContinuousAction;

        /// <summary>
        /// Indicates that when unrolling, the subsequent set of commands will be identical regardless of the action value taken. In other words, this should be true if the structure of the game remains the same for any action, even though of course the optimal decisions will depend on the action taken.
        /// </summary>
        public bool GameStructureSameForEachAction;

        /// <summary>
        /// If non-null, then through the specified iteration, the action of this player, when an opponent is being optimized using the opponent action probability in the information set, will be set to the WarmStartValue.
        /// </summary>
        public int? WarmStartThroughIteration;

        /// <summary>
        /// The action to choose when warm starting.
        /// </summary>
        public byte WarmStartValue;

        /// <summary>
        /// Information on how information in player 0's information set or decision by a player
        /// </summary>
        public (SymmetryMapInput information, SymmetryMapOutput decision) SymmetryMap;

        public Decision()
        {

        }

        public Decision(string name, string abbreviation, bool isChance, byte playerNumber, byte[] playersToInform, byte numActions, byte decisionByteCode = 0, string decisionTypeCode = null, int repetitionsAfterFirst = 0, string preevolvedStrategyFilename = null, List<string> informationSetAbbreviations = null, byte? alwaysDoAction = null, bool unevenChanceActions = false, bool criticalNode = false)
        {
            Name = name;
            Abbreviation = abbreviation;
            PlayerIndex = playerNumber;
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
            IsChance = isChance;
        }

        public Decision Clone()
        {
            Decision d = new Decision(Name, Abbreviation, IsChance, PlayerIndex, PlayersToInform?.ToArray() ?? new byte[] { }, NumPossibleActions, DecisionByteCode, DecisionTypeCode, RepetitionsAfterFirst, PreevolvedStrategyFilename, InformationSetAbbreviations, AlwaysDoAction, UnevenChanceActions, CriticalNode) { IsAlwaysPlayersLastDecision = IsAlwaysPlayersLastDecision, CanTerminateGame = CanTerminateGame, IncrementGameCacheItem = IncrementGameCacheItem, CustomByte = CustomByte, DeferNotificationOfPlayers = DeferNotificationOfPlayers, RequiresCustomInformationSetManipulation = RequiresCustomInformationSetManipulation, IsReversible = IsReversible, StoreActionInGameCacheItem = StoreActionInGameCacheItem, AlwaysTerminatesGame = AlwaysTerminatesGame,  IsChance = IsChance, IsContinuousAction = IsContinuousAction, GameStructureSameForEachAction = GameStructureSameForEachAction, WarmStartThroughIteration = WarmStartThroughIteration, WarmStartValue = WarmStartValue, SymmetryMap = SymmetryMap };
            return d;
        }

        public override string ToString()
        {
            return
                $"{Name} ({Abbreviation}) Player {PlayerIndex} ByteCode {DecisionByteCode} CustomByte {CustomByte} UnevenChanceActions {UnevenChanceActions} AlwaysDoAction {AlwaysDoAction}";
        }
    }

}
