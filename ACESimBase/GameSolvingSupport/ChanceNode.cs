﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public abstract class ChanceNode : IGameState
    {
        public static int ChanceNodesSoFar = 0;
        public int ChanceNodeNumber;
        public Decision Decision;
        public byte DecisionIndex;
        public byte PlayerNum => Decision.PlayerNumber;
        public byte DecisionByteCode => Decision.DecisionByteCode;
        public bool CriticalNode => Decision.CriticalNode;

        public abstract double GetActionProbability(int action, int distributorChanceInputs = -1);

        public ChanceNode()
        {
            ChanceNodeNumber = ChanceNodesSoFar;
            Interlocked.Increment(ref ChanceNodesSoFar);
        }

        public byte SampleAction(byte numPossibleActions, double randomNumber)
        {
            double cumulative = 0;
            byte action = 1;
            do
            {
                cumulative += GetActionProbability(action);
                if (cumulative >= randomNumber || action == numPossibleActions)
                    return action;
                else
                    action++;
            }
            while (true);
        }

        public abstract bool AllProbabilitiesEqual();

        public GameStateTypeEnum GetGameStateType()
        {
            return GameStateTypeEnum.Chance;
        }
    }
}