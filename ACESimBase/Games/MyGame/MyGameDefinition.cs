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
        public byte GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound = 6;
        public byte GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound = 7;
        public byte GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound = 8;
        public byte GameHistoryCacheIndex_PAgreesToBargain = 9;
        public byte GameHistoryCacheIndex_DAgreesToBargain = 10;
        public byte GameHistoryCacheIndex_POffer = 11;
        public byte GameHistoryCacheIndex_DOffer = 12;
        public byte GameHistoryCacheIndex_PResponse = 13;
        public byte GameHistoryCacheIndex_DResponse = 14;
        public byte GameHistoryCacheIndex_PReadyToAbandon = 15;
        public byte GameHistoryCacheIndex_DReadyToAbandon = 16;
        public byte GameHistoryCacheIndex_PChipsAction = 17;
        public byte GameHistoryCacheIndex_DChipsAction = 18;
        public byte GameHistoryCacheIndex_TotalChipsSoFar = 19;
        public byte GameHistoryCacheIndex_PWins = 20;

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
                DiscreteValueSignalParameters cParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumDamagesStrengthPoints, NumSignals = Options.NumDamagesSignals, StdevOfNormalDistribution = Options.CourtDamagesNoiseStdev, UseEndpoints = false }; // DEBUG TODO: Differentiate number of court damages signals, since we might want that to be a higher number.
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
                    IncrementGameCacheItem = new byte[] {
                        GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound, GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound,
                    },
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
                    IncrementGameCacheItem = new byte[] {
                        GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound, GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound,
                    },
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_DAgreesToBargain,
                    WarmStartThroughIteration = Options.WarmStartThroughIteration,
                    WarmStartValue = 1
                };
                decisions.Add(dAgreeToBargain);
            }

            // note that we will do all information set manipulation in CustomInformationSetManipulation below.
            if (Options.BargainingRoundsSimultaneous)
            {
                byte[] informedOfPOffer, informedOfDOffer, pIncrementGameCacheItem, dIncrementGameCacheItem;
                if (Options.SimultaneousOffersUltimatelyRevealed && Options.BargainingRoundRecall != MyGameBargainingRoundRecall.ForgetEarlierBargainingRounds)
                {
                    informedOfPOffer = informedOfDOffer = new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution };
                    pIncrementGameCacheItem = dIncrementGameCacheItem = new byte[] {
                        GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound, GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound,
                        };
                }
                else
                {
                    informedOfPOffer = new byte[] { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Resolution };
                    pIncrementGameCacheItem = new byte[] {
                        GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound,  GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound,
                        };
                    informedOfDOffer = new byte[] { (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Resolution };
                    dIncrementGameCacheItem = new byte[] {GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound, GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound,
                        };
                }
                // samuelson-chaterjee bargaining.
                var pOffer =
                    new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), false, (byte)MyGamePlayers.Plaintiff, informedOfPOffer,
                        Options.NumOffers, (byte)MyGameDecisions.POffer)
                    {
                        CustomByte = (byte)(b + 1),
                        IncrementGameCacheItem = pIncrementGameCacheItem,
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
                        IncrementGameCacheItem = dIncrementGameCacheItem,
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
                            IncrementGameCacheItem = new byte[] {
                        GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound, GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound
                        },
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
                            IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound },
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
                            IncrementGameCacheItem = new byte[] {
                                GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound,
                                GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound,
                                GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound,
                            },
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
                            IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound, GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound },
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
                new Decision("pRSideBet" + (b + 1), "PRSB" + (b + 1), false, (byte)MyGamePlayers.Plaintiff, new byte[] { },
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
                new Decision("dRSideBet" + (b + 1), "DRSB" + (b + 1), false, (byte)MyGamePlayers.Defendant, new byte[] { },
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
            // These decisions don't need to be added to P/D information sets, because if the game is abandoned or defaulted by at least one player, there are no more player decisions. If there are player decisions, this hasn't occurred. However, we still need a marker to indicate that the decision has occurred, so that the player can distinguish its own decision to abandon/default from any later pretrial decision.

            var pAbandon =
                new Decision("PAbandon" + (b + 1), "PA" + (b + 1), false, (byte)MyGamePlayers.Plaintiff, new byte[] { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.PAbandon)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false, // we always must look at whether D is defaulting too. 
                    IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumPlaintiffItemsThisBargainingRound, GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound },
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_PReadyToAbandon,
                    IsReversible = true,
                    PlayersToInformOfOccurrenceOnly = new byte[] {(byte)MyGamePlayers.Plaintiff}
                };
            decisions.Add(pAbandon);

            var dDefault =
                new Decision("DDefault" + (b + 1), "DD" + (b + 1), false, (byte)MyGamePlayers.Defendant, new byte[] { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.DDefault)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = true, // if either but not both has given up, game terminates
                    IncrementGameCacheItem = new byte[] { GameHistoryCacheIndex_NumDefendantItemsThisBargainingRound, GameHistoryCacheIndex_NumResolutionItemsThisBargainingRound },
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_DReadyToAbandon,
                    IsReversible = true,
                    PlayersToInformOfOccurrenceOnly = new byte[] { (byte)MyGamePlayers.Defendant }
                };
            decisions.Add(dDefault);

            var bothGiveUp =
                new Decision("MutualGiveUp" + (b + 1), "MGU" + (b + 1), true, (byte)MyGamePlayers.BothGiveUpChance, new byte[] { (byte)MyGamePlayers.Resolution },
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
                new Decision("PreBargainingRound" + (b + 1), "PRBR" + (b + 1), true, (byte)MyGamePlayers.PreBargainingRoundChance, null,
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

        public override unsafe double[] GetUnevenChanceActionProbabilitiesFromChanceInformationSet(byte decisionByteCode, byte* informationSet)
        {
            if (decisionByteCode == (byte)MyGameDecisions.CourtDecisionLiability)
            {
                byte litigationQuality = *informationSet;
                var probabilities = GetCLiabilitySignalProbabilities(litigationQuality);
                return probabilities;
            }
            return null;
        }

        //public override unsafe double[] GetUnevenChanceActionProbabilitiesFromChanceInformationSet(byte decisionByteCode, List<(List<byte>, double)> distributionOfChanceValues)
        //{
        //    // The distribution of chance values will include combinations of all distributor chance decisions. For now, we only care about the litigation quality decision. Each litigation quality itself produces a distribution of court signal probabilities. We average these distributions based on the probability of various litigation qualities.  
        //    if (decisionByteCode == (byte)MyGameDecisions.CourtDecision)
        //    {
        //        double[] results = new double[distributionOfChanceValues.Count]; 
        //        for (int j = 0; j < distributionOfChanceValues.Count; j++)
        //        {
        //            var item = distributionOfChanceValues[j];
        //            byte litigationQuality = item.Item1.Last(); // assume that litigation quality is last item
        //            double probabilityThisItem = item.Item2;
        //            var probabilitiesForLiabilityStrength = GetCLiabilitySignalProbabilities(litigationQuality);
        //            if (results == null)
        //                results = new double[probabilitiesForLiabilityStrength.Length];
        //            for (int i = 0; i < probabilitiesForLiabilityStrength.Length; i++)
        //                results[i] += probabilitiesForLiabilityStrength[i] * probabilityThisItem;
        //        }
        //        return results;
        //    }
        //    return null;
        //}

        public override bool SkipDecision(Decision decision, ref GameHistory gameHistory)
        {
            byte decisionByteCode = decision.DecisionByteCode;
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

            byte decisionByteCode = currentDecision.DecisionByteCode;

            switch (decisionByteCode)
            {
                case (byte)MyGameDecisions.CourtDecisionLiability:
                    bool pWins = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PWins) == 2; // 2 means P Wins, since this is action taken at this decision
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

        #region Alternative scenarios

        public bool PlayMultipleScenarios = true; // DEBUG // Note: Even if this is false, we can define a scenario as a "warm-up scenario."

        public int NumScenariosDefined = 41;

        public override int NumScenariosToDevelop => PlayMultipleScenarios ? NumScenariosDefined : 1;
        public override int NumScenariosToInitialize => NumScenariosDefined;

        public override int GetScenarioIndex(int baselineScenario, bool warmupVersion)
        {
            // Testing the same scenario each time, but with different warmups
            if (!warmupVersion)
                return 0; 
            return baselineScenario;
        }

        public override void SetScenarioIndex(int scenarioIndex)
        {
            CurrentScenarioIndex = scenarioIndex;
            int warmupTrialCosts = 10_000 + 2_000 * scenarioIndex;
            Options.PTrialCosts = Options.DTrialCosts = warmupTrialCosts;
        }

        public override string GetNameForScenario()
        {
            if (NumScenariosToDevelop == 1)
                return base.GetNameForScenario();
            int warmupTrialCosts = 10_000 + 2_000 * BaselineScenarioIndex;
            return "WarmCosts" + warmupTrialCosts.ToString();
        }

        #endregion

    }
}
