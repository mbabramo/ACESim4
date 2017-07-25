﻿using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class SimpleGame : Game
    {
        public SimpleGameDefinition MyDefinition => (SimpleGameDefinition)GameDefinition;
        public SimpleGameProgress MyProgress => (SimpleGameProgress)Progress;

        public override bool DecisionIsNeeded(Decision currentDecision)
        {
            if (currentDecision.DecisionByteCode == (byte)SimpleGameDecisions.CourtDecision)
                return !MyProgress.CaseSettles;
            return true;
        }

        public override void RespondToAction(Decision currentDecision, byte action)
        {
            if (currentDecision.DecisionByteCode == (byte)SimpleGameDecisions.LitigationQuality)
            {
                MyProgress.LitigationQualityDiscrete = action;
                MyProgress.LitigationQualityUniform = ConvertActionToUniformDistributionDraw(action);
                // If one or both parties have perfect information, then they can get their information about litigation quality now, since they don't need a signal. Note that we also specify in the game definition that the litigation quality should become part of their information set.
                if (MyDefinition.PNoiseStdev == 0)
                    MyProgress.PSignalUniform = MyProgress.LitigationQualityUniform;
                if (MyDefinition.DNoiseStdev == 0)
                    MyProgress.DSignalUniform = MyProgress.LitigationQualityUniform;
            }
            else if (currentDecision.DecisionByteCode == (byte)SimpleGameDecisions.PSignal)
            {
                // Note: This is an unequal probabilities chance decision. The action IS the discrete signal. The game definition then calculates the probability that we would get this signal, given the uniform distribution draw. In other words, this is like a weighted die, where the die is heavily weighted toward signal values that are close to the litigation quality values.
                MyProgress.PSignalDiscrete = action;
                MyProgress.PSignalUniform = EquallySpaced.GetLocationOfEquallySpacedPoint(MyProgress.PSignalDiscrete - 1 /* make it zero-based */, MyDefinition.NumSignals);
            }
            else if (currentDecision.DecisionByteCode == (byte)SimpleGameDecisions.DSignal)
            {
                MyProgress.DSignalDiscrete = action;
                MyProgress.DSignalUniform = EquallySpaced.GetLocationOfEquallySpacedPoint(MyProgress.DSignalDiscrete - 1 /* make it zero-based */, MyDefinition.NumSignals);
            }
            else if (currentDecision.DecisionByteCode == (byte)SimpleGameDecisions.POffer)
            {
                double offer = GetOfferBasedOnAction(action, true);
                MyProgress.AddOffer(true, offer);
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecision.DecisionByteCode == (byte)SimpleGameDecisions.DOffer)
            {
                double offer = GetOfferBasedOnAction(action, false);
                MyProgress.AddOffer(false, offer);
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecision.DecisionByteCode == (byte)SimpleGameDecisions.PResponse)
            {
                MyProgress.AddResponse(true, action == 1); // 1 == accept, 2 == reject
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecision.DecisionByteCode == (byte)SimpleGameDecisions.DResponse)
            {
                MyProgress.AddResponse(false, action == 1); // 1 == accept, 2 == reject
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecision.DecisionByteCode == (byte)SimpleGameDecisions.CourtDecision)
            {
                // note that the probability of P winning is defined in SimpleGameDefinition.
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
                MyProgress.PWelfare = (double)MyProgress.SettlementValue;
                MyProgress.DWelfare = 0 - (double)MyProgress.SettlementValue;
            }
            else
            {
                MyProgress.PWelfare = (MyProgress.PWinsAtTrial ? 1.0 : 0) - MyDefinition.PTrialCosts;
                MyProgress.DWelfare = (MyProgress.PWinsAtTrial ? -1.0 : 0) - MyDefinition.DTrialCosts;
            }
            double perPartyBargainingCosts = MyDefinition.PerPartyBargainingRoundCosts * MyProgress.BargainingRoundsComplete;
            MyProgress.PWelfare -= perPartyBargainingCosts;
            MyProgress.DWelfare -= perPartyBargainingCosts;
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
