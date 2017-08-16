using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class MyGameActionsGenerator
    {
        public static byte PlaintiffShouldOffer1IfReceivingSignal1(Decision decision)
        {
            switch (decision.DecisionByteCode)
            {
                case (byte)MyGameDecisions.POffer:
                    if ()

                default:
                    throw new NotImplementedException();
            }
        }

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

        public static byte SettleAtMidpoint_SecondBargainingRound(Decision decision)
        {
            switch (decision.DecisionByteCode)
            {
                case (byte)MyGameDecisions.LitigationQuality:
                case (byte)MyGameDecisions.PSignal:
                case (byte)MyGameDecisions.DSignal:
                    return 1; // all irrelevant

                case (byte)MyGameDecisions.POffer:
                    if (decision.CustomByte == 1)
                        return 5; // ask for more
                    else
                        return 3;
                case (byte)MyGameDecisions.DOffer:
                    return 3;

                default:
                    throw new NotImplementedException();
            }
        }

        public static byte SettleAtTwoThirds_SecondBargainingRound(Decision decision)
        {
            switch (decision.DecisionByteCode)
            {
                case (byte)MyGameDecisions.LitigationQuality:
                case (byte)MyGameDecisions.PSignal:
                case (byte)MyGameDecisions.DSignal:
                    return 1; // all irrelevant

                case (byte)MyGameDecisions.POffer:
                    if (decision.CustomByte == 1)
                        return 5; // ask for more
                    else
                        return 4; // this is settlement value. With 5 settlement value options, the settlement is thus 4/6 = 2/3.
                case (byte)MyGameDecisions.DOffer:
                    return 4;

                default:
                    throw new NotImplementedException();
            }
        }
    }
}
