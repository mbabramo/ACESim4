using ACESimBase.Games.LitigGame;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class LitigGameProgress : GameProgress
    {
        public LitigGameProgress(bool fullHistoryRequired) : base(fullHistoryRequired)
        {

        }

        public LitigGameDefinition LitigGameDefinition => (LitigGameDefinition)GameDefinition;
        public LitigGameOptions LitigGameOptions => LitigGameDefinition.Options;

        public bool DisputeArises;
        public bool PFiles, DAnswers, PReadyToAbandon, DReadyToAbandon, BothReadyToGiveUp, PAbandons, DDefaults;
        public byte BargainingRoundsComplete;

        public List<bool> PAgreesToBargain;
        public List<bool> DAgreesToBargain;
        public List<double> POffers;
        public List<bool> PResponses;
        public List<double> DOffers;
        public List<bool> DResponses;
        public bool CaseSettles;
        public double? SettlementValue;
        public bool TrialOccurs;
        public bool PWinsAtTrial;
        public bool WinIsByLargeMargin;
        public double DamagesAwarded;
        public bool DWinsAtTrial => TrialOccurs && !PWinsAtTrial;

        public LitigGameDisputeGeneratorActions DisputeGeneratorActions;
        public LitigGamePretrialActions PretrialActions;
        public LitigGameRunningSideBetsActions RunningSideBetsActions;

        public bool IsTrulyLiable;
        public byte LiabilityStrengthDiscrete;
        public byte PLiabilityNoiseDiscrete;
        public byte DLiabilityNoiseDiscrete;
        public byte PLiabilitySignalDiscrete;
        public byte DLiabilitySignalDiscrete;
        public byte? CLiabilitySignalDiscrete;

        public byte DamagesStrengthDiscrete;
        public byte PDamagesNoiseDiscrete;
        public byte DDamagesNoiseDiscrete;
        public byte PDamagesSignalDiscrete;
        public byte DDamagesSignalDiscrete;
        public byte? CDamagesSignalDiscrete;

        public double  PChangeWealth;
        public double  DChangeWealth;
        public double? PFinalWealthWithBestOffer;
        public double? DFinalWealthWithBestOffer;
        public double  PFinalWealth;
        public double  DFinalWealth;
        public double  PWelfare;
        public double  DWelfare;
        public byte    NumChips;

        public List<double> POfferMixedness;
        public List<double> DOfferMixedness;

        public List<(LitigGameProgress completedGame, double weight)> AlternativeEndings;

        public object lockObj = new object();

        public LitigGameProgress_PostGameInfo _PostGameInfo;
        public LitigGameProgress_PostGameInfo PostGameInfo
        {
            get
            {
                if (_PostGameInfo == null)
                {
                    if (!GameComplete)
                        throw new Exception("Accessing post-game info prematurely.");
                    lock (lockObj) // this is only place we lock on LitigGameProgress, so no danger from lock(this)
                    {
                        if (_PostGameInfo == null)
                        {
                            _PostGameInfo = new LitigGameProgress_PostGameInfo();
                            CalculatePostGameInfo();
                        }
                    }
                }
                return _PostGameInfo;
            }
        }

        public double? LiabilityStrengthUniform { get => PostGameInfo.LiabilityStrengthUniform; set { PostGameInfo.LiabilityStrengthUniform = value; } }
        public double PLiabilitySignalUniform { get => PostGameInfo.PLiabilitySignalUniform; set { PostGameInfo.PLiabilitySignalUniform = value; } }
        public double DLiabilitySignalUniform { get => PostGameInfo.DLiabilitySignalUniform; set { PostGameInfo.DLiabilitySignalUniform = value; } }
        public double? DamagesStrengthUniform { get => PostGameInfo.DamagesStrengthUniform; set { PostGameInfo.DamagesStrengthUniform = value; } }
        public double PDamagesSignalUniform { get => PostGameInfo.PDamagesSignalUniform; set { PostGameInfo.PDamagesSignalUniform = value; } }
        public double DDamagesSignalUniform { get => PostGameInfo.DDamagesSignalUniform; set { PostGameInfo.DDamagesSignalUniform = value; } }

        public double FalsePositiveExpenditures { get => PostGameInfo.FalsePositiveExpenditures; set { PostGameInfo.FalsePositiveExpenditures = value; } }
        public double FalseNegativeShortfall { get => PostGameInfo.FalseNegativeShortfall; set { PostGameInfo.FalseNegativeShortfall = value; } }
        public double TotalExpensesIncurred { get => PostGameInfo.TotalExpensesIncurred; set { PostGameInfo.TotalExpensesIncurred = value; } }
        public double PreDisputeSharedWelfare { get => PostGameInfo.PreDisputeSharedWelfare; set { PostGameInfo.PreDisputeSharedWelfare = value; } }


        public override string ToString()
        {
            return
                $"DisputeArises {DisputeArises} IsTrulyLiable {_PostGameInfo?.IsTrulyLiable} LiabilityStrengthDiscrete {LiabilityStrengthDiscrete} LiabilityStrengthUniform {_PostGameInfo?.LiabilityStrengthUniform} PLiabilitySignalDiscrete {PLiabilitySignalDiscrete} DLiabilitySignalDiscrete {DLiabilitySignalDiscrete} PLiabilitySignalUniform {_PostGameInfo?.PLiabilitySignalUniform} DLiabilitySignalUniform {_PostGameInfo?.DLiabilitySignalUniform} DamagesStrengthDiscrete {DamagesStrengthDiscrete} DamagesStrengthUniform {_PostGameInfo?.DamagesStrengthUniform} PDamagesSignalDiscrete {PDamagesSignalDiscrete} DDamagesSignalDiscrete {DDamagesSignalDiscrete} PDamagesSignalUniform {_PostGameInfo?.PDamagesSignalUniform} DDamagesSignalUniform {_PostGameInfo?.DDamagesSignalUniform} PFiles {PFiles} DAnswers {DAnswers} BargainingRoundsComplete {BargainingRoundsComplete} PLastAgreesToBargain {PLastAgreesToBargain} DLastAgreesToBargain {DLastAgreesToBargain} PLastOffer {PLastOffer} DLastOffer {DLastOffer} CaseSettles {CaseSettles} SettlementValue {SettlementValue} PAbandons {PAbandons} DDefaults {DDefaults} TrialOccurs {TrialOccurs} PWinsAtTrial {PWinsAtTrial} DamagesAwarded {DamagesAwarded} PFinalWealthWithBestOffer {PFinalWealthWithBestOffer} DFinalWealthWithBestOffer {DFinalWealthWithBestOffer} PFinalWealth {PFinalWealth} DFinalWealth {DFinalWealth} PWelfare {PWelfare} DWelfare {DWelfare} FalsePositiveExpenditures {_PostGameInfo?.FalsePositiveExpenditures} FalseNegativeShortfall {_PostGameInfo?.FalseNegativeShortfall} TotalExpensesIncurred {_PostGameInfo?.TotalExpensesIncurred} NumChips {NumChips}";
        }

        public bool? PFirstAgreesToBargain => (bool?)PAgreesToBargain?.FirstOrDefault() ?? null;
        public bool? DFirstAgreesToBargain => (bool?)DAgreesToBargain?.FirstOrDefault() ?? null;
        public bool? PLastAgreesToBargain => (bool?) PAgreesToBargain?.LastOrDefault() ?? null;
        public bool? DLastAgreesToBargain => (bool?)DAgreesToBargain?.LastOrDefault() ?? null;
        public double? PFirstOffer => (double?)POffers?.FirstOrDefault() ?? null;
        public double? DFirstOffer => (double?)DOffers?.FirstOrDefault() ?? null;
        public bool? PFirstResponse => (bool?)PResponses?.FirstOrDefault() ?? null;
        public bool? DFirstResponse => (bool?)DResponses?.FirstOrDefault() ?? null;
        public double? PLastOffer => (double?)POffers?.LastOrDefault() ?? null;
        public double? DLastOffer => (double?)DOffers?.LastOrDefault() ?? null;
        public bool? PLastResponse => (bool?)PResponses?.LastOrDefault() ?? null;
        public bool? DLastResponse => (bool?)DResponses?.LastOrDefault() ?? null;
        public bool SurvivesToRound(byte round) => BargainingRoundsComplete >= round;
        public bool SettlesInRound(byte round) => SettlementValue != null && BargainingRoundsComplete == round;
        public bool DoesntSettleInRound(byte round) => SurvivesToRound(round) && !SettlesInRound(round);
        public bool PAbandonsInRound(byte round) => DoesntSettleInRound(round) && PAbandons && BargainingRoundsComplete == round;
        public bool PDoesntAbandonInRound(byte round) => DoesntSettleInRound(round) && !(PAbandons && BargainingRoundsComplete == round);
        public bool DDefaultsInRound(byte round) => PDoesntAbandonInRound(round) && DDefaults && BargainingRoundsComplete == round;
        public bool DDoesntDefaultInRound(byte round) => PDoesntAbandonInRound(round) && !(DDefaults && BargainingRoundsComplete == round);
        public bool BothPlayersHaveCompletedRoundWithOfferResponse => POffers?.Count() == DResponses?.Count() && DOffers?.Count() == PResponses?.Count();

        public bool PAgreesToBargainInRound(int bargainingRoundNum) => !LitigGameOptions.IncludeAgreementToBargainDecisions || ((PAgreesToBargain?.Count() ?? 0) >= bargainingRoundNum && PAgreesToBargain[bargainingRoundNum - 1]);
        public bool DAgreesToBargainInRound(int bargainingRoundNum) => !LitigGameOptions.IncludeAgreementToBargainDecisions || ((DAgreesToBargain?.Count() ?? 0) >= bargainingRoundNum && DAgreesToBargain[bargainingRoundNum - 1]);
        public bool BothAgreeToBargainInRound(int bargainingRoundNum) => PAgreesToBargainInRound(bargainingRoundNum) && DAgreesToBargainInRound(bargainingRoundNum);

        public void ConcludeMainPortionOfBargainingRound(LitigGameDefinition gameDefinition)
        {
            bool playersMovingSimultaneously = gameDefinition.Options.BargainingRoundsSimultaneous;
            bool pGoesFirstIfNotSimultaneous = playersMovingSimultaneously || gameDefinition.Options.PGoesFirstIfNotSimultaneous[BargainingRoundsComplete];
            CaseSettles = SettlementReached(playersMovingSimultaneously, pGoesFirstIfNotSimultaneous);
            if (CaseSettles)
                {
                    SetSettlementValue(playersMovingSimultaneously, pGoesFirstIfNotSimultaneous);
                    BargainingRoundsComplete++; // we won't get to PostBargainingRound
                GameComplete = true;
            }
            else
            {
                if (playersMovingSimultaneously || !pGoesFirstIfNotSimultaneous)
                { // defendant has made an offer this round
                    var pMissedOpportunity = LitigGame.CalculateGameOutcome(gameDefinition, DisputeGeneratorActions, PretrialActions, RunningSideBetsActions, gameDefinition.Options.PInitialWealth, gameDefinition.Options.DInitialWealth, PFiles, PAbandons, DAnswers, DDefaults, (double) DLastOffer * (double)gameDefinition.Options.DamagesMax, true /* ignored */, false /* ignored */, 0, (byte) (BargainingRoundsComplete + 1), null, null, POffers, PResponses, DOffers, DResponses);
                    if (pMissedOpportunity.PFinalWealth > PFinalWealthWithBestOffer || PFinalWealthWithBestOffer == null)
                        PFinalWealthWithBestOffer = pMissedOpportunity.PFinalWealth;
                }
                if (playersMovingSimultaneously || pGoesFirstIfNotSimultaneous)
                { // plaintiff has made an offer this round
                    var dMissedOpportunity = LitigGame.CalculateGameOutcome(gameDefinition, DisputeGeneratorActions, PretrialActions, RunningSideBetsActions, gameDefinition.Options.PInitialWealth, gameDefinition.Options.DInitialWealth, PFiles, PAbandons, DAnswers, DDefaults, (double)PLastOffer * (double)gameDefinition.Options.DamagesMax, true /* ignored */, false /* ignored */, 0, (byte)(BargainingRoundsComplete + 1), null, null, POffers, PResponses, DOffers, DResponses);
                    if (dMissedOpportunity.DFinalWealth > DFinalWealthWithBestOffer || DFinalWealthWithBestOffer == null)
                        DFinalWealthWithBestOffer = dMissedOpportunity.DFinalWealth;
                }
            }
        }

        public byte? GetPlayerBet(bool plaintiff, byte round)
        {
            if (RunningSideBetsActions.ActionsEachBargainingRound == null)
                return null;
            if (!SurvivesToRound(round) || (SettlementValue != null && BargainingRoundsComplete == round))
                return null;
            return (byte) (RunningSideBetsActions.ActionsEachBargainingRound.Skip(round - 1).Select(x => plaintiff ? x.PAction : x.DAction).First() - 1);
        }

        public double? GetOffer(bool plaintiff, int offerNumber)
        {
            int offerNumberZeroBased = offerNumber - 1;
            if (plaintiff)
            {
                if (POffers != null && POffers.Count() > offerNumberZeroBased)
                    return POffers[offerNumberZeroBased];
                else
                    return null;
            }
            else
            {
                if (DOffers != null && DOffers.Count() > offerNumberZeroBased)
                    return DOffers[offerNumberZeroBased];
                else
                    return null;
            }
        }

        public double? GetBetMixedness(bool plaintiff, int offerNumber)
        {
            if (RunningSideBetsActions.MixednessEachBargainingRound == null)
                return null;
            int offerNumberZeroBased = offerNumber - 1;
            if (plaintiff)
            {
                if (RunningSideBetsActions.MixednessEachBargainingRound != null && RunningSideBetsActions.MixednessEachBargainingRound.Count() > offerNumberZeroBased)
                    return RunningSideBetsActions.MixednessEachBargainingRound[offerNumberZeroBased].PMixedness;
                else
                    return null;
            }
            else
            {
                if (RunningSideBetsActions.MixednessEachBargainingRound != null && RunningSideBetsActions.MixednessEachBargainingRound.Count() > offerNumberZeroBased)
                    return RunningSideBetsActions.MixednessEachBargainingRound[offerNumberZeroBased].PMixedness;
                else
                    return null;
            }
        }

        public double? GetOfferMixedness(bool plaintiff, int offerNumber)
        {
            int offerNumberZeroBased = offerNumber - 1;
            if (plaintiff)
            {
                if (POfferMixedness != null && POfferMixedness.Count() > offerNumberZeroBased)
                    return POfferMixedness[offerNumberZeroBased];
                else
                    return null;
            }
            else
            {
                if (DOfferMixedness != null && DOfferMixedness.Count() > offerNumberZeroBased)
                    return DOfferMixedness[offerNumberZeroBased];
                else
                    return null;
            }
        }

        public bool GetResponse(bool plaintiffResponse, int offerNumber)
        {
            int offerNumberZeroBased = offerNumber - 1;
            if (plaintiffResponse)
                return PResponses[offerNumberZeroBased];
            else
                return DResponses[offerNumberZeroBased];
        }

        private bool SettlementReached(bool playersMovingSimultaneously, bool pGoesFirstIfNotSimultaneous)
        {
            if (playersMovingSimultaneously)
                return DLastOffer >= PLastOffer;
            else if (pGoesFirstIfNotSimultaneous)
                return DLastResponse == true;
            else
                return PLastResponse == true;
        }
        private void SetSettlementValue(bool playersMovingSimultaneously, bool pGoesFirstIfNotSimultaneous)
        {
            // assumes that a settlement has been reached
            if (playersMovingSimultaneously)
                SettlementValue = (PLastOffer + DLastOffer) * (double)LitigGameDefinition.Options.DamagesMax / 2.0;
            else if (pGoesFirstIfNotSimultaneous)
                SettlementValue = PLastOffer * (double)LitigGameDefinition.Options.DamagesMax;
            else
                SettlementValue = DLastOffer * (double)LitigGameDefinition.Options.DamagesMax;
        }

        public override LitigGameProgress DeepCopy()
        {
            LitigGameProgress copy = new LitigGameProgress(FullHistoryRequired);

            // copy.GameComplete = this.GameComplete;
            base.CopyFieldInfo(copy);
            copy.DisputeArises = DisputeArises;

            copy.PFiles = PFiles;
            copy.DAnswers = DAnswers;
            copy.PReadyToAbandon = PReadyToAbandon;
            copy.DReadyToAbandon = DReadyToAbandon;
            copy.BothReadyToGiveUp = BothReadyToGiveUp;
            copy.PAbandons = PAbandons;
            copy.DDefaults = DDefaults;

            copy.BargainingRoundsComplete = BargainingRoundsComplete;
            copy.PAgreesToBargain = PAgreesToBargain?.ToList();
            copy.DAgreesToBargain = DAgreesToBargain?.ToList();
            copy.POffers = POffers?.ToList();
            copy.DOffers = DOffers?.ToList();
            copy.PResponses = PResponses?.ToList();
            copy.DResponses = DResponses?.ToList();
            copy.CaseSettles = CaseSettles;
            copy.SettlementValue = SettlementValue;
            copy.TrialOccurs = TrialOccurs;
            copy.PWinsAtTrial = PWinsAtTrial;
            copy.WinIsByLargeMargin = WinIsByLargeMargin;
            copy.DamagesAwarded = DamagesAwarded;

            copy.DisputeGeneratorActions = DisputeGeneratorActions;
            copy.PretrialActions = PretrialActions;
            copy.RunningSideBetsActions = RunningSideBetsActions;

            copy.IsTrulyLiable = IsTrulyLiable;
            copy.LiabilityStrengthDiscrete = LiabilityStrengthDiscrete;
            copy.PLiabilityNoiseDiscrete = PLiabilityNoiseDiscrete;
            copy.DLiabilityNoiseDiscrete = DLiabilityNoiseDiscrete;
            copy.PLiabilitySignalDiscrete = PLiabilitySignalDiscrete;
            copy.DLiabilitySignalDiscrete = DLiabilitySignalDiscrete;
            copy.CLiabilitySignalDiscrete = CLiabilitySignalDiscrete;

            copy.DamagesStrengthDiscrete = DamagesStrengthDiscrete;
            copy.PDamagesNoiseDiscrete = PDamagesNoiseDiscrete;
            copy.DDamagesNoiseDiscrete = DDamagesNoiseDiscrete;
            copy.PDamagesSignalDiscrete = PDamagesSignalDiscrete;
            copy.DDamagesSignalDiscrete = DDamagesSignalDiscrete;
            copy.CDamagesSignalDiscrete = CDamagesSignalDiscrete;

            copy.PChangeWealth = PChangeWealth;
            copy.DChangeWealth = DChangeWealth;
            copy.PFinalWealthWithBestOffer = PFinalWealthWithBestOffer;
            copy.DFinalWealthWithBestOffer = DFinalWealthWithBestOffer;
            copy.PFinalWealth = PFinalWealth;
            copy.DFinalWealth = DFinalWealth;
            copy.PWelfare = PWelfare;
            copy.DWelfare = DWelfare;
            copy.NumChips = NumChips;

            if (POfferMixedness != null)
                copy.POfferMixedness = POfferMixedness.ToList();
            if (DOfferMixedness != null)
                copy.DOfferMixedness = DOfferMixedness.ToList();

            copy.AlternativeEndings = AlternativeEndings?.Select(x => (x.completedGame.DeepCopy(), x.weight))?.ToList();

            // We don't need to copy the PostGameInfo, because that's automatically created

            return copy;
        }

        internal override void CopyFieldInfo(GameProgress copy)
        {
            base.CopyFieldInfo(copy);
        }

        public override double[] GetNonChancePlayerUtilities()
        {
            return new double[] { PWelfare, DWelfare };
        }

        public override FloatSet GetCustomResult()
        {
            return new FloatSet(
                PFiles ? 1.0f : 0,
                DAnswers ? 1.0f : 0,
                TrialOccurs ? 1.0f : 0,
                TrialOccurs && PWinsAtTrial ? 1.0f : 0
                );
        }

        public void AddPAgreesToBargain(bool agreesToBargain)
        {
            if (PAgreesToBargain == null)
                PAgreesToBargain = new List<bool>();
            PAgreesToBargain.Add(agreesToBargain);
        }

        public void AddDAgreesToBargain(bool agreesToBargain)
        {
            if (DAgreesToBargain == null)
                DAgreesToBargain = new List<bool>();
            DAgreesToBargain.Add(agreesToBargain);
        }

        public void AddOffer(bool plaintiff, double value)
        {
            if (plaintiff)
            {
                if (POffers == null)
                    POffers = new List<double>();
                POffers.Add(value);
            }
            else
            {
                if (DOffers == null)
                    DOffers = new List<double>();
                DOffers.Add(value);
            }
        }

        public void AddOfferMixedness(bool plaintiff, double value)
        {
            if (!ReportingMode)
                return;
            if (plaintiff)
            {
                if (POfferMixedness == null)
                    POfferMixedness = new List<double>();
                POfferMixedness.Add(value);
            }
            else
            {
                if (DOfferMixedness == null)
                    DOfferMixedness = new List<double>();
                DOfferMixedness.Add(value);
            }
        }

        public void AddResponse(bool plaintiff, bool value)
        {
            if (plaintiff)
            {
                if (PResponses == null)
                    PResponses = new List<bool>();
                PResponses.Add(value);
            }
            else
            {
                if (DResponses == null)
                    DResponses = new List<bool>();
                DResponses.Add(value);
            }
        }

        public void ResolveMutualGiveUp(byte action)
        {
            // both trying to give up simultaneously! revise with a coin flip
            BothReadyToGiveUp = true;
            PAbandons = action == 1;
            DDefaults = !PAbandons;
            BargainingRoundsComplete++; // we won't get to PostBargainingRound
            GameComplete = true;
        }

        public void CourtReceivesLiabilitySignal(byte action, LitigGameDefinition gameDefinition)
        {
            CLiabilitySignalDiscrete = action;
            TrialOccurs = true;
            PWinsAtTrial = false;
            if (gameDefinition.Options.NumLiabilitySignals == 1)
                PWinsAtTrial = true; /* IMPORTANT: This highlights that when there is only one liability signal, the court ALWAYS finds liability */
            else
            {
                if (gameDefinition.Options.LoserPays && gameDefinition.Options.LoserPaysOnlyLargeMarginOfVictory)
                {
                    PWinsAtTrial = action > (gameDefinition.Options.NumCourtLiabilitySignals + 1.0) / 2.0; // e.g., If we have four signals, then we need to be a 3 or 4, not a 1 or 2, when action is one-based (comparison will be to 2.5)
                                                                                                                    //if there were an odd number (not currently allowed)
                                                                                                                    //    PWinsAtTrial = action > (MyDefinition.Options.NumCourtLiabilitySignals + 1) / 2; // e.g., if we have three signals, then we need the one-based action to be greater than 4 / 2 = 2, because the midpoint (2) is not enough. If we have four signals, then we need to be a 3 or 4, not a 1 or 2, when action is one-based
                    double courtLiabilitySignal = Game.ConvertActionToUniformDistributionDraw(action, gameDefinition.Options.NumCourtLiabilitySignals, false);
                    if (PWinsAtTrial)
                    {
                        WinIsByLargeMargin = courtLiabilitySignal >= gameDefinition.Options.LoserPaysMarginOfVictoryThreshold;
                    }
                    else if (DWinsAtTrial)
                    {
                        WinIsByLargeMargin = courtLiabilitySignal <= 1.0 - gameDefinition.Options.LoserPaysMarginOfVictoryThreshold;
                    }
                }
                else
                    PWinsAtTrial = action == 2 /* signal must be the HIGH value for plaintiff to win */;
            }
            if (PWinsAtTrial == false)
            {
                DamagesAwarded = 0;
                GameComplete = true;
            }
            else
            {
                bool courtWouldDecideDamages = gameDefinition.Options.NumDamagesStrengthPoints > 1;
                if (!courtWouldDecideDamages)
                {
                    DamagesAwarded = (double)gameDefinition.Options.DamagesMax;
                    GameComplete = true;
                }
            }
        }

        public void CourtReceivesDamagesSignal(byte action, LitigGameDefinition gameDefinition)
        {
            CDamagesSignalDiscrete = action;
            double damagesProportion;
            if (gameDefinition.Options.NumDamagesSignals == 1)
                damagesProportion = 1.0;
            else
                damagesProportion = LitigGame.ConvertActionToUniformDistributionDraw(action, gameDefinition.Options.NumDamagesSignals, true);
            DamagesAwarded = (double)(gameDefinition.Options.DamagesMin + (gameDefinition.Options.DamagesMax - gameDefinition.Options.DamagesMin) * damagesProportion);
            GameComplete = true;
        }

        public void CalculateGameOutcome()
        {
            LitigGameDefinition gameDefinition = (LitigGameDefinition)GameDefinition;
            LitigGameOptions options = LitigGameDefinition.Options;

            if (AlternativeEndings == null)
            {
                LitigGame.LitigGameOutcome outcome = LitigGame.CalculateGameOutcome(gameDefinition, DisputeGeneratorActions, PretrialActions, RunningSideBetsActions, gameDefinition.Options.PInitialWealth, gameDefinition.Options.DInitialWealth, PFiles, PAbandons, DAnswers, DDefaults, SettlementValue, PWinsAtTrial, WinIsByLargeMargin, DamagesAwarded, BargainingRoundsComplete, PFinalWealthWithBestOffer, DFinalWealthWithBestOffer, POffers, PResponses, DOffers, DResponses);
                DisputeArises = options.LitigGameDisputeGenerator.PotentialDisputeArises(gameDefinition, DisputeGeneratorActions);
                PChangeWealth = outcome.PChangeWealth;
                DChangeWealth = outcome.DChangeWealth;
                PFinalWealth = outcome.PFinalWealth;
                DFinalWealth = outcome.DFinalWealth;
                PWelfare = outcome.PWelfare;
                DWelfare = outcome.DWelfare;
                TrialOccurs = outcome.TrialOccurs;
                NumChips = outcome.NumChips;

                if (options.CollapseChanceDecisions)
                    (IsTrulyLiable, LiabilityStrengthDiscrete, DamagesStrengthDiscrete) = options.LitigGameDisputeGenerator.InvertedCalculations_WorkBackwardsFromSignals(options.NumLiabilitySignals == 1 ? 1 : PLiabilitySignalDiscrete, options.NumLiabilitySignals == 1 ? 1 : DLiabilitySignalDiscrete, options.NumLiabilitySignals == 1 ? 1 : CLiabilitySignalDiscrete, options.NumDamagesSignals == 1 ? 1 : PDamagesSignalDiscrete, options.NumDamagesSignals == 1 ? 1 : DDamagesSignalDiscrete, options.NumDamagesSignals == 1 ? 1 : CDamagesSignalDiscrete, IterationID.IterationNumIntUnchecked);
            }
            else
            {
                DisputeArises = true;
                TrialOccurs = true;
                double AggregateAlternativeEndings(Func<LitigGameProgress, double> valueToAverageFunc) => AlternativeEndings.Aggregate((double)0, (weightedSum, ending) => weightedSum + ending.weight * valueToAverageFunc(ending.completedGame));
                // note that we're averaging changes in wealth, but that is irrelevant to outcome if the party is risk averse, since we are also averaging welfare directly. That's the ultimate point -- we can collapse chance decisions by producing a weighted average of utility.
                PChangeWealth = AggregateAlternativeEndings(prog => prog.PChangeWealth);
                DChangeWealth = AggregateAlternativeEndings(prog => prog.DChangeWealth);
                PFinalWealth = AggregateAlternativeEndings(prog => prog.PFinalWealth);
                PFinalWealth = AggregateAlternativeEndings(prog => prog.PFinalWealth);
                PWelfare = AggregateAlternativeEndings(prog => prog.PWelfare);
                DWelfare = AggregateAlternativeEndings(prog => prog.DWelfare);
            }
        }

        public void CalculatePostGameInfo()
        {
            LitigGameOptions o = LitigGameDefinition.Options;
            if (!o.CollapseChanceDecisions)
            {
                if (!o.LitigGameDisputeGenerator.PotentialDisputeArises(LitigGameDefinition, DisputeGeneratorActions))
                    IsTrulyLiable = false;
                else
                    IsTrulyLiable = o.LitigGameDisputeGenerator.IsTrulyLiable(LitigGameDefinition, DisputeGeneratorActions, this);
            }
            LiabilityStrengthUniform = Game.ConvertActionToUniformDistributionDraw(LiabilityStrengthDiscrete, o.NumLiabilityStrengthPoints, false);
            // If one or both parties have perfect information, then they can get their information about litigation quality now, since they don't need a signal. Note that we also specify in the game definition that the litigation quality should become part of their information set.
            if (o.PLiabilityNoiseStdev == 0)
                PLiabilitySignalUniform = (double)LiabilityStrengthUniform;
            if (o.DLiabilityNoiseStdev == 0)
                DLiabilitySignalUniform = (double)LiabilityStrengthUniform;
            PLiabilitySignalUniform = o.NumLiabilitySignals == 1 ? 0.5 : Game.ConvertActionToUniformDistributionDraw(PLiabilitySignalDiscrete, o.NumLiabilitySignals, false);
            DLiabilitySignalUniform = o.NumLiabilitySignals == 1 ? 0.5 : Game.ConvertActionToUniformDistributionDraw(DLiabilitySignalDiscrete, o.NumLiabilitySignals, false);
            DamagesStrengthUniform = Game.ConvertActionToUniformDistributionDraw(DamagesStrengthDiscrete, o.NumDamagesStrengthPoints, true /* include endpoints so that we can have possibility of max or min damages */);
            // If one or both parties have perfect information, then they can get their information about litigation quality now, since they don't need a signal. Note that we also specify in the game definition that the litigation quality should become part of their information set.
            if (o.PDamagesNoiseStdev == 0)
                PDamagesSignalUniform = (double)DamagesStrengthUniform;
            if (o.DDamagesNoiseStdev == 0)
                DDamagesSignalUniform = (double)DamagesStrengthUniform;
            PDamagesSignalUniform = o.NumDamagesSignals == 1 ? 0.5 : PDamagesSignalDiscrete * (1.0 / o.NumDamagesSignals);
            DDamagesSignalUniform = o.NumDamagesSignals == 1 ? 0.5 : DDamagesSignalDiscrete * (1.0 / o.NumDamagesSignals);

            TotalExpensesIncurred = 0 - PChangeWealth - DChangeWealth;
            if (!DisputeArises)
            {
                FalseNegativeShortfall = 0;
                FalsePositiveExpenditures = 0;
                return;
            }
            double correctDamagesIfTrulyLiable;
            if (o.NumDamagesStrengthPoints <= 1)
                correctDamagesIfTrulyLiable = (double)o.DamagesMax;
            else
                correctDamagesIfTrulyLiable = (double)(o.DamagesMin + DamagesStrengthUniform * (o.DamagesMax - o.DamagesMin));
            double falseNegativeShortfallIfTrulyLiable = Math.Max(0, correctDamagesIfTrulyLiable - PChangeWealth); // how much plaintiff's payment fell short (if at all)
            double falsePositiveExpendituresIfNotTrulyLiable = Math.Max(0, 0 - DChangeWealth); // how much defendant's payment was excessive (if at all), in the condition in which the defendant is NOT truly liable. In this case, the defendant ideally would pay 0.
            double falsePositiveExpendituresIfTrulyLiable = Math.Max(0, 0 - correctDamagesIfTrulyLiable - DChangeWealth); // how much defendant's payment was excessive (if at all), in the condition in which the defendant is truly liable. In this case, the defendant ideally would pay the correct amount of damages. E.g., if correct damages are 100 and defendant pays out 150 (including costs), then change in wealth is -150, we have -100 - -150, so we have 50.
            if (IsTrulyLiable)
            {
                FalseNegativeShortfall = falseNegativeShortfallIfTrulyLiable;
                FalsePositiveExpenditures = falsePositiveExpendituresIfTrulyLiable;
            }
            else
            {
                FalseNegativeShortfall = 0;
                FalsePositiveExpenditures = falsePositiveExpendituresIfNotTrulyLiable;
            }
            PreDisputeSharedWelfare = o.LitigGameDisputeGenerator.GetLitigationIndependentSocialWelfare(LitigGameDefinition, DisputeGeneratorActions);
        }

        public override void RecalculateGameOutcome()
        {
            CalculateGameOutcome();
        }


        public override List<(GameProgress progress, double weight)> InvertedCalculations_GenerateAllConsistentGameProgresses(double initialWeight)
        {
            List<(GameProgress progress, double weight)> results = new List<(GameProgress progress, double weight)>();
            var possibleCurrentStates = AlternativeEndings ?? new List<(LitigGameProgress completedGame, double weight)>() { (this, 1.0) };
            foreach (var currentState in possibleCurrentStates)
            {
                var p = currentState.completedGame;
                var o = LitigGameOptions;
                var consistentGameProgresses = o.LitigGameDisputeGenerator.InvertedCalculations_GenerateAllConsistentGameProgresses(o.NumLiabilitySignals == 1 ? 1 : p.PLiabilitySignalDiscrete, o.NumLiabilitySignals == 1 ? 1 : p.DLiabilitySignalDiscrete, o.NumLiabilitySignals == 1 ? 1 : p.CLiabilitySignalDiscrete, o.NumDamagesSignals == 1 ? 1 : p.PDamagesSignalDiscrete, o.NumDamagesSignals == 1 ? 1 : p.DDamagesSignalDiscrete, o.NumDamagesSignals == 1 ? 1 : p.CDamagesSignalDiscrete, p);
                //if (consistentGameProgresses.Any(x => ((LitigGameProgress)x.progress).BargainingRoundsComplete > 1))
                //    throw new Exception(); // DEBUG
                var weightedConsistentGameProgress = consistentGameProgresses.Select(x => (x.progress, currentState.weight * x.weight * initialWeight)).ToList();
                results.AddRange(weightedConsistentGameProgress);
            }
            return results;
        }
    }
}
