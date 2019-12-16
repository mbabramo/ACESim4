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
        public AdditiveEvidenceGameOptions Options;

        public override string ToString()
        {
            return Options.ToString();
        }

        public AdditiveEvidenceGameDefinition() : base()
        {

        }
        public override void Setup(GameOptions options)
        {
            Options = (AdditiveEvidenceGameOptions)options;
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

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>();
            AddInitialChanceDecisions(decisions);
            AddPlayerOffers(decisions);
            AddLaterChanceDecisions(decisions);
            return decisions;
        }

        void AddInitialChanceDecisions(List<Decision> decisions)
        {
            if (Options.Alpha_Quality > 0 && Options.Alpha_Plaintiff_Quality > 0)
                decisions.Add(new Decision("Chance_Plaintiff_Quality", "PQ", true, (byte)AdditiveEvidenceGamePlayers.Chance_Plaintiff_Quality, new byte[] { (byte)AdditiveEvidenceGamePlayers.Plaintiff, (byte) AdditiveEvidenceGamePlayers.Resolution }, Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Quality)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                //DistributorChanceInputDecision = true,
                //DistributableDistributorChanceInput = true,
                ProvidesPrivateInformationFor = (byte)AdditiveEvidenceGamePlayers.Plaintiff
            });
            if (Options.Alpha_Quality > 0 && Options.Alpha_Defendant_Quality > 0)
                decisions.Add(new Decision("Chance_Defendant_Quality", "DQ", true, (byte)AdditiveEvidenceGamePlayers.Chance_Defendant_Quality, new byte[] { (byte)AdditiveEvidenceGamePlayers.Defendant, (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Quality)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                //DistributorChanceInputDecision = true,
                //DistributableDistributorChanceInput = true,
                ProvidesPrivateInformationFor = (byte)AdditiveEvidenceGamePlayers.Defendant
            });
            if (Options.Alpha_Bias > 0 && Options.Alpha_Plaintiff_Bias > 0)
                decisions.Add(new Decision("Chance_Plaintiff_Bias", "PB", true, (byte)AdditiveEvidenceGamePlayers.Chance_Plaintiff_Bias, new byte[] { (byte)AdditiveEvidenceGamePlayers.Plaintiff, (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Bias)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                //DistributorChanceInputDecision = true,
                //DistributableDistributorChanceInput = true,
                ProvidesPrivateInformationFor = (byte)AdditiveEvidenceGamePlayers.Plaintiff
            });
            if (Options.Alpha_Bias > 0 && Options.Alpha_Defendant_Bias > 0)
                decisions.Add(new Decision("Chance_Defendant_Bias", "DB", true, (byte)AdditiveEvidenceGamePlayers.Chance_Defendant_Bias, new byte[] { (byte)AdditiveEvidenceGamePlayers.Defendant, (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Bias)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                //DistributorChanceInputDecision = true,
                //DistributableDistributorChanceInput = true,
                ProvidesPrivateInformationFor = (byte)AdditiveEvidenceGamePlayers.Defendant
            });
        }
        void AddPlayerOffers(List<Decision> decisions)
        {
            var pOffer =
                    new Decision("PlaintiffOffer", "PO", false, (byte)AdditiveEvidenceGamePlayers.Plaintiff, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
                        Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.POffer)
                    {
                        IsReversible = true,
                        IsContinuousAction = true,
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_POffer,
                    };
            decisions.Add(pOffer);
            var dOffer =
                     new Decision("DefendantOffer", "DO", false, (byte)AdditiveEvidenceGamePlayers.Defendant, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
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
                decisions.Add(new Decision("Chance_Neither_Quality", "NQ", true, (byte)AdditiveEvidenceGamePlayers.Chance_Neither_Quality, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Quality)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                CanTerminateGame = true, // if next decision is skipped
            });
            if (Options.Alpha_Bias > 0 && Options.Alpha_Neither_Bias > 0)
                decisions.Add(new Decision("Chance_Neither_Bias", "NB", true, (byte)AdditiveEvidenceGamePlayers.Chance_Neither_Bias, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Bias)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                CanTerminateGame = true, // must note even though it's the last decision
            });
        }
        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, in GameHistory gameHistory, byte actionChosen)
        {
            if (!currentDecision.CanTerminateGame)
                return false;

            byte decisionByteCode = currentDecision.DecisionByteCode;

            switch (decisionByteCode)
            {
                case (byte)AdditiveEvidenceGameDecisions.DOffer:
                    if (!((Options.Alpha_Quality > 0 && Options.Alpha_Neither_Quality > 0) || (Options.Alpha_Bias > 0 && Options.Alpha_Neither_Bias > 0)))
                        return true; // if no more chance decisions, defendant offer certainly ends it
                    byte plaintiffOffer = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_POffer);
                    byte defendantOffer = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DOffer);
                    if (defendantOffer >= plaintiffOffer)
                        return true;
                    break;
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

        public override int NumPostWarmupOptionSets => 1;
        public override int NumWarmupOptionSets => 0; // Note that this can be 0.
        public override int WarmupIterations_IfWarmingUp => 50; // CORRELATED EQ SETTING
        public override bool MultiplyWarmupScenariosByAlteringWeightOnOpponentsStrategy => true;
        public override int NumDifferentWeightsOnOpponentsStrategyPerWarmupScenario_IfMultiplyingScenarios => 10; // should be odd if we want to include zero
        public override bool VaryWeightOnOpponentsStrategySeparatelyForEachPlayer => true;
        public override (double, double) MinMaxWeightOnOpponentsStrategyDuringWarmup => (-0.8, 0.8); // NOTE: Don't go all the way up to 1, because then if costs multiplier is 0 (i.e., it is a zero-sum game), utility for a player will be invariant.



        #endregion
    }
}
