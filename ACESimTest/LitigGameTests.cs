using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ACESim;
using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util;
using ACESimBase.Util.DiscreteProbabilities;
using FluentAssertions;
using JetBrains.Annotations;
using MathNet.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public class LitigGameTests
    {
        public enum HowToSimulateBargainingFailure
        {
            PRefusesToBargain,
            DRefusesToBargain,
            BothRefuseToBargain,
            BothAgreeToBargain,
            BothHaveNoChoiceAndMustBargain // i.e., we don't have the decisions representing agreement to bargain
        }


        public enum LoserPaysPolicy
        {
            NoLoserPays,
            AfterTrialOnly,
            EvenAfterAbandonOrDefault,
            MarginOfVictory_Enough,
            MarginOfVictory_NotEnough
        }

        public enum RunningSideBetChallenges
        {
            None,
            PChallenges2D1,
            DChallenges2P1
        }

        public enum SideBetChallenges
        {
            NoChallengesAllowed,
            NoOneChallenges,
            PChallenges,
            DChallenges,
            BothChallenge,
            Irrelevant // i.e., we're not getting to trial, so doesn't matter now
        }

        public enum ShootoutSettings
        {
            None,
            Regular,
            ApplyAfterAbandonment,
            AverageAllRounds,
            ApplyAfterAbandonment_AverageAllRounds
        }

        private const double PartyLiabilityNoise = 0.2, PartyDamagesNoise = 0.25, InitialWealth = 1_000_000, DamagesAlleged = 100_000, DamagesMinMaxRatio = 0.5, CostsMultiplier = 1.1, PFileCost = 3000, DAnswerCost = 2000, PTrialCosts = 4000, DTrialCosts = 6000, PerRoundBargainingCost = 1000, RegretAversion = 0.25, LoserPaysMultiple = 1.5, ValueOfChip = 8000;
        private const byte MaxChipsPerRound = 3;
        private const byte NumLiabilityStrengthPoints = 8;
        private const byte NumLiabilitySignals = 10;
        private const byte NumDamagesStrengthPoints = 5;
        private const byte NumDamagesSignals = 6;
        private const byte NumOffers = 16;
        private const byte NumCourtNoiseValues = 11;
        public const byte ValueWhenCaseSettles = 4;
        private const byte LiabilityStrength = 3;
        private const byte PLiabilitySignal = 4, DLiabilitySignal = 2;
        private const byte DamagesStrength = 4;
        private const byte PDamagesSignal = 2, DDamagesSignal = 3, CDamagesSignal = 3;
        private int CaseNumber;
        public double DamagesMultipleForChallengedToPay = 0.75;
        public double DamagesMultipleForChallengerToPay = 1.25;
        private (double pCosts, double dCosts)[] RoundSpecificBargainingCosts =  new (double pCosts, double dCosts)[] { (10, 20), (30, 40), (50, 60) };

        // for margin of victory
        const byte liabilityResultPWinsEnough = 9;
        const byte liabilityResultPWinsNotEnough = 6;
        const byte liabilityResultDWinsEnough = 1;
        const byte liabilityResultDWinsNotEnough = 4;

        private static void GetInformationSetStrings(LitigGameProgress myGameProgress, out string pInformationSet,
            out string dInformationSet, out string resolutionSet)
        {
            pInformationSet = myGameProgress.GameFullHistory.InformationSetLog.GetPlayerInformationAtPointString((byte) LitigGamePlayers.Plaintiff, null);
            dInformationSet = myGameProgress.GameFullHistory.InformationSetLog.GetPlayerInformationAtPointString((byte) LitigGamePlayers.Defendant, null);
            resolutionSet = myGameProgress.GameFullHistory.InformationSetLog.GetPlayerInformationAtPointString((byte) LitigGamePlayers.Resolution, null);
            string pInformationSet2 = myGameProgress.GameHistory.GetCurrentPlayerInformationString((byte) LitigGamePlayers.Plaintiff);
            string dInformationSet2 = myGameProgress.GameHistory.GetCurrentPlayerInformationString((byte) LitigGamePlayers.Defendant);
            string resolutionSet2 = myGameProgress.GameHistory.GetCurrentPlayerInformationString((byte) LitigGamePlayers.Resolution);
            pInformationSet.Should().Be(pInformationSet2);
            dInformationSet.Should().Be(dInformationSet2);
            resolutionSet.Should().Be(resolutionSet2);
        }

        private LitigGameOptions GetGameOptions(bool allowDamagesVariation, bool allowAbandonAndDefaults, byte numBargainingRounds, bool roundSpecificBargainingCosts, bool simultaneousBargainingRounds, bool simultaneousOffersUltimatelyRevealed, LoserPaysPolicy loserPaysPolicy, bool rule68, HowToSimulateBargainingFailure simulatingBargainingFailure, SideBetChallenges sideBetChallenges, RunningSideBetChallenges runningSideBetChallenges, ShootoutSettings shootout)
        {
            var options = new LitigGameOptions
            {
                PInitialWealth = InitialWealth,
                DInitialWealth = InitialWealth,
                DamagesMin = allowDamagesVariation ? DamagesAlleged * DamagesMinMaxRatio : DamagesAlleged,
                DamagesMax = DamagesAlleged,
                NumLiabilityStrengthPoints = NumLiabilityStrengthPoints,
                NumLiabilitySignals = NumLiabilitySignals,
                NumDamagesStrengthPoints = allowDamagesVariation ? NumDamagesStrengthPoints : (byte)0,
                NumDamagesSignals = allowDamagesVariation ? NumDamagesSignals : (byte)0,
                NumOffers = NumOffers,
                IncludeEndpointsForOffers = true,
                LitigGameDisputeGenerator = new LitigGameEqualQualityProbabilitiesDisputeGenerator
                {
                    ProbabilityTrulyLiable_LiabilityStrength75 = 0.75,
                    ProbabilityTrulyLiable_LiabilityStrength90 = 0.90,
                    NumPointsToDetermineTrulyLiable = 100
                },
                PLiabilityNoiseStdev = PartyLiabilityNoise,
                DLiabilityNoiseStdev = PartyLiabilityNoise,
                CourtLiabilityNoiseStdev = 0.5,
                PDamagesNoiseStdev = PartyDamagesNoise,
                DDamagesNoiseStdev = PartyDamagesNoise,
                CourtDamagesNoiseStdev = 0.25,
                PFilingCost = PFileCost,
                DAnswerCost = DAnswerCost,
                CostsMultiplier = CostsMultiplier,
                PTrialCosts = PTrialCosts,
                DTrialCosts = DTrialCosts,
                RegretAversion = RegretAversion,
                LitigGamePretrialDecisionGeneratorGenerator = sideBetChallenges == SideBetChallenges.NoChallengesAllowed ? null : new LitigGameSideBet { DamagesMultipleForChallengedToPay = DamagesMultipleForChallengedToPay, DamagesMultipleForChallengerToPay = DamagesMultipleForChallengerToPay },
                LitigGameRunningSideBets = runningSideBetChallenges == RunningSideBetChallenges.None ? null : new LitigGameRunningSideBets { MaxChipsPerRound = MaxChipsPerRound, ValueOfChip = ValueOfChip, TrialCostsMultiplierAsymptote = 1.0 },
                ShootoutSettlements = shootout != ShootoutSettings.None,
                ShootoutsApplyAfterAbandonment = shootout == ShootoutSettings.ApplyAfterAbandonment || shootout == ShootoutSettings.ApplyAfterAbandonment_AverageAllRounds,
                ShootoutStrength = 1.5,
                ShootoutOfferValueIsAveraged = shootout == ShootoutSettings.AverageAllRounds,
                LoserPays = loserPaysPolicy != LoserPaysPolicy.NoLoserPays,
                LoserPaysMultiple = LoserPaysMultiple,
                LoserPaysAfterAbandonment = loserPaysPolicy == LoserPaysPolicy.EvenAfterAbandonOrDefault,
                LoserPaysMarginOfVictoryThreshold = 0.75,
                LoserPaysOnlyLargeMarginOfVictory = loserPaysPolicy is LoserPaysPolicy.MarginOfVictory_Enough or LoserPaysPolicy.MarginOfVictory_NotEnough,
                Rule68 = rule68,
                PerPartyCostsLeadingUpToBargainingRound = PerRoundBargainingCost,
                RoundSpecificBargainingCosts = roundSpecificBargainingCosts ? RoundSpecificBargainingCosts : null,
                AllowAbandonAndDefaults = allowAbandonAndDefaults,
                PredeterminedAbandonAndDefaults = false,
                DeltaOffersOptions = new DeltaOffersOptions
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                IncludeAgreementToBargainDecisions = simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain,
                NumPotentialBargainingRounds = numBargainingRounds,
                BargainingRoundsSimultaneous = simultaneousBargainingRounds,
                SimultaneousOffersUltimatelyRevealed = simultaneousOffersUltimatelyRevealed,
                PGoesFirstIfNotSimultaneous = new List<bool> {true, false, true, false, true, false, true, false},
                IncludeSignalsReport = true
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator {InitialWealth = options.PInitialWealth};
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator {InitialWealth = options.DInitialWealth};
            return options;
        }

        private string ConstructExpectedResolutionSet_CaseSettles(byte litigationQuality, bool allowDamagesVariation, byte damagesStrength, bool pFiles, bool dAnswers, HowToSimulateBargainingFailure simulatingBargainingFailure, List<(byte? pMove, byte? dMove)> bargainingRounds, bool simultaneousBargainingRounds, bool simultaneousOffersUltimatelyRevealed, bool allowAbandonAndDefault, SideBetChallenges sideBetChallenges, RunningSideBetChallenges runningSideBetChallenges)
        {
            return ConstructExpectedResolutionSet(litigationQuality, allowDamagesVariation, damagesStrength, pFiles, dAnswers, simulatingBargainingFailure, bargainingRounds, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, true, allowAbandonAndDefault,
                false, false, false, false, 0, false, 0, sideBetChallenges, runningSideBetChallenges);
        }

        private string ConstructExpectedResolutionSet(byte liabilityStrength, bool allowDamagesVariation, byte damagesStrength, bool pFiles, bool dAnswers, HowToSimulateBargainingFailure simulatingBargainingFailure, List<(byte? pMove, byte? dMove)> bargainingRounds, bool simultaneousBargainingRounds, bool simultaneousOffersUltimatelyRevealed, bool settlementReachedInLastRoundCompleted, bool allowAbandonAndDefault, bool pReadyToAbandonLastRound, bool dReadyToDefaultLastRound, bool ifBothDefaultPlaintiffLoses, bool caseGoesToTrial, byte liabilityResultAtTrial, bool usingMarginOfVictory, byte damagesResultIfAllowVariation, SideBetChallenges sideBetChallenges, RunningSideBetChallenges runningSideBetChallenges)
        {
            var l = new List<byte> {liabilityStrength};
            if (allowDamagesVariation)
                l.Add(damagesStrength);
            if (pFiles)
            {
                l.Add(1);
                if (dAnswers)
                {
                    l.Add(1);
                    byte bargainingRound = 1;
                    int numBargainingRoundsCompleted = bargainingRounds.Count();
                    foreach (var moveSet in bargainingRounds)
                    {
                        bool pAgreesToBargain = moveSet.pMove != null;
                        bool dAgreesToBargain = moveSet.dMove != null;
                        if (simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain)
                        {
                            l.Add(pAgreesToBargain ? (byte) 1 : (byte) 2);
                            l.Add(dAgreesToBargain ? (byte) 1 : (byte) 2);
                        }
                        if (pAgreesToBargain && dAgreesToBargain)
                        {
                            bool pMovesFirst = bargainingRound % 2 == 1 || simultaneousBargainingRounds;
                            if (pMovesFirst)
                            {
                                l.Add((byte) moveSet.pMove);
                                l.Add((byte) moveSet.dMove);
                            }
                            else
                            {
                                l.Add((byte) moveSet.dMove);
                                l.Add((byte) moveSet.pMove);
                            }
                        }
                        bool isLastBargainingRoundCompleted = bargainingRound == numBargainingRoundsCompleted;
                        bool caseHasSettled = settlementReachedInLastRoundCompleted && isLastBargainingRoundCompleted;
                        if (runningSideBetChallenges != RunningSideBetChallenges.None && !caseHasSettled)
                        {
                            // note that action is 1 more than number of chips bet
                            byte pAction = runningSideBetChallenges == RunningSideBetChallenges.PChallenges2D1 ? (byte) 3 : (byte) 2;
                            byte dAction = runningSideBetChallenges == RunningSideBetChallenges.DChallenges2P1 ? (byte) 3 : (byte) 2;
                            l.Add(pAction);
                            l.Add(dAction);
                        }
                        if (allowAbandonAndDefault && (!settlementReachedInLastRoundCompleted || !isLastBargainingRoundCompleted)) // if a settlement was reached last round, we don't get to this decision
                        {
                            bool pReadyToAbandon = isLastBargainingRoundCompleted && pReadyToAbandonLastRound;
                            bool dReadyToDefault = isLastBargainingRoundCompleted && dReadyToDefaultLastRound;
                            l.Add(pReadyToAbandon ? (byte) 1 : (byte) 2);
                            l.Add(dReadyToDefault ? (byte) 1 : (byte) 2);
                            if (pReadyToAbandon && dReadyToDefault)
                                l.Add(ifBothDefaultPlaintiffLoses ? (byte) 1 : (byte) 2);
                        }
                        bargainingRound++;
                    }
                    if (caseGoesToTrial)
                    {
                        switch (sideBetChallenges)
                        {
                            case SideBetChallenges.NoChallengesAllowed:
                                break;
                            case SideBetChallenges.NoOneChallenges:
                                l.Add(2);
                                l.Add(2);
                                break;
                            case SideBetChallenges.PChallenges:
                                l.Add(1);
                                l.Add(2);
                                break;
                            case SideBetChallenges.DChallenges:
                                l.Add(2);
                                l.Add(1);
                                break;
                            case SideBetChallenges.BothChallenge:
                                l.Add(1);
                                l.Add(1);
                                break;
                            case SideBetChallenges.Irrelevant:
                                throw new Exception();
                            default:
                                throw new ArgumentOutOfRangeException(nameof(sideBetChallenges), sideBetChallenges, null);
                        }
                        l.Add(liabilityResultAtTrial);
                        bool pWins;
                        if (usingMarginOfVictory)
                            pWins = liabilityResultAtTrial is (liabilityResultPWinsEnough or liabilityResultPWinsNotEnough); 
                        else
                            pWins = liabilityResultAtTrial == 2;
                        if (pWins && allowDamagesVariation)
                            l.Add(damagesResultIfAllowVariation);
                    }
                }
                else
                {
                    l.Add(2); // d doesn't answer
                }
            }
            else
            {
                l.Add(2); // p doesn't file
            }
            return string.Join(",", l);
        }

        private List<(byte? pMove, byte? dMove)> GetBargainingRoundMoves(bool simultaneousBargaining, byte numRoundsToInclude, bool settlementInLastRound, HowToSimulateBargainingFailure simulatingBargainingFailure, out List<(byte offerMove, byte bargainingRoundNumber, bool isOfferToP)> offers)
        {
            offers = new List<(byte offerMove, byte bargainingRoundNumber, bool isOfferToP)>(); // we are also going to track offers; note that not every move is an offer
            bool ifNotSettlingPRefusesToBargain = simulatingBargainingFailure == HowToSimulateBargainingFailure.PRefusesToBargain || simulatingBargainingFailure == HowToSimulateBargainingFailure.BothRefuseToBargain;
            bool ifNotSettlingDRefusesToBargain = simulatingBargainingFailure == HowToSimulateBargainingFailure.DRefusesToBargain || simulatingBargainingFailure == HowToSimulateBargainingFailure.BothRefuseToBargain;
            var moves = new List<(byte? pMove, byte? dMove)>();
            for (byte b = 1; b <= numRoundsToInclude; b++)
            {
                bool settlingThisRound = settlementInLastRound && b == numRoundsToInclude;
                if (!settlingThisRound && (ifNotSettlingPRefusesToBargain || ifNotSettlingDRefusesToBargain))
                {
                    // Note that if one player refuses to bargain, then the other party's move will be ignored -- hence, the use of the dummy 255 here
                    moves.Add((ifNotSettlingPRefusesToBargain ? (byte?) null : (byte?) 255, ifNotSettlingDRefusesToBargain ? (byte?) null : (byte?) 255));
                }
                else
                {
                    if (simultaneousBargaining)
                    {
                        if (settlingThisRound)
                        {
                            moves.Add((ValueWhenCaseSettles, ValueWhenCaseSettles));
                        }
                        else
                        {
                            // we will have different ways of making settlement fail -- this will help us when testing regret aversion. The plaintiff may be regretful based on the generous offer from the defendant in bargaining round 2, and the defendant may be regretful based on the generous offer from the plaintiff in bargaining round 1.
                            if (b == 1)
                                moves.Add(((byte) (ValueWhenCaseSettles - 1), (byte) (ValueWhenCaseSettles - 2)));
                            else
                                moves.Add(((byte) (ValueWhenCaseSettles + 2), (byte) (ValueWhenCaseSettles + 1)));
                        }
                        offers.Add(((byte) moves.Last().pMove, b, false));
                        offers.Add(((byte) moves.Last().dMove, b, true));
                    }
                    else
                    {
                        byte offer = ValueWhenCaseSettles;
                        byte response = settlingThisRound ? (byte) 1 : (byte) 2;
                        bool plaintiffIsOffering = b % 2 == 1; // plaintiff offers in first round, which is odd numbered
                        if (plaintiffIsOffering)
                            moves.Add((offer, response));
                        else
                            moves.Add((response, offer));
                        offers.Add((offer, b, !plaintiffIsOffering));
                    }
                }
            }
            return moves;
        }

        private double? GetLastDOffer(List<(byte offerMove, byte bargainingRoundNumber, bool isOfferToP)> offers)
        {
            if (!offers.Any(x => x.isOfferToP))
                return null;
            var lastDOffer = offers.Last(x => x.isOfferToP);
            return EquallySpaced.GetLocationOfEquallySpacedPoint((byte)(lastDOffer.offerMove - 1), NumOffers, true);
        }

        private (double? bestRejectedOfferToP, double? bestRejectedOfferToD) GetBestOffers(
            List<(byte offerMove, byte bargainingRoundNumber, bool isOfferToP)> offers, LitigGameOptions options)
        {
            (double? bestRejectedOfferToP, double? bestRejectedOfferToD) bestOffers = (null, null);
            foreach (var offer in offers)
                if (offer.isOfferToP)
                {
                    CalculateBargainingRoundExpenses(offer.bargainingRoundNumber, options, out double pBargainingRoundExpenses, out double dBargainingRoundExpenses);
                    double offerToP = EquallySpaced.GetLocationOfEquallySpacedPoint((byte) (offer.offerMove - 1), NumOffers, true);
                    double roundAdjustedOfferToP = InitialWealth + offerToP * DamagesAlleged - PFileCost * CostsMultiplier - pBargainingRoundExpenses * CostsMultiplier;
                    if (bestOffers.bestRejectedOfferToP == null || roundAdjustedOfferToP > bestOffers.bestRejectedOfferToP)
                        bestOffers.bestRejectedOfferToP = roundAdjustedOfferToP;
                }
                else
                {
                    CalculateBargainingRoundExpenses(offer.bargainingRoundNumber, options, out double pBargainingRoundExpenses, out double dBargainingRoundExpenses);
                    double offerToD = EquallySpaced.GetLocationOfEquallySpacedPoint((byte) (offer.offerMove - 1), NumOffers, true);
                    double roundAdjustedOfferToD = InitialWealth - offerToD * DamagesAlleged - DAnswerCost * CostsMultiplier - dBargainingRoundExpenses * CostsMultiplier; 
                    if (bestOffers.bestRejectedOfferToD == null || roundAdjustedOfferToD > bestOffers.bestRejectedOfferToD)
                        bestOffers.bestRejectedOfferToD = roundAdjustedOfferToD;
                }
            return bestOffers;
        }

        private (string pInformationSet, string dInformationSet, string pInformationSetExplanations, string dInformationSetExplanations) ConstructExpectedPartyInformationSets(byte liabilityStrength, byte pLiabilitySignal, byte dLiabilitySignal, bool allowDamagesVariation, byte pDamagesSignal, byte dDamagesSignal, bool pFiles, bool dAnswers, HowToSimulateBargainingFailure simulatingBargainingFailure, RunningSideBetChallenges runningSideBetChallenges, List<(byte? pMove, byte? dMove)> bargainingMoves, bool simultaneousBargaining, bool simultaneousOffersUltimatelyRevealed, bool allowAbandonAndDefault, byte? pReadyToAbandonRound, byte? dReadyToDefaultRound)
        {
            double liabilityStrengthUniform =
                EquallySpaced.GetLocationOfEquallySpacedPoint(liabilityStrength - 1, NumLiabilityStrengthPoints, false);
            var pInfo = new List<byte> {pLiabilitySignal};
            var dInfo = new List<byte> {dLiabilitySignal};
            var pInfoExplanations = new List<string> { $"pLiabilitySignal {pLiabilitySignal}" };
            var dInfoExplanations = new List<string> { $"dLiabilitySignal {dLiabilitySignal}" };

            if (allowDamagesVariation)
            {
                pInfo.Add(pDamagesSignal);
                dInfo.Add(dDamagesSignal);
                pInfoExplanations.Add($"pDamagesSignal {pDamagesSignal}");
                dInfoExplanations.Add($"dDamagesSignal {dDamagesSignal}");
            }

            byte pFilesByte = pFiles ? (byte) 1 : (byte) 2;
            pInfo.Add(pFilesByte);
            pInfoExplanations.Add($"PFiles {pFilesByte}");
            dInfo.Add(pFilesByte);
            dInfoExplanations.Add($"PFiles {pFilesByte}");
            if (pFiles)
            {
                byte dAnswersByte = dAnswers ? (byte)1 : (byte)2;
                pInfo.Add(dAnswersByte);
                pInfoExplanations.Add($"DAnswers {dAnswersByte}");
                dInfo.Add(dAnswersByte);
                dInfoExplanations.Add($"DAnswers {dAnswersByte}");
            }

            if (pFiles && dAnswers)
            {
                int bargainingRoundCount = bargainingMoves.Count();
                bool resettingInfoEachBargainingRound = false;
                byte startingRound = resettingInfoEachBargainingRound ? (byte)bargainingRoundCount : (byte)1;
                if (bargainingRoundCount != 0)
                {
                    byte? pLastOffer = null, dLastOffer = null;
                    // run through all bargaining rounds, but only add offers to pInfo and dInfo in startingRound and later. 
                    var settled = false;
                    for (byte b = 1; b <= bargainingRoundCount; b++)
                    {
                        
                        (byte? pMove, byte? dMove) = bargainingMoves[b - 1];
                        if (simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain && b >= startingRound)
                        {
                            byte pBargains = pMove == null ? (byte)2 : (byte)1;
                            pInfo.Add(pBargains);
                            pInfoExplanations.Add($"pBargains {pBargains}");
                            dInfo.Add(pBargains);
                            dInfoExplanations.Add($"pBargains {pBargains}");
                            byte dBargains = dMove == null ? (byte)2 : (byte)1;
                            pInfo.Add(dBargains);
                            pInfoExplanations.Add($"dBargains {dBargains}");
                            dInfo.Add(dBargains);
                            dInfoExplanations.Add($"dBargains {dBargains}");
                        }
                        if (pMove != null && dMove != null)
                            if (simultaneousBargaining)
                            {
                                if (b >= startingRound)
                                {
                                    pInfo.Add((byte)pMove);
                                    if (simultaneousOffersUltimatelyRevealed)
                                    {
                                        dInfo.Add((byte)pMove);
                                        dInfoExplanations.Add($"pMove {pMove}");
                                        pInfo.Add((byte)dMove);
                                        pInfoExplanations.Add($"dMove {dMove}");
                                    }
                                    dInfo.Add((byte)dMove);
                                    dInfoExplanations.Add($"dMove {dMove}");
                                }
                                pLastOffer = pMove;
                                dLastOffer = dMove;
                                if (dMove >= pMove)
                                    settled = true;
                            }
                            else
                            {
                                if (b % 2 == 1)
                                {
                                    // plaintiff offers
                                    if (b >= startingRound)
                                    {
                                        dInfo.Add((byte)pMove);
                                        dInfoExplanations.Add($"pMove {pMove}");
                                        pInfo.Add((byte)pMove);
                                        pInfoExplanations.Add($"pMove {pMove}");
                                        pInfo.Add((byte)dMove); 
                                        pInfoExplanations.Add($"dMove {dMove}");
                                        dInfo.Add((byte)dMove); 
                                        dInfoExplanations.Add($"dMove {dMove}");
                                        if (dMove == 1)
                                            settled = true;
                                    }
                                    pLastOffer = pMove;
                                }
                                else
                                {
                                    // defendant offers
                                    if (b >= startingRound)
                                    {
                                        pInfo.Add((byte)dMove);
                                        pInfoExplanations.Add($"dMove {dMove}");
                                        dInfo.Add((byte)dMove);
                                        dInfoExplanations.Add($"dMove {dMove}");
                                        pInfo.Add((byte)pMove); 
                                        pInfoExplanations.Add($"pMove {pMove}");
                                        dInfo.Add((byte)pMove);
                                        dInfoExplanations.Add($"pMove {pMove}");
                                        if (pMove == 1)
                                            settled = true;
                                    }
                                    dLastOffer = dMove;
                                }
                            }

                        if (runningSideBetChallenges != RunningSideBetChallenges.None && b >= startingRound && !settled)
                        {
                            // Note that the action is one more than the challenge number, since we use action 1 for a challenge of 0.
                            byte pAction = runningSideBetChallenges == RunningSideBetChallenges.PChallenges2D1 ? (byte)3 : (byte)2;
                            byte dAction = runningSideBetChallenges == RunningSideBetChallenges.DChallenges2P1 ? (byte)3 : (byte)2;
                            pInfo.Add(pAction);
                            pInfoExplanations.Add($"Own side bet {pAction}");
                            pInfo.Add(dAction);
                            pInfoExplanations.Add($"Opponent side bet {dAction}");
                            dInfo.Add(pAction);
                            dInfoExplanations.Add($"Opponent side bet {pAction}");
                            dInfo.Add(dAction);
                            dInfoExplanations.Add($"Own side bet {dAction}");
                        }
                        if (allowAbandonAndDefault && !settled && b >= startingRound) // if a settlement was reached last round, we don't get to this decision
                        {
                            // Each player must have in its information set an indication that it has already made the abandon/default decision, so that when it doesn't abandon/default, it can distinguish this decision from later decisions
                            byte pAction = pReadyToAbandonRound == b ? (byte)1 : (byte)2;
                            pInfo.Add(pAction);
                            pInfoExplanations.Add($"pReadyToAbandonRound{b} {pAction}");
                            byte dAction = dReadyToDefaultRound == b ? (byte)1 : (byte)2;
                            dInfo.Add(dAction);
                            dInfoExplanations.Add($"dReadyToDefaultRound{b} {dAction}");
                        }
                    }
                }
            } // if filed & answered

            return (string.Join(",", pInfo), string.Join(",", dInfo), string.Join(",", pInfoExplanations), string.Join(",", dInfoExplanations));
        }

        private Func<Decision, GameProgress, byte> GetPlayerActions(bool pFiles, bool dAnswers, byte liabilityStrength, byte pLiabilitySignal, byte dLiabilitySignal, byte damagesStrength, byte pDamagesSignal, byte dDamagesSignal, HowToSimulateBargainingFailure simulatingBargainingFailure, SideBetChallenges sideBetChallenges, RunningSideBetChallenges runningSideBetChallenges, List<(byte? pMove, byte? dMove)> bargainingRoundMoves, bool simultaneousBargainingRounds, bool simultaneousOffersUltimatelyRevealed, byte? pReadyToAbandonRound = null, byte? dReadyToDefaultRound = null, byte mutualGiveUpResult = 0, byte courtLiabilityResult = 0, byte courtDamagesResult = 0)
        {
            var bargaining = new List<(byte decision, byte customInfo, byte action)>();
            for (byte b = 1; b <= bargainingRoundMoves.Count(); b++)
            {
                var bargainingRoundMove = bargainingRoundMoves[b - 1];
                var pMove = bargainingRoundMove.pMove;
                var dMove = bargainingRoundMove.dMove;
                if (simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain)
                {
                    bargaining.Add(((byte) LitigGameDecisions.PAgreeToBargain, b, pMove == null ? (byte) 2 : (byte) 1));
                    bargaining.Add(((byte) LitigGameDecisions.DAgreeToBargain, b, dMove == null ? (byte) 2 : (byte) 1));
                }
                if (pMove != null && dMove != null)
                    if (simultaneousBargainingRounds)
                    {
                        bargaining.Add(((byte) LitigGameDecisions.POffer, b, (byte) pMove));
                        bargaining.Add(((byte) LitigGameDecisions.DOffer, b, (byte) dMove));
                    }
                    else
                    {
                        if (b % 2 == 1)
                        {
                            bargaining.Add(((byte) LitigGameDecisions.POffer, b, (byte) pMove));
                            bargaining.Add(((byte) LitigGameDecisions.DResponse, b, (byte) dMove));
                        }
                        else
                        {
                            bargaining.Add(((byte) LitigGameDecisions.DOffer, b, (byte) dMove));
                            bargaining.Add(((byte) LitigGameDecisions.PResponse, b, (byte) pMove));
                        }
                    }
                bargaining.Add(((byte) LitigGameDecisions.PAbandon, b, pReadyToAbandonRound == b ? (byte) 1 : (byte) 2));
                bargaining.Add(((byte) LitigGameDecisions.DDefault, b, dReadyToDefaultRound == b ? (byte) 1 : (byte) 2));
            }
            var actionsToPlay = DefineActions.ForTest(
                new List<(byte decision, byte action)>
                {
                    ((byte) LitigGameDecisions.PostPrimaryActionChance, 17), // irrelevant -- just determines probability truly liable
                    ((byte) LitigGameDecisions.PFile, pFiles ? (byte) 1 : (byte) 2),
                    ((byte) LitigGameDecisions.DAnswer, dAnswers ? (byte) 1 : (byte) 2),
                    ((byte) LitigGameDecisions.LiabilityStrength, liabilityStrength),
                    ((byte) LitigGameDecisions.PLiabilitySignal, pLiabilitySignal),
                    ((byte) LitigGameDecisions.DLiabilitySignal, dLiabilitySignal),
                    ((byte) LitigGameDecisions.DamagesStrength, damagesStrength),
                    ((byte) LitigGameDecisions.PDamagesSignal, pDamagesSignal),
                    ((byte) LitigGameDecisions.DDamagesSignal, dDamagesSignal),
                    ((byte) LitigGameDecisions.PreBargainingRound, (byte) 1 /* only action -- dummy decision */),
                    ((byte) LitigGameDecisions.MutualGiveUp, mutualGiveUpResult), // we'll only reach this if both try to give up, so it won't be called in multiple bargaining rounds
                    ((byte) LitigGameDecisions.PostBargainingRound, 1 /* only action */),
                    // Notice that the chip actions are 1 more than the number of chips
                    ((byte) LitigGameDecisions.PChips, runningSideBetChallenges == RunningSideBetChallenges.None ? (byte) 0 : (runningSideBetChallenges == RunningSideBetChallenges.DChallenges2P1 ? (byte) 2 : (byte) 3)),
                    ((byte) LitigGameDecisions.DChips, runningSideBetChallenges == RunningSideBetChallenges.None ? (byte) 0 : (runningSideBetChallenges == RunningSideBetChallenges.DChallenges2P1 ? (byte) 3 : (byte) 2)),
                    ((byte) LitigGameDecisions.PPretrialAction, sideBetChallenges == SideBetChallenges.PChallenges || sideBetChallenges == SideBetChallenges.BothChallenge ? (byte) 1 : (byte) 2),
                    ((byte) LitigGameDecisions.DPretrialAction, sideBetChallenges == SideBetChallenges.DChallenges || sideBetChallenges == SideBetChallenges.BothChallenge ? (byte) 1 : (byte) 2),
                    ((byte) LitigGameDecisions.CourtDecisionLiability, courtLiabilityResult),
                    ((byte) LitigGameDecisions.CourtDecisionDamages, courtDamagesResult)
                },
                bargaining
            );
            return actionsToPlay;
        }

        [TestMethod]
        public void CaseGivenUp()
        {
            // settings
            CaseNumber = 0;
            for (byte numPotentialBargainingRounds = 1; numPotentialBargainingRounds <= 3; numPotentialBargainingRounds++)
                foreach (bool allowDamagesVariation in new[] { true, false })
                foreach (bool simultaneousBargainingRounds in new[] {true, false})
                foreach (bool simultaneousOffersUltimatelyRevealed in new[] { true, false })
                foreach (var loserPaysPolicy in new[] {LoserPaysPolicy.NoLoserPays, LoserPaysPolicy.AfterTrialOnly, LoserPaysPolicy.EvenAfterAbandonOrDefault})
                foreach (var simulatingBargainingFailure in new[] {HowToSimulateBargainingFailure.PRefusesToBargain, HowToSimulateBargainingFailure.DRefusesToBargain, HowToSimulateBargainingFailure.BothRefuseToBargain, HowToSimulateBargainingFailure.BothAgreeToBargain, HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain})
                foreach (var runningSideBetChallenges in new[] {RunningSideBetChallenges.None, RunningSideBetChallenges.PChallenges2D1, RunningSideBetChallenges.DChallenges2P1})
                foreach (var shootout in new[] { ShootoutSettings.None, ShootoutSettings.Regular, ShootoutSettings.AverageAllRounds, ShootoutSettings.ApplyAfterAbandonment, ShootoutSettings.ApplyAfterAbandonment_AverageAllRounds})
                {
                    // must check CaseNumber within CaseGivenUpVariousActions, which runs various case scenarios
                    bool incompatible = (runningSideBetChallenges != RunningSideBetChallenges.None && simulatingBargainingFailure == HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain) || (simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain && shootout != ShootoutSettings.None);
                    if (!incompatible)
                        CaseGivenUpVariousActions(allowDamagesVariation, numPotentialBargainingRounds, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, loserPaysPolicy, simulatingBargainingFailure, runningSideBetChallenges, shootout);
                }
        }

        private void CaseGivenUpVariousActions(bool allowDamagesVariation, byte numPotentialBargainingRounds, bool simultaneousBargainingRounds, bool simultaneousOffersUltimatelyRevealed, LoserPaysPolicy loserPaysPolicy, HowToSimulateBargainingFailure simulatingBargainingFailure, RunningSideBetChallenges runningSideBetChallenges, ShootoutSettings shootout)
        {
            try
            {
                for (byte abandonmentInRound = 0;
                    abandonmentInRound <= numPotentialBargainingRounds;
                    abandonmentInRound++)
                    foreach (bool plaintiffGivesUp in new[] {true, false})
                    foreach (bool defendantGivesUp in new[] {true, false})
                    foreach (bool plaintiffWinsIfBothGiveUp in new[] {true, false})
                    {
                        if (!plaintiffGivesUp && !defendantGivesUp)
                            continue; // not interested in this case
                        if ((!plaintiffGivesUp || !defendantGivesUp) && !plaintiffWinsIfBothGiveUp)
                            continue; // only need to test both values of plaintiff wins if both give up.
                        if (CaseNumber == int.MaxValue)
                        {
                            Br.eak.Add("Case");
                            Br.eak.Add(CaseNumber.ToString());
                            GameProgressLogger.LoggingOn = true;
                            GameProgressLogger.DetailedLogging = true;
                            GameProgressLogger.OutputLogMessages = true;
                        }
                        else
                        {
                            Br.eak.Remove("Case");
                            Br.eak.Remove(CaseNumber.ToString());
                            GameProgressLogger.LoggingOn = false;
                            GameProgressLogger.OutputLogMessages = false;
                        }
                        CaseGivenUp_SpecificSettingsAndActions(numPotentialBargainingRounds, abandonmentInRound,                             simultaneousBargainingRounds,
                            simultaneousOffersUltimatelyRevealed, LiabilityStrength, DamagesStrength, plaintiffGivesUp ? abandonmentInRound : (byte?)null,
                            defendantGivesUp ? abandonmentInRound : (byte?)null,
                            plaintiffWinsIfBothGiveUp ? (byte)2 : (byte)1,
                            loserPaysPolicy, 
                            allowDamagesVariation,
                            simulatingBargainingFailure,
                            runningSideBetChallenges,
                            shootout);
                        CaseNumber++;
                    }
            }
            catch (Exception e)
            {
                throw new Exception($"Case number {CaseNumber} failed: {e.Message}");
            }
        }

        public void CaseGivenUp_SpecificSettingsAndActions(byte numPotentialBargainingRounds, byte? abandonmentInRound, bool simultaneousBargainingRounds, bool simultaneousOffersUltimatelyRevealed, byte liabilityStrength, byte damagesStrength, byte? pReadyToAbandonRound, byte? dReadyToDefaultRound, byte mutualGiveUpResult, LoserPaysPolicy loserPaysPolicy, bool allowDamagesVariation, HowToSimulateBargainingFailure simulatingBargainingFailure, RunningSideBetChallenges runningSideBetChallenges, ShootoutSettings shootout)
        {
            var bargainingRoundMoves = GetBargainingRoundMoves(simultaneousBargainingRounds,
                abandonmentInRound ?? numPotentialBargainingRounds, false, simulatingBargainingFailure, out var offers);
            var numActualRounds = (byte) bargainingRoundMoves.Count();
            var options = GetGameOptions(allowDamagesVariation, true, numPotentialBargainingRounds, numPotentialBargainingRounds == 3, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, loserPaysPolicy, false, simulatingBargainingFailure, SideBetChallenges.NoChallengesAllowed, runningSideBetChallenges, shootout);
            var bestOffers = GetBestOffers(offers, options);
            bool pFiles = pReadyToAbandonRound != 0;
            bool dAnswers = pFiles && dReadyToDefaultRound != 0;
            Func<Decision, GameProgress, byte> actionsToPlay = GetPlayerActions(pFiles, dAnswers, liabilityStrength, PLiabilitySignal,
                DLiabilitySignal, damagesStrength, PDamagesSignal,  DDamagesSignal, bargainingRoundMoves: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed: simultaneousOffersUltimatelyRevealed, pReadyToAbandonRound: pReadyToAbandonRound, dReadyToDefaultRound: dReadyToDefaultRound, mutualGiveUpResult: mutualGiveUpResult, simulatingBargainingFailure: simulatingBargainingFailure, sideBetChallenges: SideBetChallenges.NoChallengesAllowed, runningSideBetChallenges: runningSideBetChallenges);
            var myGameProgress = LitigGameLauncher.PlayLitigGameOnce(options, actionsToPlay);
            VerifyInformationSetUniqueness(myGameProgress, options);

            bool pWins = pReadyToAbandonRound == null && dReadyToDefaultRound != null ||
                         pReadyToAbandonRound != null && dReadyToDefaultRound != null && pFiles && mutualGiveUpResult == 2;

            double damagesImposed = pWins ? options.DamagesMax : 0;

            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.CaseSettles.Should().BeFalse();
            if (pReadyToAbandonRound == 0)
            {
                myGameProgress.PFiles.Should().Be(false);
                myGameProgress.DAnswers.Should().Be(false);
                myGameProgress.PAbandons.Should().Be(false);
                myGameProgress.DDefaults.Should().Be(false);
            }
            else if (dReadyToDefaultRound == 0)
            {
                myGameProgress.PFiles.Should().Be(true);
                myGameProgress.DAnswers.Should().Be(false);
                myGameProgress.PAbandons.Should().Be(false);
                myGameProgress.DDefaults.Should().Be(false);
            }
            else
            {
                myGameProgress.PAbandons.Should().Be(!pWins);
                myGameProgress.DDefaults.Should().Be(pWins);
            }
            double pExpenses, dExpenses;
            if (myGameProgress.PFiles && myGameProgress.DAnswers)
            {
                CalculateBargainingRoundExpenses(numActualRounds, options, out double pBargainingRoundExpenses, out double dBargainingRoundExpenses);
                double pInitialExpenses = options.PFilingCost * options.CostsMultiplier + pBargainingRoundExpenses * options.CostsMultiplier;
                double dInitialExpenses = options.DAnswerCost * options.CostsMultiplier + dBargainingRoundExpenses * options.CostsMultiplier;
                GetExpensesAfterFeeShifting(options, true, true, false, pWins, pInitialExpenses, dInitialExpenses, out pExpenses, out dExpenses);
            }
            else
            {
                // either p didn't file or p filed and d didn't answer
                pExpenses = myGameProgress.PFiles ? options.PFilingCost * options.CostsMultiplier : 0;
                dExpenses = 0;
            }

            double pFinalWealthExpected = options.PInitialWealth + damagesImposed - pExpenses;
            double dFinalWealthExpected = options.DInitialWealth - damagesImposed - dExpenses;

            if (pFiles && dAnswers && runningSideBetChallenges != RunningSideBetChallenges.None)
            {
                var chipsFromPreviousRounds = (byte) (2 * (numActualRounds - 1));
                byte chipsThisRound = 0;
                if (runningSideBetChallenges == RunningSideBetChallenges.DChallenges2P1 && myGameProgress.PAbandons)
                    chipsThisRound = 1;
                if (runningSideBetChallenges == RunningSideBetChallenges.DChallenges2P1 && myGameProgress.DDefaults)
                    chipsThisRound = 2;
                if (runningSideBetChallenges == RunningSideBetChallenges.PChallenges2D1 && myGameProgress.PAbandons)
                    chipsThisRound = 2;
                if (runningSideBetChallenges == RunningSideBetChallenges.PChallenges2D1 && myGameProgress.DDefaults)
                    chipsThisRound = 1;
                var totalChips = (byte) (chipsFromPreviousRounds + chipsThisRound);
                double transferToP = ValueOfChip * totalChips;
                if (myGameProgress.PAbandons)
                    transferToP = 0 - transferToP;
                pFinalWealthExpected += transferToP;
                dFinalWealthExpected -= transferToP;
            }

            double shootoutsTransferToP = 0;
            if (options.ShootoutsApplyAfterAbandonment && (options.ShootoutOfferValueIsAveraged || abandonmentInRound == numPotentialBargainingRounds))
                shootoutsTransferToP = GetShootoutTransferAmount(options, offers, damagesImposed);
            pFinalWealthExpected += shootoutsTransferToP;
            dFinalWealthExpected -= shootoutsTransferToP;

            CheckFinalWelfare(myGameProgress, pFinalWealthExpected, dFinalWealthExpected, bestOffers);

            //var informationSetHistories = myGameProgress.GameHistory.GetInformationSetHistoryItems().ToList();
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            var expectedPartyInformationSets = ConstructExpectedPartyInformationSets(LiabilityStrength, PLiabilitySignal, DLiabilitySignal, allowDamagesVariation, PDamagesSignal, DDamagesSignal, pFiles, dAnswers, simulatingBargainingFailure, runningSideBetChallenges, bargainingRoundMoves, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, true, pReadyToAbandonRound, dReadyToDefaultRound);
            string expectedResolutionSet = ConstructExpectedResolutionSet(liabilityStrength, allowDamagesVariation, damagesStrength, pFiles, dAnswers, simulatingBargainingFailure, bargainingRoundMoves, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, false, true, pReadyToAbandonRound != null, dReadyToDefaultRound != null, mutualGiveUpResult == 1, false, 0, false, 0, SideBetChallenges.Irrelevant, runningSideBetChallenges);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        private static void CalculateBargainingRoundExpenses(byte numActualRounds, LitigGameOptions options, out double pBargainingRoundExpenses, out double dBargainingRoundExpenses)
        {
            pBargainingRoundExpenses = 0;
            dBargainingRoundExpenses = 0;
            if (options.RoundSpecificBargainingCosts != null)
            {
                for (int i = 0; i < numActualRounds; i++)
                {
                    pBargainingRoundExpenses += options.RoundSpecificBargainingCosts[i].pCosts;
                    dBargainingRoundExpenses += options.RoundSpecificBargainingCosts[i].dCosts;
                }
            }
            else
            {
                pBargainingRoundExpenses = dBargainingRoundExpenses = numActualRounds * options.PerPartyCostsLeadingUpToBargainingRound;
            }
        }

        private void GetExpensesAfterFeeShifting(LitigGameOptions options, bool winIsByEnoughOrNotRequiringMarginOfVictory, bool loserPaysAfterAbandonmentRequired, bool punishPlaintiffUnderRule68, bool pWins, double pInitialExpenses, double dInitialExpenses, out double pExpenses, out double dExpenses)
        {
            bool loserPaysGenerally = options.LoserPays && (!loserPaysAfterAbandonmentRequired || options.LoserPaysAfterAbandonment);
            if (loserPaysGenerally && !winIsByEnoughOrNotRequiringMarginOfVictory)
                loserPaysGenerally = false;
            if (loserPaysGenerally || punishPlaintiffUnderRule68) // trial occurs and plaintiff won but not more than defendant's offer
            {
                if (pWins && !punishPlaintiffUnderRule68)
                {
                    pExpenses = pInitialExpenses - options.LoserPaysMultiple * pInitialExpenses;
                    dExpenses = dInitialExpenses + options.LoserPaysMultiple * pInitialExpenses;
                }
                else
                {
                    pExpenses = pInitialExpenses + options.LoserPaysMultiple * dInitialExpenses;
                    dExpenses = dInitialExpenses - options.LoserPaysMultiple * dInitialExpenses;
                }
            }
            else
            {
                // American rule
                pExpenses = pInitialExpenses;
                dExpenses = dInitialExpenses;
            }
        }

        [TestMethod]
        public void SettlingCase()
        {
            CaseNumber = 0;
            try
            {
                for (byte numPotentialBargainingRounds = 1; numPotentialBargainingRounds <= 3; numPotentialBargainingRounds++)
                    foreach (bool simultaneousBargainingRounds in new[] { true, false })
                    foreach (bool simultaneousOffersUltimatelyRevealed in new[] { true, false })
                    foreach (bool allowDamagesVariation in new[] { true, false })
                    foreach (bool allowAbandonAndDefault in new[] {true, false})
                    foreach (var loserPaysPolicy in new[] {LoserPaysPolicy.NoLoserPays, LoserPaysPolicy.AfterTrialOnly, LoserPaysPolicy.EvenAfterAbandonOrDefault})
                    foreach (var simulatingBargainingFailure in new[] {HowToSimulateBargainingFailure.PRefusesToBargain, HowToSimulateBargainingFailure.DRefusesToBargain, HowToSimulateBargainingFailure.BothRefuseToBargain, HowToSimulateBargainingFailure.BothAgreeToBargain, HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain})
                        for (byte settlementInRound = 1; settlementInRound <= numPotentialBargainingRounds; settlementInRound++)
                            foreach (var runningSideBetChallenges in new[] {RunningSideBetChallenges.None, RunningSideBetChallenges.PChallenges2D1, RunningSideBetChallenges.DChallenges2P1})
                            {
                                if (CaseNumber == 9999999)
                                {
                                    Br.eak.Add("Case");
                                    GameProgressLogger.LoggingOn = true;
                                    GameProgressLogger.OutputLogMessages = true;
                                }
                                else
                                {
                                    Br.eak.Remove("Case");
                                    GameProgressLogger.LoggingOn = false;
                                    GameProgressLogger.OutputLogMessages = false;
                                }
                                bool incompatible = runningSideBetChallenges != RunningSideBetChallenges.None && (!allowAbandonAndDefault || simulatingBargainingFailure == HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain);
                                if (!incompatible)
                                    SettlingCase_Helper(numPotentialBargainingRounds, settlementInRound, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, allowAbandonAndDefault, loserPaysPolicy, allowDamagesVariation, simulatingBargainingFailure, runningSideBetChallenges);
                                CaseNumber++;
                            }
            }
            catch (Exception e)
            {
                throw new Exception($"Failed at case number {CaseNumber}. Inner exception: {e.Message}", e);
            }
        }

        public void SettlingCase_Helper(byte numPotentialBargainingRounds, byte? settlementInRound, bool simultaneousBargainingRounds, bool simultaneousOffersUltimatelyRevealed, bool allowAbandonAndDefault, LoserPaysPolicy loserPaysPolicy, bool allowDamagesVariation, HowToSimulateBargainingFailure simulatingBargainingFailure, RunningSideBetChallenges runningSideBetChallenges)
        {
            var bargainingRoundMoves = GetBargainingRoundMoves(simultaneousBargainingRounds,
                settlementInRound ?? numPotentialBargainingRounds, settlementInRound != null, simulatingBargainingFailure, out var offers);
            var numActualRounds = (byte) bargainingRoundMoves.Count();
            var options = GetGameOptions(allowDamagesVariation, allowAbandonAndDefault, numPotentialBargainingRounds, numPotentialBargainingRounds == 2, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, loserPaysPolicy, false, simulatingBargainingFailure, SideBetChallenges.NoChallengesAllowed, runningSideBetChallenges, ShootoutSettings.None);
            var bestOffers = GetBestOffers(offers, options);
            var actionsToPlay = GetPlayerActions(true, true, LiabilityStrength, PLiabilitySignal,
                DLiabilitySignal, DamagesStrength, PDamagesSignal, DDamagesSignal, simulatingBargainingFailure, bargainingRoundMoves: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed: simultaneousOffersUltimatelyRevealed, sideBetChallenges: SideBetChallenges.NoChallengesAllowed, runningSideBetChallenges: runningSideBetChallenges);
            var myGameProgress = LitigGameLauncher.PlayLitigGameOnce(options, actionsToPlay);
            VerifyInformationSetUniqueness(myGameProgress, options);

            double settlementProportion = EquallySpaced.GetLocationOfEquallySpacedPoint(ValueWhenCaseSettles - 1, NumOffers, true);

            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.CaseSettles.Should().BeTrue();
            CalculateBargainingRoundExpenses(numActualRounds, options, out double pBargainingRoundExpenses, out double dBargainingRoundExpenses);
            double pFinalWealthExpected = options.PInitialWealth - options.PFilingCost * options.CostsMultiplier + settlementProportion * options.DamagesMax - pBargainingRoundExpenses * options.CostsMultiplier;
            double dFinalWealthExpected = options.DInitialWealth - options.DAnswerCost * options.CostsMultiplier - settlementProportion * options.DamagesMax -
                                          dBargainingRoundExpenses * options.CostsMultiplier;
            CheckFinalWelfare(myGameProgress, pFinalWealthExpected, dFinalWealthExpected, bestOffers);

            //var informationSetHistories = myGameProgress.GameHistory.GetInformationSetHistoryItems().ToList();
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            var expectedPartyInformationSets = ConstructExpectedPartyInformationSets(LiabilityStrength, PLiabilitySignal, DLiabilitySignal, allowDamagesVariation, PDamagesSignal, DDamagesSignal, true, true, simulatingBargainingFailure, runningSideBetChallenges, bargainingRoundMoves, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, allowAbandonAndDefault, null, null);
            Br.eak.IfAdded("Case");
            string expectedResolutionSet = ConstructExpectedResolutionSet_CaseSettles(LiabilityStrength, allowDamagesVariation, DamagesStrength, true, true, simulatingBargainingFailure,  bargainingRoundMoves, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, allowAbandonAndDefault, SideBetChallenges.Irrelevant, runningSideBetChallenges);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        [TestMethod]
        public void CaseTried()
        {
            CaseNumber = 0;
            bool aggregateAllExceptions = false; 
            StringBuilder b = new StringBuilder();
            try
            {   
                foreach (bool allowDamagesVariation in new[] { true, false })
                {
                    foreach (bool allowAbandonAndDefault in new[] {true, false})
                    {
                        foreach (byte numBargainingRounds in new byte[] {1, 2})
                        {
                            foreach (bool plaintiffWins in new[] {true, false})
                            {
                                foreach (bool simultaneousBargainingRounds in new[] { true , false})
                                {
                                    foreach (bool simultaneousOffersUltimatelyRevealed in new[] { true, false })
                                    {
                                        foreach (var loserPaysPolicy in new[] {LoserPaysPolicy.NoLoserPays, LoserPaysPolicy.AfterTrialOnly, LoserPaysPolicy.EvenAfterAbandonOrDefault, LoserPaysPolicy.MarginOfVictory_Enough, LoserPaysPolicy.MarginOfVictory_NotEnough})
                                        {
                                            foreach (bool rule68 in new[] { false, true })
                                            {
                                                foreach (var simulatingBargainingFailure in new[] {HowToSimulateBargainingFailure.PRefusesToBargain, HowToSimulateBargainingFailure.DRefusesToBargain, HowToSimulateBargainingFailure.BothRefuseToBargain, HowToSimulateBargainingFailure.BothAgreeToBargain, HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain})
                                                {
                                                    foreach (var sideBetChallenges in new[] {SideBetChallenges.NoChallengesAllowed, SideBetChallenges.BothChallenge, SideBetChallenges.NoOneChallenges, SideBetChallenges.PChallenges, SideBetChallenges.DChallenges})
                                                    {
                                                        foreach (var runningSideBetChallenges in new[] {RunningSideBetChallenges.None, RunningSideBetChallenges.PChallenges2D1, RunningSideBetChallenges.DChallenges2P1})
                                                        {
                                                            foreach (var shootout in new[] { ShootoutSettings.None, ShootoutSettings.Regular, ShootoutSettings.AverageAllRounds, ShootoutSettings.ApplyAfterAbandonment, ShootoutSettings.ApplyAfterAbandonment_AverageAllRounds })
                                                            {
                                                                String settings = $"allowDamagesVariation {allowDamagesVariation} allowAbandonAndDefault {allowAbandonAndDefault} numBargainingRounds {numBargainingRounds} plaintiffWins {plaintiffWins} simultaneousBargainingRounds {simultaneousBargainingRounds} loserPaysPolicy {loserPaysPolicy} rule68 {rule68} simultaneousBargainingFailure {simulatingBargainingFailure} sideBetChallenges {sideBetChallenges} runningSideBetChallenges {runningSideBetChallenges} shootout {shootout}";
                                                                var skipThis = false;
                                                                if (CaseNumber == int.MaxValue)
                                                                {
                                                                    Br.eak.Add("Case");
                                                                    GameProgressLogger.LoggingOn = true;
                                                                    GameProgressLogger.DetailedLogging = true;
                                                                    GameProgressLogger.OutputLogMessages = true;
                                                                }
                                                                else
                                                                {
                                                                    Br.eak.Remove("Case");
                                                                    //skipThis = true; 
                                                                    GameProgressLogger.LoggingOn = false;
                                                                    GameProgressLogger.DetailedLogging = false;
                                                                    GameProgressLogger.OutputLogMessages = false;
                                                                }
                                                                bool incompatible = (runningSideBetChallenges != RunningSideBetChallenges.None && (!allowAbandonAndDefault || simulatingBargainingFailure == HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain))
                                                                    ||
                                                                    (simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain && (shootout != ShootoutSettings.None || rule68))
                                                                    ||
                                                                    ((loserPaysPolicy is LoserPaysPolicy.MarginOfVictory_Enough or LoserPaysPolicy.MarginOfVictory_NotEnough) && rule68)
                                                                      ;
                                                                if (incompatible)
                                                                    skipThis = true;
                                                                if (!skipThis)
                                                                {
                                                                    try
                                                                    {
                                                                        CaseTried_Helper(allowDamagesVariation, allowAbandonAndDefault, numBargainingRounds, plaintiffWins, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, loserPaysPolicy, rule68, simulatingBargainingFailure, sideBetChallenges, runningSideBetChallenges, shootout);
                                                                    }
                                                                    catch(Exception ex)
                                                                    {
                                                                        if (aggregateAllExceptions)
                                                                            b.AppendLine($"Case number {CaseNumber}: {settings}"); 
                                                                        else throw ex;
                                                                    }
                                                                }
                                                                CaseNumber++;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Failed case number {CaseNumber}: {e.Message}");
            }
            if (aggregateAllExceptions && b.Length > 0)
                throw new Exception(b.ToString());
        }

        private void CaseTried_Helper(bool allowDamagesVariation, bool allowAbandonAndDefaults, byte numBargainingRounds, bool plaintiffWins, bool simultaneousBargainingRounds, bool simultaneousOffersUltimatelyRevealed, LoserPaysPolicy loserPaysPolicy, bool rule68, HowToSimulateBargainingFailure simulatingBargainingFailure, SideBetChallenges sideBetChallenges, RunningSideBetChallenges runningSideBetChallenges, ShootoutSettings shootout)
        {
            var options = GetGameOptions(allowDamagesVariation, allowAbandonAndDefaults, numBargainingRounds, simultaneousBargainingRounds /* doesn't matter, just must do it sometimes */, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, loserPaysPolicy, rule68, simulatingBargainingFailure, sideBetChallenges, runningSideBetChallenges, shootout);
            var bargainingMoves = GetBargainingRoundMoves(simultaneousBargainingRounds, numBargainingRounds, false, simulatingBargainingFailure, out var offers);
            var bestOffers = GetBestOffers(offers, options);
            byte courtLiabilityResult = plaintiffWins ? (byte)2 : (byte)1;
            if (loserPaysPolicy == LoserPaysPolicy.MarginOfVictory_Enough)
            { 
                courtLiabilityResult = plaintiffWins ? (byte)liabilityResultPWinsEnough : (byte)liabilityResultDWinsEnough;
            }
            else if (loserPaysPolicy == LoserPaysPolicy.MarginOfVictory_NotEnough)
            {
                courtLiabilityResult = plaintiffWins ? (byte)liabilityResultPWinsNotEnough : (byte)liabilityResultDWinsNotEnough;
            }
            byte courtDamagesResultIfAllowVariation = CDamagesSignal;
            double damagesIfAwarded = DamagesAlleged;
            if (allowDamagesVariation)
            {
                double damagesProportionWithVariation = EquallySpaced.GetLocationOfEquallySpacedPoint(courtDamagesResultIfAllowVariation - 1 /* make it zero-based */, NumDamagesSignals, true);
                double minDamages = DamagesAlleged * DamagesMinMaxRatio;
                damagesIfAwarded = minDamages + damagesProportionWithVariation * (DamagesAlleged - minDamages);
            }
            var actions = GetPlayerActions(true, true, LiabilityStrength, PLiabilitySignal, DLiabilitySignal, DamagesStrength, PDamagesSignal, DDamagesSignal, simulatingBargainingFailure, sideBetChallenges, runningSideBetChallenges, bargainingMoves, simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, null, null, 0, courtLiabilityResult, courtDamagesResultIfAllowVariation);
            var myGameProgress = LitigGameLauncher.PlayLitigGameOnce(options, actions);
            myGameProgress.GameComplete.Should().BeTrue();
            VerifyInformationSetUniqueness(myGameProgress, options);

            CalculateBargainingRoundExpenses(numBargainingRounds, options, out double pBargainingRoundExpenses, out double dBargainingRoundExpenses);
            double pInitialExpenses = options.PFilingCost * options.CostsMultiplier + pBargainingRoundExpenses * options.CostsMultiplier + options.PTrialCosts * options.CostsMultiplier;
            double dInitialExpenses = options.DAnswerCost * options.CostsMultiplier + dBargainingRoundExpenses * options.CostsMultiplier + options.DTrialCosts * options.CostsMultiplier;
            bool punishPlaintiffUnderRule68 = false;
            if (options.Rule68 && plaintiffWins && GetLastDOffer(offers) is double dOffer && dOffer >= damagesIfAwarded)
                punishPlaintiffUnderRule68 = true;

            GetExpensesAfterFeeShifting(options, loserPaysPolicy != LoserPaysPolicy.MarginOfVictory_NotEnough, false, punishPlaintiffUnderRule68, plaintiffWins, pInitialExpenses, dInitialExpenses, out double pExpenses, out double dExpenses);

            double sideBetTransferFromP = 0; // this will reduce P's wealth and increase D's (if negative, of course will have the reverse effect)
            if (sideBetChallenges != SideBetChallenges.NoChallengesAllowed && sideBetChallenges != SideBetChallenges.NoOneChallenges)
            {
                bool pChallengesD = sideBetChallenges == SideBetChallenges.PChallenges || sideBetChallenges == SideBetChallenges.BothChallenge;
                bool dChallengesP = sideBetChallenges == SideBetChallenges.DChallenges || sideBetChallenges == SideBetChallenges.BothChallenge;
                bool challengerLoses = pChallengesD && !plaintiffWins || dChallengesP && plaintiffWins;
                double multiplier = challengerLoses ? DamagesMultipleForChallengerToPay : DamagesMultipleForChallengedToPay;
                double totalTransfer = multiplier * DamagesAlleged;
                if (plaintiffWins)
                    sideBetTransferFromP = 0 - totalTransfer;
                else
                    sideBetTransferFromP = totalTransfer;
            }

            double runningSideBetTransferFromP = 0;
            if (runningSideBetChallenges != RunningSideBetChallenges.None)
            {
                // add the total chips bet so far
                var chipsBet = (byte)(2 * numBargainingRounds);
                double totalBet = chipsBet * ValueOfChip;
                runningSideBetTransferFromP = plaintiffWins ? 0 - totalBet : totalBet;
            }

            double resolutionForShootout = plaintiffWins ? damagesIfAwarded : 0;
            double shootoutsTransferToP = GetShootoutTransferAmount(options, offers, resolutionForShootout);

            double pFinalWealthExpected = options.PInitialWealth - pExpenses - sideBetTransferFromP - runningSideBetTransferFromP + shootoutsTransferToP;
            double dFinalWealthExpected = options.DInitialWealth - dExpenses + sideBetTransferFromP + runningSideBetTransferFromP - shootoutsTransferToP;
            if (plaintiffWins)
            {
                pFinalWealthExpected += damagesIfAwarded;
                dFinalWealthExpected -= damagesIfAwarded;
            }
            CheckFinalWelfare(myGameProgress, pFinalWealthExpected, dFinalWealthExpected, bestOffers);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet,
                out string resolutionSet);
            var expectedPartyInformationSets = ConstructExpectedPartyInformationSets(LiabilityStrength, PLiabilitySignal, DLiabilitySignal, allowDamagesVariation, PDamagesSignal, DDamagesSignal, true, true, simulatingBargainingFailure, runningSideBetChallenges, bargainingMoves,
              simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, allowAbandonAndDefaults, null, null);
            string expectedResolutionSet = ConstructExpectedResolutionSet(LiabilityStrength, allowDamagesVariation, DamagesStrength, true, true, simulatingBargainingFailure, bargainingMoves,
                simultaneousBargainingRounds, simultaneousOffersUltimatelyRevealed, false, allowAbandonAndDefaults, false, false, false, true, courtLiabilityResult, loserPaysPolicy is LoserPaysPolicy.MarginOfVictory_Enough or LoserPaysPolicy.MarginOfVictory_NotEnough, courtDamagesResultIfAllowVariation, sideBetChallenges, runningSideBetChallenges);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        private static double GetShootoutTransferAmount(LitigGameOptions options, List<(byte offerMove, byte bargainingRoundNumber, bool isOfferToP)> offers, double resolutionForShootout)
        {
            double shootoutsTransferToP = 0;
            if (options.ShootoutSettlements && offers != null && offers.Any())
            {
                List<(byte offerMove, byte bargainingRoundNumber, bool isOfferToP)> applicableOffers;
                List<double> applicablePOffers, applicableDOffers;
                if (!options.ShootoutOfferValueIsAveraged)
                {
                    byte lastRound = offers.Max(x => x.bargainingRoundNumber);
                    applicableOffers = offers.Where(x => x.bargainingRoundNumber == lastRound).ToList();
                }
                else
                    applicableOffers = offers;
                applicablePOffers = applicableOffers.Where(x => !x.isOfferToP).Select(x => EquallySpaced.GetLocationOfEquallySpacedPoint(x.offerMove - 1, NumOffers, true)).ToList();
                applicableDOffers = applicableOffers.Where(x => x.isOfferToP).Select(x => EquallySpaced.GetLocationOfEquallySpacedPoint(x.offerMove - 1, NumOffers, true)).ToList();
                double pricePoint;
                if (options.BargainingRoundsSimultaneous)
                    pricePoint = (applicablePOffers.Average() + applicableDOffers.Average()) / 2.0;
                else
                {
                    List<double> applicableOffers2 = new List<double>();
                    applicableOffers2.AddRange(applicablePOffers);
                    applicableOffers2.AddRange(applicableDOffers);
                    pricePoint = applicableOffers2.Average();
                }
                shootoutsTransferToP = (resolutionForShootout - pricePoint * options.DamagesMax) * options.ShootoutStrength;
            }

            return shootoutsTransferToP;
        }

        private static void CheckFinalWelfare(LitigGameProgress myGameProgress, double pFinalWealthExpected, double dFinalWealthExpected, ValueTuple<double?, double?> bestOffers)
        {
            myGameProgress.PFinalWealth.Should().Be(pFinalWealthExpected);
            myGameProgress.DFinalWealth.Should().Be(dFinalWealthExpected);
            double pRegretAversionAdjustedWealth = myGameProgress.PFinalWealth;
            double dRegretAversionAdjustedWealth = myGameProgress.DFinalWealth;
            if (bestOffers.Item1 != null && bestOffers.Item1 > myGameProgress.PFinalWealth)
                pRegretAversionAdjustedWealth -= RegretAversion * ((double) bestOffers.Item1 - myGameProgress.PFinalWealth);
            if (bestOffers.Item2 != null && bestOffers.Item2 > myGameProgress.DFinalWealth)
                dRegretAversionAdjustedWealth -= RegretAversion * ((double) bestOffers.Item2 - myGameProgress.DFinalWealth);
            myGameProgress.PWelfare.Should().Be(pRegretAversionAdjustedWealth);
            myGameProgress.DWelfare.Should().Be(dRegretAversionAdjustedWealth);
        }

        private static void VerifyInformationSetUniqueness(LitigGameProgress myGameProgress, LitigGameOptions options)
        {
            var informationSetHistoriesIndices = myGameProgress.GameFullHistory.GetInformationSetHistoryItems_OverallIndices(myGameProgress).ToList();
            var playerAndInformation = informationSetHistoriesIndices.Select(x => myGameProgress.GameFullHistory.GetInformationSetHistory_OverallIndex(x).GetPlayerAndInformationSetAsList()).ToList();
            if (playerAndInformation.Count() != playerAndInformation.Distinct().Count())
            {
                LitigGameDefinition gameDefinition = new LitigGameDefinition();
                gameDefinition.Setup(options);
                List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);
                var playerAndInformation2 = informationSetHistoriesIndices.Select(x => (myGameProgress.GameFullHistory.GetInformationSetHistory_OverallIndex(x).PlayerIndex, String.Join(",", myGameProgress.GameFullHistory.GetInformationSetHistory_OverallIndex(x).GetInformationSetForPlayerAsList()), gameDefinition.DecisionPointsExecutionOrder[myGameProgress.GameFullHistory.GetInformationSetHistory_OverallIndex(x).DecisionIndex].Name, gameDefinition.DecisionPointsExecutionOrder[myGameProgress.GameFullHistory.GetInformationSetHistory_OverallIndex(x).DecisionIndex].Decision.NumPossibleActions)).Where(x => x.Item4 != 1).ToList();
                var orderedInfo = playerAndInformation2.OrderBy(x => x.Item1).ThenBy(x => x.Item2).ToList();
                bool problemVerified = false;
                for (int i = 1; i < orderedInfo.Count(); i++)
                {
                    if (orderedInfo[i - 1].Item1 == orderedInfo[i].Item1 && orderedInfo[i - 1].Item2 == orderedInfo[i].Item2)
                    {
                        problemVerified = true;
                        Debug.WriteLine($"duplicate {orderedInfo[i - 1]}, {orderedInfo[i]}");
                    }
                }
                if (problemVerified)
                    throw new Exception("Player information not unique.");
            }
        }
    }
}