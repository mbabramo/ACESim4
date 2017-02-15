using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class GameHistory
    {
        const int MaxLength = 150;
        const byte Complete = 254;
        const byte Incomplete = 255;

        public byte? NumPlayers { get; set; } = null;
        public byte[] History = new byte[MaxLength];
        public short LastIndexAddedToHistory = 0;
        public int NumberDecisions => (LastIndexAddedToHistory - 1) / 3;
        public byte[] InformationSets = new byte[MaxLength];
        public short LastIndexAddedToInformationSets = -1;

        public GameHistory()
        {
            Initialize();
        }

        public GameHistory DeepCopy()
        {
            return new ACESim.GameHistory()
            {
                NumPlayers = NumPlayers,
                History = History.ToArray(),
                LastIndexAddedToHistory = LastIndexAddedToHistory,
                InformationSets = InformationSets.ToArray(),
                LastIndexAddedToInformationSets = LastIndexAddedToInformationSets
            };
        }

        public void Initialize()
        {
            History[0] = Incomplete;
            LastIndexAddedToHistory = 0;
            LastIndexAddedToInformationSets = -1;
        }

        public void AddToHistory(byte decisionNumber, byte playerNumber, byte decisionReached, bool addToAllInformationSets)
        {
            short i = LastIndexAddedToHistory;
            if (History[i] == Complete)
                throw new Exception("Cannot add to history of complete game.");
            History[i] = decisionNumber;
            History[i + 1] = playerNumber;
            History[i + 2] = decisionReached;
            History[i + 3] = Incomplete;
            LastIndexAddedToHistory = (short) (i + 3);
            if (addToAllInformationSets)
                AddToInformationSet(decisionNumber, true);
        }

        public IEnumerable<byte> GetDecisions()
        {
            if (LastIndexAddedToHistory == 0)
                yield break;
            for (short i = 0; i < LastIndexAddedToHistory; i++)
            {
                if (i % 3 == 0)
                    yield return History[i];
            }
        }

        public void MarkComplete()
        {
            short i = LastIndexAddedToHistory;
            if (History[i] == Complete)
                throw new Exception("Game is already complete.");
            History[i] = Complete;
        }

        public bool IsComplete()
        {
            return (History[LastIndexAddedToHistory] == Complete);
        }

        public void AddToInformationSet(byte information, bool allPlayers, byte? playerNumber = null)
        {
            if (NumPlayers == null)
                throw new Exception("Must set NumPlayers.");
            if (allPlayers)
            {
                for (byte i = 1; i <= NumPlayers; i++)
                    AddToInformationSet(information, i);
            }
            else
            {
                if (playerNumber == null || playerNumber == 0 || playerNumber > NumPlayers)
                    throw new Exception("Invalid player number. First player is 1 and last player must equal NumPlayers.");
                AddToInformationSet(information, (byte)playerNumber);
            }
        }

        private void AddToInformationSet(byte information, byte playerNumber)
        {
            LastIndexAddedToInformationSets++;
            InformationSets[LastIndexAddedToInformationSets] = playerNumber;
            LastIndexAddedToInformationSets++;
            InformationSets[LastIndexAddedToInformationSets] = information;
        }

        public IEnumerable<byte> GetPlayerInformation(byte playerNumber)
        {
            if (LastIndexAddedToInformationSets < 0)
                yield break;
            for (byte i = 0; i < LastIndexAddedToInformationSets; i += 2)
            {
                if (InformationSets[i] == playerNumber)
                    yield return InformationSets[i + 1];
            }
        }

    }
}
