using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public abstract class ChanceNode : IGameState, IAnyNode
    {
        public int ChanceNodeNumber;
        public bool IsChanceNode => true;
        public bool IsUtilitiesNode => false;
        public int GetInformationSetNodeNumber() => ChanceNodeNumber;

        public double[] GetNodeValues() => GetActionProbabilities().ToArray();
        public int? AltNodeNumber { get; set; }
        public int GetNumPossibleActions() => Decision.NumPossibleActions;
        public Decision Decision { get; set; }
        public byte DecisionIndex;
        public byte PlayerNum => Decision.PlayerIndex;
        public byte DecisionByteCode => Decision.DecisionByteCode;
        public bool CriticalNode => Decision.CriticalNode;

        public abstract ChanceNode DeepCopy();

        public string ShortString() => $"Chance {ChanceNodeNumber} Alt {AltNodeNumber} {Decision.Name} ({Decision.Abbreviation})";

        public abstract double GetActionProbability(int action, int distributorChanceInputs = -1);

        public string GetActionProbabilityString(int distributorChanceInputs) => string.Join(",", GetActionProbabilityStrings(distributorChanceInputs));

        public IEnumerable<string> GetActionProbabilityStrings(int distributorChanceInputs = -1) => GetActionProbabilities(distributorChanceInputs).Select(x => x.ToSignificantFigures(4));

        public IEnumerable<double> GetActionProbabilities(int distributorChanceInputs = -1) => Enumerable.Range(1, Decision.NumPossibleActions).Select(action => GetActionProbability(action, distributorChanceInputs));

        public IEnumerable<decimal> GetActionProbabilitiesDecimal(int distributorChanceInputs = -1) => Enumerable.Range(1, Decision.NumPossibleActions).Select(action => (decimal) GetActionProbability(action, distributorChanceInputs));

        public ChanceNode(int chanceNodeNumber)
        {
            ChanceNodeNumber = chanceNodeNumber;
        }


        public abstract Rational[] GetProbabilitiesAsRationals(int maxIntegralUtility);

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
