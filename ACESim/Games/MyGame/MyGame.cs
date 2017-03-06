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
        public MyGameInputs MyInputs => (MyGameInputs)GameInputs;
        public MyGameProgress MyProgress => (MyGameProgress)Progress;

        public override bool DecisionIsNeeded(Decision currentDecision)
        {
            if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.CourtDecision)
                return !MyProgress.CaseSettles;
            return true;
        }

        public override void RespondToAction(Decision currentDecision, byte action)
        {
            if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.LitigationQuality)
            {
                MyProgress.LitigationQuality = ConvertActionToUniformDistributionDraw(action);
                // If one or both parties have perfect information, then they can get their information about litigation quality now, since they don't need a signal. Note that we also specify in the game definition that the litigation quality should become part of their information set.
                if (MyDefinition.PNoiseStdev == 0)
                    MyProgress.PSignalUniform = MyProgress.LitigationQuality;
                if (MyDefinition.DNoiseStdev == 0)
                    MyProgress.DSignalUniform = MyProgress.LitigationQuality;
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.PSignal)
            {
                MyProgress.PSignal = GetDiscreteSignal(action, MyDefinition.PNoiseStdev, MyDefinition.PSignalParameters);
                Progress.GameHistory.AddToInformationSet(MyProgress.PSignal, (byte)MyGamePlayers.Plaintiff, (byte) CurrentDecisionIndex);
                MyProgress.PSignalUniform = EquallySpaced.GetLocationOfEquallySpacedPoint(MyProgress.PSignal - 1 /* make it zero-based */, MyDefinition.NumSignals);
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.DSignal)
            {
                MyProgress.DSignal = GetDiscreteSignal(action, MyDefinition.DNoiseStdev, MyDefinition.DSignalParameters);
                Progress.GameHistory.AddToInformationSet(MyProgress.DSignal, (byte)MyGamePlayers.Defendant, (byte)CurrentDecisionIndex);
                MyProgress.DSignalUniform = EquallySpaced.GetLocationOfEquallySpacedPoint(MyProgress.DSignal - 1 /* make it zero-based */, MyDefinition.NumSignals);
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.POffer)
            {
                double offer = GetOfferBasedOnAction(action, true);
                MyProgress.AddOffer(true, offer);
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.DOffer)
            {
                double offer = GetOfferBasedOnAction(action, false);
                MyProgress.AddOffer(false, offer);
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.PResponse)
            {
                MyProgress.AddResponse(true, action == 1); // 1 == accept, 2 == reject
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.DResponse)
            {
                MyProgress.AddResponse(false, action == 1); // 1 == accept, 2 == reject
                MyProgress.UpdateProgress(MyDefinition);
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.CourtDecision)
            {
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
                double previousOffer = plaintiffOffer ? (double)MyProgress.PLastOffer : (double)MyProgress.DLastOffer;
                offer = MyDefinition.DeltaOffersCalculation.GetOfferValue(previousOffer, action);
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
            var valuePlusNoise = MyProgress.LitigationQuality + noise;
            byte discreteSignal = (byte)DiscreteValueSignal.GetDiscreteSignal(valuePlusNoise, dvsp); // note that this is a 1-based signal
            return discreteSignal;
        }
    }
}
