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
        public int CurrentScenario = 0;
        public double[] Utilities => AllScenarioUtilities[CurrentScenario];
        public List<double[]> AllScenarioUtilities;
        public static int FinalUtilitiesNodesSoFar = 0;
        public int FinalUtilitiesNodeNumber;
        public int GetNodeNumber() => FinalUtilitiesNodeNumber;

        public FinalUtilitiesNode(List<double[]> allScenarioUtilities)
        {
            AllScenarioUtilities = allScenarioUtilities;
            FinalUtilitiesNodeNumber = FinalUtilitiesNodesSoFar;
            Interlocked.Increment(ref FinalUtilitiesNodesSoFar);
        }

        public FinalUtilitiesNode(double[] utilities)
        {
            AllScenarioUtilities = new List<double[]>() { utilities };
            FinalUtilitiesNodeNumber = FinalUtilitiesNodesSoFar;
            Interlocked.Increment(ref FinalUtilitiesNodesSoFar);
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
