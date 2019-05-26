using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public class DistributorChanceInputs
    {
        public List<(int scalarAccumulatedInput, double probability)> Accumulated = new List<(int scalarAccumulatedInput, double probability)>();
        public byte? IsDistributableForPlayer;

        public void AddScalarDistributorChanceInput(byte player, byte action, byte multiplier)
        {

        }
    }
}
