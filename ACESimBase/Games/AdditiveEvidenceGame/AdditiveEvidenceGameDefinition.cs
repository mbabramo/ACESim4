using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingSupport;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Statistical;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            AdditiveEvidenceGameOptions aeOptions = (AdditiveEvidenceGameOptions)options;

            Players = GetPlayersList();
            PlayerNames = Players.Select(x => x.PlayerName).ToArray();
            NumPlayers = (byte)Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();
            CalculateDistributorChanceInputDecisionMultipliers();

            IGameFactory gameFactory = new AdditiveEvidenceGameFactory();
            Initialize(gameFactory);
        }

        private static string PlaintiffName = "P";
        private static string DefendantName = "D";
        private static string ResolutionPlayerName = "Resolution";
        private static string ChancePlaintiffQualityName = "ChancePlaintiffQuality";
        private static string ChanceDefendantQualityName = "ChanceDefendantQuality";
        private static string ChancePlaintiffBiasName = "ChancePlaintiffBias";
        private static string ChanceDefendantBiasName = "ChanceDefendantBias";
        private static string ChanceNeitherQualityName = "ChanceNeitherQuality";
        private static string ChanceNeitherBiasName = "ChanceNeitherBias";

        private static List<PlayerInfo> GetPlayersList()
        {
            // IMPORTANT: Chance players MUST be listed after other players. Resolution player should be listed after main players.
            return new List<PlayerInfo>
                {
                    new PlayerInfo(PlaintiffName, (int) AdditiveEvidenceGamePlayers.Plaintiff, false, true),
                    new PlayerInfo(DefendantName, (int) AdditiveEvidenceGamePlayers.Defendant, false, true),
                    new PlayerInfo(ResolutionPlayerName, (int) AdditiveEvidenceGamePlayers.Resolution, true, false),
                    new PlayerInfo(ChancePlaintiffQualityName, (int) AdditiveEvidenceGamePlayers.Chance_Plaintiff_Quality, true, false),
                    new PlayerInfo(ChanceDefendantQualityName, (int) AdditiveEvidenceGamePlayers.Chance_Defendant_Quality, true, false),
                    new PlayerInfo(ChancePlaintiffBiasName, (int) AdditiveEvidenceGamePlayers.Chance_Plaintiff_Bias, true, false),
                    new PlayerInfo(ChancePlaintiffBiasName, (int) AdditiveEvidenceGamePlayers.Chance_Plaintiff_Bias_Reduction, true, false),
                    new PlayerInfo(ChanceDefendantBiasName, (int) AdditiveEvidenceGamePlayers.Chance_Defendant_Bias, true, false),
                    new PlayerInfo(ChanceDefendantBiasName, (int) AdditiveEvidenceGamePlayers.Chance_Defendant_Bias_Reduction, true, false),
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
            AddPlayerOffers(decisions);
            AddLaterChanceDecisions(decisions);
            return decisions;
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
            });
            if (Options.Alpha_Quality > 0 && Options.Alpha_Defendant_Quality > 0)
                decisions.Add(new Decision("Chance_Defendant_Quality", useAbbreviationsForSimplifiedGame ? "DInfo" : "DQ", true, (byte)AdditiveEvidenceGamePlayers.Chance_Defendant_Quality, new byte[] { (byte)AdditiveEvidenceGamePlayers.Defendant, (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumQualityAndBiasLevels_PrivateInfo, (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Quality)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                //DistributorChanceInputDecision = true,
                //DistributableDistributorChanceInput = true,
                CanTerminateGame = false
            });
            if (Options.IncludeChanceDecisionsEvenForPartyWithNoInfluence || (Options.Alpha_Bias > 0 && Options.Alpha_Plaintiff_Bias > 0)) 
            {
                decisions.Add(new Decision("P Signal" /* "Chance_Plaintiff_Bias" */, "PS", true, (byte)AdditiveEvidenceGamePlayers.Chance_Plaintiff_Bias, new byte[] { (byte)AdditiveEvidenceGamePlayers.Plaintiff, (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumQualityAndBiasLevels_PrivateInfo, (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Bias)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    //DistributorChanceInputDecision = true,
                    //DistributableDistributorChanceInput = true,
                });
            }
            if (Options.IncludeChanceDecisionsEvenForPartyWithNoInfluence || (Options.Alpha_Bias > 0 && Options.Alpha_Defendant_Bias > 0))
            {
                decisions.Add(new Decision("D Signal" /* "Chance_Defendant_Bias" */, "DS", true, (byte)AdditiveEvidenceGamePlayers.Chance_Defendant_Bias, new byte[] { (byte)AdditiveEvidenceGamePlayers.Defendant, (byte)AdditiveEvidenceGamePlayers.Resolution }, Options.NumQualityAndBiasLevels_PrivateInfo, (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Bias)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    //DistributorChanceInputDecision = true,
                    //DistributableDistributorChanceInput = true,
                    CanTerminateGame = false
                });
            }
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
                    new Decision("P Offer", useAbbreviationsForSimplifiedGame ? "POffer" : "PO", false, (byte)AdditiveEvidenceGamePlayers.Plaintiff, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
                        Options.NumOffers, (byte)AdditiveEvidenceGameDecisions.POffer)
                    {
                        IsReversible = true,
                        IsContinuousAction = true,
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_POffer,
                    };
            decisions.Add(pOffer);
            var dOffer =
                     new Decision("D Offer", useAbbreviationsForSimplifiedGame ? "DOffer" : "DO", false, (byte)AdditiveEvidenceGamePlayers.Defendant, new byte[] { (byte)AdditiveEvidenceGamePlayers.Resolution },
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

        public double PBestGuessFromSingleSignal(int signal)
        {
            double continuousSignal = EquallySpaced.GetLocationOfEquallySpacedPoint(signal - 1 /* make it zero-based */, Options.NumQualityAndBiasLevels_PrivateInfo, false);
            return Options.Alpha_Quality * (Options.Alpha_Both_Quality * Options.Evidence_Both_Quality + Options.Alpha_Plaintiff_Quality * continuousSignal + Options.Alpha_Defendant_Quality * 0.5 /* best guess of defendant's evidence */ + Options.Alpha_Neither_Quality * 0.5) + Options.Alpha_Bias * (Options.Alpha_Both_Bias * Options.Evidence_Both_Bias + Options.Alpha_Plaintiff_Bias * continuousSignal + Options.Alpha_Defendant_Bias * 0.5 + Options.Alpha_Neither_Bias * 0.5);
        }

        public double DBestGuessFromSingleSignal(int signal)
        {
            double continuousSignal = EquallySpaced.GetLocationOfEquallySpacedPoint(signal - 1 /* make it zero-based */, Options.NumQualityAndBiasLevels_PrivateInfo, false);
            return Options.Alpha_Quality * (Options.Alpha_Both_Quality * Options.Evidence_Both_Quality + Options.Alpha_Plaintiff_Quality * 0.5 + Options.Alpha_Defendant_Quality * continuousSignal + Options.Alpha_Neither_Quality * 0.5) + Options.Alpha_Bias * (Options.Alpha_Both_Bias * Options.Evidence_Both_Bias + Options.Alpha_Plaintiff_Bias * 0.5 + Options.Alpha_Defendant_Bias * continuousSignal + Options.Alpha_Neither_Bias * 0.5);
        }

        public override List<IMaybeExact<T>> GetSequenceFormInitialization<T>(bool initializeToFinalValues)
        {
            bool useDMS = Options.UseDMS; 
            var probabilities = GetProbabilitiesFocusedOnBestGuessOrDMS<T>(initializeToFinalValues, useDMS);
            // TabbedText.WriteLine("PROBS: " + String.Join(",", probabilities.Select(p => p.ToString())));
            return probabilities;
        }

        public List<IMaybeExact<T>> GetProbabilitiesFocusedOnBestGuessOrDMS<T>(bool usePureStrategies, bool useDMS) where T : IMaybeExact<T>, new()
        {
            List<IMaybeExact<T>> probabilities = new List<IMaybeExact<T>>();

            DMSCalc dmsCalc = new DMSCalc(Options.FeeShiftingThreshold, Options.TrialCost, Options.Evidence_Both_Quality);
            for (int playerIndex = 1; playerIndex <= 2; playerIndex++)
            {
                var dmsBids = dmsCalc.GetBids(Options.NumQualityAndBiasLevels_PrivateInfo).ToArray();
                for (int signalIndex = 0; signalIndex < Options.NumQualityAndBiasLevels_PrivateInfo; signalIndex++)
                {
                    double exactValue;
                    if (useDMS)
                        exactValue = playerIndex == 1 ? dmsBids[signalIndex].pBid : dmsBids[signalIndex].dBid;
                    else
                        exactValue = playerIndex == 1 ? PBestGuessFromSingleSignal(signalIndex + 1) : DBestGuessFromSingleSignal(signalIndex + 1);
                    var distances = EquallySpaced.GetAbsoluteDistanceFromLocation(exactValue, Options.NumQualityAndBiasLevels_PrivateInfo, false);
                    if (usePureStrategies)
                    {
                        double minDistance = distances.Min();
                        int minDistanceIndex = -1;
                        for (int i = 0; i < distances.Length; i++)
                            if (distances[i] == minDistance)
                            { 
                                minDistanceIndex = i; 
                                break; 
                            }
                        for (int i = 0; i < distances.Length; i++)
                            if (i == minDistanceIndex)
                                probabilities.Add(IMaybeExact<T>.One());
                            else
                                probabilities.Add(IMaybeExact<T>.Zero());
                    }
                    else
                    {
                        const double k = 3;
                        var relativeValues = distances.Select(d => Math.Pow(1 / Math.Max(d, 0.001), k)).ToList();
                        var sum = relativeValues.Sum();
                        var addingToApproxOne = relativeValues.Select(v => Math.Max((decimal)0.001, Math.Round((decimal)(v / sum), 3))).ToList();
                        var addingToOneThousand = addingToApproxOne.Select(v => (int)(v * 1000)).ToArray();
                        int overage = addingToOneThousand.Sum() - 1000;
                        for (int i = 0; i < overage; i++)
                        {
                            int indexOfMax = Array.IndexOf(addingToOneThousand, addingToOneThousand.Max());
                            addingToOneThousand[indexOfMax] -= 1;
                        }
                        IMaybeExact<T> denom = IMaybeExact<T>.FromInteger(1000);
                        var probabilitiesToAddToList = addingToOneThousand.Select(v => IMaybeExact<T>.FromInteger(v).DividedBy(denom)).ToList();
                        probabilities.AddRange(probabilitiesToAddToList);
                    }
                }
            }
            return probabilities;
        }

        #endregion

        #region Diagrams

        public override IEnumerable<(string filename, string reportcontent)> ProduceManualReports(List<(GameProgress theProgress, double weight)> gameProgresses, string supplementalString)
        {
            double includeMultiplesOf = 0.1;
            double remainder = Math.Abs(Options.FeeShiftingThreshold % includeMultiplesOf);
            if (remainder > includeMultiplesOf * 0.5) // e.g., 0.049999 -> 0.049999
                remainder = includeMultiplesOf - remainder;
            if (remainder > 1E-6)
                yield break;
            Stopwatch w = new Stopwatch();
            w.Start();
            var contents = SignalOfferReport.GenerateReport(this, gameProgresses, SignalOfferReport.TypeOfReport.Offers);
            yield return (OptionSetName + $"-offers{supplementalString}.tex", contents[0]);
            w.Stop();
            TabbedText.WriteLine($"Produced manual reports; time {w.ElapsedMilliseconds} ms");
        }

        private enum AdditiveEvidenceTreeDiagramExclusions
        {
            FullDiagram,
            BeginningOfGame,
            EndOfGame
        }
        private AdditiveEvidenceTreeDiagramExclusions Exclusions = AdditiveEvidenceTreeDiagramExclusions.FullDiagram;

        public override (Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> excludeBelow, Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> includeBelow) GetTreeDiagramExclusions()
        {
            (Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> excludeBelow, Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> includeBelow)
                = (null, null);
            switch (Exclusions)
            {
                case AdditiveEvidenceTreeDiagramExclusions.FullDiagram:
                    break;

                case AdditiveEvidenceTreeDiagramExclusions.BeginningOfGame:
                    excludeBelow = gpn =>
                    {
                        var edge = gpn.EdgeFromParent;
                        if (edge == null)
                            return false;
                        string actionString = edge.parentNameWithActionString(this);
                        string nodePlayerString = gpn.NodePlayerString(this);
                        if (actionString.Contains("D Signal"))
                            return true;
                        return false;
                    };
                    break;

                case AdditiveEvidenceTreeDiagramExclusions.EndOfGame:
                    includeBelow = gpn =>
                    {
                        var edge = gpn.EdgeFromParent;
                        if (edge == null)
                            return false;
                        string actionString = edge.parentNameWithActionString(this);
                        string nodePlayerString = gpn.NodePlayerString(this);
                        if (actionString.Contains("D Signal"))
                            return true;
                        return false;
                    };
                    break;

                default:
                    break;
            }

            return (excludeBelow, includeBelow);
        }

        #endregion
    }
}
