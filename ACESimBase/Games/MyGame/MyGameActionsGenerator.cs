using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class MyGameActionsGenerator
    {
        public static byte PBetsHeavilyWithGoodLiabilitySignal(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress)progress;
            if (p.PLiabilitySignalDiscrete >= 9)
            {
                if (decision.DecisionByteCode == (byte)MyGameDecisions.PChips)
                    return 3; // 2 chips
            }
            return 0;
        }

        public static byte PGivesNoGroundWithMaxLiabilitySignal(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress)progress;
            if (p.PLiabilitySignalDiscrete == 10)
            {
                if (decision.DecisionByteCode == (byte) MyGameDecisions.PFile)
                    return 1;
                else if (decision.DecisionByteCode == (byte) MyGameDecisions.PAgreeToBargain)
                    return 2;
                else if (decision.DecisionByteCode == (byte) MyGameDecisions.POffer)
                    return 10;
                else if (decision.DecisionByteCode == (byte) MyGameDecisions.PResponse)
                    return 2;
                else if (decision.DecisionByteCode == (byte) MyGameDecisions.PAbandon)
                    return 2;
            }
            return 0;
        }

        public static byte GamePlaysOutToTrial(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress)progress;
            if (decision.DecisionByteCode == (byte)MyGameDecisions.PFile)
                return 1;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.DAnswer)
                return 1;
            else if(decision.DecisionByteCode == (byte)MyGameDecisions.PAgreeToBargain)
                return 2;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.DAgreeToBargain)
                return 2;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.POffer)
                return 10;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.DOffer)
                return 1;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.PResponse)
                return 2;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.DResponse)
                return 2;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.PAbandon)
                return 2;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.DDefault)
                return 2;
            return 0;
        }

        public static byte NoOneSettles(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress)progress;
                if (decision.DecisionByteCode == (byte)MyGameDecisions.POffer)
                    return 10;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.DOffer)
                    return 1;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.PResponse)
                return 2;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.DResponse)
                return 2;
            return 0;
        }

        public static byte NoOneGivesUp(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress)progress;
            if (decision.DecisionByteCode == (byte)MyGameDecisions.PAbandon)
                return 2;
            else if (decision.DecisionByteCode == (byte)MyGameDecisions.DDefault)
                return 2;
            return 0;
        }

        public static byte PDoesntGiveUp(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress)progress;
            if (decision.DecisionByteCode == (byte)MyGameDecisions.PAbandon)
                return 2;
            return 0;
        }

        public static byte PlaintiffShouldOffer10IfReceivingAtLeastLiabilitySignal9(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress) progress;
            if (decision.DecisionByteCode == (byte)MyGameDecisions.POffer && p.PLiabilitySignalDiscrete >= 9)
                return 10;
            return 0;
        }

        public static byte DShouldOffer7IfReceivingAtLeastLiabilitySignal9(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress)progress;
            if (decision.DecisionByteCode == (byte)MyGameDecisions.DOffer && p.DLiabilitySignalDiscrete >= 9)
                return 7;
            return 0;
        }

        public static byte PlaintiffShouldInsistOnMaximum(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress)progress;
            if (decision.DecisionByteCode == (byte)MyGameDecisions.POffer)
                return decision.NumPossibleActions;
            return 0;
        }


        public static byte PlaintiffGivesUp(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress)progress;
            if (decision.DecisionByteCode == (byte) MyGameDecisions.PAbandon)
                return 1;
            return 0;
        }

        public static byte PlaintiffDoesntSettleThenGivesUp(Decision decision, GameProgress progress)
        {
            MyGameProgress p = (MyGameProgress)progress;
            if (decision.DecisionByteCode == (byte)MyGameDecisions.POffer)
                return decision.NumPossibleActions;
            if (decision.DecisionByteCode == (byte)MyGameDecisions.PAbandon)
                return 1;
            return 0;
        }

        public static byte SettleAtMidpointOf5Points_FirstBargainingRound(Decision decision, GameProgress progress)
        {
            switch (decision.DecisionByteCode)
            {
                case (byte)MyGameDecisions.LiabilityStrength:
                case (byte)MyGameDecisions.PLiabilitySignal:
                case (byte)MyGameDecisions.DLiabilitySignal:
                    return 1; // all irrelevant

                case (byte)MyGameDecisions.POffer:
                case (byte)MyGameDecisions.DOffer:
                    return 3;

                default:
                    throw new NotImplementedException();
            }
        }

        public static byte SettleAtMidpoint_SecondBargainingRound(Decision decision, GameProgress progress)
        {
            switch (decision.DecisionByteCode)
            {
                case (byte)MyGameDecisions.LiabilityStrength:
                case (byte)MyGameDecisions.PLiabilitySignal:
                case (byte)MyGameDecisions.DLiabilitySignal:
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

        public static byte SettleAtTwoThirds_SecondBargainingRound(Decision decision, GameProgress progress)
        {
            switch (decision.DecisionByteCode)
            {
                case (byte)MyGameDecisions.LiabilityStrength:
                case (byte)MyGameDecisions.PLiabilitySignal:
                case (byte)MyGameDecisions.DLiabilitySignal:
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

        public static byte SettlementFails_PWins(Decision decision, GameProgress progress)
        {
            switch (decision.DecisionByteCode)
            {

                case (byte)MyGameDecisions.LiabilityStrength:
                    return 3;
                case (byte)MyGameDecisions.PLiabilitySignal:
                    return 4;
                case (byte)MyGameDecisions.DLiabilitySignal:
                    return 2;
                case (byte)MyGameDecisions.POffer:
                    return 5;
                case (byte)MyGameDecisions.DOffer:
                    return 1;
                case (byte)MyGameDecisions.CourtDecisionLiability:
                    return 2;

                default:
                    throw new NotImplementedException();
            }
        }


        public static byte SettlementFails_PLoses(Decision decision, GameProgress progress)
        {
            switch (decision.DecisionByteCode)
            {

                case (byte)MyGameDecisions.LiabilityStrength:
                    return 3;
                case (byte)MyGameDecisions.PLiabilitySignal:
                    return 4;
                case (byte)MyGameDecisions.DLiabilitySignal:
                    return 2;
                case (byte)MyGameDecisions.POffer:
                    return 5;
                case (byte)MyGameDecisions.DOffer:
                    return 1;
                case (byte)MyGameDecisions.CourtDecisionLiability:
                    return 1;

                default:
                    throw new NotImplementedException();
            }
        }

        public static byte UsingRawLiabilitySignals_SettlementFails(Decision decision, GameProgress progress)
        {
            switch (decision.DecisionByteCode)
            {
                case (byte)MyGameDecisions.LiabilityStrength:
                    return 5;
                case (byte)MyGameDecisions.PLiabilitySignal:
                    return 9;
                case (byte)MyGameDecisions.DLiabilitySignal:
                    return 1; 

                case (byte)MyGameDecisions.POffer:
                    return 9;
                case (byte)MyGameDecisions.DOffer:
                    return 4;
                case (byte)MyGameDecisions.CourtDecisionLiability:
                    return 2;

                default:
                    throw new NotImplementedException();
            }
        }
    }
}
