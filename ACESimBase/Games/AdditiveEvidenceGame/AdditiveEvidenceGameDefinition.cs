using ACESim;
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

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>();
            if (Options.LinearBids)
                AddLinearBidDecisions(decisions);
            AddInitialChanceDecisions(decisions);
            AddQuitDecisions(decisions);
            if (!Options.LinearBids)
                AddPlayerOffers(decisions);
            AddLaterChanceDecisions(decisions);
            return decisions;
        }


        void AddLinearBidDecisions(List<Decision> decisions)
        {
            var pMin =
                    new Decision("PMin", "PM", false, (byte)AdditiveEvidenceGamePlayers.Plaintiff, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
                        Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.P_LinearBid_Min)
                    {
                        IsReversible = true,
                        IsContinuousAction = true,
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_PMin,
                    };
            decisions.Add(pMin);
            var pMax =
                    new Decision("PMax", "PS", false, (byte)AdditiveEvidenceGamePlayers.Plaintiff, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
                        Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.P_LinearBid_Max)
                    {
                        IsReversible = true,
                        IsContinuousAction = true,
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_PMax,
                    };
            decisions.Add(pMax);

            var dMin =
                    new Decision("DMin", "DM", false, (byte)AdditiveEvidenceGamePlayers.Defendant, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
                        Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.D_LinearBid_Min)
                    {
                        IsReversible = true,
                        IsContinuousAction = true,
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_DMin,
                    };
            decisions.Add(dMin);
            var dMax =
                    new Decision("DMax", "DS", false, (byte)AdditiveEvidenceGamePlayers.Defendant, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
                        Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.D_LinearBid_Max)
                    {
                        IsReversible = true,
                        IsContinuousAction = true,
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_DMax,
                    };
            decisions.Add(dMax);
        }

        bool useAbbreviationsForSimplifiedGame = true;

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
                CanTerminateGame = Options.LinearBids
            });
            if (Options.Alpha_Bias > 0 && Options.Alpha_Plaintiff_Bias > 0)
                decisions.Add(new Decision("Chance_Plaintiff_Bias", "PB", true, (byte)AdditiveEvidenceGamePlayers.Chance_Plaintiff_Bias, new byte[] { (byte)AdditiveEvidenceGamePlayers.Plaintiff, (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumQualityAndBiasLevels_PrivateInfo, (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Bias)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                //DistributorChanceInputDecision = true,
                //DistributableDistributorChanceInput = true,
                ProvidesPrivateInformationFor = (byte)AdditiveEvidenceGamePlayers.Plaintiff
            });
            if (Options.Alpha_Bias > 0 && Options.Alpha_Defendant_Bias > 0)
                decisions.Add(new Decision("Chance_Defendant_Bias", "DB", true, (byte)AdditiveEvidenceGamePlayers.Chance_Defendant_Bias, new byte[] { (byte)AdditiveEvidenceGamePlayers.Defendant, (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumQualityAndBiasLevels_PrivateInfo, (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Bias)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                //DistributorChanceInputDecision = true,
                //DistributableDistributorChanceInput = true,
                ProvidesPrivateInformationFor = (byte)AdditiveEvidenceGamePlayers.Defendant,
                CanTerminateGame = Options.LinearBids
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
        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, ref GameHistory gameHistory, byte actionChosen)
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
                case (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Quality:
                    if (Options.LinearBids)
                    {
                        if (!(Options.Alpha_Bias > 0))
                            return true;
                    }
                    return false;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Bias:
                    if (Options.LinearBids)
                    {
                        if (!(Options.Alpha_Bias > 0 && Options.Alpha_Neither_Bias > 0))
                            return true;
                        throw new NotImplementedException(); // if we start using the "neither bias" category, then we need to implement this, following the logic in the game class to determine whether the linear bids produce a settlement. Possibly, easiest thing to do will be to just create a progress object, pass the options to it, and then do the calculations from there. This defeats the purpose of using the game definition class, but that is likely not critical here.
                    }
                    return false;
                    case (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Quality:
                        return !(Options.Alpha_Bias > 0 && Options.Alpha_Neither_Bias > 0); // if there is no bias decision to make, then we're done after the quality decision
                case (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Bias:
                    return true;
            }
            return false;
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
    }
}
