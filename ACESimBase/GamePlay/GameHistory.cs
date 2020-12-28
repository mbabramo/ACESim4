#define SAFETYCHECKS

using ACESim.Util;
using ACESimBase.Util;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    [Serializable]
    public ref struct GameHistory
    {
        #region Construction

        // We use a ref struct here because this makes a difference in performance, allowing GameHistory to be allocated on the stack. Currently, we fix the number of players, maximum size of different players' information sets, etc. in the GameHistory (which means that we need to change the code whenever we change games). We distinguish between full and partial players because this also produces a significant performance boost. 

        public const int CacheLength = 25; // the game and game definition can use the cache to store information. This is helpful when the game player is simulating the game without playing the underlying game. The game definition may, for example, need to be able to figure out which decision is next.
        public const byte Cache_SubdivisionAggregationIndex = 0; // Use this cache entry to aggregate subdivision decisions. Thus, do NOT use it for any other purpose.

        public const byte InformationSetTerminator = 255;

        // TODO: Consider replacing const ints with something variable determined by the game. GameHistory would then be initialized with a struct including the relevant constants. This might save space, though it would increase the number of calculations.
        public const int MaxNumActions = 50;
        public const int MaxNumPlayers = 18; // includes chance players that need a very limited information set
        public const int MaxDeferredDecisionIndicesLength = 10;
        public const int SizeInBits_BitArrayForInformationSetMembership = GameHistory.MaxNumActions * MaxNumPlayers;
        public const int SizeInBytes_BitArrayForInformationSetMembership = SizeInBits_BitArrayForInformationSetMembership / 8 + (SizeInBits_BitArrayForInformationSetMembership % 8 == 0 ? 0 : 1);
        public const int SizeInBytes_BitArrayForDecisionsDeferred = GameHistory.MaxNumActions / 8 + (MaxNumActions % 8 == 0 ? 0 : 1);
        public const int TotalBufferSize = GameHistory.MaxNumActions + GameHistory.MaxNumActions + CacheLength + GameHistory.SizeInBytes_BitArrayForInformationSetMembership + GameHistory.SizeInBytes_BitArrayForDecisionsDeferred;
        public const int MaxInformationSetLength = 40; // used by code that creates Span to hold information set

        public bool Initialized;
        public bool Complete;
        public bool CompleteExceptInverted;
        public byte NextActionsAndDecisionsHistoryIndex;
        public byte LastDecisionIndexAdded;
        public byte HighestCacheIndex; // not necessarily sequentially added

        public Span<byte> Buffer;
        public Span<byte> ActionsHistory;
        public Span<byte> DecisionIndicesHistory;
        public Span<byte> Cache;
        public Span<byte> InformationSetMembership;
        public Span<byte> DecisionsDeferred;
#if SAFETYCHECKS
        public int CreatingThreadID;
#endif

        // The following are used to defer adding information to a player information set.
        public bool PreviousNotificationDeferred;
        public byte DeferredAction;
        public byte DeferredPlayerNumber;
        public byte[] DeferredPlayersToInform; // NOTE: We can leave this as an array because it is set in game definition and not changed.

        // Information set structure. We have an information set buffer for each player. We need to be able to remove information from the information set for a player, but still to remember that it was there as of a particular point in time, so that we can figure out what the information set was as of a particular decision. (This is needed for reconstructing the game play.) We thus store information in pairs. The first byte consists of the decision byte code after which we are making changes. The second byte either consists of an item to add, or 254, indicating that we are removing an item from the information set. All of this is internal. When we get the information set, we get it as of a certain point, and thus we skip decision byte codes and automatically process deletions. 


        public bool Matches(GameHistory other)
        {
            var basics = Initialized == other.Initialized && Complete == other.Complete && CompleteExceptInverted == other.CompleteExceptInverted  && NextActionsAndDecisionsHistoryIndex == other.NextActionsAndDecisionsHistoryIndex && LastDecisionIndexAdded == other.LastDecisionIndexAdded && PreviousNotificationDeferred == other.PreviousNotificationDeferred && DeferredAction == other.DeferredAction && DeferredPlayerNumber == other.DeferredPlayerNumber && ((DeferredPlayersToInform == null && other.DeferredPlayersToInform == null) || DeferredPlayersToInform.SequenceEqual(other.DeferredPlayersToInform));
            if (!basics)
                return false;
            if (!GetActionsAsList().SequenceEqual(other.GetActionsAsList())) // will ignore info after items in span
                return false;
            if (!Cache.SequenceEqual(other.Cache))
                return false;
            if (GetInformationSetsString() != other.GetInformationSetsString())
                return false;
            return true;
        }

        public void Initialize(bool createArraysForSpans = true)
        {
            if (Initialized)
            {
                ResetCache();
                return;
            }
            Initialize_Helper(createArraysForSpans);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void VerifyThread()
        {
#if SAFETYCHECKS
            // We verify that the thread has the same. Note that GameHistory is stack-only, so it will never cross threads.
            // The real question is thus whether a GameProgress is used to yield multiple different GameHistories that are
            // then saved back to the same GameProgress from different threads, causing a difficult to identify bug.
            // If we use deep copying of the GameHistory (in GameHistoryStorable), then the CreatingThreadID is reset and
            // there is no problem. But when using shallow copies, we need to be careful.
            if (!IsEmpty && CreatingThreadID != System.Threading.Thread.CurrentThread.ManagedThreadId)
                throw new Exception();
#endif
        }

        private void Initialize_Helper(bool createArraysForSpans)
        {
            if (createArraysForSpans)
                CreateArrayForSpans(true);
            Initialized = true;
            LastDecisionIndexAdded = 255;
            NextActionsAndDecisionsHistoryIndex = 0;
            for (int i = 0; i < GameHistory.CacheLength; i++)
                Cache[i] = 0;
            for (int i = 0; i < GameHistory.SizeInBytes_BitArrayForInformationSetMembership; i++)
                InformationSetMembership[i] = 0;
            for (int i = 0; i < GameHistory.SizeInBytes_BitArrayForDecisionsDeferred; i++)
                DecisionsDeferred[i] = 0;
        }

        /// <summary>
        /// Copies the temporary GameHistory object to a GameHistoryStorable. The result can thus be used in a different thread.
        /// </summary>
        /// <returns></returns>
        public GameHistoryStorable DeepCopyToStorable()
        {
            var result = new GameHistoryStorable(this);

            return result;
        }

        public void CreateArrayForSpans(bool onlyIfNeeded)
        {
            if (onlyIfNeeded && Buffer.Length > 0)
                return;
            bool usePool = true;
            if (usePool)
                Buffer = ArrayPool<byte>.Shared.Rent(GameHistory.TotalBufferSize);
            else
                Buffer = new byte[GameHistory.TotalBufferSize]; 
            SliceBuffer();
            ResetCache();
#if SAFETYCHECKS
            CreatingThreadID = System.Threading.Thread.CurrentThread.ManagedThreadId;
#endif
        }

        public void SliceBuffer()
        {
            ActionsHistory = Buffer.Slice(0, GameHistory.MaxNumActions);
            DecisionIndicesHistory = Buffer.Slice(GameHistory.MaxNumActions, GameHistory.MaxNumActions);
            Cache = Buffer.Slice(GameHistory.MaxNumActions + GameHistory.MaxNumActions, GameHistory.CacheLength);
            InformationSetMembership = Buffer.Slice(GameHistory.MaxNumActions + GameHistory.MaxNumActions + GameHistory.CacheLength, GameHistory.SizeInBytes_BitArrayForInformationSetMembership);
            DecisionsDeferred = Buffer.Slice(GameHistory.MaxNumActions + GameHistory.MaxNumActions + GameHistory.CacheLength + GameHistory.SizeInBytes_BitArrayForInformationSetMembership, GameHistory.SizeInBytes_BitArrayForDecisionsDeferred);
        }

        public bool IsEmpty => ActionsHistory.Length == 0;

        public GameHistory DeepCopy()
        {
            // This is the critical point for allocation of arrays for history. We might wish to use pooling if this is a time sink.
            GameHistory result = new GameHistory()
            {
                Complete = Complete,
                CompleteExceptInverted = CompleteExceptInverted,
                NextActionsAndDecisionsHistoryIndex = NextActionsAndDecisionsHistoryIndex,
                Initialized = Initialized,
                PreviousNotificationDeferred = PreviousNotificationDeferred,
                DeferredAction = DeferredAction,
                DeferredPlayerNumber = DeferredPlayerNumber,
                DeferredPlayersToInform = DeferredPlayersToInform, // this does not need to be duplicated because it is set in gamedefinition and not changed
                LastDecisionIndexAdded = LastDecisionIndexAdded,
            };
            if (!IsEmpty)
            {
                result.CreateArrayForSpans(false);
                int maxNumActions = Math.Min((int)GameHistory.MaxNumActions, (int) NextActionsAndDecisionsHistoryIndex);
                for (int i = 0; i < maxNumActions; i++)
                    result.ActionsHistory[i] = ActionsHistory[i];
                for (int i = 0; i < maxNumActions; i++)
                    result.DecisionIndicesHistory[i] = DecisionIndicesHistory[i];
                for (int i = 0; i <= HighestCacheIndex; i++)
                    result.Cache[i] = Cache[i];
                result.VerifyThread();
                for (int i = 0; i < SizeInBytes_BitArrayForInformationSetMembership; i++)
                    result.InformationSetMembership[i] = InformationSetMembership[i];
                for (int i = 0; i < GameHistory.SizeInBytes_BitArrayForDecisionsDeferred; i++)
                    result.DecisionsDeferred[i] = DecisionsDeferred[i];
            }
            return result;
        }

        #endregion

        #region Strings

        public override string ToString()
        {
            return $"Actions {String.Join(",", GetActionsAsList())} cache {CacheString()} {GetInformationSetsString()} PreviousNotificationDeferred {PreviousNotificationDeferred} DeferredAction {DeferredAction} DeferredPlayerNumber {DeferredPlayerNumber}";
        }

        public string CacheString()
        {
            string cacheString = "";
            for (int i = 0; i <= HighestCacheIndex; i++)
            {
                cacheString += Cache[i];
                if (i % 5 == 4)
                    cacheString += " ";
                else
                    cacheString += ",";
            }
            return cacheString;
        }

        public string GetInformationSetsString()
        {
            string informationSetsString = "";
            for (byte i = 0; i < MaxNumPlayers; i++)
                informationSetsString += $"Player {i} Information: {GetCurrentPlayerInformationString(i)} ";
            return informationSetsString;
        }

        #endregion

        #region Cache

        public void ResetCache()
        {
            for (int i = 0; i < Cache.Length; i++)
                Cache[i] = 0;
        }

        public void IncrementItemAtCacheIndex(byte cacheIndexToIncrement, byte incrementBy = 1)
        {
            // TabbedText.WriteLine($"Increment cache for {cacheIndexToIncrement}");
            Cache[(byte) cacheIndexToIncrement] += incrementBy;
            if (cacheIndexToIncrement > HighestCacheIndex)
                HighestCacheIndex = cacheIndexToIncrement;
        }

        public void DecrementItemAtCacheIndex(byte cacheIndexToDecrement, byte decrementBy = 1)
        {
            // TabbedText.WriteLine($"Decrement cache for {cacheIndexToIncrement}");
            byte currentValue = Cache[(byte)cacheIndexToDecrement];
#if SAFETYCHECKS
            if (currentValue == 0)
                ThrowHelper.Throw();
#endif
            Cache[(byte) cacheIndexToDecrement] = (byte) (currentValue - (byte)decrementBy);
            if (cacheIndexToDecrement > HighestCacheIndex)
                HighestCacheIndex = cacheIndexToDecrement;
        }

        public byte GetCacheItemAtIndex(byte cacheIndexToReset)
        {
            return Cache[(byte) cacheIndexToReset];
        }

        public void SetCacheItemAtIndex(byte cacheIndexToReset, byte newValue)
        {
            // TabbedText.WriteLine($"Set cache for {cacheIndexToReset} to {newValue}"); 
#if SAFETYCHECKS
            if (cacheIndexToReset >= CacheLength)
                ThrowHelper.Throw();
#endif
            Cache[(byte) cacheIndexToReset] = newValue;
            if (cacheIndexToReset > HighestCacheIndex)
                HighestCacheIndex = cacheIndexToReset;
        }

        #endregion

        #region History

        public void AddToHistory(byte decisionByteCode, byte decisionIndex, byte playerIndex, byte action, byte numPossibleActions, byte[] playersToInform, byte[] cacheIndicesToIncrement, byte? storeActionInCacheIndex, GameProgress gameProgress, bool deferNotification, bool delayPreviousDeferredNotification)
        {
            // Debug.WriteLine($"Add to history {decisionByteCode} for player {playerIndex} action {action} of {numPossibleActions}");
            RecordAction(action, decisionIndex, deferNotification, playersToInform);
            if (gameProgress != null && gameProgress.FullHistoryRequired)
                gameProgress.GameFullHistory = gameProgress.GameFullHistory.AddToHistory(decisionByteCode, decisionIndex, playerIndex, action, numPossibleActions);
            LastDecisionIndexAdded = decisionIndex;
            if (!delayPreviousDeferredNotification)
            {
                if (PreviousNotificationDeferred && DeferredPlayersToInform != null)
                    AddToInformationSetLog(DeferredAction, decisionIndex, DeferredPlayerNumber, DeferredPlayersToInform, gameProgress); /* we use the current decision index, not the decision from which it was deferred -- this is important in setting the information set correctly when we want all information that a player has up to some point */
                PreviousNotificationDeferred = deferNotification;
            }
            if (deferNotification)
            {
                DeferredAction = action;
                DeferredPlayerNumber = playerIndex;
                DeferredPlayersToInform = playersToInform;
            }
            else if (playersToInform != null && playersToInform.Length > 0)
            {
                AddToInformationSetLog(action, decisionIndex, playerIndex, playersToInform, gameProgress); 
            }
            if (cacheIndicesToIncrement != null && cacheIndicesToIncrement.Length > 0)
                foreach (byte cacheIndex in cacheIndicesToIncrement)
                    IncrementItemAtCacheIndex(cacheIndex);
            if (storeActionInCacheIndex != null)
                SetCacheItemAtIndex((byte) storeActionInCacheIndex, action);
        }

        private void RecordAction(byte action, byte decisionIndex, bool decisionIsDeferred, byte[] playersToInform)
        {
#if SAFETYCHECKS
            if (action == 0)
                ThrowHelper.Throw("Invalid action.");
#endif
            ActionsHistory[NextActionsAndDecisionsHistoryIndex] = action;
            DecisionIndicesHistory[NextActionsAndDecisionsHistoryIndex] = decisionIndex;
            if (decisionIsDeferred)
                SpanBitArray.Set(DecisionsDeferred, NextActionsAndDecisionsHistoryIndex, decisionIsDeferred);
            if (playersToInform != null)
                foreach (byte playerToInform in playersToInform)
                    SpanBitArray.Set(InformationSetMembership, playerToInform * MaxNumActions + NextActionsAndDecisionsHistoryIndex, true);
            NextActionsAndDecisionsHistoryIndex++;
#if SAFETYCHECKS
            if (NextActionsAndDecisionsHistoryIndex >= GameHistory.MaxNumActions)
                ThrowHelper.Throw("Internal error. Must increase MaxNumActions.");
#endif
        }

        public bool DecessionIsDeferred_FromNextActionsAndDecisionsHistoryIndex(byte nextActionsAndDecisionsHistoryIndex) => SpanBitArray.Get(DecisionsDeferred, nextActionsAndDecisionsHistoryIndex);

        public List<byte> GetActionsAsList()
        {
            List<byte> actions = new List<byte>();
            for (int i = 0; i < NextActionsAndDecisionsHistoryIndex; i++)
                actions.Add(ActionsHistory[i]);
            return actions;
        }
        public string GetActionsAsListString()
        {
            return String.Join(",", GetActionsAsList());
        }

        public List<byte> GetDecisionsAsList()
        {
            List<byte> actions = new List<byte>();
            for (int i = 0; i < NextActionsAndDecisionsHistoryIndex; i++)
                actions.Add(DecisionIndicesHistory[i]);
            return actions;
        }

        public void MarkComplete(GameProgress gameProgress = null)
        {
            Complete = true;
            if (gameProgress != null && gameProgress.FullHistoryRequired && !gameProgress.GameFullHistory.IsComplete())
                gameProgress.GameFullHistory.MarkComplete();
        }

        public bool IsComplete()
        {
            return Complete;
        }

#endregion

#region Player information sets

        private void AddToInformationSetLog(byte information, byte informationIsKnownFollowingDecisionIndex, byte playerIndex, byte[] playersToInform, GameProgress gameProgress)
        {
            if (playersToInform == null)
                return;
            foreach (byte playerToInformIndex in playersToInform)
            {
                if (gameProgress != null && gameProgress.FullHistoryRequired)
                    gameProgress.GameFullHistory.InformationSetLog.AddToLog(information, informationIsKnownFollowingDecisionIndex, playerToInformIndex, gameProgress.GameDefinition.PlayerNames, gameProgress.GameDefinition.DecisionPointsExecutionOrder);
            }
            if (GameProgressLogger.LoggingOn && GameProgressLogger.DetailedLogging)
            {
                GameProgressLogger.Log($"player {playerIndex} informing {String.Join(", ", playersToInform)} info {information} following {informationIsKnownFollowingDecisionIndex}");
                if (gameProgress != null)
                    foreach (byte playerToInformIndex in playersToInform)
                    {
                        GameProgressLogger.Log($"Player {playerToInformIndex} ({gameProgress.GameDefinition.PlayerNames[playerToInformIndex]}) information: {GetCurrentPlayerInformationString(playerToInformIndex)}");
                    }
            }
        }

        public byte[] GetCurrentInformationSetForPlayer_Array(byte playerIndex, bool addInformationSetTerminator) => GetCurrentInformationSetForPlayer(playerIndex, addInformationSetTerminator).ToArray();


        public List<byte> GetCurrentInformationSetForPlayer(byte playerIndex, bool addInformationSetTerminator)
        {
            List<byte> info = new List<byte>();
            int firstPlayerIndexAction = playerIndex * MaxNumActions;
            for (int i = 0; i < NextActionsAndDecisionsHistoryIndex; i++)
            {
                bool isMember = SpanBitArray.Get(InformationSetMembership, firstPlayerIndexAction + i);
                if (isMember)
                {
                    bool isLastAndDeferred = i == NextActionsAndDecisionsHistoryIndex - 1 && SpanBitArray.Get(DecisionsDeferred, NextActionsAndDecisionsHistoryIndex - 1);
                    if (!isLastAndDeferred)
                        info.Add(ActionsHistory[i]);
                }
            }
            if (addInformationSetTerminator)
                info.Add(InformationSetTerminator);
            return info;
        }

        public List<(byte decisionIndex, byte information)> GetLabeledCurrentInformationSetForPlayer(byte playerIndex)
        {
            List<(byte decisionIndex, byte information)> info = new List<(byte decisionIndex, byte information)>();
            int firstPlayerIndexAction = playerIndex * MaxNumActions;
            for (int i = 0; i < NextActionsAndDecisionsHistoryIndex; i++)
            {
                bool isMember = SpanBitArray.Get(InformationSetMembership, firstPlayerIndexAction + i);
                if (isMember)
                {
                    bool isLastAndDeferred = i == NextActionsAndDecisionsHistoryIndex - 1 && SpanBitArray.Get(DecisionsDeferred, NextActionsAndDecisionsHistoryIndex - 1);
                    if (!isLastAndDeferred)
                        info.Add((DecisionIndicesHistory[i], ActionsHistory[i]));
                }
            }
            return info;
        }

        public void GetCurrentInformationSetForPlayer(byte playerIndex, Span<byte> playerInfo)
        {
            GetCurrentInformationSetForPlayer(playerIndex, NextActionsAndDecisionsHistoryIndex, ActionsHistory, InformationSetMembership, DecisionsDeferred, playerInfo);
        }

        public static void GetCurrentInformationSetForPlayer(byte playerIndex, byte nextActionsAndDecisionsHistoryIndex, Span<byte> actions, Span<byte> informationSetMembership, Span<byte> decisionsDeferred, Span<byte> playerInfo)
        {
            byte playerInfoIndex = 0;
            int firstPlayerIndexAction = playerIndex * MaxNumActions;
            for (int i = 0; i < nextActionsAndDecisionsHistoryIndex; i++)
            {
                bool isMember = SpanBitArray.Get(informationSetMembership, firstPlayerIndexAction + i);
                if (isMember)
                {
                    bool isLastAndDeferred = i == nextActionsAndDecisionsHistoryIndex - 1 && SpanBitArray.Get(decisionsDeferred, nextActionsAndDecisionsHistoryIndex - 1);
                    if (!isLastAndDeferred)
                        playerInfo[playerInfoIndex++] = actions[i];
                }
            }
            playerInfo[playerInfoIndex] = InformationSetTerminator;
        }


        public string GetCurrentPlayerInformationString(byte playerIndex)
        {
            List<byte> informationSetList = GetCurrentInformationSetForPlayer(playerIndex, false);
            return String.Join(",", informationSetList);
        }

        public void ReverseAdditionsToInformationSet(IEnumerable<byte> playerIndices, GameProgress gameProgress = null)
        {
            ActionsHistory[NextActionsAndDecisionsHistoryIndex] = 0;
            DecisionIndicesHistory[NextActionsAndDecisionsHistoryIndex] = 0;
            SpanBitArray.Set(DecisionsDeferred, NextActionsAndDecisionsHistoryIndex, false);

            foreach (byte playerIndex in playerIndices)
            {
                SpanBitArray.Set(InformationSetMembership, playerIndex * MaxNumActions + NextActionsAndDecisionsHistoryIndex, false);
            }

            foreach (byte playerIndex in playerIndices)
            {
                if (gameProgress != null && gameProgress.FullHistoryRequired)
                    gameProgress.GameFullHistory.InformationSetLog.RemoveLastItemInLog(playerIndex);
            }
        }

        public List<InformationSetHistory> GetInformationSetHistories(List<Decision> decisions)
        {
            List<InformationSetHistory> informationSetHistories = new List<InformationSetHistory>();
            for (int i = 0; i < NextActionsAndDecisionsHistoryIndex; i++)
            {
                byte decisionIndex = DecisionIndicesHistory[i];
                byte actionChosen = ActionsHistory[i];
                Decision d = decisions[decisionIndex];
                List<(byte decisionIndex, byte information)> labeledInformationSet = GetLabeledCurrentInformationSetForPlayer(d.PlayerIndex).TakeWhile(x =>
                {
                    if (x.decisionIndex >= decisionIndex)
                        return false;
                    if (x.decisionIndex == decisionIndex - 1 && decisions[decisionIndex - 1].DeferNotificationOfPlayers)
                        return false;
                    return true;
                }
                ).ToList();
                var informationSetUnlabeled = labeledInformationSet.Select(x => x.information).ToList();
                informationSetUnlabeled.Add(InformationSetTerminator);
                byte[] informationSet = informationSetUnlabeled.ToArray();
                informationSetHistories.Add(new InformationSetHistory(informationSet, labeledInformationSet, d.PlayerIndex, d.DecisionByteCode, decisionIndex, actionChosen, d.NumPossibleActions));
            }
            return informationSetHistories;
        }

        public string GetInformationSetHistoryItemsString(List<Decision> decisions) => String.Join(",", GetInformationSetHistoryItemsStrings(decisions));

        public List<string> GetInformationSetHistoryItemsStrings(List<Decision> decisions)
        {
            List<string> informationSetHistoryStrings = new List<string>();
            foreach (InformationSetHistory informationSetHistory in GetInformationSetHistories(decisions))
            {
                informationSetHistoryStrings.Add(informationSetHistory.ToString());
            }
            return informationSetHistoryStrings;
        }

        #endregion

        #region Decision paths

        /// <summary>
        /// When called on a complete game, this returns the next decision path to take. 
        /// For example, if there are three decisions with three actions each, then after (1, 1, 1), it would return (1, 1, 2), then (1, 1, 3), then (1, 2). 
        /// Note that in this example there may be further decisions after (1, 2). 
        /// If called on (3, 3, 3), it will throw an Exception.
        /// </summary>
        public void GetNextDecisionPath(GameDefinition gameDefinition, Span<byte> nextDecisionPath)
        {
#if (SAFETYCHECKS)
            if (!IsComplete())
                ThrowHelper.Throw("Can get next path to try only on a completed game.");
#endif
            // We need to find the last decision made where there was another action that could have been taken.
            int? lastDecisionInNextPath = GetIndexOfLastDecisionWithAnotherAction(gameDefinition) ?? -1; // negative number symbolizes that there is nothing else to do
            int indexInNewDecisionPath = 0, indexInCurrentActions = 0;
            byte[] currentActions = new byte[GameHistory.MaxNumActions];
            GetActionsWithBlanksForSkippedDecisions(currentActions);
            //var currentActionsList = Util.ListExtensions.GetPointerAsList_255Terminated(currentActions);
            while (indexInNewDecisionPath <= lastDecisionInNextPath)
            {
                byte currentAction = currentActions[indexInCurrentActions];
                if (currentAction == 0)
                {
                    indexInCurrentActions++;
                    lastDecisionInNextPath--;
                }
                else
                {
#if (SAFETYCHECKS)
                    bool another = currentAction != GameFullHistory.HistoryTerminator;
                    if (!another)
                        ThrowHelper.Throw("Internal error. Expected another decision to exist.");
#endif
                    if (indexInNewDecisionPath == lastDecisionInNextPath)
                        nextDecisionPath[indexInNewDecisionPath] =
                            (byte)(currentAction +
                                   (byte)1); // this is the decision where we need to try the next path
                    else
                        nextDecisionPath[indexInNewDecisionPath] = currentAction; // we're still on the same path

                    indexInCurrentActions++;
                    indexInNewDecisionPath++;
                }
            }
            nextDecisionPath[indexInNewDecisionPath] = GameFullHistory.HistoryTerminator;
            //var nextDecisionPath2 = Util.ListExtensions.GetPointerAsList_255Terminated(nextDecisionPath);
        }

        private int? GetIndexOfLastDecisionWithAnotherAction(GameDefinition gameDefinition)
        {
            int? lastDecisionWithAnotherAction = null;

            for (int i = 0; i < NextActionsAndDecisionsHistoryIndex; i++)
            {
                byte action = ActionsHistory[i];
                byte decisionIndex = DecisionIndicesHistory[i];
                if (gameDefinition.DecisionsExecutionOrder[decisionIndex].NumPossibleActions > action)
                {
                    lastDecisionWithAnotherAction = decisionIndex;
                    break;
                }
            }
#if (SAFETYCHECKS)
            if (lastDecisionWithAnotherAction == null)
                ThrowHelper.Throw("No more decision paths to take."); // indicates that there are no more decisions to take
#endif
            return lastDecisionWithAnotherAction;
        }
        private void GetActionsWithBlanksForSkippedDecisions(Span<byte> actions)
        {
            int d = 0;
            for (int i = 0; i < NextActionsAndDecisionsHistoryIndex; i++)
            {
                byte action = ActionsHistory[i];
                byte decisionIndex = DecisionIndicesHistory[i];
                while (d != decisionIndex)
                    actions[d++] = 0;
                actions[d++] = action;
            }
            actions[d] = GameFullHistory.HistoryTerminator;
        }

        #endregion

    }
}
