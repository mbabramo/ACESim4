using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    [Serializable]
    public partial class AdditiveEvidenceGameDefinition : GameDefinition
    {
        public AdditiveEvidenceGameOptions Options => (AdditiveEvidenceGameOptions)GameOptions;

        public DMSCalc DMSCalculations; // used for piecewise linear bids.

        public override string ToString()
        {
            return Options.ToString();
        }

        public AdditiveEvidenceGameDefinition() : base()
        {

        }

        public override void Setup(GameOptions options)
        {
            base.Setup(options);
            FurtherOptionsSetup();

            AdditiveEvidenceGameOptions aeOptions = (AdditiveEvidenceGameOptions)options;
            if (aeOptions.PiecewiseLinearBids)
                DMSCalculations = new DMSCalc(aeOptions.FeeShiftingThreshold, aeOptions.TrialCost, aeOptions.Evidence_Both_Quality);

            Players = GetPlayersList();
            PlayerNames = Players.Select(x => x.PlayerName).ToArray();
            NumPlayers = (byte)Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();
            CalculateDistributorChanceInputDecisionMultipliers();

            IGameFactory gameFactory = new AdditiveEvidenceGameFactory();
            Initialize(gameFactory);
        }

        private static string PlaintiffName = "Plaintiff";
        private static string DefendantName = "Defendant";
        private static string ResolutionPlayerName = "Resolution";
        private static string ChancePlaintiffQualityName = "ChancePlaintiffQuality";
        private static string ChanceDefendantQualityName = "ChanceDefendantQuality";
        private static string ChancePlaintiffBiasName = "ChancePlaintiffBias";
        private static string ChanceDefendantBiasName = "ChanceDefendantBias";
        private static string ChanceNeitherQualityName = "ChanceNeitherQuality";
        private static string ChanceNeitherBiasName = "ChanceNeitherBias";

        private static List<PlayerInfo> GetPlayersList()
        {
            // IMPORTANT: Chance players MUST be listed after other players. Resolution player should be listed afte4r main players.
            return new List<PlayerInfo>
                {
                    new PlayerInfo(PlaintiffName, (int) AdditiveEvidenceGamePlayers.Plaintiff, false, true),
                    new PlayerInfo(DefendantName, (int) AdditiveEvidenceGamePlayers.Defendant, false, true),
                    new PlayerInfo(ResolutionPlayerName, (int) AdditiveEvidenceGamePlayers.Resolution, true, false),
                    new PlayerInfo(ChancePlaintiffQualityName, (int) AdditiveEvidenceGamePlayers.Chance_Plaintiff_Quality, true, false),
                    new PlayerInfo(ChanceDefendantQualityName, (int) AdditiveEvidenceGamePlayers.Chance_Defendant_Quality, true, false),
                    new PlayerInfo(ChancePlaintiffBiasName, (int) AdditiveEvidenceGamePlayers.Chance_Plaintiff_Bias, true, false),
                    new PlayerInfo(ChanceDefendantBiasName, (int) AdditiveEvidenceGamePlayers.Chance_Defendant_Bias, true, false),
                    new PlayerInfo(ChanceNeitherQualityName, (int) AdditiveEvidenceGamePlayers.Chance_Neither_Quality, true, false),
                    new PlayerInfo(ChanceNeitherBiasName, (int) AdditiveEvidenceGamePlayers.Chance_Neither_Bias, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte)AdditiveEvidenceGamePlayers.Resolution;

        // must skip zero
        public byte GameHistoryCacheIndex_POffer = 1;
        public byte GameHistoryCacheIndex_DOffer = 2;
        public byte GameHistoryCacheIndex_PMin = 3;
        public byte GameHistoryCacheIndex_PMax = 4;
        public byte GameHistoryCacheIndex_DMin = 5;
        public byte GameHistoryCacheIndex_DMax = 6;
        public byte GameHistoryCacheIndex_PSlope = 7;
        public byte GameHistoryCacheIndex_PMinValueForRange = 8;
        public byte GameHistoryCacheIndex_DSlope = 9;
        public byte GameHistoryCacheIndex_DMinValueForRange = 10;

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>();
            AddInitialChanceDecisions(decisions);
            AddQuitDecisions(decisions);
            if (Options.PiecewiseLinearBids)
                AddLinearBidDecisions(decisions);
            else
                AddPlayerOffers(decisions);
            AddLaterChanceDecisions(decisions);
            return decisions;
        }


        bool useAbbreviationsForSimplifiedGame = true;

        void AddLinearBidDecisions(List<Decision> decisions)
        {
            decisions.Add(new Decision("P_Slope", useAbbreviationsForSimplifiedGame ? "PSlope" : "PS", true, (byte)AdditiveEvidenceGamePlayers.Plaintiff, new byte[] { (byte)AdditiveEvidenceGamePlayers.Plaintiff, (byte)AdditiveEvidenceGamePlayers.Resolution }, (byte) Options.PiecewiseLinearBidsSlopeOptions.Length, (byte)AdditiveEvidenceGameDecisions.P_Slope));
            decisions.Add(new Decision("P_MinValueForRange", useAbbreviationsForSimplifiedGame ? "P_MinValueForRange" : "PMIN", true, (byte)AdditiveEvidenceGamePlayers.Plaintiff, new byte[] { (byte)AdditiveEvidenceGamePlayers.Plaintiff, (byte)AdditiveEvidenceGamePlayers.Resolution }, (byte)Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.P_MinValueForRange));
            decisions.Add(new Decision("D_Slope", useAbbreviationsForSimplifiedGame ? "DSlope" : "DS", true, (byte)AdditiveEvidenceGamePlayers.Defendant, new byte[] { (byte)AdditiveEvidenceGamePlayers.Defendant, (byte)AdditiveEvidenceGamePlayers.Resolution }, (byte)Options.PiecewiseLinearBidsSlopeOptions.Length, (byte)AdditiveEvidenceGameDecisions.D_Slope));
            decisions.Add(new Decision("D_MinValueForRange", useAbbreviationsForSimplifiedGame ? "D_MinValueForRange" : "DMIN", true, (byte)AdditiveEvidenceGamePlayers.Defendant, new byte[] { (byte)AdditiveEvidenceGamePlayers.Defendant, (byte)AdditiveEvidenceGamePlayers.Resolution }, (byte)Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.D_MinValueForRange));


        }

        void AddInitialChanceDecisions(List<Decision> decisions)
        {
            if (Options.Alpha_Quality > 0 && Options.Alpha_Plaintiff_Quality > 0)
                decisions.Add(new Decision("Chance_Plaintiff_Quality", useAbbreviationsForSimplifiedGame ? "PInfo" : "PQ", true, (byte)AdditiveEvidenceGamePlayers.Chance_Plaintiff_Quality, new byte[] { (byte)AdditiveEvidenceGamePlayers.Plaintiff, (byte) AdditiveEvidenceGamePlayers.Resolution }, Options.NumQualityAndBiasLevels_PrivateInfo, (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Quality)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                //DistributorChanceInputDecision = true,
                //DistributableDistributorChanceInput = true,
                ProvidesPrivateInformationFor = (byte)AdditiveEvidenceGamePlayers.Plaintiff
            });
            if (Options.Alpha_Quality > 0 && Options.Alpha_Defendant_Quality > 0)
                decisions.Add(new Decision("Chance_Defendant_Quality", useAbbreviationsForSimplifiedGame ? "DInfo" : "DQ", true, (byte)AdditiveEvidenceGamePlayers.Chance_Defendant_Quality, new byte[] { (byte)AdditiveEvidenceGamePlayers.Defendant, (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumQualityAndBiasLevels_PrivateInfo, (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Quality)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                //DistributorChanceInputDecision = true,
                //DistributableDistributorChanceInput = true,
                ProvidesPrivateInformationFor = (byte)AdditiveEvidenceGamePlayers.Defendant,
                CanTerminateGame = false
            });
            if (Options.Alpha_Bias > 0 && Options.Alpha_Plaintiff_Bias > 0)
                decisions.Add(new Decision("Chance_Plaintiff_Bias", "PB", true, (byte)AdditiveEvidenceGamePlayers.Chance_Plaintiff_Bias, Options.PiecewiseLinearBids && false /* DEBUG */ ? new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution } : new byte[] { (byte)AdditiveEvidenceGamePlayers.Plaintiff, (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumQualityAndBiasLevels_PrivateInfo, (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Bias)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                //DistributorChanceInputDecision = true,
                //DistributableDistributorChanceInput = true,
                ProvidesPrivateInformationFor = (byte)AdditiveEvidenceGamePlayers.Plaintiff,
                RequiresCustomInformationSetManipulation = Options.PiecewiseLinearBids
            });
            if (Options.Alpha_Bias > 0 && Options.Alpha_Defendant_Bias > 0)
                decisions.Add(new Decision("Chance_Defendant_Bias", "DB", true, (byte)AdditiveEvidenceGamePlayers.Chance_Defendant_Bias, Options.PiecewiseLinearBids && false /* DEBUG */ ? new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution } : new byte[] { (byte)AdditiveEvidenceGamePlayers.Defendant, (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumQualityAndBiasLevels_PrivateInfo, (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Bias)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                //DistributorChanceInputDecision = true,
                //DistributableDistributorChanceInput = true,
                ProvidesPrivateInformationFor = (byte)AdditiveEvidenceGamePlayers.Defendant,
                RequiresCustomInformationSetManipulation = Options.PiecewiseLinearBids,
                CanTerminateGame = false
            });
        }
        void AddQuitDecisions(List<Decision> decisions)
        {
            if (Options.IncludePQuitDecision)
            {
                var pQuit =
                        new Decision("PQuit", "PQT", false, (byte)AdditiveEvidenceGamePlayers.Plaintiff, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
                            2, (byte)AdditiveEvidenceGameDecisions.PQuit)
                        {
                            IsReversible = true,
                            CanTerminateGame = true,
                        };
                decisions.Add(pQuit);
            }
            if (Options.IncludeDQuitDecision)
            {
                var dQuit =
                        new Decision("DQuit", "DQT", false, (byte)AdditiveEvidenceGamePlayers.Defendant, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
                            2, (byte)AdditiveEvidenceGameDecisions.DQuit)
                        {
                            IsReversible = true,
                            CanTerminateGame = true,
                        };
                decisions.Add(dQuit);
            }
        }
        void AddPlayerOffers(List<Decision> decisions)
        {
            var pOffer =
                    new Decision("PlaintiffOffer", useAbbreviationsForSimplifiedGame ? "POffer" : "PO", false, (byte)AdditiveEvidenceGamePlayers.Plaintiff, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
                        Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.POffer)
                    {
                        IsReversible = true,
                        IsContinuousAction = true,
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_POffer,
                    };
            decisions.Add(pOffer);
            var dOffer =
                     new Decision("DefendantOffer", useAbbreviationsForSimplifiedGame ? "DOffer" : "DO", false, (byte)AdditiveEvidenceGamePlayers.Defendant, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
                         Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.DOffer)
                     {
                         IsReversible = true,
                         IsContinuousAction = true,
                         CanTerminateGame = true,
                         StoreActionInGameCacheItem = GameHistoryCacheIndex_DOffer,
                     };
            decisions.Add(dOffer);
        }
        void AddLaterChanceDecisions(List<Decision> decisions)
        {
            if (Options.Alpha_Quality > 0 && Options.Alpha_Neither_Quality > 0)
                decisions.Add(new Decision("Chance_Neither_Quality", "NQ", true, (byte)AdditiveEvidenceGamePlayers.Chance_Neither_Quality, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumQualityAndBiasLevels_NeitherInfo, (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Quality)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                CanTerminateGame = true, // if next decision is skipped
            });
            if (Options.Alpha_Bias > 0 && Options.Alpha_Neither_Bias > 0)
                decisions.Add(new Decision("Chance_Neither_Bias", useAbbreviationsForSimplifiedGame ? "Noise" : "NB", true, (byte)AdditiveEvidenceGamePlayers.Chance_Neither_Bias, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumQualityAndBiasLevels_NeitherInfo, (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Bias)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                CanTerminateGame = true, // must note even though it's the last decision
            });
        }
        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, in GameHistory gameHistory, byte actionChosen)
        {
            // IMPORTANT: Any decision that can terminate the game should be listed as CanTerminateGame = true. 
            // Second, the game should set Progress.GameComplete to true when this termination occurs. 
            // Third, this function should return true when that occurs.

            if (!currentDecision.CanTerminateGame)
                return false;

            byte decisionByteCode = currentDecision.DecisionByteCode;

            switch (decisionByteCode)
            {
                case (byte)AdditiveEvidenceGameDecisions.PQuit:
                    return (actionChosen == 1);
                case (byte)AdditiveEvidenceGameDecisions.DQuit:
                    return (actionChosen == 1);
                case (byte)AdditiveEvidenceGameDecisions.DOffer:
                    if (!((Options.Alpha_Quality > 0 && Options.Alpha_Neither_Quality > 0) || (Options.Alpha_Bias > 0 && Options.Alpha_Neither_Bias > 0)))
                        return true; // if no more chance decisions, defendant offer certainly ends it
                    byte plaintiffOffer = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_POffer);
                    byte defendantOffer = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DOffer);
                    if (defendantOffer >= plaintiffOffer)
                        return true;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.D_MinValueForRange:
                    if (!((Options.Alpha_Quality > 0 && Options.Alpha_Neither_Quality > 0) || (Options.Alpha_Bias > 0 && Options.Alpha_Neither_Bias > 0)))
                        return true; // if no more chance decisions, defendant completion of linear offer certainly ends it
                    throw new NotImplementedException(); // not planning to use piecewise linear in conjunction with quality decisions.
                case (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Quality:
                    return false;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Bias:
                    return false;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Quality:
                        return !(Options.Alpha_Bias > 0 && Options.Alpha_Neither_Bias > 0); // if there is no bias decision to make, then we're done after the quality decision
                case (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Bias:
                    return true;
            }
            return false;
        }

        public override void CustomInformationSetManipulation(Decision currentDecision, byte currentDecisionIndex, byte actionChosen, ref GameHistory gameHistory, GameProgress gameProgress)
        {
            // DEBUG
            //if (currentDecision.DecisionByteCode == (byte) AdditiveEvidenceGameDecisions.Chance_Plaintiff_Bias || currentDecision.DecisionByteCode == (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Bias)
            //{
            //    bool plaintiff = currentDecision.DecisionByteCode == (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Bias;
            //    byte playerIndex = plaintiff ? (byte)AdditiveEvidenceGamePlayers.Plaintiff : (byte)AdditiveEvidenceGamePlayers.Defendant;
            //    double z = EquallySpaced.GetLocationOfEquallySpacedPoint(actionChosen - 1 /* make it zero-based */, Options.NumQualityAndBiasLevels_PrivateInfo, false);
            //    byte regionIndex = DMSCalculations.GetPiecewiseLinearRangeIndex(z, plaintiff: plaintiff);
            //    gameHistory.AddToInformationSetLog(regionIndex, currentDecisionIndex, playerIndex, new byte[] { (byte)playerIndex }, gameProgress);
            //}
            base.CustomInformationSetManipulation(currentDecision, currentDecisionIndex, actionChosen, ref gameHistory, gameProgress);
        }

        private void FurtherOptionsSetup()
        {

        }

        #region Alternative scenarios

        public override bool PlayMultipleScenarios => false; // Note: Even if this is false, we can define a scenario as a "warm-up scenario."

        public override int NumPostWarmupPossibilities => 1;
        public override int NumWarmupPossibilities => 0; // Note that this can be 0.
        public override int WarmupIterations_IfWarmingUp => 100; // CORRELATED EQ SETTING
        public override bool MultiplyWarmupScenariosByAlteringWeightOnOpponentsStrategy => true;
        public override int NumDifferentWeightsOnOpponentsStrategyPerWarmupScenario_IfMultiplyingScenarios => 10; // should be odd if we want to include zero
        public override bool VaryWeightOnOpponentsStrategySeparatelyForEachPlayer => true;
        public override (double, double) MinMaxWeightOnOpponentsStrategyDuringWarmup => (-0.8, 0.8); // NOTE: Don't go all the way up to 1, because then if costs multiplier is 0 (i.e., it is a zero-sum game), utility for a player will be invariant.



        #endregion

        #region Diagrams

        public override IEnumerable<(string filename, string reportcontent)> ProduceManualReports(List<(GameProgress theProgress, double weight)> gameProgresses, string supplementalString)
        {
            var contents = SignalOfferReport.GenerateReport(this, gameProgresses, SignalOfferReport.TypeOfReport.Offers);
            yield return (OptionSetName + $"-heatmap{supplementalString}.tex", contents[0]);
        }

        #endregion
    }
}
