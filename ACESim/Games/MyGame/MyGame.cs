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
                Progress.GameHistory.AddToInformationSet(MyProgress.PSignal, (byte)MyGamePlayers.Plaintiff, (byte) CurrentDecisionIndex);
            }
            else if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.DSignal)
            {
                MyProgress.DSignal = GetDiscreteSignal(action, MyDefinition.DNoiseStdev, MyDefinition.DSignalParameters);
                Progress.GameHistory.AddToInformationSet(MyProgress.DSignal, (byte)MyGamePlayers.Defendant, (byte)CurrentDecisionIndex);
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
                MyProgress.CourtRandomSeed = EquallySpaced.GetLocationOfMidpoint(action - 1 /* make zero-based */, MyDefinition.NumCourtSignals);
                MyProgress.PWinsAtTrial = (MyProgress.CourtRandomSeed < MyProgress.LitigationQuality); // note that if random seed is equal to litigation quality, plaintiff loses (burden of proof). We thus use an odd number of court random seeds. 
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
                MyProgress.DWelfare = (MyProgress.PWinsAtTrial ? -1.0 : 0) - MyDefinition.DLitigationCosts;
            }
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
