using System;
using System.Collections.Generic;
using System.Linq;
using ACESim;
using ACESim.Util;
using FluentAssertions;
using MathNet.Numerics.Providers.LinearAlgebra.OpenBlas;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public class MyGameTests
    {

        private static void GetInformationSetStrings(MyGameProgress myGameProgress, out string pInformationSet,
            out string dInformationSet, out string resolutionSet)
        {
            pInformationSet = myGameProgress.GameHistory.GetPlayerInformationAtPointString((byte)MyGamePlayers.Plaintiff, null);
            dInformationSet = myGameProgress.GameHistory.GetPlayerInformationAtPointString((byte)MyGamePlayers.Defendant, null);
            resolutionSet = myGameProgress.GameHistory.GetPlayerInformationAtPointString((byte)MyGamePlayers.Resolution, null);
            string pInformationSet2 = myGameProgress.GameHistory.GetCurrentPlayerInformationString((byte)MyGamePlayers.Plaintiff);
            string dInformationSet2 = myGameProgress.GameHistory.GetCurrentPlayerInformationString((byte)MyGamePlayers.Defendant);
            string resolutionSet2 = myGameProgress.GameHistory.GetCurrentPlayerInformationString((byte)MyGamePlayers.Resolution);
            pInformationSet.Should().Be(pInformationSet2);
            dInformationSet.Should().Be(dInformationSet2);
            resolutionSet.Should().Be(resolutionSet2);

        }

        private const double PartyNoise = 0.2, InitialWealth = 1_000_000, DamagesAlleged = 100_000, PFileCost = 3000, DAnswerCost = 2000, PTrialCosts = 4000, DTrialCosts = 6000, PerRoundBargainingCost = 1000;
        private const byte NumDistinctPoints = 5;
        private const byte LitigationQuality = 3;
        private const byte PSignalOrNoise = 5, DSignalOrNoise = 1;


        public enum LoserPaysPolicy
        {
            NoLoserPays,
            AfterTrialOnly,
            EvenAfterAbandonOrDefault,
        }
        public enum HowToSimulateBargainingFailure
        {
            PRefusesToBargain,
            DRefusesToBargain,
            BothRefuseToBargain,
            BothAgreeToBargain,
            BothHaveNoChoiceAndMustBargain // i.e., we don't have the decisions representing agreement to bargain
        }

        private MyGameOptions GetGameOptions(bool allowAbandonAndDefaults, byte numBargainingRounds, bool forgetEarlierBargainingRounds, bool simultaneousBargainingRounds, bool actionIsNoiseNotSignal, LoserPaysPolicy loserPaysPolicy, HowToSimulateBargainingFailure simulatingBargainingFailure)
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = InitialWealth,
                DInitialWealth = InitialWealth,
                DamagesAlleged = DamagesAlleged,
                NumLitigationQualityPoints = NumDistinctPoints,
                NumSignals = NumDistinctPoints,
                NumOffers = NumDistinctPoints,
                NumNoiseValues = NumDistinctPoints,
                ActionIsNoiseNotSignal = actionIsNoiseNotSignal,
                PNoiseStdev = PartyNoise,
                DNoiseStdev = PartyNoise,
                CourtNoiseStdev = 0.5,
                PFilingCost = PFileCost,
                DAnswerCost = DAnswerCost,
                PTrialCosts = PTrialCosts,
                DTrialCosts = DTrialCosts,
                LoserPays = loserPaysPolicy != LoserPaysPolicy.NoLoserPays,
                LoserPaysAfterAbandonment = loserPaysPolicy == LoserPaysPolicy.EvenAfterAbandonOrDefault,
                PerPartyBargainingRoundCosts = PerRoundBargainingCost,
                AllowAbandonAndDefaults = allowAbandonAndDefaults,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                IncludeAgreementToBargainDecisions = simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain,
                NumPotentialBargainingRounds = numBargainingRounds,
                ForgetEarlierBargainingRounds = forgetEarlierBargainingRounds,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = simultaneousBargainingRounds,
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false, true, false, true, false },
                IncludeSignalsReport = true
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            return options;
        }

        private string ConstructExpectedResolutionSet_CaseSettles(bool actionIsNoiseNotSignal, byte litigationQuality, bool pFiles, bool dAnswers, HowToSimulateBargainingFailure simulatingBargainingFailure,
            List<(byte? pMove, byte? dMove)> bargainingRounds, bool simultaneousBargainingRounds,
            bool allowAbandonAndDefault)
        {
            return ConstructExpectedResolutionSet(actionIsNoiseNotSignal, litigationQuality, pFiles, dAnswers, simulatingBargainingFailure, bargainingRounds, simultaneousBargainingRounds, true, allowAbandonAndDefault,
                false, false, false, false, 0);
        }

        private string ConstructExpectedResolutionSet(bool actionIsNoiseNotSignal, byte litigationQuality, bool pFiles, bool dAnswers, HowToSimulateBargainingFailure simulatingBargainingFailure, List<(byte? pMove, byte? dMove)> bargainingRounds, bool simultaneousBargainingRounds, bool settlementReachedLastRound, bool allowAbandonAndDefault, bool pReadyToAbandonLastRound, bool dReadyToDefaultLastRound, bool ifBothDefaultPlaintiffLoses, bool caseGoesToTrial, byte courtResultAtTrial)
        {
            List<byte> l = new List<byte>();
            
            if (actionIsNoiseNotSignal)
                l.Add(litigationQuality); // the resolution set must contain the actual litigation quality if we're using noise actions, so then the court can determine what to do based on its own noise value. Not so when actions are signals, in which case the action the court receives simply determines its decision and is based on an uneven chance probabilities decision that doesn't need to be part of the resolution set.
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
                        bool isLastBargainedRound = bargainingRound == numBargainingRoundsCompleted;
                        // Note that the resolution information set always consists of just the last round settled, regardless of whether the case goes to trial. Some of this information may be irrelevant to trial (if we're not doing the settlement shootout).
                        if (isLastBargainedRound)
                        {
                            l.Add(bargainingRound);
                            bool pAgreesToBargain = moveSet.pMove != null;
                            bool dAgreesToBargain = moveSet.dMove != null;
                            if (simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain)
                            {
                                l.Add(pAgreesToBargain ? (byte) 1 : (byte) 2);
                                l.Add(dAgreesToBargain ? (byte) 1 : (byte) 2);
                            }
                            if (pAgreesToBargain && dAgreesToBargain)
                            {
                                bool pMovesFirst = (bargainingRound % 2 == 1) || simultaneousBargainingRounds;
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
                                if (allowAbandonAndDefault && !settlementReachedLastRound)
                                {
                                    l.Add(pReadyToAbandonLastRound ? (byte) 1 : (byte) 2);
                                    l.Add(dReadyToDefaultLastRound ? (byte) 1 : (byte) 2);
                                    if (pReadyToAbandonLastRound && dReadyToDefaultLastRound)
                                        l.Add(ifBothDefaultPlaintiffLoses ? (byte) 1 : (byte) 2);
                                }
                            }
                            break; // we don't need to enter/track anything about later bargaining rounds, since they didn't occur
                        }
                        bargainingRound++;
                    }
                    if (caseGoesToTrial)
                        l.Add(courtResultAtTrial);
                }
                else
                    l.Add(2); // d doesn't answer
            }
            else
                l.Add(2); // p doesn't file
            return String.Join(",", l);
        }

        private List<(byte? pMove, byte? dMove)> GetBargainingRoundMoves(bool simultaneousBargaining, byte numRoundsToInclude, bool settlementInLastRound, HowToSimulateBargainingFailure simulatingBargainingFailure)
        {
            bool ifNotSettlingPRefusesToBargain = simulatingBargainingFailure == HowToSimulateBargainingFailure.PRefusesToBargain || simulatingBargainingFailure == HowToSimulateBargainingFailure.BothRefuseToBargain;
            bool ifNotSettlingDRefusesToBargain = simulatingBargainingFailure == HowToSimulateBargainingFailure.DRefusesToBargain || simulatingBargainingFailure == HowToSimulateBargainingFailure.BothRefuseToBargain;
            List<(byte? pMove, byte? dMove)> moves = new List<(byte? pMove, byte? dMove)>();
            for (byte b = 1; b <= numRoundsToInclude; b++)
            {
                bool settlingThisRound = settlementInLastRound && b == numRoundsToInclude;
                if (!settlingThisRound && (ifNotSettlingPRefusesToBargain || ifNotSettlingDRefusesToBargain))
                {
                    // Note that if one player refuses to bargain, then the other party's move will be ignored -- hence, the use of the dummy 255 here
                    moves.Add((ifNotSettlingPRefusesToBargain ? (byte?) null : (byte?) 255, ifNotSettlingDRefusesToBargain ? (byte?) null : (byte?)255));
                }
                else
                {
                    if (simultaneousBargaining)
                        moves.Add((3, settlingThisRound ? (byte) 3 : (byte) 2));
                    else
                    {
                        byte offer = 3;
                        byte response = settlingThisRound ? (byte) 1 : (byte) 2;
                        if (b % 2 == 1)
                            moves.Add((offer, response));
                        else
                            moves.Add((response, offer));
                    }
                }
            }
            return moves;
        }

        private (string pInformationSet, string dInformationSet) GetExpectedPartyInformationSets(bool actionIsNoiseNotSignal, byte litigationQuality, byte pNoise, byte dNoise, HowToSimulateBargainingFailure simulatingBargainingFailure,
            List<(byte? pMove, byte? dMove)> bargainingMoves, bool forgetEarlierBargainingRounds, bool simultaneousBargaining)
        {
            byte pSignal, dSignal;
            if (actionIsNoiseNotSignal)
            {
                double litigationQualityUniform =
                    EquallySpaced.GetLocationOfEquallySpacedPoint(litigationQuality - 1, NumDistinctPoints);
                MyGame.ConvertNoiseActionToDiscreteAndUniformSignal(pNoise, litigationQualityUniform, NumDistinctPoints, PartyNoise, NumDistinctPoints, out pSignal, out _);
                MyGame.ConvertNoiseActionToDiscreteAndUniformSignal(dNoise, litigationQualityUniform, NumDistinctPoints, PartyNoise, NumDistinctPoints, out dSignal, out _);
            }
            else
            {
                pSignal = pNoise;
                dSignal = dNoise;
            }
            List<byte> pInfo = new List<byte>() { pSignal };
            List<byte> dInfo = new List<byte>() { dSignal };
            int bargainingRoundCount = bargainingMoves.Count();
            byte startingRound = forgetEarlierBargainingRounds ? (byte) bargainingRoundCount : (byte) 1;
            if (bargainingRoundCount != 0)
                for (byte b = startingRound; b <= bargainingRoundCount; b++)
                {
                    pInfo.Add(b);
                    dInfo.Add(b);
                    (byte? pMove, byte? dMove) = bargainingMoves[b - 1];
                    if (simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain)
                    {
                        dInfo.Add(pMove == null ? (byte) 2 : (byte) 1);
                        pInfo.Add(dMove == null ? (byte) 2 : (byte) 1);
                    }
                    if (pMove != null && dMove != null)
                    {
                        if (simultaneousBargaining)
                        {
                            pInfo.Add((byte) dMove);
                            dInfo.Add((byte) pMove);
                        }
                        else
                        {
                            // we add the offer and response regardless of whether it is accepted
                            dInfo.Add((byte) pMove);
                            pInfo.Add((byte) dMove);
                        }
                    }
                }
            return (String.Join(",", pInfo), String.Join(",", dInfo));
        }

        private Func<Decision, GameProgress, byte> GetPlayerActions(bool pFiles, bool dAnswers, byte litigationQuality, byte pSignalOrNoise, byte dSignalOrNoise, HowToSimulateBargainingFailure simulatingBargainingFailure, List<(byte? pMove, byte? dMove)> bargainingRoundMoves, bool simultaneousBargainingRounds, byte? pReadyToAbandonRound = null, byte? dReadyToDefaultRound = null, byte mutualGiveUpResult = 0, byte courtResult = 0)
        {
            var bargaining = new List<(byte decision, byte customInfo, byte action)>();
            for (byte b = 1; b <= bargainingRoundMoves.Count(); b++)
            {
                var bargainingRoundMove = bargainingRoundMoves[b - 1];
                var pMove = bargainingRoundMove.pMove;
                var dMove = bargainingRoundMove.dMove;
                if (simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain)
                {
                    bargaining.Add(((byte) MyGameDecisions.PAgreeToBargain, b, pMove == null ? (byte) 2 : (byte) 1));
                    bargaining.Add(((byte)MyGameDecisions.DAgreeToBargain, b, dMove == null ? (byte)2 : (byte)1));
                }
                if (pMove != null && dMove != null)
                {
                    if (simultaneousBargainingRounds)
                    {
                        bargaining.Add(((byte) MyGameDecisions.POffer, b, (byte) pMove));
                        bargaining.Add(((byte) MyGameDecisions.DOffer, b, (byte) dMove));
                    }
                    else
                    {
                        if (b % 2 == 1)
                        {
                            bargaining.Add(((byte) MyGameDecisions.POffer, b, (byte) pMove));
                            bargaining.Add(((byte) MyGameDecisions.DResponse, b, (byte) dMove));
                        }
                        else
                        {
                            bargaining.Add(((byte) MyGameDecisions.DOffer, b, (byte) dMove));
                            bargaining.Add(((byte) MyGameDecisions.PResponse, b, (byte) pMove));
                        }
                    }
                }
                bargaining.Add(((byte)MyGameDecisions.PAbandon, b, pReadyToAbandonRound == b ? (byte)1 : (byte)2));
                bargaining.Add(((byte)MyGameDecisions.DDefault, b, dReadyToDefaultRound == b ? (byte)1 : (byte)2));
            }
            var actionsToPlay = DefineActions.ForTest(
                new List<(byte decision, byte action)>()
                {
                    ((byte) MyGameDecisions.PFile, pFiles ? (byte) 1 : (byte) 2),
                    ((byte) MyGameDecisions.DAnswer, dAnswers ? (byte) 1 : (byte) 2),
                    ((byte) MyGameDecisions.LitigationQuality, litigationQuality),
                    ((byte) MyGameDecisions.PNoiseOrSignal, pSignalOrNoise),
                    ((byte) MyGameDecisions.DNoiseOrSignal, dSignalOrNoise),
                    ((byte) MyGameDecisions.PreBargainingRound, (byte)1 /* only action -- dummy decision */),
                    ((byte) MyGameDecisions.DNoiseOrSignal, dSignalOrNoise),
                    ((byte)MyGameDecisions.MutualGiveUp, mutualGiveUpResult), // we'll only reach this if both try to give up, so it won't be called in multiple bargaining rounds
                    ((byte)MyGameDecisions.PostBargainingRound, 1 /* only action */),
                    ((byte) MyGameDecisions.CourtDecision, courtResult),
                },
                bargaining
                );
            return actionsToPlay;
        }

        [TestMethod]
        public void CaseGivenUp()
        {
            // settings
            for (byte numPotentialBargainingRounds = 1; numPotentialBargainingRounds <= 3; numPotentialBargainingRounds++)
                foreach (bool forgetEarlierBargainingRounds in new bool[] { true, false })
                foreach (bool simultaneousBargainingRounds in new bool[] { true, false })
                foreach (bool actionIsNoiseNotSignal in new bool[] {true, false})
                foreach (LoserPaysPolicy loserPaysPolicy in new LoserPaysPolicy[] { LoserPaysPolicy.NoLoserPays, LoserPaysPolicy.AfterTrialOnly, LoserPaysPolicy.EvenAfterAbandonOrDefault, })
                foreach (HowToSimulateBargainingFailure simulatingBargainingFailure in new HowToSimulateBargainingFailure[] { HowToSimulateBargainingFailure.PRefusesToBargain, HowToSimulateBargainingFailure.DRefusesToBargain, HowToSimulateBargainingFailure.BothRefuseToBargain, HowToSimulateBargainingFailure.BothAgreeToBargain, HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain })
                {
                    CaseGivenUpVariousActions(numPotentialBargainingRounds, forgetEarlierBargainingRounds, simultaneousBargainingRounds, actionIsNoiseNotSignal, loserPaysPolicy, simulatingBargainingFailure);
                }
        }

        private void CaseGivenUpVariousActions(byte numPotentialBargainingRounds, bool forgetEarlierBargainingRounds,
            bool simultaneousBargainingRounds, bool actionIsNoiseNotSignal, LoserPaysPolicy loserPaysPolicy, HowToSimulateBargainingFailure simulatingBargainingFailure)
        {
            for (byte abandonmentInRound = 0;
                abandonmentInRound <= numPotentialBargainingRounds;
                abandonmentInRound++)
                foreach (bool plaintiffGivesUp in new bool[] {true, false})
                foreach (bool defendantGivesUp in new bool[] {true, false})
                foreach (bool plaintiffWinsIfBothGiveUp in new bool[] {true, false})
                {
                    if (!plaintiffGivesUp && !defendantGivesUp)
                        continue; // not interested in this case
                    if ((!plaintiffGivesUp || !defendantGivesUp) && !plaintiffWinsIfBothGiveUp)
                        continue; // only need to test both values of plaintiff wins if both give up if both give up.
                    CaseGivenUp_SpecificSettingsAndActions(numPotentialBargainingRounds, abandonmentInRound,
                        forgetEarlierBargainingRounds,
                        simultaneousBargainingRounds, actionIsNoiseNotSignal, (byte) LitigationQuality,
                        pReadyToAbandonRound: plaintiffGivesUp ? (byte?) abandonmentInRound : (byte?) null,
                        dReadyToDefaultRound: defendantGivesUp ? (byte?) abandonmentInRound : (byte?) null,
                        mutualGiveUpResult: plaintiffWinsIfBothGiveUp ? (byte) 2 : (byte) 1,
                        loserPaysPolicy: loserPaysPolicy,
                        simulatingBargainingFailure: simulatingBargainingFailure);
                }
        }

        public void CaseGivenUp_SpecificSettingsAndActions(byte numPotentialBargainingRounds, byte? abandonmentInRound, bool forgetEarlierBargainingRounds, bool simultaneousBargainingRounds, bool actionIsNoiseNotSignal, byte litigationQuality, byte? pReadyToAbandonRound, byte? dReadyToDefaultRound, byte mutualGiveUpResult, LoserPaysPolicy loserPaysPolicy, HowToSimulateBargainingFailure simulatingBargainingFailure)
        {
            var bargainingRoundMoves = GetBargainingRoundMoves(simultaneousBargainingRounds,
                abandonmentInRound ?? numPotentialBargainingRounds, false, simulatingBargainingFailure);
            byte numActualRounds = (byte)bargainingRoundMoves.Count();
            var options = GetGameOptions(allowAbandonAndDefaults: true, numBargainingRounds: numPotentialBargainingRounds, forgetEarlierBargainingRounds: forgetEarlierBargainingRounds, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal: actionIsNoiseNotSignal, loserPaysPolicy: loserPaysPolicy, simulatingBargainingFailure: simulatingBargainingFailure);
            bool pFiles = pReadyToAbandonRound != 0;
            bool dAnswers = pFiles && dReadyToDefaultRound != 0;
            var actionsToPlay = GetPlayerActions(pFiles: pFiles, dAnswers: dAnswers, litigationQuality: litigationQuality, pSignalOrNoise: PSignalOrNoise,
                dSignalOrNoise: DSignalOrNoise, bargainingRoundMoves: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, pReadyToAbandonRound: pReadyToAbandonRound, dReadyToDefaultRound: dReadyToDefaultRound, mutualGiveUpResult: mutualGiveUpResult, simulatingBargainingFailure: simulatingBargainingFailure);
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options, actionsToPlay);

            bool pWins = (pReadyToAbandonRound == null && dReadyToDefaultRound != null) ||
                         (pReadyToAbandonRound != null && dReadyToDefaultRound != null && pFiles && mutualGiveUpResult == (byte) 2);

            double damages = pWins ? options.DamagesAlleged : 0;

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
                if (options.LoserPays && options.LoserPaysAfterAbandonment)
                {
                    if (pWins)
                    {
                        pExpenses = 0;
                        dExpenses = options.PFilingCost + options.DAnswerCost + 2 * numActualRounds * options.PerPartyBargainingRoundCosts;
                    }
                    else
                    {
                        dExpenses = 0;
                        pExpenses = options.PFilingCost + options.DAnswerCost + 2 * numActualRounds * options.PerPartyBargainingRoundCosts;
                    }
                }
                else
                {
                    pExpenses = options.PFilingCost + numActualRounds * options.PerPartyBargainingRoundCosts;
                    dExpenses = options.DAnswerCost + numActualRounds * options.PerPartyBargainingRoundCosts;
                }
            }
            else
            { // either p didn't file or p filed and d didn't answer
                pExpenses = myGameProgress.PFiles ? options.PFilingCost : 0;
                dExpenses = 0;
            }

            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + damages - pExpenses);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - damages - dExpenses);
            myGameProgress.PWelfare.Should().Be(myGameProgress.PFinalWealth);
            myGameProgress.DWelfare.Should().Be(myGameProgress.DFinalWealth);

            //var informationSetHistories = myGameProgress.GameHistory.GetInformationSetHistoryItems().ToList();
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            var expectedPartyInformationSets = GetExpectedPartyInformationSets(actionIsNoiseNotSignal, LitigationQuality, PSignalOrNoise, DSignalOrNoise, simulatingBargainingFailure, bargainingRoundMoves, forgetEarlierBargainingRounds, simultaneousBargainingRounds);
            string expectedResolutionSet = ConstructExpectedResolutionSet(pFiles: pFiles, dAnswers: dAnswers,
                bargainingRounds: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal: actionIsNoiseNotSignal, litigationQuality: litigationQuality, settlementReachedLastRound: false, allowAbandonAndDefault: true, pReadyToAbandonLastRound: pReadyToAbandonRound != null, dReadyToDefaultLastRound: dReadyToDefaultRound != null, ifBothDefaultPlaintiffLoses: mutualGiveUpResult == (byte)1, caseGoesToTrial: false, courtResultAtTrial: 0, simulatingBargainingFailure:simulatingBargainingFailure);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        [TestMethod]
        public void SettlingCase()
        {
            int caseNumber = 0;
            try
            {
                for (byte numPotentialBargainingRounds = 1; numPotentialBargainingRounds <= 3; numPotentialBargainingRounds++)
                    foreach (bool forgetEarlierBargainingRounds in new bool[] { true, false })
                    foreach (bool simultaneousBargainingRounds in new bool[] { true, false })
                    foreach (bool allowAbandonAndDefault in new bool[] { true, false })
                    foreach (bool actionIsNoiseNotSignal in new bool[] { true, false })
                    foreach (LoserPaysPolicy loserPaysPolicy in new LoserPaysPolicy[] { LoserPaysPolicy.NoLoserPays, LoserPaysPolicy.AfterTrialOnly, LoserPaysPolicy.EvenAfterAbandonOrDefault, })
                    foreach (HowToSimulateBargainingFailure simulatingBargainingFailure in new HowToSimulateBargainingFailure[] { HowToSimulateBargainingFailure.PRefusesToBargain, HowToSimulateBargainingFailure.DRefusesToBargain, HowToSimulateBargainingFailure.BothRefuseToBargain, HowToSimulateBargainingFailure.BothAgreeToBargain, HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain })
                        for (byte settlementInRound = 1; settlementInRound <= numPotentialBargainingRounds; settlementInRound++)
                        {
                            if (caseNumber == 1091)
                                SettlingCase_Helper(numPotentialBargainingRounds, settlementInRound, forgetEarlierBargainingRounds, simultaneousBargainingRounds, allowAbandonAndDefault, actionIsNoiseNotSignal, loserPaysPolicy, simulatingBargainingFailure);
                            caseNumber++;
                        }
            }
            catch (Exception e)
            {
                throw new Exception($"Failed at case number {caseNumber}. Inner exception: {e.Message}", e);
            }
            throw new NotImplementedException(); // uncomment if putting in case number restriction
        }

        public void SettlingCase_Helper(byte numPotentialBargainingRounds, byte? settlementInRound, bool forgetEarlierBargainingRounds, bool simultaneousBargainingRounds, bool allowAbandonAndDefault, bool actionIsNoiseNotSignal, LoserPaysPolicy loserPaysPolicy, HowToSimulateBargainingFailure simulatingBargainingFailure)
        {
            var bargainingRoundMoves = GetBargainingRoundMoves(simultaneousBargainingRounds,
                settlementInRound ?? numPotentialBargainingRounds, settlementInRound != null, simulatingBargainingFailure);
            byte numActualRounds = (byte) bargainingRoundMoves.Count();
            var options = GetGameOptions(allowAbandonAndDefaults: allowAbandonAndDefault, numBargainingRounds: numPotentialBargainingRounds, forgetEarlierBargainingRounds: forgetEarlierBargainingRounds, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal:actionIsNoiseNotSignal, loserPaysPolicy: loserPaysPolicy, simulatingBargainingFailure: simulatingBargainingFailure);
            var actionsToPlay = GetPlayerActions(pFiles: true, dAnswers: true, litigationQuality: LitigationQuality, pSignalOrNoise: PSignalOrNoise,
                dSignalOrNoise: DSignalOrNoise, simulatingBargainingFailure: simulatingBargainingFailure, bargainingRoundMoves: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds);
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options, actionsToPlay);

            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.CaseSettles.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth - options.PFilingCost + 0.5 * options.DamagesAlleged -
                                                    numActualRounds * options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - options.DAnswerCost - 0.5 * options.DamagesAlleged -
                                                    numActualRounds * options.PerPartyBargainingRoundCosts);
            myGameProgress.PWelfare.Should().Be(myGameProgress.PFinalWealth);
            myGameProgress.DWelfare.Should().Be(myGameProgress.DFinalWealth);

            //var informationSetHistories = myGameProgress.GameHistory.GetInformationSetHistoryItems().ToList();
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            var expectedPartyInformationSets = GetExpectedPartyInformationSets(actionIsNoiseNotSignal, LitigationQuality, PSignalOrNoise, DSignalOrNoise, simulatingBargainingFailure, bargainingRoundMoves, forgetEarlierBargainingRounds, simultaneousBargainingRounds);
            string expectedResolutionSet = ConstructExpectedResolutionSet_CaseSettles(pFiles: true, dAnswers: true, simulatingBargainingFailure: simulatingBargainingFailure,
                bargainingRounds: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal:actionIsNoiseNotSignal, litigationQuality:LitigationQuality, allowAbandonAndDefault: allowAbandonAndDefault);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        [TestMethod]
        public void CaseTried()
        {
            foreach (bool allowAbandonAndDefaults in new bool[] { true, false })
                foreach (bool forgetEarlierBargainingRounds in new bool[] { true, false })
                    foreach (byte numBargainingRounds in new byte[] { 1, 2 })
                        foreach (bool plaintiffWins in new bool[] { true, false })
                            foreach (bool simultaneousBargainingRounds in new bool[] { true, false })
                                foreach (bool actionIsNoiseNotSignal in new bool[] { true, false })
                                    foreach (LoserPaysPolicy loserPaysPolicy in new LoserPaysPolicy[] { LoserPaysPolicy.NoLoserPays, LoserPaysPolicy.AfterTrialOnly, LoserPaysPolicy.EvenAfterAbandonOrDefault, })
                                    foreach (HowToSimulateBargainingFailure simulatingBargainingFailure in new HowToSimulateBargainingFailure[] { HowToSimulateBargainingFailure.PRefusesToBargain, HowToSimulateBargainingFailure.DRefusesToBargain, HowToSimulateBargainingFailure.BothRefuseToBargain, HowToSimulateBargainingFailure.BothAgreeToBargain, HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain })
                                            CaseTried_Helper(allowAbandonAndDefaults, forgetEarlierBargainingRounds, numBargainingRounds, plaintiffWins, simultaneousBargainingRounds, actionIsNoiseNotSignal, loserPaysPolicy, simulatingBargainingFailure);
        }

        private void CaseTried_Helper(bool allowAbandonAndDefaults, bool forgetEarlierBargainingRounds, byte numBargainingRounds, bool plaintiffWins, bool simultaneousBargainingRounds, bool actionIsNoiseNotSignal, LoserPaysPolicy loserPaysPolicy, HowToSimulateBargainingFailure simulatingBargainingFailure)
        {
            var options = GetGameOptions(allowAbandonAndDefaults, numBargainingRounds, forgetEarlierBargainingRounds, simultaneousBargainingRounds, actionIsNoiseNotSignal, loserPaysPolicy, simulatingBargainingFailure);
            var bargainingMoves = GetBargainingRoundMoves(simultaneousBargainingRounds, numBargainingRounds, false, simulatingBargainingFailure);
            byte courtResult;
            if (actionIsNoiseNotSignal)
                courtResult = plaintiffWins ? (byte) NumDistinctPoints : (byte) 1; // we've used a high value of court noise above, so if the court has its highest possible noise, it will definitely conclude plaintiff has won, and if the court has its lowest possible noise, it will definitely conclude that defendant has won
            else
                courtResult = plaintiffWins ? (byte) 2 : (byte) 1;
            var actions = GetPlayerActions(true, true, LitigationQuality, PSignalOrNoise, DSignalOrNoise, simulatingBargainingFailure, bargainingMoves, simultaneousBargainingRounds, null, null, 0, courtResult);
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options, actions);
            myGameProgress.GameComplete.Should().BeTrue();
            double pExpenses, dExpenses;
            if (options.LoserPays)
            {
                if (plaintiffWins)
                {
                    pExpenses = 0;
                    dExpenses = options.PFilingCost + options.DAnswerCost + 2 * numBargainingRounds * options.PerPartyBargainingRoundCosts + options.PTrialCosts + options.DTrialCosts;
                }
                else
                {
                    dExpenses = 0;
                    pExpenses = options.PFilingCost + options.DAnswerCost + 2 * numBargainingRounds * options.PerPartyBargainingRoundCosts + options.PTrialCosts + options.DTrialCosts;
                }
            }
            else
            {
                pExpenses = options.PFilingCost + numBargainingRounds * options.PerPartyBargainingRoundCosts + options.PTrialCosts;
                dExpenses = options.DAnswerCost + numBargainingRounds * options.PerPartyBargainingRoundCosts + options.DTrialCosts;
            }

            double pFinalWealthExpected = options.PInitialWealth - pExpenses;
            double dFinalWealthExpected = options.DInitialWealth - dExpenses;
            if (plaintiffWins)
            {
                pFinalWealthExpected += options.DamagesAlleged;
                dFinalWealthExpected -= options.DamagesAlleged;
            }
            myGameProgress.PFinalWealth.Should().Be(pFinalWealthExpected);
            myGameProgress.DFinalWealth.Should().Be(dFinalWealthExpected);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet,
                out string resolutionSet);
            var expectedPartyInformationSets = GetExpectedPartyInformationSets(actionIsNoiseNotSignal, LitigationQuality, PSignalOrNoise, DSignalOrNoise, simulatingBargainingFailure, bargainingMoves,
                forgetEarlierBargainingRounds, simultaneousBargainingRounds);
            var expectedResolutionSet = ConstructExpectedResolutionSet(actionIsNoiseNotSignal, LitigationQuality, true, true, simulatingBargainingFailure, bargainingMoves,
                simultaneousBargainingRounds, false, allowAbandonAndDefaults, false, false, false, true, courtResult);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }
    }
}
