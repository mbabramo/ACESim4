using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    public readonly ref struct InformationSetHistory
    {
        public readonly Span<byte> InformationSetForPlayer; // length [InformationSetLog.MaxInformationSetLoggingLengthPerFullPlayer];
        public readonly byte PlayerIndex;
        public readonly byte DecisionByteCode;
        public readonly byte DecisionIndex;
        public readonly byte ActionChosen;
        public readonly byte NumPossibleActions;
        public readonly bool IsTerminalAction;

        public InformationSetHistory(Span<byte> informationSetForPlayer, byte playerIndex, byte decisionByteCode, byte decisionIndex, byte actionChosen, byte numPossibleActions, bool isTerminalAction)
        {
            InformationSetForPlayer = informationSetForPlayer;
            PlayerIndex = playerIndex;
            DecisionByteCode = decisionByteCode;
            DecisionIndex = decisionIndex;
            ActionChosen = actionChosen;
            NumPossibleActions = numPossibleActions;
            IsTerminalAction = isTerminalAction;
        }

        public InformationSetHistory(Span<byte> informationSetForPlayer, Game justStartedGame)
        {
            InformationSetForPlayer = informationSetForPlayer;
            PlayerIndex = justStartedGame.CurrentPlayerNumber;
            DecisionByteCode = justStartedGame.CurrentDecision.DecisionByteCode;
            DecisionIndex = justStartedGame.CurrentDecisionIndex ?? 0;
            ActionChosen = 0; // assume that no action has been taken
            NumPossibleActions = justStartedGame.CurrentDecision.NumPossibleActions;
            IsTerminalAction = false;
        }

        public List<byte> GetInformationSetForPlayerAsList()
        {
            return Util.ListExtensions.GetPointerAsList_255Terminated(InformationSetForPlayer);
        }

        public (byte playerIndex, List<byte>) GetPlayerAndInformationSetAsList()
        {
            return (PlayerIndex, GetInformationSetForPlayerAsList());
        }

        public override string ToString()
        {
            StringBuilder infoSet = new StringBuilder();
            bool first = true;
            int index = 0;
            while (InformationSetForPlayer[index] != 255)
            {
                if (first)
                    first = false;
                else
                    infoSet.Append(",");
                infoSet.Append(InformationSetForPlayer[index]);
                index++; // move to next information -- note that decision indices are not included
            }
            return $"Player {PlayerIndex} Decision {DecisionByteCode} (index {DecisionIndex}) Information {infoSet.ToString()} ActionChosen {ActionChosen} NumPossible {NumPossibleActions} IsTerminal {IsTerminalAction}";
        }
    }
}
