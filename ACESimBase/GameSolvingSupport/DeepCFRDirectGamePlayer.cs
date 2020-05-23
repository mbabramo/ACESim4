﻿using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRDirectGamePlayer : DirectGamePlayer
    {
        public DeepCFRMultiModelMode Mode;
        public DeepCFRPlaybackHelper InitialPlaybackHelper;
        /// <summary>
        /// This is used if we want a number of DeepCFRDirectGamePlayers to share a thread while continuing playback from some point. It allows expensive work to be done before continuing playback.
        /// </summary>
        public Func<DeepCFRPlaybackHelper> PlaybackHelperGenerator;
        public bool UsingShortcutForSymmetricGames;

        public DeepCFRDirectGamePlayer(DeepCFRMultiModelMode mode, GameDefinition gameDefinition, GameProgress progress, bool advanceToFirstStep, bool usingShortcutForSymmetricGames, DeepCFRPlaybackHelper initialPlaybackHelper, Func<DeepCFRPlaybackHelper> playbackHelperGenerator) : base(gameDefinition, progress, advanceToFirstStep)
        {
            Mode = mode;
            UsingShortcutForSymmetricGames = usingShortcutForSymmetricGames;
            InitialPlaybackHelper = initialPlaybackHelper;
            PlaybackHelperGenerator = playbackHelperGenerator;
        }

        public override void SynchronizeForSameThread(IEnumerable<IDirectGamePlayer> othersOnSameThread)
        {
            // Note: This will be called only for first on the thread.
            InitialPlaybackHelper = PlaybackHelperGenerator();
            foreach (DeepCFRDirectGamePlayer other in othersOnSameThread.Cast<DeepCFRDirectGamePlayer>())
            {
                other.InitialPlaybackHelper = InitialPlaybackHelper;
            }
        }

        public override DirectGamePlayer DeepCopy()
        {
            GameProgress progress = GameProgress.DeepCopy();
            return new DeepCFRDirectGamePlayer(Mode, GameDefinition, progress, false, UsingShortcutForSymmetricGames, InitialPlaybackHelper, PlaybackHelperGenerator);
        }

        public (DeepCFRIndependentVariables, double[]) GetIndependentVariablesAndPlayerProbabilities(DeepCFRObservationNum observationNum)
        {
            Decision currentDecision = CurrentDecision;
            byte decisionIndex = (byte)CurrentDecisionIndex;
            byte playerMakingDecision = CurrentPlayer.PlayerIndex;
            var informationSet = GetInformationSet();
            var independentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, informationSet, 0 /* placeholder */, null /* TODO */);
            byte adjustedDecisionIndex = UsingShortcutForSymmetricGames && currentDecision.PlayerIndex == 1 ? (byte)(decisionIndex - 1) : decisionIndex;
            IRegressionMachine regressionMachineForCurrentDecision = InitialPlaybackHelper.GetRegressionMachineIfExists(adjustedDecisionIndex);
            double[] onPolicyProbabilities;
            if (InitialPlaybackHelper.ProbabilitiesCache == null)
                onPolicyProbabilities = InitialPlaybackHelper.MultiModel.GetRegretMatchingProbabilities(currentDecision, decisionIndex, independentVariables, regressionMachineForCurrentDecision);
            else
                onPolicyProbabilities = InitialPlaybackHelper.ProbabilitiesCache?.GetValue(this, () => InitialPlaybackHelper.MultiModel.GetRegretMatchingProbabilities(currentDecision, decisionIndex, independentVariables, regressionMachineForCurrentDecision));
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
