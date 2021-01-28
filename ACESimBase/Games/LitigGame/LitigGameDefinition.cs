using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.GameSolvingSupport;
using ACESimBase.Util;
using ACESimBase.Util.DiscreteProbabilities;

namespace ACESim
{
    [Serializable]
    public partial class LitigGameDefinition : GameDefinition
    {
        #region Construction and setup

        public LitigGameOptions Options => (LitigGameOptions)GameOptions;

        public override string ToString()
        {
            return Options.ToString();
        }

        public LitigGameDefinition() : base()
        {

        }
        public override void Setup(GameOptions options)
        {
            if (options == null)
                throw new Exception("Options cannot be null.");
            base.Setup(options);
            FurtherOptionsSetup();

            Players = GetPlayersList();
            PlayerNames = Players.Select(x => x.PlayerName).ToArray();
            NumPlayers = (byte) Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();
            CalculateDistributorChanceInputDecisionMultipliers();

            IGameFactory gameFactory = new LitigGameFactory();
            Initialize(gameFactory);
        }

        public override bool GameIsSymmetric()
        {
            return Options.IsSymmetric();
        }

        private void FurtherOptionsSetup()
        {
            if (Options.DeltaOffersOptions.SubsequentOffersAreDeltas)
                Options.DeltaOffersCalculation = new DeltaOffersCalculation(this);
            SetupLiabilitySignals();
            SetupDamagesSignals();
            Options.LitigGameDisputeGenerator.Setup(this);
            Options.LitigGamePretrialDecisionGeneratorGenerator?.Setup(this);
            Options.LitigGameRunningSideBets?.Setup(this);
        }

