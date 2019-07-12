using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class FinalUtilitiesNode : IGameState
    {
        public int CurrentScenarioIndex = 0;
        public double[] Utilities => AllScenarioUtilities[CurrentScenarioIndex];
        public List<double[]> AllScenarioUtilities;
        public int FinalUtilitiesNodeNumber;
        public int GetNodeNumber() => FinalUtilitiesNodeNumber;

        public FinalUtilitiesNode(List<double[]> allScenarioUtilities, int finalUtilitiesNodeNumber)
        {
            AllScenarioUtilities = allScenarioUtilities;
            FinalUtilitiesNodeNumber = finalUtilitiesNodeNumber;
        }

        public FinalUtilitiesNode(double[] utilities, int finalUtilitiesNodeNumber)
        {
            AllScenarioUtilities = new List<double[]>() { utilities };
            FinalUtilitiesNodeNumber = finalUtilitiesNodeNumber;
        }

        public override string ToString()
        {
            return $"Utilities {FinalUtilitiesNodeNumber}: {String.Join("; ", Utilities.Select(x => $"{x.ToSignificantFigures(6)}"))}";
        }

        public GameStateTypeEnum GetGameStateType()
        {
            return GameStateTypeEnum.FinalUtilities;
        }
    }
}
