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
        private AdditiveEvidenceGameOptions GetOptions(double evidenceBothQuality = 0.6, double evidenceBothBias = 0.7, double alphaQuality = 0.6, double alphaBothQuality = 0.55, double alphaPQuality = 0.20, double alphaDQuality = 0.15, double alphaBothBias = 0.40, double alphaPBias = 0.10, double alphaDBias = 0.15, byte numQualityAndBiasLevels = 10, byte numOffers = 5, double trialCost = 0.1, bool feeShifting = false, bool feeShiftingBasedOnMarginOfVictory = false, double feeShiftingThreshold = 0.7, double minOffer = -0.5, double offerRange = 2.0)
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
                NumQualityAndBiasLevels_PrivateInfo = numQualityAndBiasLevels,
                NumOffers = numOffers,
                TrialCost = trialCost,
                FeeShifting = feeShifting,
                FeeShiftingIsBasedOnMarginOfVictory = feeShiftingBasedOnMarginOfVictory,
                FeeShiftingThreshold = feeShiftingThreshold,
                MinOffer = minOffer,
                OfferRange = offerRange
            };
            (options.Alpha_Both_Quality + options.Alpha_Plaintiff_Quality + options.Alpha_Defendant_Quality + options.Alpha_Neither_Quality).Should().BeApproximately(1.0, 0.000001);
            (options.Alpha_Both_Bias + options.Alpha_Plaintiff_Bias + options.Alpha_Defendant_Bias + options.Alpha_Neither_Bias).Should().BeApproximately(1.0, 0.000001);
            return options;
        }

        private AdditiveEvidenceGameOptions GetOptions(Random r, byte numQualityAndBiasLevels = 10, byte numOffers = 5, double trialCost = 0.1, bool feeShifting = false, bool feeShiftingBasedOnMarginOfVictory = false, double feeShiftingThreshold = 0.7)
        {
            double z() => r.NextDouble() < 0.3 ? 0 : r.NextDouble();
            while (true)
            {
                var options = new AdditiveEvidenceGameOptions()
                {
                    Evidence_Both_Quality = z(),
                    Evidence_Both_Bias = z(),
                    Alpha_Quality = z(),
                    Alpha_Both_Quality = z(),
                    Alpha_Plaintiff_Quality = z(),
                    Alpha_Defendant_Quality = z(),
                    Alpha_Both_Bias = z(),
                    Alpha_Plaintiff_Bias = z(),
                    Alpha_Defendant_Bias = z(),
                    NumQualityAndBiasLevels_PrivateInfo = numQualityAndBiasLevels,
                    NumOffers = numOffers,
                    TrialCost = trialCost,
                    FeeShifting = feeShifting,
                    FeeShiftingIsBasedOnMarginOfVictory = feeShiftingBasedOnMarginOfVictory,
                    FeeShiftingThreshold = feeShiftingThreshold,
                    LinearBids = false // not tested
                };
                if (options.Alpha_Both_Quality + options.Alpha_Plaintiff_Quality + options.Alpha_Defendant_Quality <= 1.0 && options.Alpha_Both_Bias + options.Alpha_Plaintiff_Bias + options.Alpha_Defendant_Bias <= 1.0)
                    return options;
            }
        }

        public AdditiveEvidenceGameOptions GetOptions_DariMattiacci_Saraceno(double evidenceBothQuality)
        {
            return GetOptions(evidenceBothQuality: evidenceBothQuality, alphaBothBias: 0, alphaPBias: evidenceBothQuality, alphaDBias: 1.0 - evidenceBothQuality, alphaQuality: 0.5); // P's strength of information about bias is the same as the strength of the case. Half of weight is on quality
        }

        [TestMethod]
        public void AdditiveEvidence_SettlementValues()
        {
            var gameOptions = GetOptions();
            foreach ((int pOffer, int dOffer, double settlementValue) in new[] { (3, 3, 0.5), (2, 4, 0.5), (1, 5, 0.5), (1, 3, gameOptions.MinOffer + gameOptions.OfferRange * 2.0 / 6.0), (1, 4, gameOptions.MinOffer + gameOptions.OfferRange * 2.5 / 6.0), (2, 5, gameOptions.MinOffer + gameOptions.OfferRange * 3.5 / 6.0), (3, 5, gameOptions.MinOffer + gameOptions.OfferRange * 4.0 / 6.0), (4, 5, gameOptions.MinOffer + gameOptions.OfferRange * 4.5 / 6.0) }) 
            {
                Func<Decision, GameProgress, byte> actionsToPlay = AdditiveActionsGameActionsGenerator.PlaySpecifiedDecisions(pOffer: (byte)pOffer, dOffer: (byte)dOffer);
                var gameProgress = AdditiveEvidenceGameLauncher.PlayAdditiveEvidenceGameOnce(gameOptions, actionsToPlay);
                gameProgress.SettlementOccurs.Should().Be(true);
                gameProgress.TrialOccurs.Should().Be(false);
                gameProgress.SettlementValue.Should().BeApproximately(settlementValue, 1E-10);
                gameProgress.ResolutionValue.Should().BeApproximately(settlementValue, 1E-10);
                gameProgress.DsProportionOfCost.Should().Be(0.5);
                gameProgress.PWelfare.Should().BeApproximately(settlementValue, 1E-10);
                gameProgress.DWelfare.Should().BeApproximately(1.0 - settlementValue, 1E-10);
            }
        }

        [TestMethod]
        public void AdditiveEvidence_PQuits()
        {
            var gameOptions = GetOptions();
            gameOptions.IncludePQuitDecision = true;
            gameOptions.IncludeDQuitDecision = true;
            Func<Decision, GameProgress, byte> actionsToPlay = AdditiveActionsGameActionsGenerator.PlaySpecifiedDecisions(pQuit: true, dQuit: false);
            var gameProgress = AdditiveEvidenceGameLauncher.PlayAdditiveEvidenceGameOnce(gameOptions, actionsToPlay);
            gameProgress.SomeoneQuits.Should().Be(true);
            gameProgress.PQuits.Should().Be(true);
            gameProgress.DQuits.Should().Be(false);
            gameProgress.SettlementOccurs.Should().Be(false);
            gameProgress.TrialOccurs.Should().Be(false);
            gameProgress.SettlementValue.Should().BeNull();
            gameProgress.ResolutionValue.Should().BeApproximately(0, 1E-10);
            gameProgress.PWelfare.Should().BeApproximately(0, 1E-10);
            gameProgress.DWelfare.Should().BeApproximately(1.0, 1E-10); // remember this is a game about splitting an asset
            gameProgress.POfferContinuousOrNull.Should().BeNull();
            gameProgress.DOfferContinuousOrNull.Should().BeNull();
        }

        [TestMethod]
        public void AdditiveEvidence_DQuits()
        {
            var gameOptions = GetOptions();
            gameOptions.IncludePQuitDecision = true;
            gameOptions.IncludeDQuitDecision = true;
            Func<Decision, GameProgress, byte> actionsToPlay = AdditiveActionsGameActionsGenerator.PlaySpecifiedDecisions(pQuit: false, dQuit: true);
            var gameProgress = AdditiveEvidenceGameLauncher.PlayAdditiveEvidenceGameOnce(gameOptions, actionsToPlay);
            gameProgress.SomeoneQuits.Should().Be(true);
            gameProgress.PQuits.Should().Be(false);
            gameProgress.DQuits.Should().Be(true);
            gameProgress.SettlementOccurs.Should().Be(false);
            gameProgress.TrialOccurs.Should().Be(false);
            gameProgress.SettlementValue.Should().BeNull();
            gameProgress.ResolutionValue.Should().BeApproximately(1.0, 1E-10);
            gameProgress.PWelfare.Should().BeApproximately(1.0, 1E-10);
            gameProgress.DWelfare.Should().BeApproximately(0, 1E-10);
            gameProgress.POfferContinuousOrNull.Should().BeNull();
            gameProgress.DOfferContinuousOrNull.Should().BeNull();
        }

        [TestMethod]
        public void AdditiveEvidence_SpecificCase()
        {
            var gameOptions = AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.50, 0.5, 0.5, 0.2, 0.15, false, false, 0.25, false);

            gameOptions.FeeShifting = true;
            gameOptions.FeeShiftingIsBasedOnMarginOfVictory = false;
            gameOptions.FeeShiftingThreshold = 0.25;

            byte chancePlaintiffQuality = (byte)2;
            byte chanceDefendantQuality = (byte)2;
            byte chanceNeitherQuality = (byte)1;
            byte chancePlaintiffBias = (byte)1;
            byte chanceDefendantBias = (byte)1;
            byte chanceNeitherBias = (byte)2;
            AdditiveEvidence_TrialValue_Helper(gameOptions, chancePlaintiffQuality, chanceDefendantQuality, chanceNeitherQuality, chancePlaintiffBias, chanceDefendantBias, chanceNeitherBias, gameOptions.FeeShifting, gameOptions.FeeShiftingIsBasedOnMarginOfVictory, gameOptions.FeeShiftingThreshold);

        }

        [TestMethod]
        public void AdditiveEvidence_TrialValue()
        {
            Random r = new Random(1);
            for (int i = 0; i < 2_000; i++)
            {
                var gameOptions = GetOptions(r);

                gameOptions.FeeShifting = r.Next(0, 2) == 0;
                gameOptions.FeeShiftingIsBasedOnMarginOfVictory = r.Next(0, 2) == 0;
                gameOptions.FeeShiftingThreshold = r.NextDouble();

                byte chancePlaintiffQuality = (byte)r.Next(1, 6);
                byte chanceDefendantQuality = (byte)r.Next(1, 6);
                byte chanceNeitherQuality = (byte)r.Next(1, 6);
                byte chancePlaintiffBias = (byte)r.Next(1, 6);
                byte chanceDefendantBias = (byte)r.Next(1, 6);
                byte chanceNeitherBias = (byte)r.Next(1, 6);
                AdditiveEvidence_TrialValue_Helper(gameOptions, chancePlaintiffQuality, chanceDefendantQuality, chanceNeitherQuality, chancePlaintiffBias, chanceDefendantBias, chanceNeitherBias, gameOptions.FeeShifting, gameOptions.FeeShiftingIsBasedOnMarginOfVictory, gameOptions.FeeShiftingThreshold);

                gameOptions = GetOptions_DariMattiacci_Saraceno(0.6);
                AdditiveEvidence_TrialValue_Helper(gameOptions, chancePlaintiffQuality, chanceDefendantQuality, chanceNeitherQuality, chancePlaintiffBias, chanceDefendantBias, chanceNeitherBias, gameOptions.FeeShifting, gameOptions.FeeShiftingIsBasedOnMarginOfVictory, gameOptions.FeeShiftingThreshold);
            }

        }

        private static void AdditiveEvidence_TrialValue_Helper(AdditiveEvidenceGameOptions gameOptions, byte chancePlaintiffQuality, byte chanceDefendantQuality, byte chanceNeitherQuality, byte chancePlaintiffBias, byte chanceDefendantBias, byte chanceNeitherBias, bool feeShifting, bool basedOnMarginOfVictory, double feeShiftingThreshold)
        {
            GetOptionsAndProgress(gameOptions, chancePlaintiffQuality, chanceDefendantQuality, chanceNeitherQuality, chancePlaintiffBias, chanceDefendantBias, chanceNeitherBias, 3, 2, out double chancePQualityDouble, out double chanceDQualityDouble, out double chanceNQualityDouble, out double chancePBiasDouble, out double chanceDBiasDouble, out double chanceNBiasDouble, out AdditiveEvidenceGameProgress gameProgress);
            gameProgress.SettlementOccurs.Should().BeFalse();
            gameProgress.TrialOccurs.Should().BeTrue();

            static double dOr0(double n, double d) => d == 0 ? 0 : n / d; // avoid division by zero

            if (gameOptions.Alpha_Quality > 0)
            {
                gameProgress.QualitySum.Should().BeApproximately(dOr0((gameOptions.Alpha_Both_Quality * gameOptions.Evidence_Both_Quality + gameOptions.Alpha_Plaintiff_Quality * chancePQualityDouble + gameOptions.Alpha_Defendant_Quality * chanceDQualityDouble + gameOptions.Alpha_Neither_Quality * chanceNQualityDouble), (gameOptions.Alpha_Both_Quality + gameOptions.Alpha_Plaintiff_Quality + gameOptions.Alpha_Defendant_Quality + gameOptions.Alpha_Neither_Quality)), 1E-10);
                gameProgress.QualitySum_PInfoOnly.Should().BeApproximately(dOr0((gameOptions.Alpha_Both_Quality * gameOptions.Evidence_Both_Quality + gameOptions.Alpha_Plaintiff_Quality * chancePQualityDouble), (gameOptions.Alpha_Both_Quality + gameOptions.Alpha_Plaintiff_Quality)), 1E-10);
                gameProgress.QualitySum_DInfoOnly.Should().BeApproximately(dOr0((gameOptions.Alpha_Both_Quality * gameOptions.Evidence_Both_Quality + gameOptions.Alpha_Defendant_Quality * chanceDQualityDouble), (gameOptions.Alpha_Both_Quality + gameOptions.Alpha_Defendant_Quality)), 1E-10);
            }

            if (gameOptions.Alpha_Bias > 0)
            {
                gameProgress.BiasSum.Should().BeApproximately(dOr0((gameOptions.Alpha_Both_Bias * gameOptions.Evidence_Both_Bias + gameOptions.Alpha_Plaintiff_Bias * chancePBiasDouble + gameOptions.Alpha_Defendant_Bias * chanceDBiasDouble + gameOptions.Alpha_Neither_Bias * chanceNBiasDouble), (gameOptions.Alpha_Both_Bias + gameOptions.Alpha_Plaintiff_Bias + gameOptions.Alpha_Defendant_Bias + gameOptions.Alpha_Neither_Bias)), 1E-10);
                gameProgress.BiasSum_PInfoOnly.Should().BeApproximately(dOr0((gameOptions.Alpha_Both_Bias * gameOptions.Evidence_Both_Bias + gameOptions.Alpha_Plaintiff_Bias * chancePBiasDouble), (gameOptions.Alpha_Both_Bias + gameOptions.Alpha_Plaintiff_Bias)), 1E-10);
                gameProgress.BiasSum_DInfoOnly.Should().BeApproximately(dOr0((gameOptions.Alpha_Both_Bias * gameOptions.Evidence_Both_Bias + gameOptions.Alpha_Defendant_Bias * chanceDBiasDouble), (gameOptions.Alpha_Both_Bias + gameOptions.Alpha_Defendant_Bias)), 1E-10);
            }

            double trialValue = (gameOptions.Alpha_Quality * gameProgress.QualitySum + gameOptions.Alpha_Bias * gameProgress.BiasSum);
            gameProgress.TrialValuePreShiftingIfOccurs.Should().BeApproximately(trialValue, 1E-10);
            gameProgress.ResolutionValue.Should().BeApproximately(trialValue, 1E-10);

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
                gameProgress.PTrialEffect.Should().BeApproximately(gameProgress.TrialValuePreShiftingIfOccurs - (1.0 - gameProgress.DsProportionOfCost) * gameOptions.TrialCost, 1E-10);
                gameProgress.DTrialEffect.Should().BeApproximately(1.0 - gameProgress.TrialValuePreShiftingIfOccurs - gameProgress.DsProportionOfCost * gameOptions.TrialCost, 1E-10);
            }
            else
            {
                gameProgress.ShiftingOccurs.Should().Be(false);
                gameProgress.PTrialEffect.Should().BeApproximately(gameProgress.TrialValuePreShiftingIfOccurs - 0.5 * gameOptions.TrialCost, 1E-10);
                gameProgress.DTrialEffect.Should().BeApproximately(1.0 - gameProgress.TrialValuePreShiftingIfOccurs - 0.5 * gameOptions.TrialCost, 1E-10);
            }
            gameProgress.PWelfare.Should().Be(gameProgress.PTrialEffect);
            gameProgress.DWelfare.Should().Be(gameProgress.DTrialEffect);
        }

        private static void GetOptionsAndProgress(AdditiveEvidenceGameOptions gameOptions, byte chancePlaintiffQuality, byte chanceDefendantQuality, byte chanceNeitherQuality, byte chancePlaintiffBias, byte chanceDefendantBias, byte chanceNeitherBias, byte pOffer, byte dOffer, out double chancePQualityDouble, out double chanceDQualityDouble, out double chanceNQualityDouble, out double chancePBiasDouble, out double chanceDBiasDouble, out double chanceNBiasDouble, out AdditiveEvidenceGameProgress gameProgress)
        {
            chancePQualityDouble = (chancePlaintiffQuality) / (gameOptions.NumQualityAndBiasLevels_PrivateInfo + 1.0);
            chanceDQualityDouble = (chanceDefendantQuality) / (gameOptions.NumQualityAndBiasLevels_PrivateInfo + 1.0);
            chanceNQualityDouble = (chanceNeitherQuality) / (gameOptions.NumQualityAndBiasLevels_NeitherInfo + 1.0);
            chancePBiasDouble = (chancePlaintiffBias) / (gameOptions.NumQualityAndBiasLevels_PrivateInfo + 1.0);
            chanceDBiasDouble = (chanceDefendantBias) / (gameOptions.NumQualityAndBiasLevels_PrivateInfo + 1.0);
            chanceNBiasDouble = (chanceNeitherBias) / (gameOptions.NumQualityAndBiasLevels_NeitherInfo + 1.0);
            Func<Decision, GameProgress, byte> actionsToPlay = AdditiveActionsGameActionsGenerator.PlaySpecifiedDecisions(chancePlaintiffQuality: chancePlaintiffQuality, chanceDefendantQuality: chanceDefendantQuality, chanceNeitherQuality: chanceNeitherQuality, chancePlaintiffBias: chancePlaintiffBias, chanceDefendantBias: chanceDefendantBias, chanceNeitherBias: chanceNeitherBias, pOffer: pOffer, dOffer: dOffer);
            gameProgress = AdditiveEvidenceGameLauncher.PlayAdditiveEvidenceGameOnce(gameOptions, actionsToPlay);
        }

        [TestMethod]
        public void AdditiveEvidence_InformationSets()
        {
            Random r = new Random(1);
            for (int i = 0; i < 2_000; i++)
            {
                var gameOptions = r.NextDouble() > 0.5 ? GetOptions(r) : GetOptions_DariMattiacci_Saraceno(r.NextDouble());

                gameOptions.FeeShifting = r.Next(0, 2) == 0;
                gameOptions.FeeShiftingIsBasedOnMarginOfVictory = r.Next(0, 2) == 0;
                gameOptions.FeeShiftingThreshold = r.NextDouble();

                byte chancePlaintiffQuality = (byte)r.Next(1, 6);
                byte chanceDefendantQuality = (byte)r.Next(1, 6);
                byte chanceNeitherQuality = (byte)r.Next(1, 6);
                byte chancePlaintiffBias = (byte)r.Next(1, 6);
                byte chanceDefendantBias = (byte)r.Next(1, 6);
                byte chanceNeitherBias = (byte)r.Next(1, 6);

                bool playToTrial = r.NextDouble() > 0.5;

                AdditiveEvidence_InformationSets_Helper(gameOptions, chancePlaintiffQuality, chanceDefendantQuality, chanceNeitherQuality, chancePlaintiffBias, chanceDefendantBias, chanceNeitherBias, playToTrial);
            }
        }

        private static void AdditiveEvidence_InformationSets_Helper(AdditiveEvidenceGameOptions gameOptions, byte chancePlaintiffQuality, byte chanceDefendantQuality, byte chanceNeitherQuality, byte chancePlaintiffBias, byte chanceDefendantBias, byte chanceNeitherBias, bool playToTrial)
        {
            byte pOffer = 3;
            byte dOffer = playToTrial ? (byte)2 : (byte)4;
            GetOptionsAndProgress(gameOptions, chancePlaintiffQuality, chanceDefendantQuality, chanceNeitherQuality, chancePlaintiffBias, chanceDefendantBias, chanceNeitherBias, pOffer, dOffer, out double chancePQualityDouble, out double chanceDQualityDouble, out double chanceNQualityDouble, out double chancePBiasDouble, out double chanceDBiasDouble, out double chanceNBiasDouble, out AdditiveEvidenceGameProgress gameProgress);

            List<byte> pInfo = new List<byte>(), dInfo = new List<byte>(), rInfo = new List<byte>();
            if (gameOptions.Alpha_Quality > 0 && gameOptions.Alpha_Plaintiff_Quality > 0)
            {
                pInfo.Add(chancePlaintiffQuality);
                rInfo.Add(chancePlaintiffQuality);
            }
            if (gameOptions.Alpha_Quality > 0 && gameOptions.Alpha_Defendant_Quality > 0)
            {
                dInfo.Add(chanceDefendantQuality);
                rInfo.Add(chanceDefendantQuality);
            }

            if (gameOptions.Alpha_Bias > 0 && gameOptions.Alpha_Plaintiff_Bias > 0)
            {
                pInfo.Add(chancePlaintiffBias);
                rInfo.Add(chancePlaintiffBias);
            }
            if (gameOptions.Alpha_Bias > 0 && gameOptions.Alpha_Defendant_Bias > 0)
            {
                dInfo.Add(chanceDefendantBias);
                rInfo.Add(chanceDefendantBias);
            }

            rInfo.Add(pOffer);
            rInfo.Add(dOffer);

            if (playToTrial)
            {
                if (gameOptions.Alpha_Quality > 0 && gameOptions.Alpha_Neither_Quality > 0)
                    rInfo.Add(chanceNeitherQuality);
                if (gameOptions.Alpha_Bias > 0 && gameOptions.Alpha_Neither_Bias > 0)
                    rInfo.Add(chanceNeitherBias);
            }

            string expectedPString = String.Join(",", pInfo.ToArray());
            string expectedDString = String.Join(",", dInfo.ToArray());
            string expectedRString = String.Join(",", rInfo.ToArray());
            GetInformationSetStrings(gameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            pInformationSet.Should().Be(expectedPString);
            dInformationSet.Should().Be(expectedDString);
            resolutionSet.Should().Be(expectedRString);

            if (playToTrial)
                gameProgress.GameComplete.Should().BeTrue();
        }

        private static void GetInformationSetStrings(AdditiveEvidenceGameProgress AdditiveEvidenceGameProgress, out string pInformationSet,
            out string dInformationSet, out string resolutionSet)
        {
            pInformationSet = AdditiveEvidenceGameProgress.GameFullHistory.InformationSetLog.GetPlayerInformationAtPointString((byte)AdditiveEvidenceGamePlayers.Plaintiff, null);
            dInformationSet = AdditiveEvidenceGameProgress.GameFullHistory.InformationSetLog.GetPlayerInformationAtPointString((byte)AdditiveEvidenceGamePlayers.Defendant, null);
            resolutionSet = AdditiveEvidenceGameProgress.GameFullHistory.InformationSetLog.GetPlayerInformationAtPointString((byte)AdditiveEvidenceGamePlayers.Resolution, null);
            string pInformationSet2 = AdditiveEvidenceGameProgress.GameHistory.GetCurrentPlayerInformationString((byte)AdditiveEvidenceGamePlayers.Plaintiff);
            string dInformationSet2 = AdditiveEvidenceGameProgress.GameHistory.GetCurrentPlayerInformationString((byte)AdditiveEvidenceGamePlayers.Defendant);
            string resolutionSet2 = AdditiveEvidenceGameProgress.GameHistory.GetCurrentPlayerInformationString((byte)AdditiveEvidenceGamePlayers.Resolution);
            pInformationSet.Should().Be(pInformationSet2);
            dInformationSet.Should().Be(dInformationSet2);
            resolutionSet.Should().Be(resolutionSet2);
        }

    }
}
