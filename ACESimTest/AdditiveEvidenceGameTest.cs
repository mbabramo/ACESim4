using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ACESim;
using ACESim.Util;
using ACESimBase.Games.AdditiveEvidenceGame;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public class AdditiveEvidenceGameTest
    {
        private AdditiveEvidenceGameOptions GetOptions(double evidenceBothQuality = 0.6, double evidenceBothBias = 0.7, double alphaQuality = 0.6, double alphaBothQuality = 0.55, double alphaPQuality = 0.20, double alphaDQuality = 0.15, double alphaBothBias = 0.40, double alphaPBias = 0.10, double alphaDBias = 0.15, byte numQualityAndBiasLevels = 10, byte numOffers = 5, double trialCost = 0.1, bool feeShifting = false, bool feeShiftingBasedOnMarginOfVictory = false, double feeShiftingThreshold = 0.7)
        {
            var options = new AdditiveEvidenceGameOptions()
            {
                Evidence_Both_Quality = evidenceBothQuality,
                Evidence_Both_Bias = evidenceBothBias,
                Alpha_Quality = alphaQuality,
                Alpha_Both_Quality = alphaBothQuality,
                Alpha_Plaintiff_Quality = alphaPQuality,
                Alpha_Defendant_Quality = alphaDQuality,
                Alpha_Both_Bias = alphaBothBias,
                Alpha_Plaintiff_Bias = alphaPBias,
                Alpha_Defendant_Bias = alphaDBias,
                NumQualityAndBiasLevels = numQualityAndBiasLevels,
                NumOffers = numOffers,
                TrialCost = trialCost,
                FeeShifting = feeShifting,
                FeeShiftingIsBasedOnMarginOfVictory = feeShiftingBasedOnMarginOfVictory,
                FeeShiftingThreshold = feeShiftingThreshold
            };
            (options.Alpha_Both_Quality + options.Alpha_Plaintiff_Quality + options.Alpha_Defendant_Quality + options.Alpha_Neither_Quality).Should().BeApproximately(1.0, 0.000001);
            (options.Alpha_Both_Bias + options.Alpha_Plaintiff_Bias + options.Alpha_Defendant_Bias + options.Alpha_Neither_Bias).Should().BeApproximately(1.0, 0.000001);
            return options;
        }

        [TestMethod]
        public void AdditiveEvidence_EqualOffers()
        {
            var gameOptions = GetOptions();
            Func<Decision, GameProgress, byte> actionsToPlay = AdditiveActionsGameActionsGenerator.BothOffer3;
            var gameProgress = AdditiveEvidenceGameLauncher.PlayAdditiveEvidenceGameOnce(gameOptions, actionsToPlay);
            gameProgress.SettlementOccurs.Should().Be(true);
            gameProgress.SettlementValue.Should().Be(0.5);
            gameProgress.SettlementOrJudgment.Should().Be(0.5);

            ////var informationSetHistories = myGameProgress.GameHistory.GetInformationSetHistoryItems().ToList();
            //GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            //var expectedPartyInformationSets = ConstructExpectedPartyInformationSets(LiabilityStrength, PLiabilitySignal, DLiabilitySignal, allowDamagesVariation, PDamagesSignal, DDamagesSignal, true, true, simulatingBargainingFailure, runningSideBetChallenges, bargainingRoundMoves, bargainingRoundRecall, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, allowAbandonAndDefault);
            //string expectedResolutionSet = ConstructExpectedResolutionSet_CaseSettles(LiabilityStrength, allowDamagesVariation, DamagesStrength, true, true, simulatingBargainingFailure, bargainingRoundMoves, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, allowAbandonAndDefault, SideBetChallenges.Irrelevant, runningSideBetChallenges);
            //pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            //dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            //resolutionSet.Should().Be(expectedResolutionSet);
        }
    }
}
