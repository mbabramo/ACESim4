﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    public readonly struct InformationSetHistory
    {
        public readonly byte[] InformationSetForPlayer; 
        public readonly List<(byte decisionIndex, byte information)> LabeledInformationSetForPlayer;
        public readonly byte PlayerIndex;
        public readonly byte DecisionByteCode;
        public readonly byte DecisionIndex;
        public readonly byte ActionChosen;
        public readonly byte NumPossibleActions;

        public InformationSetHistory(byte[] informationSetForPlayer, List<(byte decisionIndex, byte information)> labeledInformationSetForPlayer, byte playerIndex, byte decisionByteCode, byte decisionIndex, byte actionChosen, byte numPossibleActions)
        {
            InformationSetForPlayer = informationSetForPlayer;
            LabeledInformationSetForPlayer = labeledInformationSetForPlayer;
            PlayerIndex = playerIndex;
            DecisionByteCode = decisionByteCode;
            DecisionIndex = decisionIndex;
            ActionChosen = actionChosen;
            NumPossibleActions = numPossibleActions;
        }

        public InformationSetHistory(byte[] informationSetForPlayer, List<(byte decisionIndex, byte information)> labeledInformationSetForPlayer, Game justStartedGame)
        {
            InformationSetForPlayer = informationSetForPlayer;
            LabeledInformationSetForPlayer = labeledInformationSetForPlayer;
            PlayerIndex = justStartedGame.CurrentPlayerNumber;
            DecisionByteCode = justStartedGame.CurrentDecision.DecisionByteCode;
            DecisionIndex = justStartedGame.CurrentDecisionIndex ?? 0;
            ActionChosen = 0; // assume that no action has been taken
            NumPossibleActions = justStartedGame.CurrentDecision.NumPossibleActions;
        }

        public List<byte> GetInformationSetForPlayerAsList()
        {
            return Util.ListExtensions.GetSpan255TerminatedAsList(InformationSetForPlayer);
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
            return $"Player {PlayerIndex} Decision {DecisionByteCode} (index {DecisionIndex}) Information {infoSet} ActionChosen {ActionChosen} NumPossible {NumPossibleActions}";
        }
    }
}
