using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Games.LitigGame;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.GameSolvingSupport.Symmetry;
using ACESimBase.Util;
using ACESimBase.Util.ArrayManipulation;
using ACESimBase.Util.DiscreteProbabilities;
using ACESimBase.Util.Reporting;
using ACESimBase.Util.Statistical;
using JetBrains.Annotations;

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
        public byte GameHistoryCacheIndex_DReadyToDefault = 13;
        public byte GameHistoryCacheIndex_PChipsAction = 14;
        public byte GameHistoryCacheIndex_DChipsAction = 15;
        public byte GameHistoryCacheIndex_PWins = 16;

        public bool CheckCompleteAfterPrimaryAction;
        public bool CheckCompleteAfterPostPrimaryAction;

        #endregion

        #region Decisions list

        private List<Decision> GetDecisionsList()
        {
            var decisions = Options.LitigGameDisputeGenerator
                                   .GenerateDisputeDecisions(this)
                                   .ToList();

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
        
        // DEBUG -- move all this to a class of its own

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
                new Decision("P Files", "PF", false, (byte)LitigGamePlayers.Plaintiff, new byte[]  { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
                    2, (byte)LitigGameDecisions.PFile)
                { // TODO: Maybe can eliminate notice to plaintiff and defendant here and below
                    CanTerminateGame = true, // not filing always terminates
                    IsReversible = true,
                    SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction)
                };
            decisions.Add(pFile);

            var dAnswer =
                new Decision("D Answers", "DA", false, (byte)LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
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

            string bargainingRoundString = (Options.NumPotentialBargainingRounds > 1 ? $" Round {(b + 1)}" : "");

            if (Options.IncludeAgreementToBargainDecisions)
            {
                var pAgreeToBargain = new Decision($"P Agrees To Bargain{bargainingRoundString}", "PB" + (b + 1), false, (byte) LitigGamePlayers.Plaintiff, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
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

                var dAgreeToBargain = new Decision($"D Agrees To Bargain{bargainingRoundString}", "DB" + (b + 1), false, (byte) LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
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
                    new Decision($"P Offer{bargainingRoundString}", "PO" + (b + 1), false, (byte)LitigGamePlayers.Plaintiff, informedOfPOffer,
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
                    new Decision($"D Offer{bargainingRoundString}", "DO" + (b + 1), false, (byte)LitigGamePlayers.Defendant, informedOfDOffer,
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
                        new Decision($"P Offer{bargainingRoundString}", "PO" + (b + 1), false, (byte)LitigGamePlayers.Plaintiff, new byte[] { (byte) LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
                            Options.NumOffers, (byte)LitigGameDecisions.POffer)
                        {
                            CustomByte = (byte)(b + 1),
                            StoreActionInGameCacheItem = GameHistoryCacheIndex_POffer,
                            IsContinuousAction = true,
                            SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction) // NOTE: We could make this compatible with symmetry by having an earlier chance decision that randomly chooses who goes first
                        }; // { AlwaysDoAction = 4});
                    AddOfferDecision(decisions, pOffer);
                    decisions.Add(
                        new Decision($"D Response{bargainingRoundString}", "DR" + (b + 1), false, (byte)LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution }, 2,
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
                        new Decision($"D Offer{bargainingRoundString}", "DO" + (b + 1), false, (byte)LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
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
                        new Decision($"P Response{bargainingRoundString}", "PR" + (b + 1), false, (byte)LitigGamePlayers.Plaintiff, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution }, 2,
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
            string bargainingRoundString = (Options.NumPotentialBargainingRounds > 1 ? $" Round {(b + 1)}" : "");
            var pRSideBet =
                new Decision($"P Running Side Bet{bargainingRoundString}", "PRSB" + (b + 1), false, (byte)LitigGamePlayers.Plaintiff, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
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
                new Decision($"D Running Side Bet{bargainingRoundString}", "DRSB" + (b + 1), false, (byte)LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution },
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
            string bargainingRoundString = (Options.NumPotentialBargainingRounds > 1 ? $" Round {(b + 1)}" : "");
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
                    new Decision($"P Abandons{bargainingRoundString}", "PAB" + (b + 1), false, (byte)LitigGamePlayers.Plaintiff, new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.BothGiveUpChance, (byte)LitigGamePlayers.Resolution },
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
                    new Decision($"D Defaults{bargainingRoundString}" + (b + 1), "DD" + (b + 1), false, (byte)LitigGamePlayers.Defendant, new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.BothGiveUpChance, (byte)LitigGamePlayers.Resolution },
                        2, (byte)LitigGameDecisions.DDefault)
                    {
                        CustomByte = (byte)(b + 1),
                        CanTerminateGame = !Options.PredeterminedAbandonAndDefaults, // if either but not both has given up, game terminates, unless we're predetermining abandon and defaults, in which case we still have to go through offers
                        StoreActionInGameCacheItem = GameHistoryCacheIndex_DReadyToDefault,
                        IsReversible = true,
                        SymmetryMap = (SymmetryMapInput.SameInfo, SymmetryMapOutput.SameAction)
                    };
                decisions.Add(dDefault);
            }
            if (includeChanceDecision)
            {
                var bothGiveUp =
                    new Decision($"Both Quit{bargainingRoundString}", "MGU" + (b + 1), true, (byte)LitigGamePlayers.BothGiveUpChance, new byte[] { (byte)LitigGamePlayers.Resolution },
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
            string bargainingRoundString = (Options.NumPotentialBargainingRounds > 1 ? $" Round {(b + 1)}" : "");
            var dummyDecision =
                new Decision($"Pre Bargaining Round{bargainingRoundString}", "PRBR" + (b + 1), true, (byte)LitigGamePlayers.PreBargainingRoundChance, null,
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
            string bargainingRoundString = (Options.NumPotentialBargainingRounds > 1 ? $" Round {(b + 1)}" : "");
            var dummyDecision =
                new Decision($"Post Bargaining Round{bargainingRoundString}", "POBR" + (b + 1), true, (byte)LitigGamePlayers.PostBargainingRoundChance, null,
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
                    decisions.Add(new Decision("P Pretrial", "PPT", false, (byte) LitigGamePlayers.Plaintiff, playersToInformOfPAction, pActions, (byte) LitigGameDecisions.PPretrialAction) { IsReversible = true,
                        SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction)
                    });
                }
                if (dActions > 0)
                {
                    decisions.Add(new Decision("D Pretrial", "DPT", false, (byte)LitigGamePlayers.Defendant, playersToInformOfDAction, dActions, (byte)LitigGameDecisions.DPretrialAction) { IsReversible = true,
                        SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.SameAction)
                    });
                }
            }
        }

        private void AddCourtDecisions(List<Decision> decisions)
        {
            bool courtDecidesDamages = Options.NumDamagesStrengthPoints > 1;
            bool courtDecidesLiability = Options.NumLiabilityStrengthPoints > 1;
            decisions.Add(new Decision(courtDecidesDamages ? "Court Liability Decision" : "Court Decision", "CL", true, (byte)LitigGamePlayers.CourtLiabilityChance,
                    new byte[] { (byte)LitigGamePlayers.Resolution }, (byte) Options.NumCourtLiabilitySignals, (byte)LitigGameDecisions.CourtDecisionLiability,
                    unevenChanceActions: true, criticalNode: true)
                { CanTerminateGame = true, AlwaysTerminatesGame = !courtDecidesDamages, IsReversible = true, DistributorChanceDecision = true, CanCalculateDistributorChanceDecisionProbabilitiesFromInformationSet = true, StoreActionInGameCacheItem = GameHistoryCacheIndex_PWins,
                SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
            }); // even chance options
            if (courtDecidesDamages)
                decisions.Add(new Decision(courtDecidesLiability ? "Court Damages Decision" : "Court Decision", "CD", true, (byte)LitigGamePlayers.CourtDamagesChance,
                    new byte[] { (byte)LitigGamePlayers.Resolution }, Options.NumDamagesSignals, (byte)LitigGameDecisions.CourtDecisionDamages,
                    unevenChanceActions: true, criticalNode: true)
                { CanTerminateGame = true, AlwaysTerminatesGame = true, IsReversible = true, DistributorChanceDecision = true, CanCalculateDistributorChanceDecisionProbabilitiesFromInformationSet = true,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
                }); // even chance options
        }

        public override string GetActionString(byte action, byte decisionByteCode)
        {
            LitigGameDecisions decision = (LitigGameDecisions)decisionByteCode;
            switch (decision)
            {

                case LitigGameDecisions.PrePrimaryActionChance:
                case LitigGameDecisions.PrimaryAction:
                case LitigGameDecisions.PostPrimaryActionChance:
                    return Options.LitigGameDisputeGenerator.GetActionString(action, decisionByteCode);

                case LitigGameDecisions.LiabilityStrength:
                    return Game.ConvertActionToUniformDistributionDraw(action, Options.NumLiabilityStrengthPoints, false).ToDecimalPlaces(2);

                case LitigGameDecisions.DamagesStrength:
                    return Game.ConvertActionToUniformDistributionDraw(action, Options.NumDamagesStrengthPoints, true).ToDecimalPlaces(2);


                case LitigGameDecisions.PDamagesSignal:
                case LitigGameDecisions.DDamagesSignal:
                    return Options.NumDamagesSignals == 1 ? 1.0.ToString() : Game.ConvertActionToUniformDistributionDraw(action, Options.NumDamagesSignals, false).ToDecimalPlaces(2);

                case LitigGameDecisions.PLiabilitySignal:
                case LitigGameDecisions.DLiabilitySignal:
                    return Options.NumLiabilitySignals == 1 ? 0.5.ToString() : Game.ConvertActionToUniformDistributionDraw(action, Options.NumLiabilitySignals, false).ToDecimalPlaces(2);

                case LitigGameDecisions.POffer:
                case LitigGameDecisions.DOffer:
                    return Game.ConvertActionToUniformDistributionDraw(action, Options.NumOffers, Options.IncludeEndpointsForOffers).ToDecimalPlaces(2); // Note: This won't be right if delta offers are being used.

                case LitigGameDecisions.PFile:
                case LitigGameDecisions.DAnswer:
                case LitigGameDecisions.PAgreeToBargain:
                case LitigGameDecisions.DAgreeToBargain:
                case LitigGameDecisions.PResponse:
                case LitigGameDecisions.DResponse:
                case LitigGameDecisions.PAbandon:
                case LitigGameDecisions.DDefault:
                    return action == 1 ? "Yes" : "No";

                case LitigGameDecisions.CourtDecisionLiability:
                    return action == 1 ? "Not Liable" : "Liable";

                case LitigGameDecisions.MutualGiveUp:
                    return action == 1 ? "P Abandons" : "D Defaults";

                default:
                    return base.GetActionString(action, decisionByteCode);
            }
        }

        #endregion

        #region Game play support 

        public override double[] GetUnevenChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            double[] results = GetUnevenChanceActionProbabilities_Helper(decisionByteCode, gameProgress);
            bool roundOff = false;
            if (roundOff)
            {
                const int maxDigits = 5;
                results = results.Select(x => Math.Round(x, maxDigits)).ToArray();
                results[results.Length - 1] = 1.0 - (results.Sum() - results[results.Length - 1]);
            }
            return results;
        }

        private double[] GetUnevenChanceActionProbabilities_Helper(byte decisionByteCode, GameProgress gameProgress)
        {
            if (decisionByteCode == (byte)LitigGameDecisions.PrePrimaryActionChance)
            {
                var myGameProgress = ((LitigGameProgress)gameProgress);
                var probabilities = Options.LitigGameStandardDisputeGenerator.GetPrePrimaryChanceProbabilities(this);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else if (decisionByteCode == (byte)LitigGameDecisions.PostPrimaryActionChance)
            {
                var myGameProgress = ((LitigGameProgress)gameProgress);
                var probabilities = Options.LitigGameStandardDisputeGenerator.GetPostPrimaryChanceProbabilities(this, myGameProgress.DisputeGeneratorActions);
                if (Math.Abs(probabilities.Sum() - 1) > 1E-8)
                    throw new Exception();
                return probabilities;
            }
            else if (decisionByteCode == (byte) LitigGameDecisions.LiabilityStrength)
            {
                var myGameProgress = ((LitigGameProgress) gameProgress);
                double[] probabilities;
                if (Options.CollapseChanceDecisions)
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
                if (Options.CollapseChanceDecisions)
                    probabilities = Options.LitigGameDisputeGenerator.InvertedCalculations_GetPLiabilitySignalProbabilities(myGameProgress.DLiabilitySignalDiscrete);
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
                if (Options.CollapseChanceDecisions)
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
                if (Options.CollapseChanceDecisions)
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
                if (Options.CollapseChanceDecisions)
                    probabilities = Options.LitigGameDisputeGenerator.InvertedCalculations_GetPDamagesSignalProbabilities(myGameProgress.DDamagesSignalDiscrete);
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
                if (Options.CollapseChanceDecisions)
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
                if (Options.CollapseChanceDecisions)
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
                if (Options.CollapseChanceDecisions)
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
                bool dTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DReadyToDefault) == 1;
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
                        bool dTryingToGiveUp2 = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DReadyToDefault) == 1;
                        if (pTryingToGiveUp2 ^ dTryingToGiveUp2)
                            return true; // exactly one trying to give up in last bargaining round
                    }
                    if (Options.CollapseChanceDecisions && Options.CollapseAlternativeEndings)
                        return true; // NOTE: If we want to support this over multiple bargaining rounds (currently excluded by code in LitigGame.CheckCollapseFinalGameDecisions), then we'll need to do more checks to make sure that this is the right time. Also, if we allow for non-predetermined decisions, we'll have to complicate this as well.
                    break;
                case (byte)LitigGameDecisions.PrimaryAction:
                    return Options.LitigGameStandardDisputeGenerator.MarkComplete(this, gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrePrimaryChance), gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrimaryAction));
                case (byte)LitigGameDecisions.PostPrimaryActionChance:
                    return Options.LitigGameStandardDisputeGenerator.MarkComplete(this, gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrePrimaryChance), gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PrimaryAction), gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_PostPrimaryChance));
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
                    bool dTryingToGiveUp = gameHistory.GetCacheItemAtIndex(GameHistoryCacheIndex_DReadyToDefault) == 1;
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

        public override int NumPostWarmupPossibilities => 11; // We can change this, for example when doing PCA, to generate strategies that vary based on (e.g.) cost levels but that don't otherwise change the structure of the game.
        public override int NumWarmupPossibilities => 4; // Note that this can be 0 (indicating that costs multiplier doesn't change). This indicates the variations on the costs multiplier; variations on weight to opponent are below. 
        public override int WarmupIterations_IfWarmingUp => 10; 
        public override bool MultiplyWarmupScenariosByAlteringWeightOnOpponentsStrategy => true;
        public override int NumDifferentWeightsOnOpponentsStrategyPerWarmupScenario_IfMultiplyingScenarios => 3; // should be odd if we want to include zero weight (the default) on opponent's strategy, because we also need positive and negative weights
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

        public override double MakeMarginalChangeToTestInformationSetPressure(bool changeToAlternate)
        {
            double magnitudeOfChange = 1.0;
            // This is for a test in the sequence form algorithm, to see what will be out of equilibrium when the settings change.
            if (changeToAlternate)
            {
                if (Options.LoserPays == false)
                    throw new Exception("Must set loser pays to true and multiple to relevant value (possibly 0 for American rule) to start information set pressure analysis");
                Options.LoserPaysMultiple += magnitudeOfChange;
            }
            else
            {
                Options.LoserPaysMultiple -= magnitudeOfChange;
            }
            return magnitudeOfChange;
        }

        #endregion

        #region Manual reports

        public override IEnumerable<(string suffix, string reportcontent)> ProduceManualReports(List<(GameProgress theProgress, double weight)> gameProgresses, string supplementalString)
        {
            const double yAxisTop = 4.0;
            var contents = CostBreakdownReport.GenerateReport(gameProgresses.Select(x => ((LitigGameProgress) x.theProgress, x.weight)), yAxisTop);
            yield return ($"-costbreakdownlight{supplementalString}.csv", contents[0]);
            yield return ($"-costbreakdownlight{supplementalString}.tex", contents[1]);
            yield return ($"-costbreakdowndark{supplementalString}.csv", contents[2]);
            yield return ($"-costbreakdowndark{supplementalString}.tex", contents[3]);
            contents = StageCostReport.GenerateReport(gameProgresses);
            yield return ($"-stagecostlight{supplementalString}.csv", contents[0]);
            yield return ($"-stagecostlight{supplementalString}.tex", contents[1]);
            yield return ($"-stagecostdark{supplementalString}.csv", contents[2]);
            yield return ($"-stagecostdark{supplementalString}.tex", contents[3]);
            contents = SignalOfferReport.GenerateReport(this, gameProgresses, SignalOfferReport.TypeOfReport.Offers);
            yield return ($"-offers{supplementalString}.tex", contents[0]);
            contents = SignalOfferReport.GenerateReport(this, gameProgresses, SignalOfferReport.TypeOfReport.FileAndAnswer);
            yield return ($"-fileans{supplementalString}.tex", contents[0]);
        }


        private enum TreeDiagramExclusions
        {
            FullDiagram,
            BeginningOfGame,
            EndOfGame
        }
        private TreeDiagramExclusions Exclusions = TreeDiagramExclusions.FullDiagram;

        public override (Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> excludeBelow, Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> includeBelow) GetTreeDiagramExclusions()
        {
            (Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> excludeBelow, Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> includeBelow)
                = (null, null);
            switch (Exclusions)
            {
                case TreeDiagramExclusions.FullDiagram:
                break;

                case TreeDiagramExclusions.BeginningOfGame:
                    excludeBelow = gpn =>
                    {
                        var edge = gpn.EdgeFromParent;
                        if (edge == null)
                            return false;
                        string actionString = edge.parentNameWithActionString(this);
                        string nodePlayerString = gpn.NodePlayerString(this);
                        if (actionString.Contains("D Liability Signal"))
                            return true;
                        return false;
                    };
                    break;

                case TreeDiagramExclusions.EndOfGame:
                    includeBelow = gpn =>
                    {
                        var edge = gpn.EdgeFromParent;
                        if (edge == null)
                            return false;
                        string actionString = edge.parentNameWithActionString(this);
                        string nodePlayerString = gpn.NodePlayerString(this);
                        if (actionString.Contains("D Liability Signal"))
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