        private void SetupDamagesSignals()
        {
            Options.PDamagesSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumDamagesStrengthPoints,
                StdevOfNormalDistribution = Options.PDamagesNoiseStdev,
                NumSignals = Options.NumDamagesSignals,
                SourcePointsIncludeExtremes = false
            };
            Options.DDamagesSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumDamagesStrengthPoints,
                StdevOfNormalDistribution = Options.DDamagesNoiseStdev,
                NumSignals = Options.NumDamagesSignals,
                SourcePointsIncludeExtremes = false
            };
        }

        private void SetupLiabilitySignals()
        {
            Options.PLiabilitySignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumLiabilityStrengthPoints,
                StdevOfNormalDistribution = Options.PLiabilityNoiseStdev,
                NumSignals = Options.NumLiabilitySignals,
                SourcePointsIncludeExtremes = false
            };
            Options.DLiabilitySignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumLiabilityStrengthPoints,
                StdevOfNormalDistribution = Options.DLiabilityNoiseStdev,
                NumSignals = Options.NumLiabilitySignals
            };
        }

        private LitigGameProgress MyGP(GameProgress gp) => gp as LitigGameProgress;

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
                    new PlayerInfo(PlaintiffName, (int) LitigGamePlayers.Plaintiff, false, true),
                    new PlayerInfo(DefendantName, (int) LitigGamePlayers.Defendant, false, true),
                    new PlayerInfo(ResolutionPlayerName, (int) LitigGamePlayers.Resolution, true, false),
                    new PlayerInfo(PrePrimaryChanceName, (int) LitigGamePlayers.PrePrimaryChance, true, false),
                    new PlayerInfo(PostPrimaryChanceName, (int) LitigGamePlayers.PrePrimaryChance, true, false),
                    new PlayerInfo(LiabilityStrengthChanceName, (int) LitigGamePlayers.LiabilityStrengthChance, true, false),
                    new PlayerInfo(PLiabilityNoiseChanceName, (int) LitigGamePlayers.PLiabilitySignalChance, true, false),
                    new PlayerInfo(DLiabilityNoiseChanceName, (int) LitigGamePlayers.DLiabilitySignalChance, true, false),
                    new PlayerInfo(DamagesStrengthChanceName, (int) LitigGamePlayers.DamagesStrengthChance, true, false),
                    new PlayerInfo(PDamagesNoiseChanceName, (int) LitigGamePlayers.PDamagesSignalChance, true, false),
                    new PlayerInfo(DDamagesNoiseChanceName, (int) LitigGamePlayers.DDamagesSignalChance, true, false),
                    new PlayerInfo(BothGiveUpChanceName, (int) LitigGamePlayers.BothGiveUpChance, true, false),
                    new PlayerInfo(PreBargainingRoundChanceName, (int) LitigGamePlayers.PreBargainingRoundChance, true, false),
                    new PlayerInfo(PostBargainingRoundChanceName, (int) LitigGamePlayers.PostBargainingRoundChance, true, false),
                    new PlayerInfo(CourtLiabilityChanceName, (int) LitigGamePlayers.CourtLiabilityChance, true, false),
                    new PlayerInfo(CourtDamagesChanceName, (int) LitigGamePlayers.CourtDamagesChance, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte) LitigGamePlayers.Resolution;

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
                if (Options.AllowAbandonAndDefaults)
                    AddAbandonOrDefaultDecisions(b, decisions, true);
                AddDecisionsForBargainingRound(b, decisions);
                if (Options.AllowAbandonAndDefaults)
                {
                    if (Options.LitigGameRunningSideBets != null)
                        AddRunningSideBetDecisions(b, decisions);
                    AddAbandonOrDefaultDecisions(b, decisions, false);
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
                (byte) LitigGamePlayers.PLiabilitySignalChance,
                (byte) LitigGamePlayers.DLiabilitySignalChance,
                (byte) LitigGamePlayers.CourtLiabilityChance,
                (byte) LitigGamePlayers.Resolution
            };
            if (Options.PLiabilityNoiseStdev == 0)
                playersKnowingLiabilityStrength.Add((byte)LitigGamePlayers.Plaintiff);
            if (Options.DLiabilityNoiseStdev == 0)
                playersKnowingLiabilityStrength.Add((byte)LitigGamePlayers.Defendant);
            List<byte> playersKnowingDamagesStrength = new List<byte>()
            {
                (byte) LitigGamePlayers.PDamagesSignalChance,
                (byte) LitigGamePlayers.DDamagesSignalChance,
                (byte) LitigGamePlayers.CourtDamagesChance,
                (byte) LitigGamePlayers.Resolution
            };
            if (Options.PDamagesNoiseStdev == 0)
                playersKnowingDamagesStrength.Add((byte)LitigGamePlayers.Plaintiff);
            if (Options.DDamagesNoiseStdev == 0)
                playersKnowingDamagesStrength.Add((byte)LitigGamePlayers.Defendant);
            ILitigGameDisputeGenerator disputeGenerator = Options.LitigGameDisputeGenerator;
            disputeGenerator.GetActionsSetup(this, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate);
            CheckCompleteAfterPrimaryAction = primaryActionCanTerminate;
            CheckCompleteAfterPostPrimaryAction = postPrimaryChanceCanTerminate;
            if (!Options.InvertChanceDecisions)
            {
                if (prePrimaryChanceActions > 0)
                {
                    decisions.Add(new Decision("PrePrimaryChanceActions", "PrePrimary", true, (byte)LitigGamePlayers.PrePrimaryChance, prePrimaryPlayersToInform, prePrimaryChanceActions, (byte)LitigGameDecisions.PrePrimaryActionChance) { StoreActionInGameCacheItem = GameHistoryCacheIndex_PrePrimaryChance, IsReversible = true, UnevenChanceActions = prePrimaryUnevenChance, Unroll_Parallelize = disputeGenerator.GetPrePrimaryUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetPrePrimaryUnrollSettings().unrollIdentical, DistributedChanceDecision = true, SymmetryMap = (disputeGenerator.GetPrePrimaryUnrollSettings().symmetryMapInput, SymmetryMapOutput.ChanceDecision) });
                }
                if (primaryActions > 0)
                {
                    decisions.Add(new Decision("PrimaryActions", "Primary", true, (byte)LitigGamePlayers.PrePrimaryChance /* there is no primary chance player */, primaryPlayersToInform, primaryActions, (byte)LitigGameDecisions.PrimaryAction) { StoreActionInGameCacheItem = GameHistoryCacheIndex_PrimaryAction, IsReversible = true, CanTerminateGame = primaryActionCanTerminate, Unroll_Parallelize = disputeGenerator.GetPrimaryUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetPrimaryUnrollSettings().unrollIdentical, DistributedChanceDecision = true, SymmetryMap = (disputeGenerator.GetPrimaryUnrollSettings().symmetryMapInput, SymmetryMapOutput.ChanceDecision) });
                }
                if (postPrimaryChanceActions > 0)
                {
                    decisions.Add(new Decision("PostPrimaryChanceActions", "PostPrimary", true, (byte)LitigGamePlayers.PostPrimaryChance, postPrimaryPlayersToInform, postPrimaryChanceActions, (byte)LitigGameDecisions.PostPrimaryActionChance) { StoreActionInGameCacheItem = GameHistoryCacheIndex_PostPrimaryChance, IsReversible = true, UnevenChanceActions = postPrimaryUnevenChance, CanTerminateGame = postPrimaryChanceCanTerminate, Unroll_Parallelize = disputeGenerator.GetPostPrimaryUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetPostPrimaryUnrollSettings().unrollIdentical, DistributedChanceDecision = true, SymmetryMap = (disputeGenerator.GetPostPrimaryUnrollSettings().symmetryMapInput, SymmetryMapOutput.ChanceDecision) });
                }
                decisions.Add(new Decision("LiabilityStrength", "LiabStr", true, (byte)LitigGamePlayers.LiabilityStrengthChance,
                        playersKnowingLiabilityStrength.ToArray(), Options.NumLiabilityStrengthPoints, (byte)LitigGameDecisions.LiabilityStrength)
                { StoreActionInGameCacheItem = GameHistoryCacheIndex_LiabilityStrength, IsReversible = true, UnevenChanceActions = litigationQualityUnevenChance, Unroll_Parallelize = disputeGenerator.GetLiabilityStrengthUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetLiabilityStrengthUnrollSettings().unrollIdentical, DistributedChanceDecision = true, SymmetryMap = (disputeGenerator.GetLiabilityStrengthUnrollSettings().symmetryMapInput, SymmetryMapOutput.ChanceDecision) });
                if (Options.NumDamagesStrengthPoints > 1)
                    decisions.Add(new Decision("DamagesStrength", "DamStr", true, (byte)LitigGamePlayers.DamagesStrengthChance,
                        playersKnowingDamagesStrength.ToArray(), Options.NumDamagesStrengthPoints, (byte)LitigGameDecisions.DamagesStrength)
                    { StoreActionInGameCacheItem = GameHistoryCacheIndex_DamagesStrength, IsReversible = true, UnevenChanceActions = litigationQualityUnevenChance, Unroll_Parallelize = disputeGenerator.GetDamagesStrengthUnrollSettings().unrollParallelize, Unroll_Parallelize_Identical = disputeGenerator.GetDamagesStrengthUnrollSettings().unrollIdentical, DistributedChanceDecision = true, SymmetryMap = (disputeGenerator.GetDamagesStrengthUnrollSettings().symmetryMapInput, SymmetryMapOutput.ChanceDecision) });
            }
        }

        private void AddLiabilitySignalsDecisions(List<Decision> decisions)
        {
            // Plaintiff and defendant signals. If a player has perfect information, then no signal is needed.
            if (Options.PLiabilityNoiseStdev != 0)
                decisions.Add(new Decision("PlaintiffLiabilitySignal", "PLS", true, (byte)LitigGamePlayers.PLiabilitySignalChance,
                    Options.InvertChanceDecisions ? new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte) LitigGamePlayers.DLiabilitySignalChance, (byte) LitigGamePlayers.CourtLiabilityChance } : new byte[] {(byte) LitigGamePlayers.Plaintiff},
                    Options.NumLiabilitySignals, (byte)LitigGameDecisions.PLiabilitySignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true,
                    ProvidesPrivateInformationFor = (byte)LitigGamePlayers.Plaintiff,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
                });
            if (Options.DLiabilityNoiseStdev != 0)
                decisions.Add(new Decision("DefendantLiabilitySignal", "DLS", true, (byte)LitigGamePlayers.DLiabilitySignalChance,
                    Options.InvertChanceDecisions ? new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.CourtLiabilityChance } : new byte[] { (byte)LitigGamePlayers.Defendant },
                    Options.NumLiabilitySignals, (byte)LitigGameDecisions.DLiabilitySignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true,
                    ProvidesPrivateInformationFor = (byte) LitigGamePlayers.Defendant,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
                });
            CreateLiabilitySignalsTables();
        }
        
        private double[][] PLiabilitySignalsTable, DLiabilitySignalsTable, CLiabilitySignalsTable, PDamagesSignalsTable, DDamagesSignalsTable, CDamagesSignalsTable;
        public void CreateLiabilitySignalsTables()
        {
            PLiabilitySignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumLiabilityStrengthPoints, Options.NumLiabilitySignals });
            DLiabilitySignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumLiabilityStrengthPoints, Options.NumLiabilitySignals });
            CLiabilitySignalsTable = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { Options.NumLiabilityStrengthPoints, Options.NumCourtLiabilitySignals }); 
            for (byte litigationQuality = 1;
                litigationQuality <= Options.NumLiabilityStrengthPoints;
                litigationQuality++)
            {
                double litigationQualityUniform =
                    EquallySpaced.GetLocationOfEquallySpacedPoint(litigationQuality - 1,
                        Options.NumLiabilityStrengthPoints, false);

                PLiabilitySignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, Options.PLiabilitySignalParameters);
                DLiabilitySignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, Options.DLiabilitySignalParameters);
                int numCourtSignals = Options.NumCourtLiabilitySignals;
                DiscreteValueSignalParameters cParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumLiabilityStrengthPoints, NumSignals = Options.NumCourtLiabilitySignals, StdevOfNormalDistribution = Options.CourtLiabilityNoiseStdev, SourcePointsIncludeExtremes = false };
                CLiabilitySignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, cParams);
            }
        }

        private void AddDamagesSignalsDecisions(List<Decision> decisions)
        {
            if (Options.NumDamagesStrengthPoints <= 1)
                return;

            // Plaintiff and defendant signals. If a player has perfect information, then no signal is needed.
            if (Options.PDamagesNoiseStdev != 0)
                decisions.Add(new Decision("PlaintiffDamagesSignal", "PDS", true, (byte)LitigGamePlayers.PDamagesSignalChance,
                    Options.InvertChanceDecisions ? new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.DDamagesSignalChance, (byte)LitigGamePlayers.CourtDamagesChance} : new byte[] { (byte)LitigGamePlayers.Plaintiff },
                    Options.NumDamagesSignals, (byte)LitigGameDecisions.PDamagesSignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true,
                    ProvidesPrivateInformationFor = (byte)LitigGamePlayers.Plaintiff,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
                });
            if (Options.DDamagesNoiseStdev != 0)
                decisions.Add(new Decision("DefendantDamagesSignal", "DDS", true, (byte)LitigGamePlayers.DDamagesSignalChance,
                    Options.InvertChanceDecisions ? new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.CourtDamagesChance } : new byte[] { (byte)LitigGamePlayers.Defendant },
                    Options.NumDamagesSignals, (byte)LitigGameDecisions.DDamagesSignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true,
                    ProvidesPrivateInformationFor = (byte)LitigGamePlayers.Defendant,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
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

                PDamagesSignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, Options.PDamagesSignalParameters);
                DDamagesSignalsTable[litigationQuality - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(litigationQuality, Options.DDamagesSignalParameters);
                DiscreteValueSignalParameters cParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = Options.NumDamagesStrengthPoints, NumSignals = Options.NumDamagesSignals, StdevOfNormalDistribution = Options.CourtDamagesNoiseStdev, SourcePointsIncludeExtremes = false }; // TODO: Differentiate number of court damages signals, since we might want that to be a higher number.
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
                new Decision("PFile", "PF", false, (byte)LitigGamePlayers.Plaintiff, new byte[]  { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
                    2, (byte)LitigGameDecisions.PFile)
                { // TODO: Maybe can eliminate notice to plaintiff and defendant here and below
                    CanTerminateGame = true, // not filing always terminates
                    IsReversible = true,
                    SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction)
                };
            decisions.Add(pFile);

            var dAnswer =
                new Decision("DAnswer", "DA", false, (byte)LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
                    2, (byte)LitigGameDecisions.DAnswer)
                {
                    CanTerminateGame = true, // not answering terminates, with defendant paying full damages
                    IsReversible = true,
                    SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction)
                };
            decisions.Add(dAnswer);
        }

        private void AddDecisionsForBargainingRound(int b, List<Decision> decisions)
        {
            // Agreement to bargain: We do want to add this to the information set of the opposing player, since that may be relevant in future rounds and also might affect decisions whether to abandon/default later in this round, but we want to defer addition of the plaintiff statement, so that it doesn't influence the defendant decision, since the players are supposedly making the decisions at the same time.

            if (Options.IncludeAgreementToBargainDecisions)
            {
                var pAgreeToBargain = new Decision("PAgreeToBargain" + (b + 1), "PB" + (b + 1), false, (byte) LitigGamePlayers.Plaintiff, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
                    2, (byte) LitigGameDecisions.PAgreeToBargain)
                {
                    CustomByte = (byte) (b + 1),
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_PAgreesToBargain,
                    DeferNotificationOfPlayers = true,
                    WarmStartThroughIteration = Options.WarmStartThroughIteration,
                    WarmStartValue = 1,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.SameAction)
                };
                decisions.Add(pAgreeToBargain);

                var dAgreeToBargain = new Decision("DAgreeToBargain" + (b + 1), "DB" + (b + 1), false, (byte) LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
                    2, (byte) LitigGameDecisions.DAgreeToBargain)
                {
                    CustomByte = (byte) (b + 1),
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_DAgreesToBargain,
                    WarmStartThroughIteration = Options.WarmStartThroughIteration,
                    WarmStartValue = 1,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.SameAction)
                };
                decisions.Add(dAgreeToBargain);
            }

            // note that we will do all information set manipulation in CustomInformationSetManipulation below.
            if (Options.BargainingRoundsSimultaneous)
            {
                byte[] informedOfPOffer, informedOfDOffer;
                if (Options.SimultaneousOffersUltimatelyRevealed)
                {
                    informedOfPOffer = informedOfDOffer = new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution };
                }
                else
                {
                    informedOfPOffer = new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Resolution };
                    informedOfDOffer = new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution };
                }
                // samuelson-chaterjee bargaining.
                // TODO: Try Adding IsReversible to all decisions
                var pOffer =
                    new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), false, (byte)LitigGamePlayers.Plaintiff, informedOfPOffer,
                        Options.NumOffers, (byte)LitigGameDecisions.POffer)
                    {
                        CustomByte = (byte)(b + 1),
                        DeferNotificationOfPlayers = true, // wait until after defendant has gone for defendant to find out -- of course, we don't do that with defendant decision
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_POffer,
                        IsContinuousAction = true,
                        WarmStartThroughIteration = Options.WarmStartThroughIteration,
                        WarmStartValue = (byte)(Options.WarmStartOptions switch
                        {
                            LitigGameWarmStartOptions.DiscourageSettlementByMakingOpponentGenerous => 1,
                            LitigGameWarmStartOptions.FacilitateSettlementByMakingOpponentStingy => Options.NumOffers,
                            _ => 0,
                        }),
                        SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ReverseAction)
                    };
                AddOfferDecision(decisions, pOffer);
                var dOffer =
                    new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), false, (byte)LitigGamePlayers.Defendant, informedOfDOffer,
                        Options.NumOffers, (byte)LitigGameDecisions.DOffer)
                    {
                        CanTerminateGame = true,
                        CustomByte = (byte)(b + 1),
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_DOffer,
                        IsContinuousAction = true,
                        WarmStartThroughIteration = Options.WarmStartThroughIteration,
                        WarmStartValue = (byte)(Options.WarmStartOptions switch
                        {
                            LitigGameWarmStartOptions.DiscourageSettlementByMakingOpponentGenerous => Options.NumOffers,
                            LitigGameWarmStartOptions.FacilitateSettlementByMakingOpponentStingy => 1,
                            _ => 0,
                        }),
                        SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ReverseAction)
                    };
                AddOfferDecision(decisions, dOffer);
            }
            else
            {
                // offer-response bargaining. We add the offer and response to the players' information sets. 
                if (Options.PGoesFirstIfNotSimultaneous[b])
                {
                    var pOffer =
                        new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), false, (byte)LitigGamePlayers.Plaintiff, new byte[] { (byte) LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
                            Options.NumOffers, (byte)LitigGameDecisions.POffer)
                        {
                            CustomByte = (byte)(b + 1),
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_POffer,
                            IsContinuousAction = true,
                            SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction) // NOTE: We could make this compatible with symmetry by having an earlier chance decision that randomly chooses who goes first
                        }; // { AlwaysDoAction = 4});
                    AddOfferDecision(decisions, pOffer);
                    decisions.Add(
                        new Decision("DefendantResponse" + (b + 1), "DR" + (b + 1), false, (byte)LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution }, 2,
                            (byte)LitigGameDecisions.DResponse)
                        {
                            CanTerminateGame = true,
                            CustomByte = (byte)(b + 1),
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_DResponse,
                            WarmStartThroughIteration = Options.WarmStartThroughIteration,
                            WarmStartValue = (byte)(Options.WarmStartOptions switch
                            {
                                LitigGameWarmStartOptions.DiscourageSettlementByMakingOpponentGenerous => 1,
                                LitigGameWarmStartOptions.FacilitateSettlementByMakingOpponentStingy => 2,
                                _ => 0,
                            }),
                            SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction)
                        });
                }
                else
                {
                    var dOffer =
                        new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), false, (byte)LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
                            Options.NumOffers, (byte)LitigGameDecisions.DOffer)
                        {
                            CustomByte = (byte)(b + 1),
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_DOffer,
                            IsContinuousAction = true,
                            WarmStartThroughIteration = Options.WarmStartThroughIteration,
                            WarmStartValue = (byte)(Options.WarmStartOptions switch
                            {
                                LitigGameWarmStartOptions.DiscourageSettlementByMakingOpponentGenerous => Options.NumOffers,
                                LitigGameWarmStartOptions.FacilitateSettlementByMakingOpponentStingy => 1,
                                _ => 0,
                            }),
                            SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction)
                        };
                    AddOfferDecision(decisions, dOffer);
                    decisions.Add(
                        new Decision("PlaintiffResponse" + (b + 1), "PR" + (b + 1), false, (byte)LitigGamePlayers.Plaintiff, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution }, 2,
                            (byte)LitigGameDecisions.PResponse)
                        {
                            CanTerminateGame = true,
                            CustomByte = (byte)(b + 1),
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_PResponse,
                            WarmStartThroughIteration = Options.WarmStartThroughIteration,
                            WarmStartValue = (byte)(Options.WarmStartOptions switch
                            {
                                LitigGameWarmStartOptions.DiscourageSettlementByMakingOpponentGenerous => 1,
                                LitigGameWarmStartOptions.FacilitateSettlementByMakingOpponentStingy => 2,
                                _ => 0,
                            }),
                            SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction)
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
                new Decision("pRSideBet" + (b + 1), "PRSB" + (b + 1), false, (byte)LitigGamePlayers.Plaintiff, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
                    (byte) (Options.LitigGameRunningSideBets.MaxChipsPerRound + 1), (byte)LitigGameDecisions.PChips)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false,
                    DeferNotificationOfPlayers = true,
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_PChipsAction,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.SameAction)
                };
            decisions.Add(pRSideBet);
            var dRSideBet =
                new Decision("dRSideBet" + (b + 1), "DRSB" + (b + 1), false, (byte)LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
                    (byte)(Options.LitigGameRunningSideBets.MaxChipsPerRound + 1), (byte)LitigGameDecisions.DChips)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false,
                    StoreActionInGameCacheItem = GameHistoryCacheIndex_DChipsAction,
                    IsReversible = true,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.SameAction)
                };
            decisions.Add(dRSideBet);
        }

        private void AddAbandonOrDefaultDecisions(int b, List<Decision> decisions, bool predetermining)
        {
            bool includePlayerDecisions;
            bool includeChanceDecision;
            if (Options.PredeterminedAbandonAndDefaults)
            {
                includePlayerDecisions = predetermining;
                includeChanceDecision = !predetermining;
            }
            else
            {
                includePlayerDecisions = includeChanceDecision = !predetermining;
            }
            if (includePlayerDecisions)
            {
                var pAbandon =
                    new Decision("PAbandon" + (b + 1), "PAB" + (b + 1), false, (byte)LitigGamePlayers.Plaintiff, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.BothGiveUpChance, (byte)LitigGamePlayers.Resolution },
                        2, (byte)LitigGameDecisions.PAbandon)
                    {
                        CustomByte = (byte)(b + 1),
                        CanTerminateGame = false, // we always must look at whether D is defaulting too. 
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_PReadyToAbandon,
                        IsReversible = true,
                        SymmetryMap = (SymmetryMapInput.SameInfo, SymmetryMapOutput.SameAction)
                    };
                decisions.Add(pAbandon);

                var dDefault =
                    new Decision("DDefault" + (b + 1), "DD" + (b + 1), false, (byte)LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.BothGiveUpChance, (byte)LitigGamePlayers.Resolution },
                        2, (byte)LitigGameDecisions.DDefault)
                    {
                        CustomByte = (byte)(b + 1),
                        CanTerminateGame = !Options.PredeterminedAbandonAndDefaults, // if either but not both has given up, game terminates, unless we're predetermining abandon and defaults, in which case we still have to go through offers
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_DReadyToAbandon,
                        IsReversible = true,
                        SymmetryMap = (SymmetryMapInput.SameInfo, SymmetryMapOutput.SameAction)
                    };
                decisions.Add(dDefault);
            }
            if (includeChanceDecision)
            {
                var bothGiveUp =
                    new Decision("MutualGiveUp" + (b + 1), "MGU" + (b + 1), true, (byte)LitigGamePlayers.BothGiveUpChance, new byte[] { (byte)LitigGamePlayers.Resolution },
                        2, (byte)LitigGameDecisions.MutualGiveUp, unevenChanceActions: false)
                    {
                        CustomByte = (byte)(b + 1),
                        CanTerminateGame = true, // if this decision is needed, then both have given up, and the decision always terminates the game
                        CriticalNode = true, // always play out both sides of this coin flip
                        IsReversible = true,
                        SymmetryMap = (SymmetryMapInput.NotInInformationSet, SymmetryMapOutput.ChanceDecision)
                    };
                decisions.Add(bothGiveUp);
            }
        }

        private void AddPreBargainingRoundDummyDecision(int b, List<Decision> decisions)
        {
            var dummyDecision =
                new Decision("PreBargainingRound" + (b + 1), "PRBR" + (b + 1), true, (byte)LitigGamePlayers.PreBargainingRoundChance, null,
                    1 /* i.e., just an opportunity to do some calculation and cleanup */, (byte)LitigGameDecisions.PreBargainingRound, unevenChanceActions: false)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false, 
                    CriticalNode = false, // doesn't matter -- just one possibility,
                    SymmetryMap = (SymmetryMapInput.NotInInformationSet, SymmetryMapOutput.ChanceDecision),
                    IsReversible = true
                };
            decisions.Add(dummyDecision);
        }

        private void AddPostBargainingRoundDummyDecision(int b, List<Decision> decisions)
        {
            var dummyDecision =
                new Decision("PostBargainingRound" + (b + 1), "POBR" + (b + 1), true, (byte)LitigGamePlayers.PostBargainingRoundChance, null,
                    1 /* i.e., just an opportunity to do some calculation and cleanup */, (byte)LitigGameDecisions.PostBargainingRound, unevenChanceActions: false)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false,
                    CriticalNode = false, // doesn't matter -- just one possibility
                    IsReversible = true,
                    SymmetryMap = (SymmetryMapInput.NotInInformationSet, SymmetryMapOutput.ChanceDecision)
                };
            decisions.Add(dummyDecision);
        }

        private void AddPreTrialDecisions(List<Decision> decisions)
        {
            if (Options.LitigGamePretrialDecisionGeneratorGenerator != null)
            {
                Options.LitigGamePretrialDecisionGeneratorGenerator.GetActionsSetup(this, out byte pActions, out byte dActions, out byte[] playersToInformOfPAction, out byte[] playersToInformOfDAction);
                if (pActions > 0)
                {
                    decisions.Add(new Decision("PPreTrial", "PPT", false, (byte) LitigGamePlayers.Plaintiff, playersToInformOfPAction, pActions, (byte) LitigGameDecisions.PPretrialAction) { IsReversible = true,
                        SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction)
                    });
                }
                if (dActions > 0)
                {
                    decisions.Add(new Decision("DPreTrial", "DPT", false, (byte)LitigGamePlayers.Defendant, playersToInformOfDAction, dActions, (byte)LitigGameDecisions.DPretrialAction) { IsReversible = true,
                        SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction)
                    });
                }
            }
        }

        private void AddCourtDecisions(List<Decision> decisions)
        {
            bool courtDecidesDamages = Options.NumDamagesStrengthPoints > 1;
            decisions.Add(new Decision("CourtLiabilityDecision", "CL", true, (byte)LitigGamePlayers.CourtLiabilityChance,
                    new byte[] { (byte)LitigGamePlayers.Resolution }, (byte) Options.NumCourtLiabilitySignals, (byte)LitigGameDecisions.CourtDecisionLiability,
                    unevenChanceActions: true, criticalNode: true)
                { CanTerminateGame = true, AlwaysTerminatesGame = !courtDecidesDamages, IsReversible = true, DistributorChanceDecision = true, CanCalculateDistributorChanceDecisionProbabilitiesFromInformationSet = true, StoreActionInGameCacheItem = GameHistoryCacheIndex_PWins,
                SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
            }); // even chance options
            if (courtDecidesDamages)
                decisions.Add(new Decision("CourtDamagesDecision", "CD", true, (byte)LitigGamePlayers.CourtDamagesChance,
                    new byte[] { (byte)LitigGamePlayers.Resolution }, Options.NumDamagesSignals, (byte)LitigGameDecisions.CourtDecisionDamages,
                    unevenChanceActions: true, criticalNode: true)
                { CanTerminateGame = true, AlwaysTerminatesGame = true, IsReversible = true, DistributorChanceDecision = true, CanCalculateDistributorChanceDecisionProbabilitiesFromInformationSet = true,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
                }); // even chance options
        }

        #endregion

        #region Game play support 

        public override double[] GetUnevenChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            if (decisionByteCode == (byte)LitigGameDecisions.PrePrimaryActionChance)
            {
                var myGameProgress = ((LitigGameProgress)gameProgress);
                var probabilities = Options.LitigGameDisputeGenerator.GetPrePrimaryChanceProbabilities(this);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else if (decisionByteCode == (byte)LitigGameDecisions.PostPrimaryActionChance)
            {
                var myGameProgress = ((LitigGameProgress)gameProgress);
                var probabilities = Options.LitigGameDisputeGenerator.GetPostPrimaryChanceProbabilities(this, myGameProgress.DisputeGeneratorActions);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else if (decisionByteCode == (byte) LitigGameDecisions.LiabilityStrength)
            {
                var myGameProgress = ((LitigGameProgress) gameProgress);
                double[] probabilities;
                if (Options.InvertChanceDecisions)
                    throw new Exception("Should not be called"); // Note: Could update this to call the appropriate dispute generator method, so long as this is called after everything else
                else
                    probabilities = Options.LitigGameDisputeGenerator.GetLiabilityStrengthProbabilities(this, myGameProgress.DisputeGeneratorActions);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else if (decisionByteCode == (byte)LitigGameDecisions.PLiabilitySignal)
            {
                var myGameProgress = ((LitigGameProgress)gameProgress);
                double[] probabilities;
                if (Options.InvertChanceDecisions)
                    probabilities = Options.LitigGameDisputeGenerator.InvertedCalculations_GetPLiabilitySignalProbabilities();
                else
                    probabilities = GetPLiabilitySignalProbabilities(myGameProgress.LiabilityStrengthDiscrete);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else if (decisionByteCode == (byte)LitigGameDecisions.DLiabilitySignal)
            {
                var myGameProgress = ((LitigGameProgress)gameProgress);
                double[] probabilities;
                if (Options.InvertChanceDecisions)
                    probabilities = Options.LitigGameDisputeGenerator.InvertedCalculations_GetDLiabilitySignalProbabilities(myGameProgress.PLiabilitySignalDiscrete);
                else
                    probabilities = GetDLiabilitySignalProbabilities(myGameProgress.LiabilityStrengthDiscrete);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else if (decisionByteCode == (byte)LitigGameDecisions.DamagesStrength)
            {
                var myGameProgress = ((LitigGameProgress)gameProgress);
                double[] probabilities;
                if (Options.InvertChanceDecisions)
                    throw new Exception("Should not be called"); // Note: Could update this to call the appropriate dispute generator method, so long as this is called after everything else
                else
                    probabilities = Options.LitigGameDisputeGenerator.GetDamagesStrengthProbabilities(this, myGameProgress.DisputeGeneratorActions);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else if (decisionByteCode == (byte)LitigGameDecisions.PDamagesSignal)
            {
                var myGameProgress = ((LitigGameProgress)gameProgress);
                double[] probabilities;
                if (Options.InvertChanceDecisions)
                    probabilities = Options.LitigGameDisputeGenerator.InvertedCalculations_GetPDamagesSignalProbabilities();
                else
                    probabilities = GetPDamagesSignalProbabilities(myGameProgress.DamagesStrengthDiscrete);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else if (decisionByteCode == (byte)LitigGameDecisions.DDamagesSignal)
            {
                var myGameProgress = ((LitigGameProgress)gameProgress);
                double[] probabilities;
                if (Options.InvertChanceDecisions)
                    probabilities = Options.LitigGameDisputeGenerator.InvertedCalculations_GetDDamagesSignalProbabilities(myGameProgress.PDamagesSignalDiscrete);
                else
                    probabilities = GetDDamagesSignalProbabilities(myGameProgress.DamagesStrengthDiscrete);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else if (decisionByteCode == (byte)LitigGameDecisions.CourtDecisionLiability)
            {
                var myGameProgress = ((LitigGameProgress)gameProgress);
                double[] probabilities;
                if (Options.InvertChanceDecisions)
                    probabilities = Options.LitigGameDisputeGenerator.InvertedCalculations_GetCLiabilitySignalProbabilities(myGameProgress.PLiabilitySignalDiscrete, myGameProgress.DLiabilitySignalDiscrete);
                else
                    probabilities = GetCLiabilitySignalProbabilities(myGameProgress.LiabilityStrengthDiscrete);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else if (decisionByteCode == (byte)LitigGameDecisions.CourtDecisionDamages)
            {
                var myGameProgress = ((LitigGameProgress)gameProgress);
                double[] probabilities;
                if (Options.InvertChanceDecisions)
                    probabilities = Options.LitigGameDisputeGenerator.InvertedCalculations_GetCDamagesSignalProbabilities(myGameProgress.PDamagesSignalDiscrete, myGameProgress.DDamagesSignalDiscrete);
                else
                    probabilities = GetCDamagesSignalProbabilities(myGameProgress.DamagesStrengthDiscrete);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else
                throw new NotImplementedException(); // subclass should define if needed
        }

        public override bool SkipDecision(Decision decision, in GameHistory gameHistory)
        {
            byte decisionByteCode = decision.DecisionByteCode;
            if (decisionByteCode == (byte) LitigGameDecisions.MutualGiveUp)
            {
                bool pTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PReadyToAbandon) == 1;
                bool dTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DReadyToAbandon) == 1;
                bool skip = !pTryingToGiveUp || !dTryingToGiveUp; // if anyone is NOT trying to give up, we don't have to deal with mutual giving up
                return skip;
            }
            else if (decisionByteCode >= (byte) LitigGameDecisions.POffer && decisionByteCode <= (byte) LitigGameDecisions.DResponse)
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
                case (byte)LitigGameDecisions.CourtDecisionLiability:
                    bool pWins = Options.NumLiabilitySignals == 1 || gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PWins) == 2; // 2 means P Wins, since this is action taken at this decision
                    if (pWins)
                    {
                        bool courtWouldDecideDamages = Options.NumDamagesStrengthPoints > 1;
                        return !courtWouldDecideDamages;
                    }
                    else
                        return true; // defendant wins --> always over
                case (byte)LitigGameDecisions.CourtDecisionDamages:
                    return true;
                case (byte)LitigGameDecisions.DResponse:
                    bool dAccepts = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DResponse) == 1;
                    if (dAccepts)
                        return true;
                    break;
                case (byte)LitigGameDecisions.PResponse:
                    bool pAccepts = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PResponse) == 1;
                    if (pAccepts)
                        return true;
                    break;
                case (byte)LitigGameDecisions.DOffer:
                    // this is simultaneous bargaining (plaintiff offer is always first). 
                    if (!Options.BargainingRoundsSimultaneous)
                        throw new Exception("Internal error.");
                    byte plaintiffOffer = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_POffer);
                    byte defendantOffer = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DOffer);
                    if (defendantOffer >= plaintiffOffer)
                        return true;
                    if (Options.AllowAbandonAndDefaults && Options.PredeterminedAbandonAndDefaults)
                    {
                        bool pTryingToGiveUp2 = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PReadyToAbandon) == 1;
                        bool dTryingToGiveUp2 = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DReadyToAbandon) == 1;
                        if (pTryingToGiveUp2 ^ dTryingToGiveUp2)
                            return true; // exactly one trying to give up in last bargaining round
                    }
                    break;
                case (byte)LitigGameDecisions.PrimaryAction:
                    return Options.LitigGameDisputeGenerator.MarkComplete(this, gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrePrimaryChance), gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrimaryAction));
                case (byte)LitigGameDecisions.PostPrimaryActionChance:
                    return Options.LitigGameDisputeGenerator.MarkComplete(this, gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrePrimaryChance), gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrimaryAction), gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PostPrimaryChance));
                case (byte)LitigGameDecisions.PFile:
                    if (actionChosen == 2)
                        return true; // plaintiff hasn't filed
                    break;
                case (byte)LitigGameDecisions.DAnswer:
                    if (actionChosen == 2)
                        return true; // defendant's hasn't answered
                    break;
                case (byte)LitigGameDecisions.DDefault:
                    if (Options.PredeterminedAbandonAndDefaults)
                        return false; // we need to wait for the bargaining round
                    bool pTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PReadyToAbandon) == 1;
                    bool dTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DReadyToAbandon) == 1;
                    if (pTryingToGiveUp ^ dTryingToGiveUp) // i.e., one but not both parties try to default
                        return true;
                    break;
                case (byte)LitigGameDecisions.MutualGiveUp:
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

        public override int NumPostWarmupPossibilities => 11; 
        public override int NumWarmupPossibilities => 4; // Note that this can be 0 (indicating that costs multiplier doesn't change). This indicates the variations on the costs multiplier; variations on weight to opponent are below. 
        public override int WarmupIterations_IfWarmingUp => 10; 
        public override bool MultiplyWarmupScenariosByAlteringWeightOnOpponentsStrategy => true;
        public override int NumDifferentWeightsOnOpponentsStrategyPerWarmupScenario_IfMultiplyingScenarios => 3; // should be odd if we want to include zero
        public override bool VaryWeightOnOpponentsStrategySeparatelyForEachPlayer => false; // NOTE: If this is true, this multiplies number of scenarios by NumDifferentWeightsOnOpponentsStrategyPerWarmupScenario_IfMultiplyingScenarios^2
        public override (double, double) MinMaxWeightOnOpponentsStrategyDuringWarmup => (-0.8, 0.8); // NOTE: Don't go all the way up to 1, because then if costs multiplier is 0 (i.e., it is a zero-sum game), utility for a player will be invariant.

        public enum ChangeInScenario
        {
            TrialCosts,
            CostsMultiplier
        }

        // note that we do not integrate the warmup and postwarmup at any one time. So, if a different variable is changing warmup and postwarmup, then the warmup phase will reflect the default value for the post warmup variable, not the eventual value.
        ChangeInScenario? WhatToChange_Warmup = ChangeInScenario.CostsMultiplier;
        ChangeInScenario? WhatToChange_PostWarmup = ChangeInScenario.CostsMultiplier; 

        public double TrialCostsScenarioPerPartyMin = 0; 
        public double TrialCostsScenarioPerPartyMax = 30_000; 
        public double TrialCostsScenarioPerPartyMin_Warmup = 0;
        public double TrialCostsScenarioPerPartyMax_Warmup = 30_000;
        bool changeTrialCostsForPlaintiff = true;
        bool changeTrialCostsForDefendant = true;

        public double CostsMultiplierMin = 0.0;
        public double CostsMultiplierMax = 1.0;
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
            if (NumPostWarmupPossibilities > 1 && WhatToChange_PostWarmup == null)
                throw new Exception("WhatToChange_PostWarmup is undefined");
            if (NumWarmupPossibilities > 0 && WhatToChange_Warmup == null)
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
                        Options.PTrialCosts = GetParameterInRange(TrialCostsScenarioPerPartyMin, TrialCostsScenarioPerPartyMax, (int) postWarmupScenarioIndex, NumPostWarmupPossibilities);
                    if (changeTrialCostsForDefendant)
                        Options.DTrialCosts = GetParameterInRange(TrialCostsScenarioPerPartyMin, TrialCostsScenarioPerPartyMax, (int) postWarmupScenarioIndex, NumPostWarmupPossibilities);
                }
                else if (WhatToChange_PostWarmup == ChangeInScenario.CostsMultiplier)
                {
                    Options.CostsMultiplier = GetParameterInRange(CostsMultiplierMin, CostsMultiplierMax, (int) postWarmupScenarioIndex, NumPostWarmupPossibilities);
                }
            }
            if (NumWarmupPossibilities > 0)
            {
                if (warmupScenarioIndex != null)
                {
                    if (WhatToChange_Warmup == ChangeInScenario.TrialCosts)
                    {
                        if (changeTrialCostsForPlaintiff)
                            Options.PTrialCosts = GetParameterInRange(TrialCostsScenarioPerPartyMin_Warmup, TrialCostsScenarioPerPartyMax_Warmup, (int)warmupScenarioIndex, NumWarmupPossibilities);
                        if (changeTrialCostsForDefendant)
                            Options.DTrialCosts = GetParameterInRange(TrialCostsScenarioPerPartyMin_Warmup, TrialCostsScenarioPerPartyMax_Warmup, (int)warmupScenarioIndex, NumWarmupPossibilities);
                    }
                    else if (WhatToChange_Warmup == ChangeInScenario.CostsMultiplier)
                    {
                        Options.CostsMultiplier = GetParameterInRange(CostsMultiplierMin_Warmup, CostsMultiplierMax_Warmup, (int)warmupScenarioIndex, NumWarmupPossibilities);
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

        public override void SwitchToAlternativeOptions(bool changeToAlternate)
        {
            // This is for a test in the sequence form algorithm, to see what will be out of equilibrium when the settings change.
            if (changeToAlternate)
            {
                Options.LoserPays = true;
                Options.LoserPaysMultiple = 0.01;
            }
            else
            {
                Options.LoserPays = false;
                Options.LoserPaysMultiple = 1.0;
            }
        }


        #endregion

    }
}
