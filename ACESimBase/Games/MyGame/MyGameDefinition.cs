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
            PlayerNames = Players.Select(x => x.PlayerName).ToArray();
            NumPlayers = (byte) Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();
            CalculateNondistributedDecisionMultipliers();

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
            Options.MyGameDisputeGenerator.Setup(this);
            Options.MyGamePretrialDecisionGeneratorGenerator?.Setup(this);
            Options.MyGameRunningSideBets?.Setup(this);
        }

        private MyGameProgress MyGP(GameProgress gp) => gp as MyGameProgress;

        private static string PlaintiffName = "P";
        private static string DefendantName = "D";
        private static string PrePrimaryChanceName = "PPC";
        private static string PostPrimaryChanceName = "POPC";
        private static string LitigationQualityChanceName = "QC";
        private static string PlaintiffNoiseChanceName = "PNC";
        private static string DefendantNoiseChanceName = "DNC";
        private static string BothGiveUpChanceName = "GUC";
        private static string PreBargainingRoundChanceName = "PRE";
        private static string PostBargainingRoundChanceName = "POST";
        private static string CourtChanceName = "CC";
        private static string ResolutionPlayerName = "R";

        private static List<PlayerInfo> GetPlayersList()
        {
            // IMPORTANT: Chance players MUST be listed after other players. Resolution player should be listed afte4r main players.
            return new List<PlayerInfo>
                {
                    new PlayerInfo(PlaintiffName, (int) MyGamePlayers.Plaintiff, false, true),
                    new PlayerInfo(DefendantName, (int) MyGamePlayers.Defendant, false, true),
                    new PlayerInfo(ResolutionPlayerName, (int) MyGamePlayers.Resolution, true, false),
                    new PlayerInfo(PrePrimaryChanceName, (int) MyGamePlayers.PrePrimaryChance, true, false),
                    new PlayerInfo(PostPrimaryChanceName, (int) MyGamePlayers.PrePrimaryChance, true, false),
                    new PlayerInfo(LitigationQualityChanceName, (int) MyGamePlayers.QualityChance, true, false),
                    new PlayerInfo(PlaintiffNoiseChanceName, (int) MyGamePlayers.PSignalChance, true, false),
                    new PlayerInfo(DefendantNoiseChanceName, (int) MyGamePlayers.DSignalChance, true, false),
                    new PlayerInfo(BothGiveUpChanceName, (int) MyGamePlayers.BothGiveUpChance, true, false),
                    new PlayerInfo(PreBargainingRoundChanceName, (int) MyGamePlayers.PreBargainingRoundChance, true, false),
                    new PlayerInfo(PostBargainingRoundChanceName, (int) MyGamePlayers.PostBargainingRoundChance, true, false),
                    new PlayerInfo(CourtChanceName, (int) MyGamePlayers.CourtChance, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte) MyGamePlayers.Resolution;

        // NOTE: Must skip 0, because that is used for subdivision aggregation decisions. Note that the first three may be augmented if we are using subdivision decisions
        public byte GameHistoryCacheIndex_PrePrimaryChance = 1; // defined, for example, in discrimination game to determine whether employee is good or bad and whether employer has taste for discrimination
        public byte GameHistoryCacheIndex_PrimaryAction = 2;
        public byte GameHistoryCacheIndex_PostPrimaryChance = 3; // e.g., exogenous dispute generator sometimes chooses between is truly liable and is not truly liable
        public byte GameHistoryCacheIndex_LitigationQuality = 4;
        public byte GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound = 5;
        public byte GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound = 6;
        public byte GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound = 7;
        public byte GameHistoryCacheIndex_PAgreesToBargain = 8;
        public byte GameHistoryCacheIndex_DAgreesToBargain = 9;
        public byte GameHistoryCacheIndex_POffer = 10;
        public byte GameHistoryCacheIndex_DOffer = 11;
        public byte GameHistoryCacheIndex_PResponse = 12;
        public byte GameHistoryCacheIndex_DResponse = 13;
        public byte GameHistoryCacheIndex_PReadyToAbandon = 14;
        public byte GameHistoryCacheIndex_DReadyToAbandon = 15;
        public byte GameHistoryCacheIndex_PChipsAction = 16;
        public byte GameHistoryCacheIndex_DChipsAction = 17;
        public byte GameHistoryCacheIndex_TotalChipsSoFar = 18;

        public bool CheckCompleteAfterPrimaryAction;
        public bool CheckCompleteAfterPostPrimaryAction;

        #endregion

        #region Decisions list

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>();
            AddDisputeGeneratorDecisions(decisions);
            AddSignalsDecisions(decisions);
            AddFileAndAnswerDecisions(decisions);
            for (int b = 0; b < Options.NumPotentialBargainingRounds; b++)
            {
                AddPreBargainingRoundDummyDecision(b, decisions);
                AddDecisionsForBargainingRound(b, decisions);
                if (Options.AllowAbandonAndDefaults)
                {
                    if (Options.MyGameRunningSideBets != null)
                        AddRunningSideBetDecisions(b, decisions);
                    AddAbandonOrDefaultDecisions(b, decisions);
                }
                AddPostBargainingRoundDummyDecision(b, decisions);
            }
            AddPreTrialDecisions(decisions);
            AddCourtDecision(decisions);
            return decisions;
        }

        private void AddDisputeGeneratorDecisions(List<Decision> decisions)
        {
            // Litigation Quality. This is not known by a player unless the player has perfect information. 
            // The SignalChance player relies on this information in calculating the probabilities of different signals
            List<byte> playersKnowingLitigationQuality = new List<byte>()
            {
                (byte) MyGamePlayers.PSignalChance,
                (byte) MyGamePlayers.DSignalChance,
                (byte) MyGamePlayers.CourtChance,
                (byte) MyGamePlayers.Resolution
            };
            if (Options.PNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte)MyGamePlayers.Plaintiff);
            if (Options.DNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte)MyGamePlayers.Defendant);
            IMyGameDisputeGenerator disputeGenerator = Options.MyGameDisputeGenerator;
            disputeGenerator.GetActionsSetup(this, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate);
            CheckCompleteAfterPrimaryAction = primaryActionCanTerminate;
            CheckCompleteAfterPostPrimaryAction = postPrimaryChanceCanTerminate;
            if (prePrimaryChanceActions > 0)
            {
                decisions.Add(new Decision("PrePrimaryChanceActions", "PrePrimary", (byte) MyGamePlayers.PrePrimaryChance, prePrimaryPlayersToInform, prePrimaryChanceActions, (byte) MyGameDecisions.PrePrimaryActionChance) {StoreActionInGameCacheItem = GameHistoryCacheIndex_PrePrimaryChance, IsReversible = true, UnevenChanceActions = prePrimaryUnevenChance, Unroll_Parallelize = disputeGenerator.GetPrePrimaryUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetPrePrimaryUnrollSettings().unrollIdentical, DistributedDecision = true });
            }
            if (primaryActions > 0)
            {
                decisions.Add(new Decision("PrimaryActions", "Primary", (byte) MyGamePlayers.PrePrimaryChance /* there is no primary chance player */, primaryPlayersToInform, primaryActions, (byte) MyGameDecisions.PrimaryAction) {StoreActionInGameCacheItem = GameHistoryCacheIndex_PrimaryAction, IsReversible = true, CanTerminateGame = primaryActionCanTerminate, Unroll_Parallelize = disputeGenerator.GetPrimaryUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetPrimaryUnrollSettings().unrollIdentical, DistributedDecision = true });
            }
            if (postPrimaryChanceActions > 0)
            {
                decisions.Add(new Decision("PostPrimaryChanceActions", "PostPrimary", (byte) MyGamePlayers.PostPrimaryChance, postPrimaryPlayersToInform, postPrimaryChanceActions, (byte) MyGameDecisions.PostPrimaryActionChance) {StoreActionInGameCacheItem = GameHistoryCacheIndex_PostPrimaryChance, IsReversible = true, UnevenChanceActions = postPrimaryUnevenChance, CanTerminateGame = postPrimaryChanceCanTerminate, Unroll_Parallelize = disputeGenerator.GetPostPrimaryUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetPostPrimaryUnrollSettings().unrollIdentical, DistributedDecision = true });
            }
            decisions.Add(new Decision("LitigationQuality", "Qual", (byte)MyGamePlayers.QualityChance,
                    playersKnowingLitigationQuality.ToArray(), Options.NumLitigationQualityPoints, (byte)MyGameDecisions.LitigationQuality)
                { StoreActionInGameCacheItem = GameHistoryCacheIndex_LitigationQuality, IsReversible = true, UnevenChanceActions = litigationQualityUnevenChance, Unroll_Parallelize = disputeGenerator.GetLitigationQualityUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetLitigationQualityUnrollSettings().unrollIdentical, DistributedDecision = true });
        }
        
        private void AddSignalsDecisions(List<Decision> decisions)
        {
            // Plaintiff and defendant signals. If a player has perfect information, then no signal is needed.
            if (Options.PNoiseStdev != 0)
                decisions.Add(new Decision("PlaintiffSignal", "PS", (byte)MyGamePlayers.PSignalChance,
                    new byte[] {(byte) MyGamePlayers.Plaintiff},
                    Options.NumSignals, (byte)MyGameDecisions.PSignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    NondistributedDecision = true,
                });
            if (Options.DNoiseStdev != 0)
                decisions.Add(new Decision("DefendantSignal", "DS", (byte)MyGamePlayers.DSignalChance,
                    new byte[] { (byte)MyGamePlayers.Defendant },
                    Options.NumSignals, (byte)MyGameDecisions.DSignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    NondistributedDecision = true,
                });
            CreateSignalsTables();
        }
        
        private double[][] PSignalsTable, DSignalsTable, CSignalsTable;
        public void CreateSignalsTables()
        {
            PSignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumLitigationQualityPoints, Options.NumSignals });
            DSignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumLitigationQualityPoints, Options.NumSignals });
            CSignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumLitigationQualityPoints, 2 });
            for (byte litigationQuality = 1;
                litigationQuality <= Options.NumLitigationQualityPoints;
                litigationQuality++)
            {
                double litigationQualityUniform =
                    EquallySpaced.GetLocationOfEquallySpacedPoint(litigationQuality - 1,
                        Options.NumLitigationQualityPoints, false);

                DiscreteValueSignalParameters pParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumLitigationQualityPoints, NumSignals = Options.NumSignals, StdevOfNormalDistribution = Options.PNoiseStdev, UseEndpoints = false };
                PSignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, pParams);
                DiscreteValueSignalParameters dParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumLitigationQualityPoints, NumSignals = Options.NumSignals, StdevOfNormalDistribution = Options.DNoiseStdev, UseEndpoints = false };
                DSignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, dParams);
                DiscreteValueSignalParameters cParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumLitigationQualityPoints, NumSignals = 2, StdevOfNormalDistribution = Options.CourtNoiseStdev, UseEndpoints = false };
                CSignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, cParams);
            }
        }

        public double[] GetPSignalProbabilities(byte litigationQuality)
        {
            return PSignalsTable[litigationQuality - 1];
        }
        public double[] GetDSignalProbabilities(byte litigationQuality)
        {
            return DSignalsTable[litigationQuality - 1];
        }
        public double[] GetCSignalProbabilities(byte litigationQuality)
        {
            return CSignalsTable[litigationQuality - 1];
        }

        private void AddFileAndAnswerDecisions(List<Decision> decisions)
        {
            var pFile =
                new Decision("PFile", "PF", (byte)MyGamePlayers.Plaintiff, new byte[]  { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.PFile)
                { // TODO: Maybe can eliminate notice to plaintiff and defendant here and below
                    CanTerminateGame = true, // not filing always terminates
                    IsReversible = true
                };
            decisions.Add(pFile);

            var dAnswer =
                new Decision("DAnswer", "DA", (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
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
                debug; // Plaintiff is not being informed of his own offer -- and defendant is not being informed of his own offer. This becomes a problem in developing the best response, because we end up having a single information set regardless of what the player previously decided. This messes up the best response calculation, which assumes perfect recall. If we have a single information set for two situations, we end up choosing one best response decision for both situations, when we want to have different best response decisions in the different situations, since the OTHER player will do different things in those situations. Add some testing to ensure perfect recall. We should eliminate all options that are inconsistent with perfect recall. 
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
                            PlayersToInformOfOccurrenceOnly = new byte[] { (byte)MyGamePlayers.Defendant },
                            CanTerminateGame = true,
                            CustomByte = (byte)(b + 1),
                            IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound },
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
                            PlayersToInformOfOccurrenceOnly = new byte[] { (byte) MyGamePlayers.Plaintiff },
                            CanTerminateGame = true,
                            CustomByte = (byte)(b + 1),
                            IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound },
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_PResponse,
                        });
                }
            }
        }

        private void AddOfferDecisionOrSubdivisions(List<Decision> decisions, Decision offerDecision)
        {
            AddPotentiallySubdividableDecision(decisions, offerDecision, Options.SubdivideOffers, (byte)MyGameDecisions.SubdividableOffer, 2, Options.NumOffers);
        }

        private void AddRunningSideBetDecisions(int b, List<Decision> decisions)
        {
            var pRSideBet =
                new Decision("pRSideBet" + (b + 1), "PRSB" + (b + 1), (byte)MyGamePlayers.Plaintiff, new byte[] { },
                    (byte) (Options.MyGameRunningSideBets.MaxChipsPerRound + 1), (byte)MyGameDecisions.PChips)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false, 
                    IncrementGameCacheItem = new byte[] { },
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_PChipsAction,
                    IsReversible = true,
                };
            decisions.Add(pRSideBet);
            var dRSideBet =
                new Decision("dRSideBet" + (b + 1), "DRSB" + (b + 1), (byte)MyGamePlayers.Defendant, new byte[] { },
                    (byte)(Options.MyGameRunningSideBets.MaxChipsPerRound + 1), (byte)MyGameDecisions.DChips)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false,
                    IncrementGameCacheItem = new byte[] { },
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_DChipsAction,
                    IsReversible = true,
                    RequiresCustomInformationSetManipulation = true
                };
            decisions.Add(dRSideBet);
        }

        private void AddAbandonOrDefaultDecisions(int b, List<Decision> decisions)
        {
            // no need to notify other player, as if abandon/default takes place, this will be the last decision. But we do need to make sure that player can distinguish between abandon/default decision and a later decision (such as a pretrial decision), so the player notifies itself that a decision has taken place.

            var pAbandon =
                new Decision("PAbandon" + (b + 1), "PA" + (b + 1), (byte)MyGamePlayers.Plaintiff, new byte[] { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.PAbandon)
                {
                    CustomByte = (byte)(b + 1),
                    PlayersToInformOfOccurrenceOnly = new byte[] { (byte)MyGamePlayers.Plaintiff },
                    CanTerminateGame = false, // we always must look at whether D is defaulting too. 
                    IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound },
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_PReadyToAbandon,
                    IsReversible = true
                };
            decisions.Add(pAbandon);

            var dDefault =
                new Decision("DDefault" + (b + 1), "DD" + (b + 1), (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.DDefault)
                {
                    CustomByte = (byte)(b + 1),
                    PlayersToInformOfOccurrenceOnly = new byte[] { (byte)MyGamePlayers.Defendant },
                    CanTerminateGame = true, // if either but not both has given up, game terminates
                    IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound, GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound },
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

        private void AddPreTrialDecisions(List<Decision> decisions)
        {
            if (Options.MyGamePretrialDecisionGeneratorGenerator != null)
            {
                Options.MyGamePretrialDecisionGeneratorGenerator.GetActionsSetup(this, out byte pActions, out byte dActions, out byte[] playersToInformOfPAction, out byte[] playersToInformOfDAction);
                if (pActions > 0)
                {
                    decisions.Add(new Decision("PPreTrial", "PPT", (byte) MyGamePlayers.Plaintiff, playersToInformOfPAction, pActions, (byte) MyGameDecisions.PPretrialAction) { IsReversible = true});
                }
                if (dActions > 0)
                {
                    decisions.Add(new Decision("DPreTrial", "DPT", (byte)MyGamePlayers.Defendant, playersToInformOfDAction, dActions, (byte)MyGameDecisions.DPretrialAction) { IsReversible = true });
                }
            }
        }

        private void AddCourtDecision(List<Decision> decisions)
        {
            decisions.Add(new Decision("CourtDecision", "CD", (byte)MyGamePlayers.CourtChance,
                    new byte[] { (byte)MyGamePlayers.Resolution }, 2, (byte)MyGameDecisions.CourtDecision,
                    unevenChanceActions: true, criticalNode: true)
                { CanTerminateGame = true, AlwaysTerminatesGame = true, IsReversible = true, DistributorChanceDecision = true }); // even chance options
        }

        #endregion

        #region Game play support 
        
        public override double[] GetUnevenChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            if (decisionByteCode == (byte)MyGameDecisions.PrePrimaryActionChance)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = Options.MyGameDisputeGenerator.GetPrePrimaryChanceProbabilities(this);
                return probabilities;
            }
            else if (decisionByteCode == (byte)MyGameDecisions.PostPrimaryActionChance)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = Options.MyGameDisputeGenerator.GetPostPrimaryChanceProbabilities(this, myGameProgress.DisputeGeneratorActions);
                return probabilities;
            }
            else if (decisionByteCode == (byte) MyGameDecisions.LitigationQuality)
            {
                var myGameProgress = ((MyGameProgress) gameProgress);
                var probabilities = Options.MyGameDisputeGenerator.GetLitigationQualityProbabilities(this, myGameProgress.DisputeGeneratorActions);
                return probabilities;
            }
            else if (decisionByteCode == (byte)MyGameDecisions.PSignal)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = GetPSignalProbabilities(myGameProgress.LitigationQualityDiscrete);
                return probabilities;
            }
            else if (decisionByteCode == (byte)MyGameDecisions.DSignal)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = GetDSignalProbabilities(myGameProgress.LitigationQualityDiscrete);
                return probabilities;
            }
            else if (decisionByteCode == (byte)MyGameDecisions.CourtDecision)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = GetCSignalProbabilities(myGameProgress.LitigationQualityDiscrete);
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
                case (byte)MyGameDecisions.PrimaryAction:
                    return Options.MyGameDisputeGenerator.MarkComplete(this, gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrePrimaryChance), gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrimaryAction));
                case (byte)MyGameDecisions.PostPrimaryActionChance:
                    return Options.MyGameDisputeGenerator.MarkComplete(this, gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrePrimaryChance), gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrimaryAction), gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PostPrimaryChance));
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
            if (decisionByteCode == (byte) MyGameDecisions.DChips)
            {
                // Inform the players of the total number of chips bet in this round. This will allow the players to make a decision about whether to abandon/default this round. We separately add information on P/D chips to the players' information sets, because we want the players to have a sense of who is bidding more aggressively (thus allowing them to track their own bluffing). 
                // We also have to update the resolution set with the same information. The reason is that if a player bets a certain number of chips and then withdraws, then the smaller of that number and the other player's bet is still counted.
                // Note that below, we delete the information from the previous round but then add back in the total number of chips bet so far.
                
                // the pChipsAction and dChipsAction here are 1 more than the number of chips bet
                byte pChipsAction = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PChipsAction);
                byte dChipsAction = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DChipsAction);
                gameHistory.AddToInformationSetAndLog(pChipsAction, currentDecisionIndex, (byte) MyGamePlayers.Plaintiff, gameProgress);
                gameHistory.AddToInformationSetAndLog(dChipsAction, currentDecisionIndex, (byte)MyGamePlayers.Plaintiff, gameProgress);
                gameHistory.AddToInformationSetAndLog(pChipsAction, currentDecisionIndex, (byte)MyGamePlayers.Defendant, gameProgress);
                gameHistory.AddToInformationSetAndLog(dChipsAction, currentDecisionIndex, (byte)MyGamePlayers.Defendant, gameProgress);
                gameHistory.AddToInformationSetAndLog(pChipsAction, currentDecisionIndex, (byte)MyGamePlayers.Resolution, gameProgress);
                gameHistory.AddToInformationSetAndLog(dChipsAction, currentDecisionIndex, (byte)MyGamePlayers.Resolution, gameProgress);
                gameHistory.IncrementItemAtCacheIndex(GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, 2);
                gameHistory.IncrementItemAtCacheIndex(GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound, 2);
                gameHistory.IncrementItemAtCacheIndex(GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, 2);
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

                if (Options.BargainingRoundRecall == MyGameBargainingRoundRecall.ForgetEarlierBargainingRounds || Options.BargainingRoundRecall == MyGameBargainingRoundRecall.RememberOnlyLastBargainingRound)
                { // first remove everything, even if we want to remember the last bargaining round
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

                byte numPlaintiffItems = 1, numDefendantItems = 1, numResolutionItems = 1;
                if (Options.BargainingRoundRecall == MyGameBargainingRoundRecall.RememberOnlyLastBargainingRound)
                { // add back in opponent's last offer, if applicable, and increment the cache index
                    byte lastPOffer = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_POffer);
                    if (lastPOffer != 0)
                    {
                        gameHistory.AddToInformationSetAndLog(lastPOffer, currentDecisionIndex, (byte) MyGamePlayers.Defendant, gameProgress);
                        numDefendantItems++;
                    }
                    byte lastDOffer = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DOffer);
                    if (lastDOffer != 0)
                    {
                        gameHistory.AddToInformationSetAndLog(lastDOffer, currentDecisionIndex, (byte) MyGamePlayers.Plaintiff, gameProgress);
                        numPlaintiffItems++;
                    }
                }
                if (Options.MyGameRunningSideBets != null)
                { // Add the total number of chips bet so far to each player's information sets.
                    byte totalChipsSoFar = 0;
                    if (bargainingRound > 1)
                    {
                        // adjust for fact that chips action is 1 more than number of chips
                        byte pChipsLastRound = (byte) (gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PChipsAction) - 1);
                        byte dChipsLastRound = (byte) (gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DChipsAction) - 1);
                        byte chipsLastRound = Math.Max(pChipsLastRound, dChipsLastRound);
                        totalChipsSoFar = chipsLastRound;
                        if (bargainingRound > 2)
                        {
                            byte chipsFromBeforeLastRound = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_TotalChipsSoFar); // chips from before last round exist only if we're in the third round or later
                            totalChipsSoFar += chipsFromBeforeLastRound;
                        }
                    }
                    gameHistory.SetCacheItemAtIndex(GameHistoryCacheIndex_TotalChipsSoFar, totalChipsSoFar); // so, at round n, this shows total chips from round n - 1
                    byte totalChipsSoFarAction = (byte) (totalChipsSoFar + 1); // can't use 0 as an action
                    gameHistory.AddToInformationSetAndLog(totalChipsSoFarAction, currentDecisionIndex, (byte) MyGamePlayers.Plaintiff, gameProgress);
                    gameHistory.AddToInformationSetAndLog(totalChipsSoFarAction, currentDecisionIndex, (byte)MyGamePlayers.Defendant, gameProgress);
                    gameHistory.AddToInformationSetAndLog(totalChipsSoFarAction, currentDecisionIndex, (byte)MyGamePlayers.Resolution, gameProgress);
                    numPlaintiffItems++;
                    numDefendantItems++;
                    numResolutionItems++;
                }

                // Reset the cache indices to reflect how many items we have placed for this bargaining round
                gameHistory.SetCacheItemAtIndex(GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, numResolutionItems);
                gameHistory.SetCacheItemAtIndex(GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, numPlaintiffItems);
                gameHistory.SetCacheItemAtIndex(GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound, numDefendantItems);

            }
        }

        public override void ReverseDecision(Decision decisionToReverse, ref HistoryPoint historyPoint, IGameState originalGameState)
        {
            base.ReverseDecision(decisionToReverse, ref historyPoint, originalGameState);
            ref GameHistory gameHistory = ref historyPoint.HistoryToPoint;
            byte decisionByteCode = decisionToReverse.DecisionByteCode;
            if (decisionByteCode == (byte)MyGameDecisions.DChips)
            {
                gameHistory.ReverseAdditionsToInformationSet((byte)MyGamePlayers.Plaintiff, 2, null);
                gameHistory.ReverseAdditionsToInformationSet((byte)MyGamePlayers.Defendant, 2, null);
                gameHistory.ReverseAdditionsToInformationSet((byte)MyGamePlayers.Resolution, 2, null);
                gameHistory.DecrementItemAtCacheIndex(GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, 2);
                gameHistory.DecrementItemAtCacheIndex(GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound, 2);
                gameHistory.DecrementItemAtCacheIndex(GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, 2);
            }
        }

        #endregion


    }
}
