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
                // Note that each action is equally likely at any time in the game. The action amounts to the noise that obfuscates the original 
                // value. The signals are equally likely ex ante, but not equally likely ex post.
                problem(); // here's the problem with this approach. We add to the information set the signal rather than the action. That is then hard to deal with we are not really playing the game but are just simulating game play. In the alternative approach, the number of possible actions is always equal to the number of signals. (This is a change.) Suppose it's 2. So we then calculate the probability that the source value is between 0.0 - 0.5, given the actual value + noise; and the probability that the source value is between 0.5 and 1.0, given the same. Thus, we might say "if we started with a value between 0.0 and 0.5, what's the probability that we would end up with a signal in the given range"? Similarly, we consider every other start value. 
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
            var valuePlusNoise = MyProgress.LitigationQuality + noise;
            byte discreteSignal = (byte)DiscreteValueSignal.GetDiscreteSignal(valuePlusNoise, dvsp); // note that this is a 1-based signal
            return discreteSignal;
        }
    }
}
