using ACESim;
using ACESimBase.Util.Randomization;
using ACESimBase.Util.Statistical;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport.DeepCFR
{
    public class DeepCFRDirectGamePlayer : DirectGamePlayer
    {
        public DeepCFRMultiModelMode Mode;
        public DeepCFRPlaybackHelper PlaybackHelper;
        public bool UsingShortcutForSymmetricGames;

        public DeepCFRDirectGamePlayer(DeepCFRMultiModelMode mode, GameDefinition gameDefinition, GameProgress progress, bool advanceToFirstStep, bool usingShortcutForSymmetricGames, DeepCFRPlaybackHelper playbackHelper) : base(gameDefinition, progress, advanceToFirstStep)
        {
            Mode = mode;
            UsingShortcutForSymmetricGames = usingShortcutForSymmetricGames;
            PlaybackHelper = playbackHelper;
        }

        public override DirectGamePlayer DeepCopy()
        {
            GameProgress progress = GameProgress.DeepCopy();
            return new DeepCFRDirectGamePlayer(Mode, GameDefinition, progress, false, UsingShortcutForSymmetricGames, PlaybackHelper);
        }

        public (DeepCFRIndependentVariables, double[]) GetIndependentVariablesAndPlayerProbabilities(DeepCFRObservationNum observationNum)
        {
            Decision currentDecision = CurrentDecision;
            byte decisionIndex = (byte)CurrentDecisionIndex;
            byte playerMakingDecision = CurrentPlayer.PlayerIndex;
            var informationSet = GetInformationSet();
            var independentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, informationSet, 0 /* placeholder */, null /* TODO */);
            byte adjustedDecisionIndex = UsingShortcutForSymmetricGames && currentDecision.PlayerIndex == 1 ? (byte)(decisionIndex - 1) : decisionIndex;
            IRegressionMachine regressionMachineForCurrentDecision = PlaybackHelper.GetRegressionMachineIfExists(adjustedDecisionIndex);
            double[] onPolicyProbabilities;
            if (PlaybackHelper.ProbabilitiesCache == null)
                onPolicyProbabilities = PlaybackHelper.MultiModel.GetRegretMatchingProbabilities(currentDecision, decisionIndex, independentVariables, regressionMachineForCurrentDecision);
            else
                onPolicyProbabilities = PlaybackHelper.ProbabilitiesCache?.GetValue(this, () => PlaybackHelper.MultiModel.GetRegretMatchingProbabilities(currentDecision, decisionIndex, independentVariables, regressionMachineForCurrentDecision));
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
