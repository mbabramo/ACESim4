using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class MyGameActionsGenerator
    {
        public static byte SettleAtMidpoint_OneBargainingRound(Decision decision)
        {
            switch (decision.DecisionByteCode)
            {
                case (byte)MyGameDecisions.LitigationQuality:
                case (byte)MyGameDecisions.PSignal:
                case (byte)MyGameDecisions.DSignal:
                    return 1; // all irrelevant

                case (byte)MyGameDecisions.POffer:
                case (byte)MyGameDecisions.DOffer:
                    return 3;

                default:
                    throw new NotImplementedException();
            }
        }
    }
}
