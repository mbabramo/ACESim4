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
                gameProgress.PWelfare.Should().BeApproximately(settlementValue, 1E-10);
                gameProgress.DWelfare.Should().BeApproximately(0 - settlementValue, 1E-10);
            }
        }

        [TestMethod]
        public void AdditiveEvidence_TrialValue()
        {
            Random r = new Random(1);
            for (int i = 0; i < 10_000; i++)
            {
                var gameOptions = GetOptions();

                gameOptions.FeeShifting = r.Next(0, 2) == 0;
                gameOptions.FeeShiftingIsBasedOnMarginOfVictory = r.Next(0, 2) == 0;
                gameOptions.FeeShiftingThreshold = r.NextDouble();

                byte chancePlaintiffQuality = (byte) r.Next(1, 6);
                byte chanceDefendantQuality = (byte)r.Next(1, 6);
                byte chanceNeitherQuality = (byte)r.Next(1, 6);
                byte chancePlaintiffBias = (byte)r.Next(1, 6);
                byte chanceDefendantBias = (byte)r.Next(1, 6);
                byte chanceNeitherBias = (byte)r.Next(1, 6);
                AdditiveEvidence_TrialValue_Helper(gameOptions, chancePlaintiffQuality, chanceDefendantQuality, chanceNeitherQuality, chancePlaintiffBias, chanceDefendantBias, chanceNeitherBias, gameOptions.FeeShifting, gameOptions.FeeShiftingIsBasedOnMarginOfVictory, gameOptions.FeeShiftingThreshold);

                gameOptions = GetOptions_DariMattiacci_Saraceno(6.0);
                AdditiveEvidence_TrialValue_Helper(gameOptions, chancePlaintiffQuality, chanceDefendantQuality, chanceNeitherQuality, chancePlaintiffBias, chanceDefendantBias, chanceNeitherBias, gameOptions.FeeShifting, gameOptions.FeeShiftingIsBasedOnMarginOfVictory, gameOptions.FeeShiftingThreshold);
            }

        }

        private static void AdditiveEvidence_TrialValue_Helper(AdditiveEvidenceGameOptions gameOptions, byte chancePlaintiffQuality, byte chanceDefendantQuality, byte chanceNeitherQuality, byte chancePlaintiffBias, byte chanceDefendantBias, byte chanceNeitherBias, bool feeShifting, bool basedOnMarginOfVictory, double feeShiftingThreshold)
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

            double trialValue = (gameOptions.Alpha_Quality * gameProgress.QualitySum + gameOptions.Alpha_Bias * gameProgress.BiasSum);
            gameProgress.TrialValueIfOccurs.Should().BeApproximately(trialValue, 1E-10);
            gameProgress.SettlementOrJudgment.Should().BeApproximately(trialValue, 1E-10);

            if (feeShifting)
            {
                bool pWins = trialValue > 0.5;
                bool feeShiftingShouldOccur = false;
                if (basedOnMarginOfVictory)
                {
                    if (pWins && trialValue > 1 - feeShiftingThreshold)
                        feeShiftingShouldOccur = true;
                    else if (!pWins && trialValue < feeShiftingThreshold)
                        feeShiftingShouldOccur = true;
                    if (feeShiftingThreshold > 0.5)
                        feeShiftingShouldOccur.Should().BeTrue(); // only makes a difference between 0 and 0.5
                }
                else
                {
                    if (!pWins && gameProgress.AnticipatedTrialValue_DInfo < feeShiftingThreshold)
                        feeShiftingShouldOccur = true;
                    else if (pWins && gameProgress.AnticipatedTrialValue_PInfo > 1 - feeShiftingThreshold)
                        feeShiftingShouldOccur = true;
                }
                gameProgress.ShiftingOccurs.Should().Be(feeShiftingShouldOccur);
                if (feeShiftingShouldOccur)
                    gameProgress.DsProportionOfCost.Should().Be(pWins ? 1.0 : 0.0);
                else
                    gameProgress.DsProportionOfCost.Should().Be(0.5);
                gameProgress.PTrialEffect.Should().BeApproximately(gameProgress.TrialValueIfOccurs - (1.0 - gameProgress.DsProportionOfCost) * gameOptions.TrialCost, 1E-10);
                gameProgress.DTrialEffect.Should().BeApproximately(0 - gameProgress.TrialValueIfOccurs - gameProgress.DsProportionOfCost * gameOptions.TrialCost, 1E-10);
            }
            else
            {
                gameProgress.ShiftingOccurs.Should().Be(false);
                gameProgress.PTrialEffect.Should().BeApproximately(gameProgress.TrialValueIfOccurs - 0.5 * gameOptions.TrialCost, 1E-10);
                gameProgress.DTrialEffect.Should().BeApproximately(0 - gameProgress.TrialValueIfOccurs - 0.5 * gameOptions.TrialCost, 1E-10);
            }
            gameProgress.PWelfare.Should().Be(gameProgress.PTrialEffect);
            gameProgress.DWelfare.Should().Be(gameProgress.DTrialEffect);
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
