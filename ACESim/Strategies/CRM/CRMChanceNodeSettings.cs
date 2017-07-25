using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public abstract class CRMChanceNodeSettings : ICRMGameState
    {
        public byte PlayerNum;
        public byte DecisionByteCode;
        public byte DecisionIndex;
        public abstract double GetActionProbability(int action);

        bool DEBUG = false;

        public byte SampleAction(byte numPossibleActions, double randomNumber)
        {
            if (DEBUG)
                return 2;
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
    }
}
