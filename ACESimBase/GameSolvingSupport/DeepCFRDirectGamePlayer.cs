using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRDirectGamePlayer : DirectGamePlayer
    {
        public DeepCFRPlaybackHelper PlaybackHelper;

        public DeepCFRDirectGamePlayer(GameDefinition gameDefinition, GameProgress progress, bool advanceToFirstStep, DeepCFRPlaybackHelper playbackHelper) : base(gameDefinition, progress, advanceToFirstStep)
        {
            PlaybackHelper = playbackHelper;
        }

        public override DirectGamePlayer DeepCopy()
        {
            return new DeepCFRDirectGamePlayer(GameDefinition, GameProgress.DeepCopy(), false, PlaybackHelper);
        }

        public (DeepCFRIndependentVariables, double[]) GetIndependentVariablesAndPlayerProbabilities(DeepCFRObservationNum observationNum)
        {
            byte decisionIndex = (byte)CurrentDecisionIndex;
            byte playerMakingDecision = CurrentPlayer.PlayerIndex;
            var informationSet = GetInformationSet(true);
            var independentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, informationSet, 0 /* placeholder */, null /* TODO */);
            IRegressionMachine regressionMachineForCurrentDecision = PlaybackHelper.RegressionMachines?.GetValueOrDefault(CurrentDecision.DecisionByteCode);
            double[] onPolicyProbabilities;
            if (PlaybackHelper.ProbabilitiesCache == null)
                onPolicyProbabilities = PlaybackHelper.MultiModel.GetRegretMatchingProbabilities(independentVariables, CurrentDecision, regressionMachineForCurrentDecision);
            else
                onPolicyProbabilities = PlaybackHelper.ProbabilitiesCache?.GetValue(this, () => PlaybackHelper.MultiModel.GetRegretMatchingProbabilities(independentVariables, CurrentDecision, regressionMachineForCurrentDecision));
            byte actionChosen = ChooseAction(observationNum, decisionIndex, onPolicyProbabilities);
            independentVariables.ActionChosen = actionChosen;
            return (independentVariables, onPolicyProbabilities);
        }

        public byte ChooseAction(DeepCFRObservationNum observationNum, byte decisionIndex, double[] onPolicyProbabilities)
        {
            double randomValue = observationNum.GetRandomDouble(decisionIndex);
            byte actionChosen = (byte)(1 + ConsistentRandomSequenceProducer.GetRandomIndex(onPolicyProbabilities, randomValue));
            return actionChosen;
        }

        public override double[] GetPlayerProbabilities() => GetIndependentVariablesAndPlayerProbabilities(default).Item2;
    }
}
