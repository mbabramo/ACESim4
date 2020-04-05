#define SAFETYCHECKS

using ACESim.Util;
using ACESimBase.Util;
using System;
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
    public struct InformationSetLog
    {

        // must also set similar values ref GameHistory.
        public const int MaxInformationSetLoggingLength = 1200; // MUST equal MaxInformationSetLoggingLengthPerFullPlayer * NumFullPlayers + MaxInformationSetLoggingLengthPerPartialPlayer * NumPartialPlayers. 
        public const int MaxInformationSetLoggingLengthPerFullPlayer = 300;
        public const int MaxInformationSetLoggingLengthPerPartialPlayer = 30;

        public const int NumFullPlayers = 3; // includes main players and resolution player and any chance players that need full size information set
        public const int MaxNumPlayers = 13; // includes chance players that need a very limited information set
        public int NumPartialPlayers => MaxNumPlayers - NumFullPlayers;


        public int InformationSetLoggingIndex(byte playerIndex) => playerIndex <= NumFullPlayers ? MaxInformationSetLoggingLengthPerFullPlayer * playerIndex : MaxInformationSetLoggingLengthPerFullPlayer * NumFullPlayers + (playerIndex - NumFullPlayers) * MaxInformationSetLoggingLengthPerPartialPlayer;
        public int MaxInformationSetLoggingLengthForPlayer(byte playerIndex) => playerIndex < NumFullPlayers ? MaxInformationSetLoggingLengthPerFullPlayer : MaxInformationSetLoggingLengthPerPartialPlayer;

        public bool Initialized;

        private byte[] _LogStorage;
        public byte[] LogStorage
        {
            get
            {
                return _LogStorage;
            }
            set
            {
                if (_LogStorage != null)
                    throw new Exception("Already set");
                if (value.Length < MaxInformationSetLoggingLength)
                    throw new Exception("Invalid log length.");
                _LogStorage = value;
            }
        }

        public const byte InformationSetTerminator = 255;
        public const byte RemoveItemFromInformationSet = 254;

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            // Use the AddValue method to specify serialized values.
            byte[] informationSetLogs = LogStorage.ToArray();
            
            info.AddValue("informationSetLogs", informationSetLogs, typeof(byte[]));
        }

        // The special constructor is used to deserialize values.
        public InformationSetLog(SerializationInfo info, StreamingContext context)
        {
            byte[] informationSetLogs = (byte[])info.GetValue("informationSetLogs", typeof(byte[]));
            _LogStorage = informationSetLogs;
            Initialized = true;
        }

        public void Initialize()
        {
            if (LogStorage == null)
                LogStorage = new byte[MaxInformationSetLoggingLength]; // TODO: Use array pool. But only high priority if we are using PlayUnderlyingGame, since this will not otherwise be called in main algorithm. If doing this, must also use in GameProgress.CopyFieldInfo. Then, we must return the array. We don't necessarily need to catch all uses, just common ones, since unreturned array will still eventually be garbage collected and recycled. Key collection point would be GenerateReports_RandomPaths but also where we use GetGameState.
            if (MaxInformationSetLoggingLength != MaxInformationSetLoggingLengthPerFullPlayer * NumFullPlayers + MaxInformationSetLoggingLengthPerPartialPlayer * NumPartialPlayers)
                ThrowHelper.Throw("Lengths not set correctly.");
            for (byte p = 0; p < MaxNumPlayers; p++)
            {
                LogStorage[InformationSetLoggingIndex(p)] = InformationSetTerminator;
            }
            Initialized = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddToLog(byte information, byte followingDecisionIndex, byte playerIndex, string[] playerNames, List<ActionPoint> actionPoints)
        {
#if (SAFETYCHECKS)
            if (playerIndex >= MaxNumPlayers)
                ThrowHelper.Throw();
#endif

            int playerArrayIndex = InformationSetLoggingIndex(playerIndex);
            int nextPlayerArrayIndexMinusTwo = playerArrayIndex + MaxInformationSetLoggingLengthForPlayer(playerIndex) - 2;
            // advance to the end of the information set
            while (LogStorage[playerArrayIndex] != InformationSetTerminator)
                playerArrayIndex += 2;
#if (SAFETYCHECKS)
            if (playerArrayIndex >= nextPlayerArrayIndexMinusTwo)
                ThrowHelper.Throw("Internal error. Must increase size of information set.");
#endif
            // now record the information
            LogStorage[playerArrayIndex] = followingDecisionIndex; // we must record the decision
            playerArrayIndex++;
            LogStorage[playerArrayIndex] = information;
            playerArrayIndex++;
            LogStorage[playerArrayIndex] = InformationSetTerminator; // terminator
            if (GameProgressLogger.LoggingOn && GameProgressLogger.DetailedLogging)
            {
                GameProgressLogger.Log($"Adding information {information} following decision {followingDecisionIndex} ({actionPoints[followingDecisionIndex].Name}) for Player {playerIndex} ({playerNames[playerIndex]})");
                string playerInformation = GetPlayerInformationAtPointString(playerIndex, null);
                GameProgressLogger.Log($"Player {playerIndex} ({playerNames[playerIndex]}) info: {playerInformation}");
            }
        }

        public void RemoveLastItemsInLog(byte playerIndex, byte numItems)
        {
            for (byte i = 0; i < numItems; i++)
                RemoveLastItemInLog(playerIndex);
        }

        public void RemoveLastItemInLog(byte playerIndex)
        {
#if (SAFETYCHECKS)
            if (playerIndex >= MaxNumPlayers)
                ThrowHelper.Throw();
#endif
            // TabbedText.WriteLine($"Adding information {information} following decision {followingDecisionIndex} for Player number {playerIndex}"); 
            int playerArrayIndex = InformationSetLoggingIndex(playerIndex);
            // advance to the end of the information set
            while (LogStorage[playerArrayIndex] != InformationSetTerminator)
                playerArrayIndex += 2;
            playerArrayIndex -= 2;
            LogStorage[playerArrayIndex] = InformationSetTerminator;
        }

        public byte GetPlayerInformationItem(byte playerIndex, byte decisionIndex)
        {
#if (SAFETYCHECKS)
            if (playerIndex >= MaxNumPlayers)
                ThrowHelper.Throw();
#endif

            int playerArrayIndex = InformationSetLoggingIndex(playerIndex);
            while (LogStorage[playerArrayIndex] != InformationSetTerminator)
            {
                if (LogStorage[playerArrayIndex] == decisionIndex)
                {
                    playerArrayIndex++;
                    if (LogStorage[playerArrayIndex] != RemoveItemFromInformationSet)
                        return LogStorage[playerArrayIndex];
                    else
                        playerArrayIndex++;
                }
                else if (LogStorage[playerArrayIndex] == InformationSetTerminator)
                    break;
                else
                {
                    playerArrayIndex++;
                    playerArrayIndex++;
                }
            }
            return 0;
        }

        public void GetPlayerInformationAtPoint(byte playerIndex, byte? upToDecision, Span<byte> playerInfoBuffer)
        {
            int playerInfoBufferIndex = 0;
            if (playerIndex >= MaxNumPlayers)
            {
                // player has no information
                playerInfoBuffer[playerInfoBufferIndex] = InformationSetTerminator;
                return;
            }
            int playerArrayIndex = InformationSetLoggingIndex(playerIndex);
            while (LogStorage[playerArrayIndex] != InformationSetTerminator)
            {
                if (LogStorage[playerArrayIndex] >= upToDecision)
                    break;
                playerArrayIndex++;
                if (LogStorage[playerArrayIndex] == RemoveItemFromInformationSet)
                    playerInfoBufferIndex--; // delete an item
                else
                {
                    playerInfoBuffer[playerInfoBufferIndex] = LogStorage[playerArrayIndex];
                    playerInfoBufferIndex++;
                }
                playerArrayIndex++;
            }
            playerInfoBuffer[playerInfoBufferIndex] = InformationSetTerminator;
        }

        public string GetPlayerInformationAtPointString(byte playerIndex, byte? upToDecision)
        {
            Span<byte> playerInfoBuffer = stackalloc byte[MaxInformationSetLoggingLengthPerFullPlayer];
            GetPlayerInformationAtPoint(playerIndex, upToDecision, playerInfoBuffer);
            List<byte> informationSetList = ListExtensions.GetSpan255TerminatedAsList(playerInfoBuffer);
            return String.Join(",", informationSetList);
        }

        public List<byte> GetPlayerInformationUpToNow(byte playerIndex)
        {
            Span<byte> playerInfoBuffer = stackalloc byte[MaxInformationSetLoggingLengthPerFullPlayer];
            GetPlayerInformationAtPoint(playerIndex, null, playerInfoBuffer);
            return ListExtensions.GetSpan255TerminatedAsList(playerInfoBuffer);
        }

        public byte CountItemsInInformationSet(byte playerIndex)
        {
#if (SAFETYCHECKS)
            if (playerIndex >= MaxNumPlayers)
                ThrowHelper.Throw();
#endif
            byte b = 0;
            int playerArrayIndex = InformationSetLoggingIndex(playerIndex);
            while (LogStorage[playerArrayIndex] != InformationSetTerminator)
            {
                playerArrayIndex++; // skip the decision code
                if (LogStorage[playerArrayIndex] == RemoveItemFromInformationSet)
                    b--;
                else
                    b++;
                playerArrayIndex++; // now move past the information
            }
            return b;
        }

        public void AddRemovalToInformationSetLog(byte followingDecisionIndex, byte playerIndex, string[] playerNames, List<ActionPoint> actionPoints)
        {
#if (SAFETYCHECKS)
            if (playerIndex >= MaxNumPlayers)
                ThrowHelper.Throw();
#endif

            AddToLog(RemoveItemFromInformationSet, followingDecisionIndex, playerIndex, playerNames, actionPoints);
        }
    }
}
