using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.LitigGame
{

    public class LitigGameProgress_PostGameInfo
    {
        public double? LiabilityStrengthUniform;
        public double PLiabilitySignalUniform;
        public double DLiabilitySignalUniform;
        public double? DamagesStrengthUniform;
        public double PDamagesSignalUniform;
        public double DDamagesSignalUniform;

        public double FalsePositiveExpenditures;
        public double FalseNegativeShortfall;
        public double TotalExpensesIncurred;
        public double PreDisputeSharedWelfare;

        public double OpportunityCost;
        public double HarmCost;

        public bool IsTrulyLiable;
    }
}
