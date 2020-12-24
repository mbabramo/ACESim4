using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class LitigGameActionsGenerator
    {

        public static byte GamePlaysOutToTrial(Decision decision, GameProgress progress)
        {
            LitigGameProgress p = (LitigGameProgress)progress;
            switch (decision.DecisionByteCode)
            {
                case (byte)LitigGameDecisions.PFile:
                    return 1;
                case (byte)LitigGameDecisions.DAnswer:
                    return 1;
                case (byte)LitigGameDecisions.PAgreeToBargain:
                    return 2;
                case (byte)LitigGameDecisions.DAgreeToBargain:
                    return 2;
                case (byte)LitigGameDecisions.POffer:
                    return 10;
                case (byte)LitigGameDecisions.DOffer:
                    return 1;
                case (byte)LitigGameDecisions.PResponse:
                    return 2;
                case (byte)LitigGameDecisions.DResponse:
                    return 2;
                case (byte)LitigGameDecisions.PAbandon:
                    return 2;
                case (byte)LitigGameDecisions.DDefault:
                    return 2;
            }
            return 0;
        }

    }
}
