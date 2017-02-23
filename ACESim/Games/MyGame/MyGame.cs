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

        public override void RespondToAction(Decision currentDecision, int action)
        {
            if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.LitigationQuality)
            {
                MyProgress.LitigationQuality = ConvertActionToUniformDistributionDraw(action);
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.PSignal)
            {
                MyProgress.PSignal = GetDiscreteSignal(action, MyDefinition.PNoiseStdev, MyDefinition.PSignalParameters);
                Progress.GameHistory.AddToInformationSet(MyProgress.PSignal, (byte)MyGamePlayers.Plaintiff);
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.DSignal)
            {
                MyProgress.DSignal = GetDiscreteSignal(action, MyDefinition.DNoiseStdev, MyDefinition.DSignalParameters);
                Progress.GameHistory.AddToInformationSet(MyProgress.DSignal, (byte)MyGamePlayers.Defendant);
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.POffer)
            {
                MyProgress.POffer = ConvertActionToUniformDistributionDraw(action);
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.DOffer)
            {
                MyProgress.DOffer = ConvertActionToUniformDistributionDraw(action);
                if (MyProgress.DOffer >= MyProgress.POffer)
                {
                    MyProgress.CaseSettles = true;
                    MyProgress.SettlementValue = (MyProgress.POffer + MyProgress.DOffer) / 2.0;
                    MyProgress.GameComplete = true; // will still do FinalProcessing
                }
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.CourtDecision)
            {
                MyProgress.CourtSignal = GetDiscreteSignal(action, MyDefinition.CourtNoiseStdev, MyDefinition.CourtSignalParameters); // This will produce a signal of 0 in 1/10th of cases, 1 in 1/10th, etc. 
                double courtSignalLocation = EquallySpaced.GetLocationOfMidpoint(MyProgress.CourtSignal - 1 /* make zero-based */, MyDefinition.NumCourtSignals); // note that we're actually converting the signal the court gets (not the original action) into a uniform distribution draw. We could try to enhance GetDiscreteSignal so that we turn this back into an estimate of the probability of a win.
                MyProgress.PWinsAtTrial = (courtSignalLocation > 0.5);
            }
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
                MyProgress.PWelfare = (MyProgress.PWinsAtTrial ? 1.0 : 0) - MyDefinition.PLitigationCosts;
                MyProgress.PWelfare = (MyProgress.PWinsAtTrial ? -1.0 : 0) - MyDefinition.DLitigationCosts;
            }
            base.FinalProcessing();
        }

        public override double Score(int playerNumber)
        {
            if (playerNumber == (byte)MyGamePlayers.Plaintiff)
            {
                return MyProgress.PWelfare;
            }
            else if (playerNumber == (byte)MyGamePlayers.Defendant)
            {
                return MyProgress.DWelfare;
            }
            else
                throw new NotImplementedException("Unexpected player to score.");
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
