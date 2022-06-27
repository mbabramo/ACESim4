using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ACESim;
using ACESim.Util;
using ACESimBase.Games.AdditiveEvidenceGame;
using ACESimBase.Util;
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
                };
                if (options.Alpha_Both_Quality + options.Alpha_Plaintiff_Quality + options.Alpha_Defendant_Quality <= 1.0 && options.Alpha_Both_Bias + options.Alpha_Plaintiff_Bias + options.Alpha_Defendant_Bias <= 1.0)
                    return options;
            }
        }

        public AdditiveEvidenceGameOptions GetOptions_DariMattiacci_Saraceno(double evidenceBothQuality, double feeShiftingThreshold = 0)
        {
            // P's strength of information about bias is the same as the strength of the case. Half of weight is on quality
            var options = GetOptions(evidenceBothQuality: evidenceBothQuality, alphaBothBias: 0, alphaPBias: evidenceBothQuality, alphaDBias: 1.0 - evidenceBothQuality, alphaQuality: 0.5, alphaBothQuality: 1.0, alphaPQuality: 0, alphaDQuality: 0, feeShifting: true, feeShiftingThreshold: feeShiftingThreshold);
            return options;
        }

        [TestMethod]
        public void AdditiveEvidence_SettlementValues()
        {
            var gameOptions = GetOptions();
            foreach (int pOffer in new[] { 1, 2, 3, 4, 5 })
            {
                foreach (int dOffer in new[] { 1, 2, 3, 4, 5 })
                {
                    if (dOffer >= pOffer)
                    {
                        Func<Decision, GameProgress, byte> actionsToPlay = AdditiveActionsGameActionsGenerator.PlaySpecifiedDecisions(pOffer: (byte)pOffer, dOffer: (byte)dOffer);
                        var gameProgress = AdditiveEvidenceGameLauncher.PlayAdditiveEvidenceGameOnce(gameOptions, actionsToPlay);
                        gameProgress.SettlementOccurs.Should().Be(true);
                        gameProgress.TrialOccurs.Should().Be(false);
                        double expectedSettlementValue = gameOptions.MinOffer + gameOptions.OfferRange * 0.5 * (EquallySpaced.GetLocationOfEquallySpacedPoint(pOffer - 1, gameOptions.NumOffers, false) + EquallySpaced.GetLocationOfEquallySpacedPoint(dOffer - 1, gameOptions.NumOffers, false));
                        double? observedSettlementValue = gameProgress.SettlementValue;
                        observedSettlementValue.Should().BeApproximately(expectedSettlementValue, 1E-10);
                        gameProgress.ResolutionValue.Should().BeApproximately(expectedSettlementValue, 1E-10);
                        gameProgress.DsProportionOfCost.Should().Be(0.5);
                        gameProgress.PWelfare.Should().BeApproximately(expectedSettlementValue, 1E-10);
                        gameProgress.DWelfare.Should().BeApproximately(1.0 - expectedSettlementValue, 1E-10);
                    }
                }
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

                var q = 0.6;
                var t = gameOptions.FeeShiftingThreshold;
                gameOptions = GetOptions_DariMattiacci_Saraceno(q, t);
                var gameProgress = AdditiveEvidence_TrialValue_Helper(gameOptions, chancePlaintiffQuality, chanceDefendantQuality, chanceNeitherQuality, chancePlaintiffBias, chanceDefendantBias, chanceNeitherBias, gameOptions.FeeShifting, gameOptions.FeeShiftingIsBasedOnMarginOfVictory, gameOptions.FeeShiftingThreshold);
                DMSCalc c = new DMSCalc(gameOptions.FeeShiftingThreshold, gameOptions.TrialCost, q);
                var result = c.GetOutcome(gameProgress.Chance_Plaintiff_Bias_Continuous, gameProgress.Chance_Defendant_Bias_Continuous, gameProgress.POfferContinuousIfMade, gameProgress.DOfferContinuousIfMade);

                result.outcome.pNet.Should().BeApproximately(gameProgress.PWelfare, 0.01);
            }
        }

        [TestMethod]
        public void AdditiveEvidence_DMSCalculations()
        {
            DMSCalc dmsCalcHighCost = new DMSCalc(0.5, 0.5, 0.5);
            string tableOfCases = dmsCalcHighCost.ProduceTableOfCases();
            string expected = @"1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
2,1,1,1,1,1,3
2,2,1,1,1,3,3
2,2,2,1,3,3,3
2,2,2,5,5,3,3
2,2,5,5,5,5,3
5,5,5,5,5,5,5
5,5,5,5,5,5,5
5,5,5,5,5,5,5
5,5,5,5,5,5,5
5,5,5,5,5,5,5
5,5,5,5,5,5,5
5,5,5,5,5,5,5
5,5,5,5,5,5,5
";
            tableOfCases.Should().Be(expected);
            DMSCalc dmsCalcLowCost = new DMSCalc(0.5, 0.02, 0.5);
            tableOfCases = dmsCalcLowCost.ProduceTableOfCases();
            expected = @"1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
1,1,1,1,1,1,1
2,1,1,1,1,1,3
2,2,1,1,1,3,3
2,2,2,1,3,3,3
2,2,2,4,4,3,3
2,2,4,4,4,4,3
4,4,4,4,4,4,4
4,4,4,4,4,4,4
4,4,4,4,4,4,4
4,4,4,4,4,4,4
4,4,4,4,4,4,4
4,4,4,4,4,4,4
4,4,4,4,4,4,4
5,5,5,5,5,5,5
";
            tableOfCases.Should().Be(expected);
            var dmsCalc_T80_C10_Q50 = new DMSCalc(0.8, 0.10, 0.5); // should be case 4A
            dmsCalc_T80_C10_Q50.pPiecewiseLinearRanges.Count.Should().Be(3);
            // Make sure that linear ranges span from 0 to 1. Also, make sure that we get the right range index for a number in the range.
            int counter = 0;
            for (double tvar = 0.0; tvar <= 1.01; tvar += 0.05)
            {
                foreach (double cvar in new double[] {0, 0.02, 0.1, 0.2, 0.5, 1.0})
                {
                    for (double qvar = 0.35; qvar <= 0.65; qvar += 0.05)
                    {
                        DMSCalc dmsCalc = new DMSCalc(tvar, cvar, qvar);
                        for (int i = 0; i < dmsCalc.pPiecewiseLinearRanges.Count; i++)
                        {
                            var range = dmsCalc.pPiecewiseLinearRanges[i];
                            if (i == 0)
                                range.low.Should().Be(0);
                            if (i == dmsCalc.pPiecewiseLinearRanges.Count - 1)
                                range.high.Should().Be(1);
                            else range.high.Should().Be(dmsCalc.pPiecewiseLinearRanges[i + 1].low); // high should be low of next range
                            double midpoint = 0.5 * range.low + 0.5 * range.high;
                            byte rangeIndex = dmsCalc.GetPiecewiseLinearRangeIndex(midpoint, plaintiff: true);
                            rangeIndex.Should().Be((byte)i);
                            counter++;
                            dmsCalc.GetTruncatedDistanceFromStartOfLinearRange(midpoint, true, 0).Should().BeApproximately(midpoint - range.low, 0.001);
                            dmsCalc.GetTruncatedDistanceFromStartOfLinearRange(midpoint, true, 0.45).Should().BeApproximately(midpoint - range.low, 0.001); // this truncation has no effect
                            dmsCalc.GetTruncatedDistanceFromStartOfLinearRange(midpoint, true, 0.55).Should().BeGreaterThan(midpoint - range.low); // bottom 70% is truncated, so we end up higher than midpoint.
                        }
                        for (int i = 0; i < dmsCalc.dPiecewiseLinearRanges.Count; i++)
                        {
                            var range = dmsCalc.dPiecewiseLinearRanges[i];
                            if (i == 0)
                                range.low.Should().Be(0);
                            if (i == dmsCalc.dPiecewiseLinearRanges.Count - 1)
                                range.high.Should().Be(1);
                            else range.high.Should().Be(dmsCalc.dPiecewiseLinearRanges[i + 1].low); // high should be low of next range
                            double midpoint = 0.5 * range.low + 0.5 * range.high;
                            byte rangeIndex = dmsCalc.GetPiecewiseLinearRangeIndex(midpoint, plaintiff: false);
                            rangeIndex.Should().Be((byte)i);
                            counter++;
                            dmsCalc.GetTruncatedDistanceFromStartOfLinearRange(midpoint, false, 0).Should().BeApproximately(midpoint - range.low, 0.001);
                            dmsCalc.GetTruncatedDistanceFromStartOfLinearRange(midpoint, false, 0.45).Should().BeApproximately(midpoint - range.low, 0.001); // this truncation has no effect
                            dmsCalc.GetTruncatedDistanceFromStartOfLinearRange(midpoint, false, 0.55).Should().BeLessThan(midpoint - range.low); // this truncation has an effect since we truncate after 45% of the range.
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void AdditiveEvidence_LineSegment()
        {
            var x = new List<(double low, double high)>() { (0, 0.3), (0.3, 0.6), (0.6, 1.0) };
            var y = new List<double>() { 0.1, 0.5, 0.8 };
            var slope = 1.0;

            var results = LineSegment.GetTruncatedLineSegments(x, y, slope, 0.15, true);
            results.Count.Should().Be(3); // last two should be combined
            results[0].slope.Should().Be(0);
            y = new List<double>() { 0.1, 0.5, 0.85 };
            results = LineSegment.GetTruncatedLineSegments(x, y, slope, 0.15, true);
            results.Count.Should().Be(4);
            results[0].slope.Should().Be(0);
            results = LineSegment.GetTruncatedLineSegments(x, y, slope, 0.6, true);
            results.Count.Should().Be(3); // first two should be combined now
            results[0].slope.Should().Be(0);

            results = LineSegment.GetTruncatedLineSegments(x, y, slope, 0.85, false);
            results.Count.Should().Be(3); 
            results = LineSegment.GetTruncatedLineSegments(x, y, slope, 0.5, false);
            results.Count.Should().Be(2);
            results = LineSegment.GetTruncatedLineSegments(x, y, slope, 0.05, false);
            results.Count.Should().Be(1);

            bool RangesApproximatelyEqual((double start, double end) n1, (double start, double end) n2) => Math.Abs(n1.start - n2.start) < 1E-12 && Math.Abs(n1.end - n2.end) < 1E-12;

            Random r = new Random(0);
            for (int i = 0; i < 10_000; i++)
            {
                double a = Math.Round(r.NextDouble(), 2), b = Math.Round(r.NextDouble(), 2), c = Math.Round(r.NextDouble(), 2), d = Math.Round(r.NextDouble(), 2);
                LineSegment s1 = new LineSegment(Math.Min(a, b), Math.Max(a, b), Math.Round(r.NextDouble(), 2), Math.Round(r.NextDouble(), 2));
                LineSegment s2 = new LineSegment(Math.Min(c, d), Math.Max(c, d), Math.Round(r.NextDouble(), 2), Math.Round(r.NextDouble(), 2));
                
                var pairs = s1.GetPairsOfNonoverlappingAndEntirelyOverlappingYRanges(s2);
                foreach (var pair in pairs)
                {
                    if (!RangesApproximatelyEqual((pair.l1.yStart, pair.l1.yEnd), (pair.l2.yStart,pair.l2.yEnd)) && pair.l1.yStart + 1E-12 < pair.l2.yEnd && pair.l2.yStart + 1E-12 < pair.l1.yEnd)
                        throw new Exception("Pair y ranges are not equal but overlapping");
                }
                var pairs_firstItems = pairs.Select(x => x.l1).Distinct().OrderBy(x => x.xStart).ToArray();
                var pairs_secondItems = pairs.Select(x => x.l2).Distinct().OrderBy(x => x.xStart).ToArray();
                for (int j = 0; j < pairs_firstItems.Length - 1; j++)
                {
                    if (pairs_firstItems[j].xEnd != pairs_firstItems[j + 1].xStart || Math.Abs(pairs_firstItems[j].yEnd - pairs_firstItems[j + 1].yStart) > 1E-12)
                        throw new Exception();
                }
                for (int j = 0; j < pairs_secondItems.Length - 1; j++)
                {
                    if (pairs_secondItems[j].xEnd != pairs_secondItems[j + 1].xStart || Math.Abs(pairs_secondItems[j].yEnd - pairs_secondItems[j + 1].yStart) > 1E-12)
                        throw new Exception();
                }
            }
        }

        [TestMethod]
        public void AdditiveEvidence_PiecewiseLinear()
        {
            byte numBiasLevels = 20;
            byte numOffers = 20;
            byte numTruncationLevels = AdditiveEvidenceGameOptions.NumTruncationPortions;

            var tOptions = Enumerable.Range(0, 6).Select(x => 0.2 * x).ToArray();
            var cOptions = Enumerable.Range(0, 26).Select(x => 0 + x * 0.04).ToArray();
            var qOptions = Enumerable.Range(0, 7).Select(x => 0.35 + x * 0.05).ToArray();
            var chancePlaintiffBiasIndexOptions = Enumerable.Range(0, numBiasLevels).Select(x => (byte)(x + 1)).ToArray();
            var chanceDefendantBiasIndexOptions = Enumerable.Range(0, numBiasLevels).Select(x => (byte)(x + 1)).ToArray();
            var pSlopeIndexOptions = Enumerable.Range(0, AdditiveEvidenceGameOptions.PiecewiseLinearBidsSlopeOptions.Length).Select(x => (byte)(x + 1)).ToArray();
            var dSlopeIndexOptions = Enumerable.Range(0, AdditiveEvidenceGameOptions.PiecewiseLinearBidsSlopeOptions.Length).Select(x => (byte)(x + 1)).ToArray();
            var pMinValIndexOptions = Enumerable.Range(0, numOffers).Select(x => (byte)(x + 1)).ToArray();
            var dMinValIndexOptions = Enumerable.Range(0, numOffers).Select(x => (byte)(x + 1)).ToArray();
            var pTruncationOptions = Enumerable.Range(0, numTruncationLevels).Select(x => (byte)(x + 1)).ToArray();
            var dTruncationOptions = Enumerable.Range(0, numTruncationLevels).Select(x => (byte)(x + 1)).ToArray();



            Random r = new Random(0);
            T GetRandom<T>(T[] items) => items[r.Next(items.Length)];

            int numRepetitions = 1_000; 
            for (int i = 0; i < numRepetitions; i++)
            {
                double t = GetRandom(tOptions);
                double c = GetRandom(cOptions);
                double q = GetRandom(qOptions);
                byte chancePlaintiffBiasIndex = GetRandom(chancePlaintiffBiasIndexOptions);
                byte chanceDefendantBiasIndex = GetRandom(chanceDefendantBiasIndexOptions);
                byte pSlopeIndex = GetRandom(pSlopeIndexOptions);
                byte dSlopeIndex = GetRandom(dSlopeIndexOptions);
                byte pMinValIndex = GetRandom(pMinValIndexOptions);
                byte dMinValIndex = GetRandom(dMinValIndexOptions);
                byte pTruncationIndex = GetRandom(pTruncationOptions);
                byte dTruncationIndex = GetRandom(dTruncationOptions);

                var gameOptions = GetOptions(feeShifting: true, feeShiftingBasedOnMarginOfVictory: false, feeShiftingThreshold: t, trialCost: c, evidenceBothQuality: q, numOffers: numOffers, numQualityAndBiasLevels: numBiasLevels);
                gameOptions.PiecewiseLinearBids = true;

                Func<Decision, GameProgress, byte> actionsToPlay = AdditiveActionsGameActionsGenerator.PlaySpecifiedDecisions(chancePlaintiffBias: chancePlaintiffBiasIndex, chanceDefendantBias: chanceDefendantBiasIndex, pSlope: pSlopeIndex, dSlope: dSlopeIndex, pMinValForRange: pMinValIndex, dMinValForRange: dMinValIndex, pTruncationPortion: pTruncationIndex, dTruncationPortion: dTruncationIndex);
                var gameProgress = AdditiveEvidenceGameLauncher.PlayAdditiveEvidenceGameOnce(gameOptions, actionsToPlay);
                gameProgress.PiecewiseLinearCalcs.T.Should().Be(t);
                gameProgress.PiecewiseLinearCalcs.C.Should().Be(c);
                gameProgress.PiecewiseLinearCalcs.Q.Should().Be(q);
                gameProgress.Chance_Plaintiff_Bias.Should().Be(chancePlaintiffBiasIndex);
                gameProgress.Chance_Defendant_Bias.Should().Be(chanceDefendantBiasIndex);
                gameProgress.PSlope.Should().Be(AdditiveEvidenceGameOptions.PiecewiseLinearBidsSlopeOptions[pSlopeIndex - 1]);
                gameProgress.DSlope.Should().Be(AdditiveEvidenceGameOptions.PiecewiseLinearBidsSlopeOptions[dSlopeIndex - 1]);
                gameProgress.PMinValueForRange.Should().Be(EquallySpaced.GetLocationOfEquallySpacedPoint(pMinValIndex - 1 /* make it zero-based */, numOffers, false));
                gameProgress.DMinValueForRange.Should().Be(EquallySpaced.GetLocationOfEquallySpacedPoint(dMinValIndex - 1 /* make it zero-based */, numOffers, false));
                gameProgress.PTruncationPortion.Should().Be(EquallySpaced.GetLocationOfEquallySpacedPoint(pTruncationIndex - 1, numTruncationLevels, true));
                gameProgress.DTruncationPortion.Should().Be(EquallySpaced.GetLocationOfEquallySpacedPoint(dTruncationIndex - 1, numTruncationLevels, true));
                var dmsCalcs = gameProgress.PiecewiseLinearCalcs;
                byte pSignal = (byte) (dmsCalcs.GetPiecewiseLinearRangeIndex(gameProgress.Chance_Plaintiff_Bias_Continuous, true) + 1);
                pSignal.Should().Be(gameProgress.Chance_Plaintiff_Bias_Reduction);
                byte dSignal = (byte)(dmsCalcs.GetPiecewiseLinearRangeIndex(gameProgress.Chance_Defendant_Bias_Continuous, false) + 1);
                dSignal.Should().Be(gameProgress.Chance_Defendant_Bias_Reduction);
                double pBid = dmsCalcs.GetPiecewiseLinearBidTruncated(gameProgress.Chance_Plaintiff_Bias_Continuous, true, gameProgress.PMinValueForRange, gameProgress.PSlope, gameProgress.PTruncationPortion);
                gameProgress.PiecewiseLinearPBid.Should().Be(pBid);
                double dBid = dmsCalcs.GetPiecewiseLinearBidTruncated(gameProgress.Chance_Defendant_Bias_Continuous, false, gameProgress.DMinValueForRange, gameProgress.DSlope, gameProgress.DTruncationPortion);
                gameProgress.PiecewiseLinearDBid.Should().Be(dBid);
                gameProgress.SettlementOccurs.Should().Be(dBid >= pBid);
                if (gameProgress.SettlementOccurs)
                    gameProgress.SettlementValue.Should().Be(0.5 * (pBid + dBid));
                // DEBUG Debug.WriteLine(gameProgress.Chance_Plaintiff_Bias_Reduction + " " + gameProgress.Chance_Defendant_Bias_Reduction);

                gameProgress.GameComplete.Should().BeTrue();

                //gameProgress.SomeoneQuits.Should().Be(false);
                //gameProgress.PQuits.Should().Be(false);
                //gameProgress.DQuits.Should().Be(false);
                //gameProgress.SettlementOccurs.Should().Be(false);
                //gameProgress.TrialOccurs.Should().Be(false);
                //gameProgress.SettlementValue.Should().BeNull();
                //gameProgress.ResolutionValue.Should().BeApproximately(1.0, 1E-10);
                //gameProgress.PWelfare.Should().BeApproximately(1.0, 1E-10);
                //gameProgress.DWelfare.Should().BeApproximately(0, 1E-10);
                //gameProgress.POfferContinuousOrNull.Should().BeNull();
                //gameProgress.DOfferContinuousOrNull.Should().BeNull();
            }
        }

        private IEnumerable<DMSCalc> GetRandomOptions(bool useFriedmanWittman = false)
        {
            byte numBiasLevels = 20;
            byte numOffers = 20;
            byte numTruncationLevels = AdditiveEvidenceGameOptions.NumTruncationPortions;

            var tOptions = useFriedmanWittman ? new double[] { 0 } : Enumerable.Range(0, 6).Select(x => 0.2 * x).ToArray();
            var cOptions = Enumerable.Range(0, 40).Select(x => 0 + x * 0.04).ToArray();
            var qOptions = useFriedmanWittman ? new double[] { 0.5 } : Enumerable.Range(0, 7).Select(x => 0.35 + x * 0.05).ToArray();
            var chancePlaintiffBiasIndexOptions = Enumerable.Range(0, numBiasLevels).Select(x => (byte)(x + 1)).ToArray();
            var chanceDefendantBiasIndexOptions = Enumerable.Range(0, numBiasLevels).Select(x => (byte)(x + 1)).ToArray();
            var pSlopeIndexOptions = Enumerable.Range(0, AdditiveEvidenceGameOptions.PiecewiseLinearBidsSlopeOptions.Length).Select(x => (byte)(x + 1)).ToArray();
            var dSlopeIndexOptions = Enumerable.Range(0, AdditiveEvidenceGameOptions.PiecewiseLinearBidsSlopeOptions.Length).Select(x => (byte)(x + 1)).ToArray();
            var pMinValIndexOptions = Enumerable.Range(0, numOffers).Select(x => (byte)(x + 1)).ToArray();
            var dMinValIndexOptions = Enumerable.Range(0, numOffers).Select(x => (byte)(x + 1)).ToArray();
            var pTruncationOptions = Enumerable.Range(0, numTruncationLevels).Select(x => (byte)(x + 1)).ToArray();
            var dTruncationOptions = Enumerable.Range(0, numTruncationLevels).Select(x => (byte)(x + 1)).ToArray();

            Random r = new Random(0);
            T GetRandom<T>(T[] items) => items[r.Next(items.Length)];

            while (true)
            {
                double t = GetRandom(tOptions);
                double c = GetRandom(cOptions);
                double q = GetRandom(qOptions);
                DMSCalc dmsCalc = new DMSCalc(t, c, q);
                yield return dmsCalc;
            }
        }

        [TestMethod]
        public void AdditiveEvidence_FWOutcomesVerification()
        {
            // Note: This tests whether the calculation of P and D are correct by comparing an analytical and empirical result.
            // But we have not completed the code to allow analytical calculation of results outside the Friedman-Wittman subset.
            bool friedmanWittmanOnly = true;
            IEnumerator<DMSCalc> optionsGenerator = GetRandomOptions(friedmanWittmanOnly).GetEnumerator();

            int numRepetitions = 1000;
            for (int i = 0; i < numRepetitions; i++)
            {
                optionsGenerator.MoveNext();

                DMSCalc dmsCalc = optionsGenerator.Current;

                var correctStrategyPretruncation = dmsCalc.GetCorrectStrategiesPretruncation();
                bool overlap = !((correctStrategyPretruncation.p.MaxVal() < correctStrategyPretruncation.d.MinVal() && correctStrategyPretruncation.p.MinVal() < correctStrategyPretruncation.d.MinVal()) ||
                (correctStrategyPretruncation.d.MaxVal() < correctStrategyPretruncation.p.MinVal() && correctStrategyPretruncation.d.MinVal() < correctStrategyPretruncation.p.MinVal()));
                if (!overlap)
                    continue;
                var analytical = new DMSCalc.DMSStrategiesPair(correctStrategyPretruncation.p, correctStrategyPretruncation.d, dmsCalc, true);
                if (analytical.Nontrivial == false)
                    continue;
                var empirical = new DMSCalc.DMSStrategiesPair(correctStrategyPretruncation.p, correctStrategyPretruncation.d, dmsCalc, false);
                const double marginForRounding = 1E-2;
                Math.Abs(analytical.PNet - empirical.PNet).Should().BeLessThan(marginForRounding);
                Math.Abs(analytical.DNet - empirical.DNet).Should().BeLessThan(marginForRounding);
                Math.Abs(analytical.SettlementProportion - empirical.SettlementProportion).Should().BeLessThan(marginForRounding);

            }
        }

        [TestMethod]
        public void AdditiveEvidence_VerifyEquilibria()
        {
            bool friedmanWittmanOnly = false; // other DMS equilibria not yet implemented (except single segment)
            IEnumerator<DMSCalc> optionsGenerator = GetRandomOptions(friedmanWittmanOnly).GetEnumerator();

            int numRepetitions = 1_000;
            for (int i = 0; i < numRepetitions; i++)
            {
                optionsGenerator.MoveNext();

                DMSCalc dmsCalc = optionsGenerator.Current;

                if (i == 556749)
                {
                    var DEBUG = "asdf";
                    var DEBUG2 = new DMSCalc(dmsCalc.T, dmsCalc.C, dmsCalc.Q);
                }

                var correctStrategyPretruncation = dmsCalc.GetCorrectStrategiesPretruncation();
                var correctStrategyTruncated = new DMSCalc.DMSStrategiesPair(correctStrategyPretruncation.p, correctStrategyPretruncation.d, dmsCalc, true);

                if (dmsCalc.manyEquilibria)
                    continue;
                bool limitToSingleSegmentEquilibria = true; // this will have no relevance for FW, which are all single segment.
                if (limitToSingleSegmentEquilibria && (dmsCalc.pPiecewiseLinearRanges.Count != 1 || dmsCalc.pPiecewiseLinearRanges.Count != 1))
                    continue;
                bool limitToStrategiesBetween0And1 = false; // this will have no relevance for FW either -- it's only with fee shifting that the strategy might not be between 0 and 1
                if (limitToStrategiesBetween0And1 && (correctStrategyPretruncation.p.MinVal() < 0 || correctStrategyPretruncation.d.MaxVal() > 1))
                    continue;

                var pUntruncatedAlternatives = correctStrategyPretruncation.p.EnumerateStrategiesDifferingInOneRange().ToList();
                var dUntruncatedAlternatives = correctStrategyPretruncation.d.EnumerateStrategiesDifferingInOneRange().ToList();

                double dCorrectMin = correctStrategyPretruncation.d.MinVal();
                double pCorrectMax = correctStrategyPretruncation.p.MaxVal();
                var pStrategiesWithTruncation = pUntruncatedAlternatives.Select(x => x.GetStrategyWithTruncation(dCorrectMin, true)).ToList();
                var dStrategiesWithTruncation = dUntruncatedAlternatives.Select(x => x.GetStrategyWithTruncation(pCorrectMax, false)).ToList();

                double maxDeviationForRounding = friedmanWittmanOnly ? 1E-5 : 1E-2;

                foreach (var dStrategyTruncated in dStrategiesWithTruncation)
                {
                    var altStrategyPair = new DMSCalc.DMSStrategiesPair(correctStrategyTruncated.pStrategy, dStrategyTruncated, dmsCalc, true);
                    if (altStrategyPair.DNet > correctStrategyTruncated.DNet + maxDeviationForRounding)
                        goto onFail;
                }
                foreach (var pStrategyPotentiallyTruncated in pStrategiesWithTruncation)
                {
                    var altStrategyPair = new DMSCalc.DMSStrategiesPair(pStrategyPotentiallyTruncated, correctStrategyTruncated.dStrategy, dmsCalc, true);
                    if (altStrategyPair.PNet > correctStrategyTruncated.PNet + maxDeviationForRounding)
                    {
                        //Debug.WriteLine(dmsCalc);
                        //Debug.WriteLine($"Correct P {correctStrategyTruncated.pStrategy}");
                        //Debug.WriteLine($"Correct D {correctStrategyTruncated.dStrategy}");
                        //Debug.WriteLine($"Correct P vs. Correct D result: {correctStrategyTruncated}");
                        //Debug.WriteLine($"Better P {pStrategyPotentiallyTruncated}");
                        //Debug.WriteLine($"Better P vs. Correct D result: {altStrategyPair}");
                        goto onFail;
                    }
                }

                continue;

            onFail:
                Debug.WriteLine($"Problem with {i}: Not true equilibrium. DMSCalc {dmsCalc} case num {dmsCalc.CaseNum} num piecewise ranges {dmsCalc.pPiecewiseLinearRanges.Count}, {dmsCalc.dPiecewiseLinearRanges.Count}");
                // DEBUG throw new Exception("Not true equilibrium.");


                //for (int i1 = 0; i1 < strategyPairs.Count; i1++)
                //{
                //    DMSCalc.DMSStrategiesPair strategyPair = strategyPairs[i1];
                //    if (strategyPair.pStrategy.index == 0 && strategyPair.dStrategy.index == 0)
                //        strategyPairs[i1] = new DMSCalc.DMSStrategiesPair(DEBUG_CorrectStrategy.p, DEBUG_CorrectStrategy.d, dmsCalc);
                //    else if (strategyPair.pStrategy.index == 0)
                //        strategyPairs[i1] = new DMSCalc.DMSStrategiesPair(DEBUG_CorrectStrategy.p, dUntruncatedStrategies[strategyPairs[i1].dStrategy.index], dmsCalc);
                //    else if (strategyPair.dStrategy.index == 0)
                //        strategyPairs[i1] = new DMSCalc.DMSStrategiesPair(pUntruncatedStrategies[strategyPairs[i1].pStrategy.index], DEBUG_CorrectStrategy.d, dmsCalc);
                //}
                //double[,] pUtilities = new double[pStrategiesWithTruncation.Count, dStrategiesWithTruncation.Count];
                //double[,] dUtilities = new double[pStrategiesWithTruncation.Count, dStrategiesWithTruncation.Count];
                //foreach (var strategyPair in strategyPairs)
                //{
                //    pUtilities[strategyPair.pStrategy.index, strategyPair.dStrategy.index] = strategyPair.PNet;
                //    dUtilities[strategyPair.pStrategy.index, strategyPair.dStrategy.index] = strategyPair.DNet;
                //}
                //var pMatrixString = Matrix.ToString(pUtilities);
                //var dMatrixString = Matrix.ToString(dUtilities);
                //var combinedMatrixString = pMatrixString + "\n" + dMatrixString;

                //List<(int pIndex, int dIndex)> equilibriaIndices = PureStrategiesFinder.ComputeNashEquilibria(pUtilities, dUtilities, false);
                //DMSCalc.DMSStrategiesPair GetEq((int pIndex, int dIndex) eq) => strategyPairs.First(x => x.pStrategy.index == eq.pIndex && x.dStrategy.index == eq.dIndex);
                //IEnumerable<((int pIndex, int dIndex) eq, DMSCalc.DMSStrategiesPair)> equilibria = equilibriaIndices.Select(eq => (eq, GetEq(eq)));
                //var nonTrivialEquilibria = equilibria.Where(x => x.Item2.Nontrivial).ToList();
            }
        }

        private static AdditiveEvidenceGameProgress AdditiveEvidence_TrialValue_Helper(AdditiveEvidenceGameOptions gameOptions, byte chancePlaintiffQuality, byte chanceDefendantQuality, byte chanceNeitherQuality, byte chancePlaintiffBias, byte chanceDefendantBias, byte chanceNeitherBias, bool feeShifting, bool basedOnMarginOfVictory, double feeShiftingThreshold)
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
                    if (Math.Abs(trialValue - 0.5) > 1E-12)
                    {
                        if (!pWins && gameProgress.ThetaD_Generalized < feeShiftingThreshold)
                            feeShiftingShouldOccur = true;
                        else if (pWins && gameProgress.ThetaP_Generalized > 1 - feeShiftingThreshold)
                            feeShiftingShouldOccur = true;
                    }
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
            return gameProgress;
        }

        private static void GetOptionsAndProgress(AdditiveEvidenceGameOptions gameOptions, byte chancePlaintiffQuality, byte chanceDefendantQuality, byte chanceNeitherQuality, byte chancePlaintiffBias, byte chanceDefendantBias, byte chanceNeitherBias, byte pOffer, byte dOffer, out double chancePQualityDouble, out double chanceDQualityDouble, out double chanceNQualityDouble, out double chancePBiasDouble, out double chanceDBiasDouble, out double chanceNBiasDouble, out AdditiveEvidenceGameProgress gameProgress)
        {
            chancePQualityDouble = EquallySpaced.GetLocationOfEquallySpacedPoint(chancePlaintiffQuality - 1, gameOptions.NumQualityAndBiasLevels_PrivateInfo, false);
            chanceDQualityDouble = EquallySpaced.GetLocationOfEquallySpacedPoint(chanceDefendantQuality - 1, gameOptions.NumQualityAndBiasLevels_PrivateInfo, false); 
            chanceNQualityDouble = EquallySpaced.GetLocationOfEquallySpacedPoint(chanceNeitherQuality - 1, gameOptions.NumQualityAndBiasLevels_NeitherInfo, false); 
            chancePBiasDouble = EquallySpaced.GetLocationOfEquallySpacedPoint(chancePlaintiffBias - 1, gameOptions.NumQualityAndBiasLevels_PrivateInfo, false); 
            chanceDBiasDouble = EquallySpaced.GetLocationOfEquallySpacedPoint(chanceDefendantBias - 1, gameOptions.NumQualityAndBiasLevels_PrivateInfo, false);
            chanceNBiasDouble = EquallySpaced.GetLocationOfEquallySpacedPoint(chanceNeitherBias - 1, gameOptions.NumQualityAndBiasLevels_NeitherInfo, false); 
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
