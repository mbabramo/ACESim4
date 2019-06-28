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
        /// If this is a chance decision and this is true, then in optimization and best response calculation, we do not need to sample every value of this decision in walking through the tree. This is because the values are relevant only insofar as they affect the values of later chance decisions. Instead we just use a dummy value for this decision, and then we arrive at a distributor chance decisions, the unequal chance probabilities will be based on a distribution of what these decisions are, given the nondistributed decisions that affect the chance decision. A decision can be a distributed decision only if it is hidden to all non-chance players and its effects on the distributor chance decisions can be captured by the distribution of the nondistributed decisions.
        /// </summary>
        public bool DistributedChanceDecision;

        /// <summary>
        /// True if this is a chance decision with uneven chance probabilities where those probabilities can be based on the distribution of earlier DistributedChanceDecisions for each permutation of the DistributorChanceInputDecisions.
        /// </summary>
        public bool DistributorChanceDecision;

        /// <summary>
        /// True if each chance node for the decision can calculate the probabilities directly based on the distributed chance decisions, i.e. GetUnevenChanceActionProbabilitiesFromChanceInformationSet is defined for this decision. This is used by the accelerated best response algorithm as an alternative approach to calculating chance probabilities based on the DistributorChanceInputDecisions.
        /// </summary>
        public bool CanCalculateDistributorChanceDecisionProbabilitiesFromInformationSet;

        /// <summary>
        /// True if this is a decision that is not a distributed chance decision but that is relevant to the correct calculation of chance probabilities in a later DistributorChanceDecision. That is, the DistributedChanceDecision would ordinarily be part of the information set for the DistributorChanceDecision, but because the distributed decision is distributed, we combine all the permutations of the DistributorChanceInputDecision into a single information set. This can be a chance decision that provides information to one of the parties, or it could be a player decision that affects later chance probabilities. 
        /// </summary>
        public bool DistributorChanceInputDecision;

        /// <summary>
        /// True if this is a decision that should be distributed (passed forward as an array of probabilities), for example as in accelerated best resposne. This is applicable only for a decision for which DistributorChanceInputDecision is true. The decisions will generally be passed as an array only for the players for whom this does not provide private information, so ProvidesPrivateInformationFor should be set below.
        /// </summary>
        public bool DistributableDistributorChanceInput;

        /// <summary>
        /// When passing forward nondistributed decision values, we combine them into a single value. The game definition sets a multiplier for each nondistributed decision to enable calculate of the nondistributed decision value. For example, if each decision had 10 actions, we might use a value of 11 for one, 121 for another, etc. (thus leaving open the possibility that no action has been taken).
        /// </summary>
        public int DistributorChanceInputDecisionMultiplier;

        /// <summary>
        /// Indicates whom this chance decision provides private information for. This is used with DistributableDistributorChanceInputs.
        /// </summary>
        public byte? ProvidesPrivateInformationFor;

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
        /// Indicates that when using an unrolling algorithm, different versions of the decision should be run in parallel.
        /// </summary>
        public bool Unroll_Parallelize;

        /// <summary>
        /// Indicates that when unrolling, the subsequent set of commands will be identical regardless of the action value taken. In other words, this should be true if the structure of the game remains the same for any action, even though of course the optimal decisions will depend on the action taken.
        /// </summary>
        public bool Unroll_Parallelize_Identical;

        /// <summary>
        /// If non-null, then through the specified iteration, the action of this player, when an opponent is being optimized using the opponent action probability in the information set, will be set to the WarmStartValue.
        /// </summary>
        public int? WarmStartThroughIteration;

        /// <summary>
        /// The action to choose when warm starting.
        /// </summary>
        public byte WarmStartValue;

        public Decision()
        {

        }

        public Decision(string name, string abbreviation, bool isChance, byte playerNumber, byte[] playersToInform, byte numActions, byte decisionByteCode = 0, string decisionTypeCode = null, int repetitionsAfterFirst = 0, string preevolvedStrategyFilename = null, List<string> informationSetAbbreviations = null, byte? alwaysDoAction = null, bool unevenChanceActions = false, bool criticalNode = false)
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
            Decision d = new Decision(Name, Abbreviation, IsChance, PlayerNumber, PlayersToInform?.ToArray() ?? new byte[] { }, NumPossibleActions, DecisionByteCode, DecisionTypeCode, RepetitionsAfterFirst, PreevolvedStrategyFilename, InformationSetAbbreviations, AlwaysDoAction, UnevenChanceActions, CriticalNode) { PlayersToInformOfOccurrenceOnly = PlayersToInformOfOccurrenceOnly?.ToArray(), IsAlwaysPlayersLastDecision = IsAlwaysPlayersLastDecision, CanTerminateGame = CanTerminateGame, IncrementGameCacheItem = IncrementGameCacheItem, CustomByte = CustomByte, DeferNotificationOfPlayers = DeferNotificationOfPlayers, RequiresCustomInformationSetManipulation = RequiresCustomInformationSetManipulation, IsReversible = IsReversible, StoreActionInGameCacheItem = StoreActionInGameCacheItem, DistributedChanceDecision = DistributedChanceDecision, DistributableDistributorChanceInput = DistributableDistributorChanceInput, DistributorChanceDecision = DistributorChanceDecision, DistributorChanceInputDecision = DistributorChanceInputDecision, DistributorChanceInputDecisionMultiplier = DistributorChanceInputDecisionMultiplier, ProvidesPrivateInformationFor = ProvidesPrivateInformationFor, AlwaysTerminatesGame = AlwaysTerminatesGame,  IsChance = IsChance, IsContinuousAction = IsContinuousAction, Unroll_Parallelize = Unroll_Parallelize, Unroll_Parallelize_Identical = Unroll_Parallelize_Identical, WarmStartThroughIteration = WarmStartThroughIteration, WarmStartValue = WarmStartValue };
            return d;
        }

        public override string ToString()
        {
            return
                $"{Name} ({Abbreviation}) Player {PlayerNumber} ByteCode {DecisionByteCode} CustomByte {CustomByte} UnevenChanceActions {UnevenChanceActions} AlwaysDoAction {AlwaysDoAction}";
        }
    }

}
