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

        public AdditiveEvidenceGameOptions GetOptions_DariMattiacci_Saraceno(double evidenceBothQuality)
        {
            return GetOptions(evidenceBothQuality: evidenceBothQuality, alphaBothBias: 0, alphaPBias: evidenceBothQuality, alphaDBias: 1.0 - evidenceBothQuality, alphaQuality: 0); // P's strength of information about bias is the same as the strength of the case. 
        }

        [TestMethod]
        public void AdditiveEvidence_SettlementValues()
        {
            var gameOptions = GetOptions();
            foreach ((int pOffer, int dOffer, double settlementValue) in new[] { (3, 3, 0.5), (2, 4, 0.5), (1, 5, 0.5), (1, 3, 2.0 / 6.0), (1, 4, 2.5 / 6.0), (2, 5, 3.5 / 6.0), (3, 5, 4.0 / 6.0), (4, 5, 4.5 / 6.0) })
            {
                Func<Decision, GameProgress, byte> actionsToPlay = AdditiveActionsGameActionsGenerator.PlaySpecifiedDecisions(pOffer: (byte)pOffer, dOffer: (byte)dOffer);
                var gameProgress = AdditiveEvidenceGameLauncher.PlayAdditiveEvidenceGameOnce(gameOptions, actionsToPlay);
                gameProgress.SettlementOccurs.Should().Be(true);
                gameProgress.TrialOccurs.Should().Be(false);
                gameProgress.SettlementValue.Should().BeApproximately(settlementValue, 1E-10);
                gameProgress.SettlementOrJudgment.Should().BeApproximately(settlementValue, 1E-10);
                gameProgress.DsProportionOfCost.Should().Be(0.5);
            }
        }

        [TestMethod]
        public void AdditiveEvidence_TrialValue()
        {
            var gameOptions = GetOptions();
            byte chancePlaintiffQuality = 3;
            byte chanceDefendantQuality = 1;
            byte chanceNeitherQuality = 5;
            byte chancePlaintiffBias = 3;
            byte chanceDefendantBias = 1;
            byte chanceNeitherBias = 2;
            AdditiveEvidence_TrialValue_Helper(gameOptions, chancePlaintiffQuality, chanceDefendantQuality, chanceNeitherQuality, chancePlaintiffBias, chanceDefendantBias, chanceNeitherBias);

            gameOptions = GetOptions_DariMattiacci_Saraceno(6.0);
            AdditiveEvidence_TrialValue_Helper(gameOptions, chancePlaintiffQuality, chanceDefendantQuality, chanceNeitherQuality, chancePlaintiffBias, chanceDefendantBias, chanceNeitherBias);

        }

        private static void AdditiveEvidence_TrialValue_Helper(AdditiveEvidenceGameOptions gameOptions, byte chancePlaintiffQuality, byte chanceDefendantQuality, byte chanceNeitherQuality, byte chancePlaintiffBias, byte chanceDefendantBias, byte chanceNeitherBias)
        {
            double chancePQualityDouble = (chancePlaintiffQuality) / (gameOptions.NumQualityAndBiasLevels + 1.0);
            double chanceDQualityDouble = (chanceDefendantQuality) / (gameOptions.NumQualityAndBiasLevels + 1.0);
            double chanceNQualityDouble = (chanceNeitherQuality) / (gameOptions.NumQualityAndBiasLevels + 1.0);
            double chancePBiasDouble = (chancePlaintiffBias) / (gameOptions.NumQualityAndBiasLevels + 1.0);
            double chanceDBiasDouble = (chanceDefendantBias) / (gameOptions.NumQualityAndBiasLevels + 1.0);
            double chanceNBiasDouble = (chanceNeitherBias) / (gameOptions.NumQualityAndBiasLevels + 1.0);
            Func<Decision, GameProgress, byte> actionsToPlay = AdditiveActionsGameActionsGenerator.PlaySpecifiedDecisions(chancePlaintiffQuality: chancePlaintiffQuality, chanceDefendantQuality: chanceDefendantQuality, chanceNeitherQuality: chanceNeitherQuality, chancePlaintiffBias: chancePlaintiffBias, chanceDefendantBias: chanceDefendantBias, chanceNeitherBias: chanceNeitherBias, pOffer: 3, dOffer: 2);
            var gameProgress = AdditiveEvidenceGameLauncher.PlayAdditiveEvidenceGameOnce(gameOptions, actionsToPlay);
            gameProgress.SettlementOccurs.Should().BeFalse();
            gameProgress.TrialOccurs.Should().BeTrue();

            gameProgress.QualitySum.Should().BeApproximately((gameOptions.Alpha_Both_Quality * gameOptions.Evidence_Both_Quality + gameOptions.Alpha_Plaintiff_Quality * chancePQualityDouble + gameOptions.Alpha_Defendant_Quality * chanceDQualityDouble + gameOptions.Alpha_Neither_Quality * chanceNQualityDouble) / (gameOptions.Alpha_Both_Quality + gameOptions.Alpha_Plaintiff_Quality + gameOptions.Alpha_Defendant_Quality + gameOptions.Alpha_Neither_Quality), 1E-10);
            gameProgress.QualitySum_PInfoOnly.Should().BeApproximately((gameOptions.Alpha_Both_Quality * gameOptions.Evidence_Both_Quality + gameOptions.Alpha_Plaintiff_Quality * chancePQualityDouble) / (gameOptions.Alpha_Both_Quality + gameOptions.Alpha_Plaintiff_Quality), 1E-10);
            gameProgress.QualitySum_DInfoOnly.Should().BeApproximately((gameOptions.Alpha_Both_Quality * gameOptions.Evidence_Both_Quality + gameOptions.Alpha_Defendant_Quality * chanceDQualityDouble) / (gameOptions.Alpha_Both_Quality + gameOptions.Alpha_Defendant_Quality), 1E-10);

            gameProgress.BiasSum.Should().BeApproximately((gameOptions.Alpha_Both_Bias * gameOptions.Evidence_Both_Bias + gameOptions.Alpha_Plaintiff_Bias * chancePBiasDouble + gameOptions.Alpha_Defendant_Bias * chanceDBiasDouble + gameOptions.Alpha_Neither_Bias * chanceNBiasDouble) / (gameOptions.Alpha_Both_Bias + gameOptions.Alpha_Plaintiff_Bias + gameOptions.Alpha_Defendant_Bias + gameOptions.Alpha_Neither_Bias), 1E-10);
            gameProgress.BiasSum_PInfoOnly.Should().BeApproximately((gameOptions.Alpha_Both_Bias * gameOptions.Evidence_Both_Bias + gameOptions.Alpha_Plaintiff_Bias * chancePBiasDouble) / (gameOptions.Alpha_Both_Bias + gameOptions.Alpha_Plaintiff_Bias), 1E-10);
            gameProgress.BiasSum_DInfoOnly.Should().BeApproximately((gameOptions.Alpha_Both_Bias * gameOptions.Evidence_Both_Bias + gameOptions.Alpha_Defendant_Bias * chanceDBiasDouble) / (gameOptions.Alpha_Both_Bias + gameOptions.Alpha_Defendant_Bias), 1E-10);

            gameProgress.TrialValueIfOccurs.Should().BeApproximately((gameOptions.Alpha_Quality * gameProgress.QualitySum + gameOptions.Alpha_Bias * gameProgress.BiasSum), 1E-10);
        }

        [TestMethod]
        public void AdditiveEvidence_InformationSets()
        {
            throw new NotImplementedException();

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
