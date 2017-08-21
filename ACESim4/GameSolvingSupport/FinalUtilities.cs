using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class FinalUtilities : IGameState
    {
        public double[] Utilities;

        public FinalUtilities(double[] utilities)
        {
            Utilities = utilities;
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
