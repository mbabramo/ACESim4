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
        #region Construction and setup

        public MyGameOptions Options;

        public override string ToString()
        {
            return Options.ToString();
        }

        public MyGameDefinition() : base()
        {

        }
        public override void Setup(GameOptions options)
        {
            Options = (MyGameOptions) options;
            FurtherOptionsSetup();

            Players = GetPlayersList();
            PlayerNames = Players.Select(x => x.PlayerName).ToArray();
            NumPlayers = (byte) Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();
            CalculateDistributorChanceInputDecisionMultipliers();

            IGameFactory gameFactory = new MyGameFactory();
            Initialize(gameFactory);
        }



        private void FurtherOptionsSetup()
        {
            if (Options.DeltaOffersOptions.SubsequentOffersAreDeltas)
                Options.DeltaOffersCalculation = new DeltaOffersCalculation(this);
            SetupLiabilitySignals();
            SetupDamagesSignals();
            Options.MyGameDisputeGenerator.Setup(this);
            Options.MyGamePretrialDecisionGeneratorGenerator?.Setup(this);
            Options.MyGameRunningSideBets?.Setup(this);
        }

        private void SetupDamagesSignals()
        {
            Options.PDamagesSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumDamagesStrengthPoints,
                StdevOfNormalDistribution = Options.PDamagesNoiseStdev,
                NumSignals = Options.NumDamagesSignals
            };
            Options.DDamagesSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumDamagesStrengthPoints,
                StdevOfNormalDistribution = Options.DDamagesNoiseStdev,
                NumSignals = Options.NumDamagesSignals
            };
        }

        private void SetupLiabilitySignals()
        {
            Options.PLiabilitySignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumLiabilityStrengthPoints,
                StdevOfNormalDistribution = Options.PLiabilityNoiseStdev,
                NumSignals = Options.NumLiabilitySignals
            };
            Options.DLiabilitySignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumLiabilityStrengthPoints,
                StdevOfNormalDistribution = Options.DLiabilityNoiseStdev,
                NumSignals = Options.NumLiabilitySignals
            };
        }

        private MyGameProgress MyGP(GameProgress gp) => gp as MyGameProgress;

        private static string PlaintiffName = "P";
        private static string DefendantName = "D";
        private static string PrePrimaryChanceName = "PPC";
        private static string PostPrimaryChanceName = "POPC";
        private static string LiabilityStrengthChanceName = "LC";
        private static string PLiabilityNoiseChanceName = "PLC";
        private static string DLiabilityNoiseChanceName = "DLC";
        private static string DamagesStrengthChanceName = "DC";
        private static string PDamagesNoiseChanceName = "PDC";
        private static string DDamagesNoiseChanceName = "DDC";
        private static string BothGiveUpChanceName = "GUC";
        private static string PreBargainingRoundChanceName = "PRE";
        private static string PostBargainingRoundChanceName = "POST";
        private static string CourtLiabilityChanceName = "CL";
        private static string CourtDamagesChanceName = "CL";
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
                    new PlayerInfo(LiabilityStrengthChanceName, (int) MyGamePlayers.LiabilityStrengthChance, true, false),
                    new PlayerInfo(PLiabilityNoiseChanceName, (int) MyGamePlayers.PLiabilitySignalChance, true, false),
                    new PlayerInfo(DLiabilityNoiseChanceName, (int) MyGamePlayers.DLiabilitySignalChance, true, false),
                    new PlayerInfo(DamagesStrengthChanceName, (int) MyGamePlayers.DamagesStrengthChance, true, false),
                    new PlayerInfo(PDamagesNoiseChanceName, (int) MyGamePlayers.PDamagesSignalChance, true, false),
                    new PlayerInfo(DDamagesNoiseChanceName, (int) MyGamePlayers.DDamagesSignalChance, true, false),
                    new PlayerInfo(BothGiveUpChanceName, (int) MyGamePlayers.BothGiveUpChance, true, false),
                    new PlayerInfo(PreBargainingRoundChanceName, (int) MyGamePlayers.PreBargainingRoundChance, true, false),
                    new PlayerInfo(PostBargainingRoundChanceName, (int) MyGamePlayers.PostBargainingRoundChance, true, false),
                    new PlayerInfo(CourtLiabilityChanceName, (int) MyGamePlayers.CourtLiabilityChance, true, false),
                    new PlayerInfo(CourtDamagesChanceName, (int) MyGamePlayers.CourtDamagesChance, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte) MyGamePlayers.Resolution;

        // NOTE: Must skip 0, because that is used for subdivision aggregation decisions. Note that the first three may be augmented if we are using subdivision decisions
        public byte GameHistoryCacheIndex_PrePrimaryChance = 1; // defined, for example, in discrimination game to determine whether employee is good or bad and whether employer has taste for discrimination
        public byte GameHistoryCacheIndex_PrimaryAction = 2;
        public byte GameHistoryCacheIndex_PostPrimaryChance = 3; // e.g., exogenous dispute generator sometimes chooses between is truly liable and is not truly liable
        public byte GameHistoryCacheIndex_LiabilityStrength = 4;
        public byte GameHistoryCacheIndex_DamagesStrength = 5;
        public byte GameHistoryCacheIndex_PAgreesToBargain = 6;
        public byte GameHistoryCacheIndex_DAgreesToBargain = 7;
        public byte GameHistoryCacheIndex_POffer = 8;
        public byte GameHistoryCacheIndex_DOffer = 9;
        public byte GameHistoryCacheIndex_PResponse = 10;
        public byte GameHistoryCacheIndex_DResponse = 11;
        public byte GameHistoryCacheIndex_PReadyToAbandon = 12;
        public byte GameHistoryCacheIndex_DReadyToAbandon = 13;
        public byte GameHistoryCacheIndex_PChipsAction = 14;
        public byte GameHistoryCacheIndex_DChipsAction = 15;
        public byte GameHistoryCacheIndex_PWins = 16;

        public bool CheckCompleteAfterPrimaryAction;
        public bool CheckCompleteAfterPostPrimaryAction;

        #endregion

        #region Decisions list

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>();
            AddDisputeGeneratorDecisions(decisions);
            AddLiabilitySignalsDecisions(decisions);
            AddDamagesSignalsDecisions(decisions);
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
            AddCourtDecisions(decisions);
            return decisions;
        }

        private void AddDisputeGeneratorDecisions(List<Decision> decisions)
        {
            // Litigation Quality. This is not known by a player unless the player has perfect information. 
            // The LiabilitySignalChance player relies on this information in calculating the probabilities of different signals
            List<byte> playersKnowingLiabilityStrength = new List<byte>()
            {
                (byte) MyGamePlayers.PLiabilitySignalChance,
                (byte) MyGamePlayers.DLiabilitySignalChance,
                (byte) MyGamePlayers.CourtLiabilityChance,
                (byte) MyGamePlayers.Resolution
            };
            if (Options.PLiabilityNoiseStdev == 0)
                playersKnowingLiabilityStrength.Add((byte)MyGamePlayers.Plaintiff);
            if (Options.DLiabilityNoiseStdev == 0)
                playersKnowingLiabilityStrength.Add((byte)MyGamePlayers.Defendant);
            List<byte> playersKnowingDamagesStrength = new List<byte>()
            {
                (byte) MyGamePlayers.PDamagesSignalChance,
                (byte) MyGamePlayers.DDamagesSignalChance,
                (byte) MyGamePlayers.CourtDamagesChance,
                (byte) MyGamePlayers.Resolution
            };
            if (Options.PDamagesNoiseStdev == 0)
                playersKnowingDamagesStrength.Add((byte)MyGamePlayers.Plaintiff);
            if (Options.DDamagesNoiseStdev == 0)
                playersKnowingDamagesStrength.Add((byte)MyGamePlayers.Defendant);
            IMyGameDisputeGenerator disputeGenerator = Options.MyGameDisputeGenerator;
            disputeGenerator.GetActionsSetup(this, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate);
            CheckCompleteAfterPrimaryAction = primaryActionCanTerminate;
            CheckCompleteAfterPostPrimaryAction = postPrimaryChanceCanTerminate;
            if (prePrimaryChanceActions > 0)
            {
                decisions.Add(new Decision("PrePrimaryChanceActions", "PrePrimary", true, (byte) MyGamePlayers.PrePrimaryChance, prePrimaryPlayersToInform, prePrimaryChanceActions, (byte) MyGameDecisions.PrePrimaryActionChance) {StoreActionInGameCacheItem = GameHistoryCacheIndex_PrePrimaryChance, IsReversible = true, UnevenChanceActions = prePrimaryUnevenChance, Unroll_Parallelize = disputeGenerator.GetPrePrimaryUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetPrePrimaryUnrollSettings().unrollIdentical, DistributedChanceDecision = true });
            }
            if (primaryActions > 0)
            {
                decisions.Add(new Decision("PrimaryActions", "Primary", true, (byte) MyGamePlayers.PrePrimaryChance /* there is no primary chance player */, primaryPlayersToInform, primaryActions, (byte) MyGameDecisions.PrimaryAction) {StoreActionInGameCacheItem = GameHistoryCacheIndex_PrimaryAction, IsReversible = true, CanTerminateGame = primaryActionCanTerminate, Unroll_Parallelize = disputeGenerator.GetPrimaryUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetPrimaryUnrollSettings().unrollIdentical, DistributedChanceDecision = true });
            }
            if (postPrimaryChanceActions > 0)
            {
                decisions.Add(new Decision("PostPrimaryChanceActions", "PostPrimary", true, (byte) MyGamePlayers.PostPrimaryChance, postPrimaryPlayersToInform, postPrimaryChanceActions, (byte) MyGameDecisions.PostPrimaryActionChance) {StoreActionInGameCacheItem = GameHistoryCacheIndex_PostPrimaryChance, IsReversible = true, UnevenChanceActions = postPrimaryUnevenChance, CanTerminateGame = postPrimaryChanceCanTerminate, Unroll_Parallelize = disputeGenerator.GetPostPrimaryUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetPostPrimaryUnrollSettings().unrollIdentical, DistributedChanceDecision = true });
            }
            decisions.Add(new Decision("LiabilityStrength", "LiabStr", true, (byte)MyGamePlayers.LiabilityStrengthChance,
                    playersKnowingLiabilityStrength.ToArray(), Options.NumLiabilityStrengthPoints, (byte)MyGameDecisions.LiabilityStrength)
                { StoreActionInGameCacheItem = GameHistoryCacheIndex_LiabilityStrength, IsReversible = true, UnevenChanceActions = litigationQualityUnevenChance, Unroll_Parallelize = disputeGenerator.GetLiabilityStrengthUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetLiabilityStrengthUnrollSettings().unrollIdentical, DistributedChanceDecision = true });
            if (Options.NumDamagesStrengthPoints > 1)
                decisions.Add(new Decision("DamagesStrength", "DamStr", true, (byte)MyGamePlayers.DamagesStrengthChance,
                    playersKnowingDamagesStrength.ToArray(), Options.NumDamagesStrengthPoints, (byte)MyGameDecisions.DamagesStrength)
                { StoreActionInGameCacheItem = GameHistoryCacheIndex_DamagesStrength, IsReversible = true, UnevenChanceActions = litigationQualityUnevenChance, Unroll_Parallelize = disputeGenerator.GetDamagesStrengthUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetDamagesStrengthUnrollSettings().unrollIdentical, DistributedChanceDecision = true });
        }
        
        private void AddLiabilitySignalsDecisions(List<Decision> decisions)
        {
            // Plaintiff and defendant signals. If a player has perfect information, then no signal is needed.
            if (Options.PLiabilityNoiseStdev != 0)
                decisions.Add(new Decision("PlaintiffLiabilitySignal", "PLS", true, (byte)MyGamePlayers.PLiabilitySignalChance,
                    new byte[] {(byte) MyGamePlayers.Plaintiff},
                    Options.NumLiabilitySignals, (byte)MyGameDecisions.PLiabilitySignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true,
                    ProvidesPrivateInformationFor = (byte)MyGamePlayers.Plaintiff
                });
            if (Options.DLiabilityNoiseStdev != 0)
                decisions.Add(new Decision("DefendantLiabilitySignal", "DLS", true, (byte)MyGamePlayers.DLiabilitySignalChance,
                    new byte[] { (byte)MyGamePlayers.Defendant },
                    Options.NumLiabilitySignals, (byte)MyGameDecisions.DLiabilitySignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true,
                    ProvidesPrivateInformationFor = (byte) MyGamePlayers.Defendant
                });
            CreateLiabilitySignalsTables();
        }
        
        private double[][] PLiabilitySignalsTable, DLiabilitySignalsTable, CLiabilitySignalsTable, PDamagesSignalsTable, DDamagesSignalsTable, CDamagesSignalsTable;
        public void CreateLiabilitySignalsTables()
        {
            PLiabilitySignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumLiabilityStrengthPoints, Options.NumLiabilitySignals });
            DLiabilitySignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumLiabilityStrengthPoints, Options.NumLiabilitySignals });
            CLiabilitySignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumLiabilityStrengthPoints, 2 });
            for (byte litigationQuality = 1;
                litigationQuality <= Options.NumLiabilityStrengthPoints;
                litigationQuality++)
            {
                double litigationQualityUniform =
                    EquallySpaced.GetLocationOfEquallySpacedPoint(litigationQuality - 1,
                        Options.NumLiabilityStrengthPoints, false);

                DiscreteValueSignalParameters pParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumLiabilityStrengthPoints, NumSignals = Options.NumLiabilitySignals, StdevOfNormalDistribution = Options.PLiabilityNoiseStdev, UseEndpoints = false };
                PLiabilitySignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, pParams);
                DiscreteValueSignalParameters dParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumLiabilityStrengthPoints, NumSignals = Options.NumLiabilitySignals, StdevOfNormalDistribution = Options.DLiabilityNoiseStdev, UseEndpoints = false };
                DLiabilitySignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, dParams);
                DiscreteValueSignalParameters cParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumLiabilityStrengthPoints, NumSignals = 2, StdevOfNormalDistribution = Options.CourtLiabilityNoiseStdev, UseEndpoints = false };
                CLiabilitySignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, cParams);
            }
        }

        private void AddDamagesSignalsDecisions(List<Decision> decisions)
        {
            if (Options.NumDamagesStrengthPoints <= 1)
                return;

            // Plaintiff and defendant signals. If a player has perfect information, then no signal is needed.
            if (Options.PDamagesNoiseStdev != 0)
                decisions.Add(new Decision("PlaintiffDamagesSignal", "PDS", true, (byte)MyGamePlayers.PDamagesSignalChance,
                    new byte[] { (byte)MyGamePlayers.Plaintiff },
                    Options.NumDamagesSignals, (byte)MyGameDecisions.PDamagesSignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true,
                    ProvidesPrivateInformationFor = (byte)MyGamePlayers.Plaintiff
                });
            if (Options.DDamagesNoiseStdev != 0)
                decisions.Add(new Decision("DefendantDamagesSignal", "DDS", true, (byte)MyGamePlayers.DDamagesSignalChance,
                    new byte[] { (byte)MyGamePlayers.Defendant },
                    Options.NumDamagesSignals, (byte)MyGameDecisions.DDamagesSignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true,
                    ProvidesPrivateInformationFor = (byte)MyGamePlayers.Defendant
                });
            CreateDamagesSignalsTables();
        }

        public void CreateDamagesSignalsTables()
        {
            PDamagesSignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumDamagesStrengthPoints, Options.NumDamagesSignals });
            DDamagesSignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumDamagesStrengthPoints, Options.NumDamagesSignals });
            CDamagesSignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumDamagesStrengthPoints, Options.NumDamagesSignals });
            for (byte litigationQuality = 1;
                litigationQuality <= Options.NumDamagesStrengthPoints;
                litigationQuality++)
            {
                double litigationQualityUniform =
                    EquallySpaced.GetLocationOfEquallySpacedPoint(litigationQuality - 1,
                        Options.NumDamagesStrengthPoints, false);

                DiscreteValueSignalParameters pParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumDamagesStrengthPoints, NumSignals = Options.NumDamagesSignals, StdevOfNormalDistribution = Options.PDamagesNoiseStdev, UseEndpoints = false };
                PDamagesSignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, pParams);
                DiscreteValueSignalParameters dParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumDamagesStrengthPoints, NumSignals = Options.NumDamagesSignals, StdevOfNormalDistribution = Options.DDamagesNoiseStdev, UseEndpoints = false };
                DDamagesSignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, dParams);
                DiscreteValueSignalParameters cParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumDamagesStrengthPoints, NumSignals = Options.NumDamagesSignals, StdevOfNormalDistribution = Options.CourtDamagesNoiseStdev, UseEndpoints = false }; // TODO: Differentiate number of court damages signals, since we might want that to be a higher number.
                CDamagesSignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, cParams);
            }
        }

        public double[] GetPLiabilitySignalProbabilities(byte litigationQuality)
        {
            return PLiabilitySignalsTable[litigationQuality - 1];
        }
        public double[] GetDLiabilitySignalProbabilities(byte litigationQuality)
        {
            return DLiabilitySignalsTable[litigationQuality - 1];
        }
        public double[] GetCLiabilitySignalProbabilities(byte litigationQuality)
        {
            return CLiabilitySignalsTable[litigationQuality - 1];
        }
        public double[] GetPDamagesSignalProbabilities(byte litigationQuality)
        {
            return PDamagesSignalsTable[litigationQuality - 1];
        }
        public double[] GetDDamagesSignalProbabilities(byte litigationQuality)
        {
            return DDamagesSignalsTable[litigationQuality - 1];
        }
        public double[] GetCDamagesSignalProbabilities(byte litigationQuality)
        {
            return CDamagesSignalsTable[litigationQuality - 1];
        }

        private void AddFileAndAnswerDecisions(List<Decision> decisions)
        {
            if (Options.SkipFileAndAnswerDecisions)
                return;
            var pFile =
                new Decision("PFile", "PF", false, (byte)MyGamePlayers.Plaintiff, new byte[]  { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.PFile)
                { // TODO: Maybe can eliminate notice to plaintiff and defendant here and below
                    CanTerminateGame = true, // not filing always terminates
                    IsReversible = true
                };
            decisions.Add(pFile);

            var dAnswer =
                new Decision("DAnswer", "DA", false, (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.DAnswer)
                {
                    CanTerminateGame = true, // not answering terminates, with defendant paying full damages
                    IsReversible = true
                };
            decisions.Add(dAnswer);
        }

        private void AddDecisionsForBargainingRound(int b, List<Decision> decisions)
        {
            // Agreement to bargain: We do want to add this to the information set of the opposing player, since that may be relevant in future rounds and also might affect decisions whether to abandon/default later in this round, but we want to defer addition of the plaintiff statement, so that it doesn't influence the defendant decision, since the players are supposedly making the decisions at the same time.

            if (Options.IncludeAgreementToBargainDecisions)
            {
                var pAgreeToBargain = new Decision("PAgreeToBargain" + (b + 1), "PB" + (b + 1), false, (byte) MyGamePlayers.Plaintiff, new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
                    2, (byte) MyGameDecisions.PAgreeToBargain)
                {
                    CustomByte = (byte) (b + 1),
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_PAgreesToBargain,
                    DeferNotificationOfPlayers = true,
                    WarmStartThroughIteration = Options.WarmStartThroughIteration,
                    WarmStartValue = 1
                };
                decisions.Add(pAgreeToBargain);

                var dAgreeToBargain = new Decision("DAgreeToBargain" + (b + 1), "DB" + (b + 1), false, (byte) MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
                    2, (byte) MyGameDecisions.DAgreeToBargain)
                {
                    CustomByte = (byte) (b + 1),
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_DAgreesToBargain,
                    WarmStartThroughIteration = Options.WarmStartThroughIteration,
                    WarmStartValue = 1
                };
                decisions.Add(dAgreeToBargain);
            }

            // note that we will do all information set manipulation in CustomInformationSetManipulation below.
            if (Options.BargainingRoundsSimultaneous)
            {
                byte[] informedOfPOffer, informedOfDOffer;
                if (Options.SimultaneousOffersUltimatelyRevealed)
                {
                    informedOfPOffer = informedOfDOffer = new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution };
                }
                else
                {
                    informedOfPOffer = new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Resolution };
                    informedOfDOffer = new byte[] { (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution };
                }
                // samuelson-chaterjee bargaining.
                var pOffer =
                    new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), false, (byte)MyGamePlayers.Plaintiff, informedOfPOffer,
                        Options.NumOffers, (byte)MyGameDecisions.POffer)
                    {
                        CustomByte = (byte)(b + 1),
                        DeferNotificationOfPlayers = true, // wait until after defendant has gone for defendant to find out -- of course, we don't do that with defendant decision
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_POffer,
                        IsContinuousAction = true,
                        WarmStartThroughIteration = Options.WarmStartThroughIteration,
                        WarmStartValue = (byte)(Options.WarmStartOptions switch
                        {
                            MyGameWarmStartOptions.DiscourageSettlementByMakingOpponentGenerous => 1,
                            MyGameWarmStartOptions.FacilitateSettlementByMakingOpponentStingy => Options.NumOffers,
                            _ => 0,
                        })
                    };
                AddOfferDecision(decisions, pOffer);
                var dOffer =
                    new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), false, (byte)MyGamePlayers.Defendant, informedOfDOffer,
                        Options.NumOffers, (byte)MyGameDecisions.DOffer)
                    {
                        CanTerminateGame = true,
                        CustomByte = (byte)(b + 1),
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_DOffer,
                        IsContinuousAction = true,
                        WarmStartThroughIteration = Options.WarmStartThroughIteration,
                        WarmStartValue = (byte)(Options.WarmStartOptions switch
                        {
                            MyGameWarmStartOptions.DiscourageSettlementByMakingOpponentGenerous => Options.NumOffers,
                            MyGameWarmStartOptions.FacilitateSettlementByMakingOpponentStingy => 1,
                            _ => 0,
                        })
                    };
                AddOfferDecision(decisions, dOffer);
            }
            else
            {
                // offer-response bargaining. We add the offer and response to the players' information sets. 
                if (Options.PGoesFirstIfNotSimultaneous[b])
                {
                    var pOffer =
                        new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), false, (byte)MyGamePlayers.Plaintiff, new byte[] { (byte) MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
                            Options.NumOffers, (byte)MyGameDecisions.POffer)
                        {
                            CustomByte = (byte)(b + 1),
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_POffer,
                            IsContinuousAction = true,
                        }; // { AlwaysDoAction = 4});
                    AddOfferDecision(decisions, pOffer);
                    decisions.Add(
                        new Decision("DefendantResponse" + (b + 1), "DR" + (b + 1), false, (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution }, 2,
                            (byte)MyGameDecisions.DResponse)
                        {
                            CanTerminateGame = true,
                            CustomByte = (byte)(b + 1),
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_DResponse,
                            WarmStartThroughIteration = Options.WarmStartThroughIteration,
                            WarmStartValue = (byte)(Options.WarmStartOptions switch
                            {
                                MyGameWarmStartOptions.DiscourageSettlementByMakingOpponentGenerous => 1,
                                MyGameWarmStartOptions.FacilitateSettlementByMakingOpponentStingy => 2,
                                _ => 0,
                            })
                        });
                }
                else
                {
                    var dOffer =
                        new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), false, (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
                            Options.NumOffers, (byte)MyGameDecisions.DOffer)
                        {
                            CustomByte = (byte)(b + 1),
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_DOffer,
                            IsContinuousAction = true,
                            WarmStartThroughIteration = Options.WarmStartThroughIteration,
                            WarmStartValue = (byte)(Options.WarmStartOptions switch
                            {
                                MyGameWarmStartOptions.DiscourageSettlementByMakingOpponentGenerous => Options.NumOffers,
                                MyGameWarmStartOptions.FacilitateSettlementByMakingOpponentStingy => 1,
                                _ => 0,
                            })
                        };
                    AddOfferDecision(decisions, dOffer);
                    decisions.Add(
                        new Decision("PlaintiffResponse" + (b + 1), "PR" + (b + 1), false, (byte)MyGamePlayers.Plaintiff, new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution }, 2,
                            (byte)MyGameDecisions.PResponse)
                        {
                            CanTerminateGame = true,
                            CustomByte = (byte)(b + 1),
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_PResponse,
                            WarmStartThroughIteration = Options.WarmStartThroughIteration,
                            WarmStartValue = (byte)(Options.WarmStartOptions switch
                            {
                                MyGameWarmStartOptions.DiscourageSettlementByMakingOpponentGenerous => 1,
                                MyGameWarmStartOptions.FacilitateSettlementByMakingOpponentStingy => 2,
                                _ => 0,
                            })
                        });
                }
            }
        }

        private void AddOfferDecision(List<Decision> decisions, Decision offerDecision)
        {
            decisions.Add(offerDecision);
        }

        private void AddRunningSideBetDecisions(int b, List<Decision> decisions)
        {
            var pRSideBet =
                new Decision("pRSideBet" + (b + 1), "PRSB" + (b + 1), false, (byte)MyGamePlayers.Plaintiff, new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
                    (byte) (Options.MyGameRunningSideBets.MaxChipsPerRound + 1), (byte)MyGameDecisions.PChips)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false,
                    DeferNotificationOfPlayers = true,
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_PChipsAction,
                };
            decisions.Add(pRSideBet);
            var dRSideBet =
                new Decision("dRSideBet" + (b + 1), "DRSB" + (b + 1), false, (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
                    (byte)(Options.MyGameRunningSideBets.MaxChipsPerRound + 1), (byte)MyGameDecisions.DChips)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false,
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_DChipsAction,
                    IsReversible = true,
                };
            decisions.Add(dRSideBet);
        }

        private void AddAbandonOrDefaultDecisions(int b, List<Decision> decisions)
        {
            var pAbandon =
                new Decision("PAbandon" + (b + 1), "PA" + (b + 1), false, (byte)MyGamePlayers.Plaintiff, new byte[] {(byte) MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.PAbandon)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false, // we always must look at whether D is defaulting too. 
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_PReadyToAbandon,
                    IsReversible = true,
                };
            decisions.Add(pAbandon);

            var dDefault =
                new Decision("DDefault" + (b + 1), "DD" + (b + 1), false, (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.DDefault)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = true, // if either but not both has given up, game terminates
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_DReadyToAbandon,
                    IsReversible = true,
                };
            decisions.Add(dDefault);

            var bothGiveUp =
                new Decision("MutualGiveUp" + (b + 1), "MGU" + (b + 1), true, (byte)MyGamePlayers.BothGiveUpChance, new byte[] { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.MutualGiveUp, unevenChanceActions: false)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = true, // if this decision is needed, then both have given up, and the decision always terminates the game
                    CriticalNode = true, // always play out both sides of this coin flip
                    IsReversible = true
                };
            decisions.Add(bothGiveUp);
        }

        private void AddPreBargainingRoundDummyDecision(int b, List<Decision> decisions)
        {
            var dummyDecision =
                new Decision("PreBargainingRound" + (b + 1), "PRBR" + (b + 1), true, (byte)MyGamePlayers.PreBargainingRoundChance, null,
                    1 /* i.e., just an opportunity to do some calculation and cleanup */, (byte)MyGameDecisions.PreBargainingRound, unevenChanceActions: false)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false, 
                    CriticalNode = false, // doesn't matter -- just one possibility
                };
            decisions.Add(dummyDecision);
        }

        private void AddPostBargainingRoundDummyDecision(int b, List<Decision> decisions)
        {
            var dummyDecision =
                new Decision("PostBargainingRound" + (b + 1), "POBR" + (b + 1), true, (byte)MyGamePlayers.PostBargainingRoundChance, null,
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
                    decisions.Add(new Decision("PPreTrial", "PPT", false, (byte) MyGamePlayers.Plaintiff, playersToInformOfPAction, pActions, (byte) MyGameDecisions.PPretrialAction) { IsReversible = true});
                }
                if (dActions > 0)
                {
                    decisions.Add(new Decision("DPreTrial", "DPT", false, (byte)MyGamePlayers.Defendant, playersToInformOfDAction, dActions, (byte)MyGameDecisions.DPretrialAction) { IsReversible = true });
                }
            }
        }

        private void AddCourtDecisions(List<Decision> decisions)
        {
            bool courtDecidesDamages = Options.NumDamagesStrengthPoints > 1;
            decisions.Add(new Decision("CourtLiabilityDecision", "CL", true, (byte)MyGamePlayers.CourtLiabilityChance,
                    new byte[] { (byte)MyGamePlayers.Resolution }, 2, (byte)MyGameDecisions.CourtDecisionLiability,
                    unevenChanceActions: true, criticalNode: true)
                { CanTerminateGame = true, AlwaysTerminatesGame = !courtDecidesDamages, IsReversible = true, DistributorChanceDecision = true, CanCalculateDistributorChanceDecisionProbabilitiesFromInformationSet = true, StoreActionInGameCacheItem = GameHistoryCacheIndex_PWins }); // even chance options
            if (courtDecidesDamages)
                decisions.Add(new Decision("CourtDamagesDecision", "CD", true, (byte)MyGamePlayers.CourtDamagesChance,
                    new byte[] { (byte)MyGamePlayers.Resolution }, Options.NumDamagesSignals, (byte)MyGameDecisions.CourtDecisionDamages,
                    unevenChanceActions: true, criticalNode: true)
                { CanTerminateGame = true, AlwaysTerminatesGame = true, IsReversible = true, DistributorChanceDecision = true, CanCalculateDistributorChanceDecisionProbabilitiesFromInformationSet = true }); // even chance options
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
            else if (decisionByteCode == (byte) MyGameDecisions.LiabilityStrength)
            {
                var myGameProgress = ((MyGameProgress) gameProgress);
                var probabilities = Options.MyGameDisputeGenerator.GetLiabilityStrengthProbabilities(this, myGameProgress.DisputeGeneratorActions);
                return probabilities;
            }
            else if (decisionByteCode == (byte)MyGameDecisions.PLiabilitySignal)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = GetPLiabilitySignalProbabilities(myGameProgress.LiabilityStrengthDiscrete);
                return probabilities;
            }
            else if (decisionByteCode == (byte)MyGameDecisions.DLiabilitySignal)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = GetDLiabilitySignalProbabilities(myGameProgress.LiabilityStrengthDiscrete);
                return probabilities;
            }
            else if (decisionByteCode == (byte)MyGameDecisions.CourtDecisionLiability)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = GetCLiabilitySignalProbabilities(myGameProgress.LiabilityStrengthDiscrete);
                return probabilities;
            }
            else if (decisionByteCode == (byte)MyGameDecisions.DamagesStrength)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = Options.MyGameDisputeGenerator.GetDamagesStrengthProbabilities(this, myGameProgress.DisputeGeneratorActions);
                return probabilities;
            }
            else if (decisionByteCode == (byte)MyGameDecisions.PDamagesSignal)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = GetPDamagesSignalProbabilities(myGameProgress.DamagesStrengthDiscrete);
                return probabilities;
            }
            else if (decisionByteCode == (byte)MyGameDecisions.DDamagesSignal)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = GetDDamagesSignalProbabilities(myGameProgress.DamagesStrengthDiscrete);
                return probabilities;
            }
            else if (decisionByteCode == (byte)MyGameDecisions.CourtDecisionDamages)
            {
                var myGameProgress = ((MyGameProgress)gameProgress);
                var probabilities = GetCDamagesSignalProbabilities(myGameProgress.DamagesStrengthDiscrete);
                return probabilities;
            }
            else
                throw new NotImplementedException(); // subclass should define if needed
        }

        public override bool SkipDecision(Decision decision, in GameHistory gameHistory)
        {
            byte decisionByteCode = decision.DecisionByteCode;
            if (decisionByteCode == (byte) MyGameDecisions.MutualGiveUp)
            {
                bool pTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PReadyToAbandon) == 1;
                bool dTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DReadyToAbandon) == 1;
                bool skip = !pTryingToGiveUp || !dTryingToGiveUp; // if anyone is NOT trying to give up, we don't have to deal with mutual giving up
                return skip;
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
        
        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, in GameHistory gameHistory, byte actionChosen)
        {
            if (!currentDecision.CanTerminateGame)
                return false;

            byte decisionByteCode = currentDecision.DecisionByteCode;

            switch (decisionByteCode)
            {
                case (byte)MyGameDecisions.CourtDecisionLiability:
                    bool pWins = Options.NumLiabilitySignals == 1 || gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PWins) == 2; // 2 means P Wins, since this is action taken at this decision
                    if (pWins)
                    {
                        bool courtWouldDecideDamages = Options.NumDamagesStrengthPoints > 1;
                        return !courtWouldDecideDamages;
                    }
                    else
                        return true; // defendant wins --> always over
                case (byte)MyGameDecisions.CourtDecisionDamages:
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
            byte decisionByteCode = currentDecision.DecisionByteCode; // get the original decision byte code
            
        }

        public override void ReverseSwitchToBranchEffects(Decision decisionToReverse, in HistoryPoint historyPoint)
        {
            if (historyPoint.HistoryToPoint.IsEmpty)
                return; // we aren't tracking the game history (maybe because we are using a game tree instead of cached history)
            base.ReverseSwitchToBranchEffects(decisionToReverse, in historyPoint);
            byte decisionByteCode = decisionToReverse.DecisionByteCode;
        }

        #endregion

        #region Alternative scenarios

        public override bool PlayMultipleScenarios => false; // Note: Even if this is false, we can define a scenario as a "warm-up scenario."

        public override int NumPostWarmupOptionSets => 1; 
        public override int NumWarmupOptionSets => 2; // Note that this can be 0. This indicates the variations on the costs multiplier; variations on weight to opponent are below. 
        public override int WarmupIterations_IfWarmingUp => 30;
        public override bool MultiplyWarmupScenariosByAlteringWeightOnOpponentsStrategy => true;
        public override int NumDifferentWeightsOnOpponentsStrategyPerWarmupScenario_IfMultiplyingScenarios => 2; // should be odd if we want to include zero
        public override bool VaryWeightOnOpponentsStrategySeparatelyForEachPlayer => true; 
        public override (double, double) MinMaxWeightOnOpponentsStrategyDuringWarmup => (-0.8, 0.8); // NOTE: Don't go all the way up to 1, because then if costs multiplier is 0 (i.e., it is a zero-sum game), utility for a player will be invariant.

        public enum ChangeInScenario
        {
            TrialCosts,
            CostsMultiplier
        }

        // note that we do not integrate the warmup and postwarmup at any one time. So, if a different variable is changing warmup and postwarmup, then the warmup phase will reflect the default value for the post warmup variable, not the eventual value.
        ChangeInScenario? WhatToChange_Warmup = ChangeInScenario.CostsMultiplier;
        ChangeInScenario? WhatToChange_PostWarmup = null; 

        public double TrialCostsScenarioPerPartyMin = 0; 
        public double TrialCostsScenarioPerPartyMax = 30_000; 
        public double TrialCostsScenarioPerPartyMin_Warmup = 0;
        public double TrialCostsScenarioPerPartyMax_Warmup = 30_000;
        bool changeTrialCostsForPlaintiff = true;
        bool changeTrialCostsForDefendant = true;

        public double CostsMultiplierMin = 0.0;
        public double CostsMultiplierMax = 2.0;
        public double CostsMultiplierMin_Warmup = 0.0;
        public double CostsMultiplierMax_Warmup = 2.0;

        public override void RememberOriginalChangeableOptions()
        {
            if (Options.CostsMultiplier_Original == null)
            {
                Options.CostsMultiplier_Original = Options.CostsMultiplier;
            }
            if (Options.PTrialCosts_Original == null)
            {
                Options.PTrialCosts_Original = Options.PTrialCosts;
            }
            if (Options.DTrialCosts_Original == null)
            {
                Options.DTrialCosts_Original = Options.DTrialCosts;
            }
        }

        public override void RestoreOriginalChangeableOptions()
        {
            if (Options.CostsMultiplier_Original != null)
            {
                Options.CostsMultiplier = (double)Options.CostsMultiplier_Original;
            }
            if (Options.PTrialCosts_Original != null)
            {
                Options.PTrialCosts = (double)Options.PTrialCosts_Original;
            }
            if (Options.DTrialCosts_Original != null)
            {
                Options.DTrialCosts = (double) Options.DTrialCosts_Original;
            }
        }

        public override void ChangeOptionsBasedOnScenario(int? postWarmupScenarioIndex, int? warmupScenarioIndex)
        {
            RememberOriginalChangeableOptions(); 
            RestoreOriginalChangeableOptions();
            if (NumPostWarmupOptionSets > 1 && WhatToChange_PostWarmup == null)
                throw new Exception("WhatToChange_PostWarmup is undefined");
            if (NumWarmupOptionSets > 0 && WhatToChange_Warmup == null)
                throw new Exception("WhatToChange_Warmup is undefined");
            if (postWarmupScenarioIndex != null && warmupScenarioIndex != null)
                throw new Exception("We can change variables to warmup or postwarmup scenario, but not to both at once, because we don't support having a warmup and a postwarmup scenario at the same time.");
            if (WhatToChange_PostWarmup == WhatToChange_Warmup)
            {
                if (WhatToChange_PostWarmup == null)
                {
                    return;
                }
            }
            if (postWarmupScenarioIndex != null)
            {
                if (WhatToChange_PostWarmup == ChangeInScenario.TrialCosts)
                {
                    if (changeTrialCostsForPlaintiff)
                        Options.PTrialCosts = GetParameterInRange(TrialCostsScenarioPerPartyMin, TrialCostsScenarioPerPartyMax, (int) postWarmupScenarioIndex, NumPostWarmupOptionSets);
                    if (changeTrialCostsForDefendant)
                        Options.DTrialCosts = GetParameterInRange(TrialCostsScenarioPerPartyMin, TrialCostsScenarioPerPartyMax, (int) postWarmupScenarioIndex, NumPostWarmupOptionSets);
                }
                else if (WhatToChange_PostWarmup == ChangeInScenario.CostsMultiplier)
                {
                    Options.CostsMultiplier = GetParameterInRange(CostsMultiplierMin, CostsMultiplierMax, (int) postWarmupScenarioIndex, NumPostWarmupOptionSets);
                }
            }
            if (NumWarmupOptionSets > 0)
            {
                if (warmupScenarioIndex != null)
                {
                    if (WhatToChange_Warmup == ChangeInScenario.TrialCosts)
                    {
                        if (changeTrialCostsForPlaintiff)
                            Options.PTrialCosts = GetParameterInRange(TrialCostsScenarioPerPartyMin_Warmup, TrialCostsScenarioPerPartyMax_Warmup, (int)warmupScenarioIndex, NumWarmupOptionSets);
                        if (changeTrialCostsForDefendant)
                            Options.DTrialCosts = GetParameterInRange(TrialCostsScenarioPerPartyMin_Warmup, TrialCostsScenarioPerPartyMax_Warmup, (int)warmupScenarioIndex, NumWarmupOptionSets);
                    }
                    else if (WhatToChange_Warmup == ChangeInScenario.CostsMultiplier)
                    {
                        Options.CostsMultiplier = GetParameterInRange(CostsMultiplierMin_Warmup, CostsMultiplierMax_Warmup, (int)warmupScenarioIndex, NumWarmupOptionSets);
                    }
                }
            }
        }

        public override string GetNameForScenario()
        { 
            if (!PlayMultipleScenarios)
                return base.GetNameForScenario(); // null
            string warmupResult = "";
            if (WhatToChange_Warmup == ChangeInScenario.TrialCosts || WhatToChange_PostWarmup == ChangeInScenario.TrialCosts)
                warmupResult += $"TrialCosts{Options.PTrialCosts},{Options.DTrialCosts} ";
            if (WhatToChange_Warmup == ChangeInScenario.CostsMultiplier || WhatToChange_PostWarmup == ChangeInScenario.CostsMultiplier)
                warmupResult += $"CostsMult{Options.CostsMultiplier}";
            if (warmupResult.Trim() == "")
                warmupResult = "Baseline";
            return warmupResult;
        }

        #endregion

    }
}
