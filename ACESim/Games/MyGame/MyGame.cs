using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class MyGame : Game
    {
        public MyGameDefinition MyDefinition => (MyGameDefinition)GameDefinition;
        public MyGameProgress MyProgress => (MyGameProgress)Progress;

        public override bool DecisionIsNeeded(Decision currentDecision)
        {
            if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.CourtDecision)
                return !MyProgress.CaseSettles;
            return true;
        }

        public override void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
            if (currentDecisionByteCode == (byte)MyGameDecisions.LitigationQuality)
            {
                MyProgress.PInitialWealth = MyDefinition.PInitialWealth;
                MyProgress.DInitialWealth = MyDefinition.DInitialWealth;
                MyProgress.DamagesAlleged = MyDefinition.DamagesAlleged;
                MyProgress.LitigationQualityDiscrete = action;
                MyProgress.LitigationQualityUniform = ConvertActionToUniformDistributionDraw(action);
                // If one or both parties have perfect information, then they can get their information about litigation quality now, since they don't need a signal. Note that we also specify in the game definition that the litigation quality should become part of their information set.
                if (MyDefinition.PNoiseStdev == 0)
                    MyProgress.PSignalUniform = MyProgress.LitigationQualityUniform;
                if (MyDefinition.DNoiseStdev == 0)
                    MyProgress.DSignalUniform = MyProgress.LitigationQualityUniform;
            }
            else if (currentDecisionByteCode == (byte)MyGameDecisions.PSignal)
            {
                // Note: This is an unequal probabilities chance decision. The action IS the discrete signal. The game definition then calculates the probability that we would get this signal, given the uniform distribution draw. In other words, this is like a weighted die, where the die is heavily weighted toward signal values that are close to the litigation quality values.
                MyProgress.PSignalDiscrete = action;
                MyProgress.PSignalUniform = EquallySpaced.GetLocationOfEquallySpacedPoint(MyProgress.PSignalDiscrete - 1 /* make it zero-based */, MyDefinition.NumSignals);
            }
            else if (currentDecisionByteCode == (byte)MyGameDecisions.DSignal)
            {
                MyProgress.DSignalDiscrete = action;
                MyProgress.DSignalUniform = EquallySpaced.GetLocationOfEquallySpacedPoint(MyProgress.DSignalDiscrete - 1 /* make it zero-based */, MyDefinition.NumSignals);
            }
            else if (currentDecisionByteCode == (byte)MyGameDecisions.POffer)
            {
                double offer = GetOfferBasedOnAction(action, true);
                MyProgress.AddOffer(true, offer);
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecisionByteCode == (byte)MyGameDecisions.DOffer)
            {
                double offer = GetOfferBasedOnAction(action, false);
                MyProgress.AddOffer(false, offer);
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecisionByteCode == (byte)MyGameDecisions.PResponse)
            {
                MyProgress.AddResponse(true, action == 1); // 1 == accept, 2 == reject
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecisionByteCode == (byte)MyGameDecisions.DResponse)
            {
                MyProgress.AddResponse(false, action == 1); // 1 == accept, 2 == reject
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecisionByteCode == (byte)MyGameDecisions.CourtDecision)
            {
                MyProgress.TrialOccurs = true;
                // note that the probability of P winning is defined in MyGameDefinition.
                MyProgress.PWinsAtTrial = action == 2;
            }
        }

        private double GetOfferBasedOnAction(byte action, bool plaintiffOffer)
        {
            double offer;
            if (MyProgress.BargainingRoundsComplete == 0 || !MyDefinition.SubsequentOffersAreDeltas)
                offer = ConvertActionToUniformDistributionDraw(action);
            else
            {
                double? previousOffer = plaintiffOffer ? MyProgress.PLastOffer : MyProgress.DLastOffer;
                if (previousOffer == null)
                    offer = ConvertActionToUniformDistributionDraw(action);
                else
                    offer = MyDefinition.DeltaOffersCalculation.GetOfferValue((double) previousOffer, action);
            }
            return offer;
        }

        public override void FinalProcessing()
        {
            if (MyProgress.CaseSettles)
            {
                MyProgress.PChangeWealth = (double)MyProgress.SettlementValue;
                MyProgress.DChangeWealth = 0 - (double)MyProgress.SettlementValue;
            }
            else
            {
                MyProgress.PChangeWealth = (MyProgress.PWinsAtTrial ? MyProgress.DamagesAlleged : 0) - MyDefinition.PTrialCosts;
                MyProgress.DChangeWealth = (MyProgress.PWinsAtTrial ? -MyProgress.DamagesAlleged : 0) - MyDefinition.DTrialCosts;
            }
            double perPartyBargainingCosts = MyDefinition.PerPartyBargainingRoundCosts * MyProgress.BargainingRoundsComplete;
            MyProgress.PChangeWealth -= perPartyBargainingCosts;
            MyProgress.DChangeWealth -= perPartyBargainingCosts;
            MyProgress.PFinalWealth = MyProgress.PInitialWealth + MyProgress.PChangeWealth;
            MyProgress.DFinalWealth = MyProgress.DInitialWealth + MyProgress.DChangeWealth;
            MyProgress.PWelfare =
                MyDefinition.PUtilityCalculator.GetSubjectiveUtilityForWealthLevel(MyProgress.PFinalWealth);
            MyProgress.DWelfare =
                MyDefinition.DUtilityCalculator.GetSubjectiveUtilityForWealthLevel(MyProgress.DFinalWealth);
            base.FinalProcessing();
        }

        private byte GetDiscreteSignal(int action, double noiseStdev, DiscreteValueSignalParameters dvsp)
        {
            var noise = ConvertActionToNormalDistributionDraw(action, noiseStdev);
            var valuePlusNoise = MyProgress.LitigationQualityUniform + noise;
            byte discreteSignal = (byte)DiscreteValueSignal.GetDiscreteSignal(valuePlusNoise, dvsp); // note that this is a 1-based signal
            return discreteSignal;
        }
    }
}
