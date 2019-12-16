using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class MyGameActionsGenerator
    {

        public static byte GamePlaysOutToTrial(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress)progress;
            switch (decision.DecisionByteCode)
            {
                case (byte)MyGameDecisions.PFile:
                    return 1;
                case (byte)MyGameDecisions.DAnswer:
                    return 1;
                case (byte)MyGameDecisions.PAgreeToBargain:
                    return 2;
                case (byte)MyGameDecisions.DAgreeToBargain:
                    return 2;
                case (byte)MyGameDecisions.POffer:
                    return 10;
                case (byte)MyGameDecisions.DOffer:
                    return 1;
                case (byte)MyGameDecisions.PResponse:
                    return 2;
                case (byte)MyGameDecisions.DResponse:
                    return 2;
                case (byte)MyGameDecisions.PAbandon:
                    return 2;
                case (byte)MyGameDecisions.DDefault:
                    return 2;
            }
            return 0;
        }

    }
}
