using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESim.Util;

namespace ACESim
{
    [Serializable]
    public partial class MyGameDefinition : GameDefinition
    {
        public MyGameOptions Options;

        #region Construction and setup

        public MyGameDefinition() : base()
        {

        }

        public void Setup(MyGameOptions options)
        {
            Options = options;
            FurtherOptionsSetup();

            Players = GetPlayersList();
            NumPlayers = (byte) Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();

            IGameFactory gameFactory = new MyGameFactory();
            Initialize(gameFactory);
        }



        private void FurtherOptionsSetup()
        {
            if (Options.DeltaOffersOptions.SubsequentOffersAreDeltas)
                Options.DeltaOffersCalculation = new DeltaOffersCalculation(this);
            Options.PSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumLitigationQualityPoints,
                StdevOfNormalDistribution = Options.PNoiseStdev,
                NumSignals = Options.NumSignals
            };
            Options.DSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumLitigationQualityPoints,
                StdevOfNormalDistribution = Options.DNoiseStdev,
                NumSignals = Options.NumSignals
            };
            if (Options.LitigationQualitySource == MyGameOptions.LitigationQualitySourceEnum.EachValueEquallyLikely)
                CorrectnessGivenLitigationQuality = MonotonicCurve.CalculateCurvatureForThreePoints(0.5, 0.5, 0.75, Options.ProbabilityTrulyLiable_LitigationQuality75, 0.9, Options.ProbabilityTrulyLiable_LitigationQuality90);
            if (Options.LitigationQualitySource == MyGameOptions.LitigationQualitySourceEnum.GenerateFromTrulyLiableStatus)
                CreateExogenousLitigationQualityTable();
        }

        private MyGameProgress MyGP(GameProgress gp) => gp as MyGameProgress;

        private static string PlaintiffName = "P";
        private static string DefendantName = "D";
        private static string TrulyLiableChanceName = "TLC";
        private static string LitigationQualityChanceName = "QC";
        private static string PlaintiffNoiseOrSignalChanceName = "PNS";
        private static string DefendantNoiseOrSignalChanceName = "DNS";
        private static string BothGiveUpChanceName = "GUC";
        private static string PreBargainingRoundChanceName = "PRE";
        private static string PostBargainingRoundChanceName = "POST";
        private static string CourtChanceName = "CC";
        private static string ResolutionPlayerName = "R";

        private static List<PlayerInfo> GetPlayersList()
        {
            // IMPORTANT: Chance players MUST be listed after other players. Resolution player should be listed last.
            return new List<PlayerInfo>
                {
                    new PlayerInfo(PlaintiffName, (int) MyGamePlayers.Plaintiff, false, true),
                    new PlayerInfo(DefendantName, (int) MyGamePlayers.Defendant, false, true),
                    new PlayerInfo(ResolutionPlayerName, (int) MyGamePlayers.Resolution, true, false),
                    new PlayerInfo(TrulyLiableChanceName, (int) MyGamePlayers.TrulyLiableChance, true, false),
                    new PlayerInfo(LitigationQualityChanceName, (int) MyGamePlayers.QualityChance, true, false),
                    new PlayerInfo(PlaintiffNoiseOrSignalChanceName, (int) MyGamePlayers.PNoiseOrSignalChance, true, false),
                    new PlayerInfo(DefendantNoiseOrSignalChanceName, (int) MyGamePlayers.DNoiseOrSignalChance, true, false),
                    new PlayerInfo(BothGiveUpChanceName, (int) MyGamePlayers.BothGiveUpChance, true, false),
                    new PlayerInfo(PreBargainingRoundChanceName, (int) MyGamePlayers.PreBargainingRoundChance, true, false),
                    new PlayerInfo(PostBargainingRoundChanceName, (int) MyGamePlayers.PostBargainingRoundChance, true, false),
                    new PlayerInfo(CourtChanceName, (int) MyGamePlayers.CourtChance, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte) MyGamePlayers.Resolution;

        // NOTE: Must skip 0, because that is used for subdivision aggregation decisions. Note that the first three may be augmented if we are using subdivision decisions
        public byte GameHistoryCacheIndex_TrulyLiable = 1;
        public byte GameHistoryCacheIndex_LitigationQuality = 2;
        public byte GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound = 3;
        public byte GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound = 4;
        public byte GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound = 5;
        public byte GameHistoryCacheIndex_PAgreesToBargain = 6;
        public byte GameHistoryCacheIndex_DAgreesToBargain = 7;
        public byte GameHistoryCacheIndex_POffer = 8;
        public byte GameHistoryCacheIndex_DOffer = 9;
        public byte GameHistoryCacheIndex_PResponse = 10;
        public byte GameHistoryCacheIndex_DResponse = 11;
        public byte GameHistoryCacheIndex_PReadyToAbandon = 12;
        public byte GameHistoryCacheIndex_DReadyToAbandon = 13;

        #endregion

        #region Decisions list

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>();
            AddLitigationQualityAndSignalsDecisions(decisions);
            AddFileAndAnswerDecisions(decisions);
            for (int b = 0; b < Options.NumPotentialBargainingRounds; b++)
            {
                AddPreBargainingRoundDummyDecision(b, decisions);
                AddDecisionsForBargainingRound(b, decisions);
                if (Options.AllowAbandonAndDefaults)
                    AddAbandonOrDefaultDecisions(b, decisions);
                AddPostBargainingRoundDummyDecision(b, decisions);
            }
            AddCourtDecision(decisions);
            return decisions;
        }

        private void AddLitigationQualityAndSignalsDecisions(List<Decision> decisions)
        {
            // Litigation Quality. This is not known by a player unless the player has perfect information. 
            // The SignalChance player relies on this information in calculating the probabilities of different signals
            List<byte> playersKnowingLitigationQuality = new List<byte>()
            {
                (byte) MyGamePlayers.PNoiseOrSignalChance,
                (byte) MyGamePlayers.DNoiseOrSignalChance,
                (byte) MyGamePlayers.CourtChance
            };
            if (Options.ActionIsNoiseNotSignal)
                playersKnowingLitigationQuality.Add((byte) MyGamePlayers.Resolution);
            if (Options.PNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte) MyGamePlayers.Plaintiff);
            if (Options.DNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte) MyGamePlayers.Defendant);
            if (Options.LitigationQualitySource == MyGameOptions.LitigationQualitySourceEnum.EachValueEquallyLikely)
            {
                decisions.Add(new Decision("LitigationQuality", "Qual", (byte) MyGamePlayers.QualityChance,
                    playersKnowingLitigationQuality.ToArray(), Options.NumLitigationQualityPoints, (byte) MyGameDecisions.LitigationQuality) {StoreActionInGameCacheItem = GameHistoryCacheIndex_LitigationQuality, IsReversible = true});
            }
            else
            {
                decisions.Add(new Decision("TrulyLiable", "Truly", (byte)MyGamePlayers.TrulyLiableChance, null, 2, (byte)MyGameDecisions.TrulyLiable) { StoreActionInGameCacheItem = GameHistoryCacheIndex_TrulyLiable, IsReversible = true, UnevenChanceActions = Options.ExogenousProbabilityTrulyLiable != 0.5});
                decisions.Add(new Decision("LitigationQuality", "Qual", (byte)MyGamePlayers.QualityChance,
                        playersKnowingLitigationQuality.ToArray(), Options.NumLitigationQualityPoints, (byte)MyGameDecisions.LitigationQuality)
                    { StoreActionInGameCacheItem = GameHistoryCacheIndex_LitigationQuality, IsReversible = true, UnevenChanceActions = true });
            }
            // Plaintiff and defendant signals. If a player has perfect information, then no signal is needed.
            // when action is the signal, we have an uneven chance decision, and the party receives the signal directly. When the action is the noise, we still want the party to receive the signal rather than the noise and we add that with custom information set manipulation below.
            if (!Options.ActionIsNoiseNotSignal && Options.NumNoiseValues != Options.NumSignals)
                throw new NotImplementedException(); // our uneven chance probabilities assumes this is true
            if (Options.ActionIsNoiseNotSignal)
            {

                if (Options.PNoiseStdev != 0)
                    decisions.Add(new Decision("PlaintiffNoise", "PN", (byte)MyGamePlayers.PNoiseOrSignalChance,
                        null,
                        Options.NumNoiseValues, (byte)MyGameDecisions.PNoiseOrSignal, unevenChanceActions: false)
                        {
                            RequiresCustomInformationSetManipulation = true,
                            IsReversible = true // custom ReverseDecision defined below
                        });
                if (Options.DNoiseStdev != 0)
                    decisions.Add(new Decision("DefendantNoise", "DN", (byte)MyGamePlayers.DNoiseOrSignalChance,
                        null,
                        Options.NumNoiseValues, (byte)MyGameDecisions.DNoiseOrSignal, unevenChanceActions: false)
                    {
                        RequiresCustomInformationSetManipulation = true,
                        IsReversible = true // custom ReverseDecision defined below
                    });
            }
            else
            {
                if (Options.PNoiseStdev != 0)
                    decisions.Add(new Decision("PlaintiffSignal", "PSig", (byte)MyGamePlayers.PNoiseOrSignalChance,
                        new byte[] { (byte)MyGamePlayers.Plaintiff },
                        Options.NumNoiseValues, (byte)MyGameDecisions.PNoiseOrSignal, unevenChanceActions: true) { IsReversible = true });
                if (Options.DNoiseStdev != 0)
                    decisions.Add(new Decision("DefendantSignal", "DSig", (byte)MyGamePlayers.DNoiseOrSignalChance,
                        new byte[] { (byte)MyGamePlayers.Defendant },
                        Options.NumNoiseValues, (byte)MyGameDecisions.DNoiseOrSignal, unevenChanceActions: true)
                        { IsReversible = true });
            }
            if (Options.ActionIsNoiseNotSignal)
                CreateSignalsTables();
            else
            { // make sure we don't use it!
                PSignalsTable = null;
                DSignalsTable = null;
            }
        }

        public void ConvertNoiseToSignal(byte litigationQuality, byte noise, bool plaintiff, out byte discreteSignal,
            out double uniformSignal)
        {
            ValueTuple<byte, double> tableValue;
            if (plaintiff)
                tableValue = PSignalsTable[litigationQuality, noise];
            else
                tableValue = DSignalsTable[litigationQuality, noise];
            discreteSignal = tableValue.Item1;
            uniformSignal = tableValue.Item2;
        }

        public double CorrectnessGivenLitigationQuality;

        private double[] ProbabilityOfTrulyLiabilityValues, ProbabilitiesLitigationQuality_TrulyNotLiable, ProbabilitiesLitigationQuality_TrulyLiable;
        public void CreateExogenousLitigationQualityTable()
        {
            ProbabilityOfTrulyLiabilityValues = new double[] {1.0 - Options.ExogenousProbabilityTrulyLiable, Options.ExogenousProbabilityTrulyLiable};
            // A case is assigned a "true" value of 1 (should not be liable) or 2 (should be liable).
            // Based on the litigation quality noise parameter, we then collect a distribution of possible realized values, on the assumption
            // that the true values are equally likely. We then break this distribution into evenly sized buckets to get cutoff points.
            DiscreteValueSignalParameters dsParams = new DiscreteValueSignalParameters() {NumPointsInSourceUniformDistribution = 2, NumSignals = Options.NumLitigationQualityPoints, StdevOfNormalDistribution = Options.StdevNoiseToProduceLitigationQuality, UseEndpoints = true};
            ProbabilitiesLitigationQuality_TrulyNotLiable = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, dsParams);
            ProbabilitiesLitigationQuality_TrulyLiable = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, dsParams);
        }

        private ValueTuple<byte, double>[,] PSignalsTable, DSignalsTable;
        public void CreateSignalsTables()
        {
            PSignalsTable = new ValueTuple<byte, double>[Options.NumLitigationQualityPoints + 1, Options.NumNoiseValues + 1];
            DSignalsTable = new ValueTuple<byte, double>[Options.NumLitigationQualityPoints + 1, Options.NumNoiseValues + 1];
            for (byte litigationQuality = 1;
                litigationQuality <= Options.NumLitigationQualityPoints;
                litigationQuality++)
            {
                double litigationQualityUniform =
                    EquallySpaced.GetLocationOfEquallySpacedPoint(litigationQuality - 1,
                        Options.NumLitigationQualityPoints, false);
                for (byte noise = 1; noise <= Options.NumNoiseValues; noise++)
                {
                    MyGame.ConvertNoiseActionToDiscreteAndUniformSignal(noise,
                        litigationQualityUniform, Options.NumNoiseValues,
                        Options.PNoiseStdev, Options.NumSignals,
                        out byte pDiscreteSignal, out double pUniformSignal);
                    PSignalsTable[litigationQuality, noise] = (pDiscreteSignal, pUniformSignal);
                    MyGame.ConvertNoiseActionToDiscreteAndUniformSignal(noise,
                        litigationQualityUniform, Options.NumNoiseValues,
                        Options.DNoiseStdev, Options.NumSignals,
                        out byte dDiscreteSignal, out double dUniformSignal);
                    DSignalsTable[litigationQuality, noise] = (dDiscreteSignal, dUniformSignal);
                }
            }
        }
        
        private void AddFileAndAnswerDecisions(List<Decision> decisions)
        {
            var pFile =
                new Decision("PFile", "PF", (byte)MyGamePlayers.Plaintiff, new byte[]  { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.PFile)
                {
                    CanTerminateGame = true, // not filing always terminates
                    IsReversible = true
                };
            decisions.Add(pFile);

            var dAnswer =
                new Decision("DAnswer", "DA", (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.DAnswer)
                {
                    CanTerminateGame = true, // not answering terminates, with defendant paying full damages
                    IsReversible = true
                };
            decisions.Add(dAnswer);
        }

        private void AddDecisionsForBargainingRound(int b, List<Decision> decisions)
        {
            // Agreement to bargain: We do want to add this to the information set of the opposing player, since that may be relevant in future rounds and also might affect decisions whether to abandon/default later in this round, but we want to defer addition of the plaintiff statement, so that it doesn't influence the defendant decision.

            if (Options.IncludeAgreementToBargainDecisions)
            {
                var pAgreeToBargain = new Decision("PAgreeToBargain" + (b + 1), "PB" + (b + 1), (byte) MyGamePlayers.Plaintiff, new byte[] { (byte) MyGamePlayers.Resolution, (byte) MyGamePlayers.Defendant},
                    2, (byte) MyGameDecisions.PAgreeToBargain)
                {
                    CustomByte = (byte) (b + 1),
                    IncrementGameCacheItem = new byte[] {GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound},
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_PAgreesToBargain,
                    DeferNotificationOfPlayers = true
                };
                decisions.Add(pAgreeToBargain);

                var dAgreeToBargain = new Decision("DAgreeToBargain" + (b + 1), "DB" + (b + 1), (byte) MyGamePlayers.Defendant, new byte[] { (byte) MyGamePlayers.Resolution, (byte) MyGamePlayers.Plaintiff},
                    2, (byte) MyGameDecisions.DAgreeToBargain)
                {
                    CustomByte = (byte) (b + 1),
                    IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound},
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_DAgreesToBargain
                };
                decisions.Add(dAgreeToBargain);
            }

            // note that we will do all information set manipulation in CustomInformationSetManipulation below.
            if (Options.BargainingRoundsSimultaneous)
            {
                // samuelson-chaterjee bargaining.
                var pOffer =
                    new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), (byte)MyGamePlayers.Plaintiff, new byte[] { (byte)MyGamePlayers.Resolution, (byte) MyGamePlayers.Defendant },
                        Options.NumOffers, (byte)MyGameDecisions.POffer)
                    {
                        CustomByte = (byte)(b + 1),
                        IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound },
                        DeferNotificationOfPlayers = true, // wait until after defendant has gone for defendant to find out -- of course, we don't do that with defendant decision
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_POffer,
                    };
                if (Options.SubdivideOffers)
                {
                    var list = pOffer.IncrementGameCacheItem.ToList();
                    list.Add(GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound);
                    pOffer.IncrementGameCacheItem = list.ToArray(); // for detour marker
                }
                AddOfferDecisionOrSubdivisions(decisions, pOffer);
                var dOffer =
                    new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Resolution, (byte) MyGamePlayers.Plaintiff },
                        Options.NumOffers, (byte)MyGameDecisions.DOffer)
                    {
                        CanTerminateGame = true,
                        CustomByte = (byte)(b + 1),
                        IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound },
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_DOffer,
                    };
                if (Options.SubdivideOffers)
                {
                    var list = dOffer.IncrementGameCacheItem.ToList();
                    list.Add(GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound);
                    dOffer.IncrementGameCacheItem = list.ToArray(); // for detour marker
                }
                AddOfferDecisionOrSubdivisions(decisions, dOffer);
            }
            else
            {
                // offer-response bargaining. We add the offer and response to the opposing players' information sets. Note that the reason that we add the response is only so that if we decide to have some other decision in this bargaining round, we don't have the decisions confused. 
                if (Options.PGoesFirstIfNotSimultaneous[b])
                {
                    var pOffer =
                        new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), (byte)MyGamePlayers.Plaintiff, new byte[] { (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
                            Options.NumOffers, (byte)MyGameDecisions.POffer)
                        {
                            CustomByte = (byte)(b + 1),
                            IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound },
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_POffer,
                        }; // { AlwaysDoAction = 4});
                    if (Options.SubdivideOffers)
                        pOffer.IncrementGameCacheItem = pOffer.IncrementGameCacheItem.Concat(new byte[] {GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound}).ToArray(); // for detour marker
                    AddOfferDecisionOrSubdivisions(decisions, pOffer);
                    decisions.Add(
                        new Decision("DefendantResponse" + (b + 1), "DR" + (b + 1), (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Resolution }, 2,
                            (byte)MyGameDecisions.DResponse)
                        {
                            CanTerminateGame = true,
                            CustomByte = (byte)(b + 1),
                            IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound },
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_DResponse,
                        });
                }
                else
                {
                    var dOffer =
                        new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Resolution },
                            Options.NumOffers, (byte)MyGameDecisions.DOffer)
                        {
                            CustomByte = (byte)(b + 1),
                            IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound },
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_DOffer,
                        };
                    if (Options.SubdivideOffers)
                        dOffer.IncrementGameCacheItem = dOffer.IncrementGameCacheItem.Concat(new byte[] {GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound}).ToArray(); // for detour marker
                    AddOfferDecisionOrSubdivisions(decisions, dOffer);
                    decisions.Add(
                        new Decision("PlaintiffResponse" + (b + 1), "PR" + (b + 1), (byte)MyGamePlayers.Plaintiff, new byte[] { (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution }, 2,
                            (byte)MyGameDecisions.PResponse)
                        {
                            CanTerminateGame = true,
                            CustomByte = (byte)(b + 1),
                            IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound },
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_PResponse,
                        });
                }
            }
        }

        private void AddOfferDecisionOrSubdivisions(List<Decision> decisions, Decision offerDecision)
        {
            AddPotentiallySubdividableDecision(decisions, offerDecision, Options.SubdivideOffers, (byte)MyGameDecisions.SubdividableOffer, 2, Options.NumOffers);
        }
        
        private void AddAbandonOrDefaultDecisions(int b, List<Decision> decisions)
        {
            // no need to notify other player, as if abandon/default takes place, this will be the last decision

            var pAbandon =
                new Decision("PAbandon" + (b + 1), "PA" + (b + 1), (byte)MyGamePlayers.Plaintiff, new byte[] { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.PAbandon)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false, // we always must look at whether D is defaulting too. 
                    IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound },
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_PReadyToAbandon,
                    IsReversible = true
                };
            decisions.Add(pAbandon);

            var dDefault =
                new Decision("DDefault" + (b + 1), "DD" + (b + 1), (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.DDefault)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = true, // if either but not both has given up, game terminates
                    IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound },
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_DReadyToAbandon,
                    IsReversible = true
                };
            decisions.Add(dDefault);

            var bothGiveUp =
                new Decision("MutualGiveUp" + (b + 1), "MGU" + (b + 1), (byte)MyGamePlayers.BothGiveUpChance, new byte[] { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.MutualGiveUp, unevenChanceActions: false)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = true, // if this decision is needed, then both have given up, and the decision always terminates the game
                    CriticalNode = true, // always play out both sides of this coin flip
                    IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound },
                    IsReversible = true
                };
            decisions.Add(bothGiveUp);
        }

        private void AddPreBargainingRoundDummyDecision(int b, List<Decision> decisions)
        {
            var dummyDecision =
                new Decision("PreBargainingRound" + (b + 1), "PRBR" + (b + 1), (byte)MyGamePlayers.PreBargainingRoundChance, null,
                    1 /* i.e., just an opportunity to do some calculation and cleanup */, (byte)MyGameDecisions.PreBargainingRound, unevenChanceActions: false)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false, 
                    CriticalNode = false, // doesn't matter -- just one possibility
                    RequiresCustomInformationSetManipulation = true // this is where we do cleanup from previous bargaining rounds
                };
            decisions.Add(dummyDecision);
        }

        private void AddPostBargainingRoundDummyDecision(int b, List<Decision> decisions)
        {
            var dummyDecision =
                new Decision("PostBargainingRound" + (b + 1), "POBR" + (b + 1), (byte)MyGamePlayers.PostBargainingRoundChance, null,
                    1 /* i.e., just an opportunity to do some calculation and cleanup */, (byte)MyGameDecisions.PostBargainingRound, unevenChanceActions: false)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false,
                    CriticalNode = false, // doesn't matter -- just one possibility
                    IsReversible = true
                };
            decisions.Add(dummyDecision);
        }
        
        private void AddCourtDecision(List<Decision> decisions)
        {
            if (Options.ActionIsNoiseNotSignal)
                decisions.Add(new Decision("CourtDecision", "CD", (byte)MyGamePlayers.CourtChance,
                        new byte[] { (byte)MyGamePlayers.Resolution }, Options.NumCourtNoiseValues, (byte)MyGameDecisions.CourtDecision,
                        unevenChanceActions: false, criticalNode: true)
                    { CanTerminateGame = true, AlwaysTerminatesGame = true, IsReversible = true }); // even chance options
            else
                decisions.Add(new Decision("CourtDecision", "CD", (byte)MyGamePlayers.CourtChance,
                    new byte[] { (byte)MyGamePlayers.Resolution }, 2 /* for plaintiff or for defendant */,
                    (byte)MyGameDecisions.CourtDecision, unevenChanceActions: true, criticalNode: true)
                {
                    CanTerminateGame = true,
                    IsReversible = true
                }); // uneven chance options
        }

        #endregion

        #region Game play support 
        
        public override double[] GetChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            if (decisionByteCode == (byte)MyGameDecisions.TrulyLiable)
            {
                if (Options.LitigationQualitySource != MyGameOptions.LitigationQualitySourceEnum.GenerateFromTrulyLiableStatus)
                    throw new NotImplementedException();
                return ProbabilityOfTrulyLiabilityValues;
            }
            else if (decisionByteCode == (byte) MyGameDecisions.LitigationQuality)
            {
                if (Options.LitigationQualitySource != MyGameOptions.LitigationQualitySourceEnum.GenerateFromTrulyLiableStatus)
                    throw new NotImplementedException();
                byte trulyLiableActionValue = gameProgress.GameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_TrulyLiable);
                bool isTrulyLiable = trulyLiableActionValue == (byte) 2;
                if (isTrulyLiable)
                    return ProbabilitiesLitigationQuality_TrulyLiable;
                else
                    return ProbabilitiesLitigationQuality_TrulyNotLiable;
            }
            if (decisionByteCode == (byte)MyGameDecisions.PNoiseOrSignal)
            {
                if (Options.ActionIsNoiseNotSignal)
                    throw new NotImplementedException();
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                return DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(myGameProgress.LitigationQualityDiscrete, Options.PSignalParameters);
            }
            else if (decisionByteCode == (byte)MyGameDecisions.DNoiseOrSignal)
            {
                if (Options.ActionIsNoiseNotSignal)
                    throw new NotImplementedException();
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                return DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(myGameProgress.LitigationQualityDiscrete, Options.DSignalParameters);
            }
            else if (decisionByteCode == (byte)MyGameDecisions.CourtDecision)
            {
                if (Options.ActionIsNoiseNotSignal)
                    throw new NotImplementedException();
                double[] probabilities = new double[2];
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                probabilities[0] =
                    1.0 - myGameProgress.LitigationQualityUniform; // probability action 1 ==> rule for defendant
                probabilities[1] =
                    myGameProgress.LitigationQualityUniform; // probability action 2 ==> rule for plaintiff
                return probabilities;
            }
            else
                throw new NotImplementedException(); // subclass should define if needed
        }

        public override bool SkipDecision(Decision decision, ref GameHistory gameHistory)
        {
            byte decisionByteCode = decision.Subdividable_IsSubdivision ? decision.Subdividable_CorrespondingDecisionByteCode : decision.DecisionByteCode;
            if (decisionByteCode == (byte) MyGameDecisions.MutualGiveUp)
            {
                bool pTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PReadyToAbandon) == 1;
                bool dTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DReadyToAbandon) == 1;
                return !pTryingToGiveUp || !dTryingToGiveUp; // if anyone is NOT trying to give up, we don't have to deal with mutual giving up
            }
            else if (decisionByteCode >= (byte) MyGameDecisions.POffer && decisionByteCode <= (byte) MyGameDecisions.DResponse)
            {
                if (Options.IncludeAgreementToBargainDecisions)
                {
                    byte pAgreesToBargainCacheValue = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PAgreesToBargain);
                    byte dAgreesToBargainCacheValue = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DAgreesToBargain);
                    if (pAgreesToBargainCacheValue == 0 || dAgreesToBargainCacheValue == 0)
                        throw new NotImplementedException();
                    bool pAgreesToBargain = pAgreesToBargainCacheValue == (byte) 1;
                    bool dAgreesToBargain = dAgreesToBargainCacheValue == (byte) 1;
                    return !pAgreesToBargain || !dAgreesToBargain; // if anyone refuses to bargain, we skip the decisions
                }
            }
            return false;
        }
        
        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, ref GameHistory gameHistory, byte actionChosen)
        {
            if (!currentDecision.CanTerminateGame)
                return false;

            byte decisionByteCode = currentDecision.Subdividable_IsSubdivision ? currentDecision.Subdividable_CorrespondingDecisionByteCode : currentDecision.DecisionByteCode;

            switch (decisionByteCode)
            {
                case (byte)MyGameDecisions.CourtDecision:
                    return true;
                case (byte)MyGameDecisions.DResponse:
                    bool dAccepts = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DResponse) == 1;
                    if (dAccepts)
                        return true;
                    break;
                case (byte)MyGameDecisions.PResponse:
                    bool pAccepts = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PResponse) == 1;
                    if (pAccepts)
                        return true;
                    break;
                case (byte)MyGameDecisions.DOffer:
                    // this is simultaneous bargaining (plaintiff offer is always first). 
                    if (!Options.BargainingRoundsSimultaneous)
                        throw new Exception("Internal error.");
                    byte plaintiffOffer = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_POffer);
                    byte defendantOffer = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DOffer);
                    if (defendantOffer >= plaintiffOffer)
                        return true;
                    break;
                case (byte)MyGameDecisions.PFile:
                    if (actionChosen == 2)
                        return true; // plaintiff hasn't filed
                    break;
                case (byte)MyGameDecisions.DAnswer:
                    if (actionChosen == 2)
                        return true; // defendant's hasn't answered
                    break;
                case (byte)MyGameDecisions.DDefault:
                    bool pTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PReadyToAbandon) == 1;
                    bool dTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DReadyToAbandon) == 1;
                    if (pTryingToGiveUp ^ dTryingToGiveUp) // i.e., one but not both parties try to default
                        return true;
                    break;
                case (byte)MyGameDecisions.MutualGiveUp:
                    return true; // if we reach this decision, the game is definitely over; just a question of who wins
            }
            return false;
        }

        public override void CustomInformationSetManipulation(Decision currentDecision, byte currentDecisionIndex, byte actionChosen, ref GameHistory gameHistory, GameProgress gameProgress)
        {
            byte decisionByteCode = currentDecision.Subdividable_IsSubdivision ? currentDecision.Subdividable_CorrespondingDecisionByteCode : currentDecision.DecisionByteCode; // get the original decision byte code
            if (Options.ActionIsNoiseNotSignal && (decisionByteCode == (byte) MyGameDecisions.PNoiseOrSignal || decisionByteCode == (byte) MyGameDecisions.DNoiseOrSignal))
            {
                // When the action is the signal, we just send the signal that the player receives, because there are unequal chance probabilities. When the action is the noise, we have an even chance of each noise value. We can't just give the player the noise value; we have to take into account the litigation quality. So, we do that here.
                byte litigationQuality = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_LitigationQuality);
                ConvertNoiseToSignal(litigationQuality, actionChosen, decisionByteCode == (byte)MyGameDecisions.PNoiseOrSignal, out byte discreteSignal, out _);
                gameHistory.AddToInformationSetAndLog(discreteSignal, currentDecisionIndex, decisionByteCode == (byte)MyGameDecisions.PNoiseOrSignal ? (byte) MyGamePlayers.Plaintiff : (byte) MyGamePlayers.Defendant, gameProgress);
                // NOTE: We don't have to do anything like this for the court's information set. The court simply gets the actual litigation quality and the noise. When the game is actually being played, the court will combine these to determine whether the plaintiff wins. The plaintiff and defendant are non-chance players, and so we want to have the same information set for all situations with the same signal.  But with the court, that doesn't matter. We can have lots of information sets, covering the wide range of possibilities.
            }
            else if (decisionByteCode == (byte)MyGameDecisions.PreBargainingRound)
            {
                // Clean up previous round after the bargaining round:
                // We don't want to do it immediately after the bargaining round. If the game has ended as a result of a settlement, a post-bargaining round decision won't
                // execute. That's OK. But if the game ends because this is the last bargaining round and bargaining fails, then we have a trial, and the outcomes may depend on the offers in the last bargaining round. That is why we want to do this cleanup at the beginning of the next bargaining round.
                // At the beginning of one bargaining round, we must clean up the results of the previous bargaining round. Thus, if we are forgetting earlier bargaining rounds, then we want to delete all of the items in the resolution information set from that bargaining round. We need to know the number of items that have been added since the beginning of the previous bargaining round. We can do this by incrementing something in the game history cache whenever we process any of these decisions. We do this by using the IncrementGameCacheItem option of Decision.

                // Clean up resolution set and (if necessary) players' sets
                byte numItemsInResolutionSetFromPreviousBargainingRound = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound);
                if (numItemsInResolutionSetFromPreviousBargainingRound > 0)
                    gameHistory.RemoveItemsInInformationSetAndLog((byte) MyGamePlayers.Resolution, currentDecisionIndex, numItemsInResolutionSetFromPreviousBargainingRound, gameProgress);

                if (Options.ForgetEarlierBargainingRounds)
                {
                    byte numItemsInPlaintiffSetFromPreviousBargainingRound = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound);
                    if (numItemsInPlaintiffSetFromPreviousBargainingRound > 0)
                        gameHistory.RemoveItemsInInformationSetAndLog((byte) MyGamePlayers.Plaintiff, currentDecisionIndex, numItemsInPlaintiffSetFromPreviousBargainingRound, gameProgress);

                    byte numItemsInDefendantSetFromPreviousBargainingRound = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound);
                    if (numItemsInDefendantSetFromPreviousBargainingRound > 0)
                        gameHistory.RemoveItemsInInformationSetAndLog((byte) MyGamePlayers.Defendant, currentDecisionIndex, numItemsInDefendantSetFromPreviousBargainingRound, gameProgress);
                }

                // Add an indication of the bargaining round we're in.
                byte bargainingRound = currentDecision.CustomByte;
                gameHistory.AddToInformationSetAndLog(bargainingRound, currentDecisionIndex, (byte)MyGamePlayers.Resolution, gameProgress);
                gameHistory.AddToInformationSetAndLog(bargainingRound, currentDecisionIndex, (byte)MyGamePlayers.Plaintiff, gameProgress);
                gameHistory.AddToInformationSetAndLog(bargainingRound, currentDecisionIndex, (byte)MyGamePlayers.Defendant, gameProgress);

                // Reset the cache indices to reflect that there is only one item
                gameHistory.SetCacheItemAtIndex(GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, (byte) 1);
                gameHistory.SetCacheItemAtIndex(GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, (byte)1);
                gameHistory.SetCacheItemAtIndex(GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound, (byte)1);
            }
        }

        public override void ReverseDecision(Decision decisionToReverse, ref HistoryPoint historyPoint, IGameState originalGameState)
        {
            base.ReverseDecision(decisionToReverse, ref historyPoint, originalGameState);
            ref GameHistory gameHistory = ref historyPoint.HistoryToPoint;
            byte decisionByteCode = decisionToReverse.DecisionByteCode;
            if (Options.ActionIsNoiseNotSignal && (decisionByteCode == (byte)MyGameDecisions.PNoiseOrSignal || decisionByteCode == (byte)MyGameDecisions.DNoiseOrSignal))
            {
                gameHistory.ReverseAdditionsToInformationSet(decisionByteCode == (byte)MyGameDecisions.PNoiseOrSignal ? (byte)MyGamePlayers.Plaintiff : (byte)MyGamePlayers.Defendant, 1, null);
            }
        }

        #endregion


    }
}
