using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public unsafe struct InformationSetLog
    {
        public const int MaxInformationSetLoggingLength = 690; // MUST equal MaxInformationSetLoggingLengthPerFullPlayer * NumFullPlayers + MaxInformationSetLoggingLengthPerPartialPlayer * NumPartialPlayers. 
        public const int MaxInformationSetLoggingLengthPerFullPlayer = 200;
        public const int MaxInformationSetLoggingLengthPerPartialPlayer = 30;


        public const int NumFullPlayers = 3; // includes main players and resolution player and any chance players that need full size information set
        public const int MaxNumPlayers = 6; // includes chance players that need a very limited information set
        public int NumPartialPlayers => MaxNumPlayers - NumFullPlayers;


        public int InformationSetLoggingIndex(byte playerIndex) => playerIndex <= NumFullPlayers ? MaxInformationSetLoggingLengthPerFullPlayer * playerIndex : MaxInformationSetLoggingLengthPerFullPlayer * NumFullPlayers + (playerIndex - NumFullPlayers) * MaxInformationSetLoggingLengthPerPartialPlayer;
        public int MaxInformationSetLoggingLengthForPlayer(byte playerIndex) => playerIndex < NumFullPlayers ? MaxInformationSetLoggingLengthPerFullPlayer : MaxInformationSetLoggingLengthPerPartialPlayer;

        public fixed byte InformationSetLogs[MaxInformationSetLoggingLength]; // a buffer for each player, terminated by 255. This includes removal characters, so that we can replay the history of an information set.

        public const byte InformationSetTerminator = 255;
        public const byte RemoveItemFromInformationSet = 254;

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            // Use the AddValue method to specify serialized values.
            byte[] informationSetLogs = new byte[MaxInformationSetLoggingLength];
            fixed (byte* ptr = InformationSetLogs)
                for (int b = 0; b < MaxInformationSetLoggingLength; b++)
                    informationSetLogs[b] = *(ptr + b);
            
            info.AddValue("informationSetLogs", informationSetLogs, typeof(byte[]));
        }

        // The special constructor is used to deserialize values.
        public InformationSetLog(SerializationInfo info, StreamingContext context)
        {
            byte[] informationSetLogs = (byte[])info.GetValue("informationSetLogs", typeof(byte[]));
            fixed (byte* ptr = InformationSetLogs)
                for (int b = 0; b < MaxInformationSetLoggingLength; b++)
                    *(ptr + b) = informationSetLogs[b];
        }

        public void Initialize()
        {
            if (MaxInformationSetLoggingLength != MaxInformationSetLoggingLengthPerFullPlayer * NumFullPlayers + MaxInformationSetLoggingLengthPerPartialPlayer * NumPartialPlayers)
                throw new Exception("Lengths not set correctly.");
            fixed (byte* logPtr = InformationSetLogs)
                for (byte p = 0; p < MaxNumPlayers; p++)
                {
                    *(logPtr + InformationSetLoggingIndex(p)) = InformationSetTerminator;
                }
        }


        public void AddToLog(byte information, byte followingDecisionIndex, byte playerIndex)
        {
            if (playerIndex >= MaxNumPlayers)
                throw new NotImplementedException();
            fixed (byte* informationSetsLogPtr = InformationSetLogs)
            {
                // Console.WriteLine($"Adding information {information} following decision {followingDecisionIndex} for Player number {playerIndex}"); 
                byte* playerPointer = informationSetsLogPtr + InformationSetLoggingIndex(playerIndex);
                byte* nextPlayerPointer = playerPointer + MaxInformationSetLoggingLengthForPlayer(playerIndex);
                // advance to the end of the information set
                while (*playerPointer != InformationSetTerminator)
                    playerPointer += 2;
                // now record the information
                *playerPointer = followingDecisionIndex; // we must record the decision
                playerPointer++;
                *playerPointer = information;
                playerPointer++;
                *playerPointer = InformationSetTerminator; // terminator
                if (playerPointer >= nextPlayerPointer)
                    throw new Exception("Internal error. Must increase size of information set.");
            }
        }

        public void RemoveLastItemsInLog(byte playerIndex, byte numItems)
        {
            for (byte i = 0; i < numItems; i++)
                RemoveLastItemInLog(playerIndex);
        }

        public void RemoveLastItemInLog(byte playerIndex)
        {
            if (playerIndex >= MaxNumPlayers)
                throw new NotImplementedException();
            fixed (byte* informationSetsLogPtr = InformationSetLogs)
            {
                // Console.WriteLine($"Adding information {information} following decision {followingDecisionIndex} for Player number {playerIndex}"); 
                byte* playerPointer = informationSetsLogPtr + InformationSetLoggingIndex(playerIndex);
                // advance to the end of the information set
                while (*playerPointer != InformationSetTerminator)
                    playerPointer += 2;
                playerPointer -= 2;
                *playerPointer = InformationSetTerminator;
            }
        }

        public unsafe byte GetPlayerInformationItem(byte playerIndex, byte decisionIndex)
        {
            if (playerIndex >= MaxNumPlayers)
                throw new NotImplementedException();
            fixed (byte* informationSetsPtr = InformationSetLogs)
            {
                byte* playerPointer = informationSetsPtr + InformationSetLoggingIndex(playerIndex);
                while (*playerPointer != InformationSetTerminator)
                {
                    if (*playerPointer == decisionIndex)
                    {
                        playerPointer++;
                        if (*playerPointer != RemoveItemFromInformationSet)
                            return *playerPointer;
                        else
                            playerPointer++;
                    }
                    else if (*playerPointer == InformationSetTerminator)
                        break;
                    else
                    {
                        playerPointer++;
                        playerPointer++;
                    }
                }
                return 0;
            }
        }
        public unsafe void GetPlayerInformationAtPoint(byte playerIndex, byte? upToDecision, byte* playerInfoBuffer)
        {
            if (playerIndex >= MaxNumPlayers)
            {
                // player has no information
                *playerInfoBuffer = InformationSetTerminator;
                return;
            }
            fixed (byte* informationSetsPtr = InformationSetLogs)
            {
                byte* playerPointer = informationSetsPtr + InformationSetLoggingIndex(playerIndex);
                while (*playerPointer != InformationSetTerminator)
                {
                    if (*playerPointer >= upToDecision)
                        break;
                    playerPointer++;
                    if (*playerPointer == RemoveItemFromInformationSet)
                        playerInfoBuffer--; // delete an item
                    else
                    {
                        *playerInfoBuffer = *playerPointer;
                        playerInfoBuffer++;
                    }
                    playerPointer++;
                }
                *playerInfoBuffer = InformationSetTerminator;
            }
        }

        public unsafe string GetPlayerInformationAtPointString(byte playerIndex, byte? upToDecision)
        {
            byte* playerInfoBuffer = stackalloc byte[MaxInformationSetLoggingLengthPerFullPlayer];
            GetPlayerInformationAtPoint(playerIndex, upToDecision, playerInfoBuffer);
            List<byte> informationSetList = ListExtensions.GetPointerAsList_255Terminated(playerInfoBuffer);
            return String.Join(",", informationSetList);
        }

        public byte CountItemsInInformationSet(byte playerIndex)
        {
            if (playerIndex >= MaxNumPlayers)
                throw new NotImplementedException();
            byte b = 0;
            fixed (byte* informationSetsPtr = InformationSetLogs)
            {
                byte* ptr = informationSetsPtr + InformationSetLoggingIndex(playerIndex);
                while (*ptr != InformationSetTerminator)
                {
                    ptr++; // skip the decision code
                    if (*ptr == RemoveItemFromInformationSet)
                        b--;
                    else
                        b++;
                    ptr++; // now move past the information
                }
            }
            return b;
        }

        public void AddRemovalToInformationSetLog(byte followingDecisionIndex, byte playerIndex)
        {
            if (playerIndex >= MaxNumPlayers)
                throw new NotImplementedException();
            fixed (byte* informationSetsLogPtr = InformationSetLogs)
            {
                AddToLog(RemoveItemFromInformationSet, followingDecisionIndex, playerIndex);
            }
        }
    }
}
