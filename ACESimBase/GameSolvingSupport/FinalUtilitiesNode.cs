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
        public double[] Utilities;
        public static int FinalUtilitiesNodesSoFar = 0;
        public int FinalUtilitiesNodeNumber;

        public FinalUtilitiesNode(double[] utilities)
        {
            Utilities = utilities;
            FinalUtilitiesNodeNumber = FinalUtilitiesNodesSoFar;
            Interlocked.Increment(ref FinalUtilitiesNodesSoFar);
        }

        public override string ToString()
        {
            return $"Utilities: {String.Join(",", Utilities.Select(x => $"{x:N2}"))}";
        }

        public GameStateTypeEnum GetGameStateType()
        {
            return GameStateTypeEnum.FinalUtilities;
        }
    }
}
