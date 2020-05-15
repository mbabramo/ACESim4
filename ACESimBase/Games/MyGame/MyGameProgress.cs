using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class MyGameProgress : GameProgress
    {
        public MyGameDefinition MyGameDefinition => (MyGameDefinition)GameDefinition;
        public MyGameOptions MyGameOptions => MyGameDefinition.Options;



        public bool DisputeArises;
        public bool IsTrulyLiable;
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
        public bool DWinsAtTrial => TrialOccurs && !PWinsAtTrial;

        public MyGameDisputeGeneratorActions DisputeGeneratorActions;
        public MyGamePretrialActions PretrialActions;
        public MyGameRunningSideBetsActions RunningSideBetsActions;

        public byte NumChips;
        public double DamagesAwarded;

        public byte LiabilityStrengthDiscrete;
        public byte PLiabilityNoiseDiscrete;
        public byte DLiabilityNoiseDiscrete;
        public byte PLiabilitySignalDiscrete;
        public byte DLiabilitySignalDiscrete;

        public byte DamagesStrengthDiscrete;
        public byte PDamagesNoiseDiscrete;
        public byte DDamagesNoiseDiscrete;
        public byte PDamagesSignalDiscrete;
        public byte DDamagesSignalDiscrete;

        public double? LiabilityStrengthUniform;
        public double PLiabilitySignalUniform;
        public double DLiabilitySignalUniform;
        public double? DamagesStrengthUniform;
        public double PDamagesSignalUniform;
        public double DDamagesSignalUniform;

        public double FalsePositiveExpenditures;
        public double FalseNegativeShortfall;
        public double TotalExpensesIncurred;
        public double PreDisputeSharedWelfare;

        public double PChangeWealth;
        public double DChangeWealth;
        public double? PFinalWealthWithBestOffer;
        public double? DFinalWealthWithBestOffer;
        public double PFinalWealth;
        public double DFinalWealth;
        public double PWelfare;
        public double DWelfare;
        public List<double> POfferMixedness;
        public List<double> DOfferMixedness;

        public override string ToString()
        {
            return
                $"DisputeArises {DisputeArises} IsTrulyLiable {IsTrulyLiable} LiabilityStrengthDiscrete {LiabilityStrengthDiscrete} LiabilityStrengthUniform {LiabilityStrengthUniform} PLiabilitySignalDiscrete {PLiabilitySignalDiscrete} DLiabilitySignalDiscrete {DLiabilitySignalDiscrete} PLiabilitySignalUniform {PLiabilitySignalUniform} DLiabilitySignalUniform {DLiabilitySignalUniform} DamagesStrengthDiscrete {DamagesStrengthDiscrete} DamagesStrengthUniform {DamagesStrengthUniform} PDamagesSignalDiscrete {PDamagesSignalDiscrete} DDamagesSignalDiscrete {DDamagesSignalDiscrete} PDamagesSignalUniform {PDamagesSignalUniform} DDamagesSignalUniform {DDamagesSignalUniform} PFiles {PFiles} DAnswers {DAnswers} BargainingRoundsComplete {BargainingRoundsComplete} PLastAgreesToBargain {PLastAgreesToBargain} DLastAgreesToBargain {DLastAgreesToBargain} PLastOffer {PLastOffer} DLastOffer {DLastOffer} CaseSettles {CaseSettles} SettlementValue {SettlementValue} PAbandons {PAbandons} DDefaults {DDefaults} TrialOccurs {TrialOccurs} PWinsAtTrial {PWinsAtTrial} DamagesAwarded {DamagesAwarded} PFinalWealthWithBestOffer {PFinalWealthWithBestOffer} DFinalWealthWithBestOffer {DFinalWealthWithBestOffer} PFinalWealth {PFinalWealth} DFinalWealth {DFinalWealth} PWelfare {PWelfare} DWelfare {DWelfare} FalsePositiveExpenditures {FalsePositiveExpenditures} FalseNegativeShortfall {FalseNegativeShortfall} TotalExpensesIncurred {TotalExpensesIncurred} NumChips {NumChips}";
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

        public bool PAgreesToBargainInRound(int bargainingRoundNum) => !MyGameOptions.IncludeAgreementToBargainDecisions || ((PAgreesToBargain?.Count() ?? 0) >= bargainingRoundNum && PAgreesToBargain[bargainingRoundNum - 1]);
        public bool DAgreesToBargainInRound(int bargainingRoundNum) => !MyGameOptions.IncludeAgreementToBargainDecisions || ((DAgreesToBargain?.Count() ?? 0) >= bargainingRoundNum && DAgreesToBargain[bargainingRoundNum - 1]);
        public bool BothAgreeToBargainInRound(int bargainingRoundNum) => PAgreesToBargainInRound(bargainingRoundNum) && DAgreesToBargainInRound(bargainingRoundNum);

        public void ConcludeMainPortionOfBargainingRound(MyGameDefinition gameDefinition)
        {
            bool playersMovingSimultaneously = gameDefinition.Options.BargainingRoundsSimultaneous;
            bool pGoesFirstIfNotSimultaneous = playersMovingSimultaneously || gameDefinition.Options.PGoesFirstIfNotSimultaneous[BargainingRoundsComplete];
            CaseSettles = SettlementReached(playersMovingSimultaneously, pGoesFirstIfNotSimultaneous);
            if (CaseSettles)
                SetSettlementValue(playersMovingSimultaneously, pGoesFirstIfNotSimultaneous);
            if (CaseSettles)
            {
                BargainingRoundsComplete++;
                GameComplete = true;
            }
            else
            {
                if (playersMovingSimultaneously || !pGoesFirstIfNotSimultaneous)
                { // defendant has made an offer this round
                    var pMissedOpportunity = MyGame.CalculateGameOutcome(gameDefinition, DisputeGeneratorActions, PretrialActions, RunningSideBetsActions, gameDefinition.Options.PInitialWealth, gameDefinition.Options.DInitialWealth, PFiles, PAbandons, DAnswers, DDefaults, (double) DLastOffer * (double)gameDefinition.Options.DamagesMax, true /* ignored */, 0, (byte) (BargainingRoundsComplete + 1), null, null, POffers, PResponses, DOffers, DResponses);
                    if (pMissedOpportunity.PFinalWealth > PFinalWealthWithBestOffer || PFinalWealthWithBestOffer == null)
                        PFinalWealthWithBestOffer = pMissedOpportunity.PFinalWealth;
                }
                if (playersMovingSimultaneously || pGoesFirstIfNotSimultaneous)
                { // plaintiff has made an offer this round
                    var dMissedOpportunity = MyGame.CalculateGameOutcome(gameDefinition, DisputeGeneratorActions, PretrialActions, RunningSideBetsActions, gameDefinition.Options.PInitialWealth, gameDefinition.Options.DInitialWealth, PFiles, PAbandons, DAnswers, DDefaults, (double)PLastOffer * (double)gameDefinition.Options.DamagesMax, true /* ignored */, 0, (byte)(BargainingRoundsComplete + 1), null, null, POffers, PResponses, DOffers, DResponses);
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
                SettlementValue = (PLastOffer + DLastOffer) * (double)MyGameDefinition.Options.DamagesMax / 2.0;
            else if (pGoesFirstIfNotSimultaneous)
                SettlementValue = PLastOffer * (double)MyGameDefinition.Options.DamagesMax;
            else
                SettlementValue = DLastOffer * (double)MyGameDefinition.Options.DamagesMax;
        }

        public override GameProgress DeepCopy()
        {
            MyGameProgress copy = new MyGameProgress();

            // copy.GameComplete = this.GameComplete;
            base.CopyFieldInfo(copy);
            copy.DisputeArises = DisputeArises;
            copy.IsTrulyLiable = IsTrulyLiable;
            copy.LiabilityStrengthDiscrete = LiabilityStrengthDiscrete;
            copy.PLiabilityNoiseDiscrete = PLiabilityNoiseDiscrete;
            copy.DLiabilityNoiseDiscrete = DLiabilityNoiseDiscrete;
            copy.PLiabilitySignalDiscrete = PLiabilitySignalDiscrete;
            copy.DLiabilitySignalDiscrete = DLiabilitySignalDiscrete;
            copy.LiabilityStrengthUniform = LiabilityStrengthUniform;
            copy.PLiabilitySignalDiscrete = PLiabilitySignalDiscrete;
            copy.DLiabilitySignalDiscrete = DLiabilitySignalDiscrete;
            copy.PLiabilitySignalUniform = PLiabilitySignalUniform;
            copy.DLiabilitySignalUniform = DLiabilitySignalUniform;

            copy.DamagesStrengthDiscrete = DamagesStrengthDiscrete;
            copy.PDamagesNoiseDiscrete = PDamagesNoiseDiscrete;
            copy.DDamagesNoiseDiscrete = DDamagesNoiseDiscrete;
            copy.PDamagesSignalDiscrete = PDamagesSignalDiscrete;
            copy.DDamagesSignalDiscrete = DDamagesSignalDiscrete;
            copy.DamagesStrengthUniform = DamagesStrengthUniform;
            copy.PDamagesSignalDiscrete = PDamagesSignalDiscrete;
            copy.DDamagesSignalDiscrete = DDamagesSignalDiscrete;
            copy.PDamagesSignalUniform = PDamagesSignalUniform;
            copy.DDamagesSignalUniform = DDamagesSignalUniform;

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
            copy.POfferMixedness = POfferMixedness?.ToList();
            copy.DOfferMixedness = DOfferMixedness?.ToList();
            copy.PResponses = PResponses?.ToList();
            copy.DResponses = DResponses?.ToList();
            copy.CaseSettles = CaseSettles;
            copy.SettlementValue = SettlementValue;
            copy.TrialOccurs = TrialOccurs;
            copy.PWinsAtTrial = PWinsAtTrial;
            copy.DamagesAwarded = DamagesAwarded;
            copy.PChangeWealth = PChangeWealth;
            copy.DChangeWealth = DChangeWealth;
            copy.PFinalWealthWithBestOffer = PFinalWealthWithBestOffer;
            copy.DFinalWealthWithBestOffer = DFinalWealthWithBestOffer;
            copy.PFinalWealth = PFinalWealth;
            copy.DFinalWealth = DFinalWealth;
            copy.PWelfare = PWelfare;
            copy.DWelfare = DWelfare;
            copy.FalsePositiveExpenditures = FalsePositiveExpenditures;
            copy.FalseNegativeShortfall = FalseNegativeShortfall;
            copy.TotalExpensesIncurred = TotalExpensesIncurred;
            copy.PreDisputeSharedWelfare = PreDisputeSharedWelfare;
            copy.DisputeGeneratorActions = DisputeGeneratorActions;
            copy.PretrialActions = PretrialActions;
            copy.RunningSideBetsActions = RunningSideBetsActions;

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

        public void CalculateGameOutcome()
        {
            MyGameDefinition gameDefinition = (MyGameDefinition)GameDefinition;
            var outcome = MyGame.CalculateGameOutcome(gameDefinition, DisputeGeneratorActions, PretrialActions, RunningSideBetsActions, gameDefinition.Options.PInitialWealth, gameDefinition.Options.DInitialWealth, PFiles, PAbandons, DAnswers, DDefaults, SettlementValue, PWinsAtTrial, DamagesAwarded, BargainingRoundsComplete, PFinalWealthWithBestOffer, DFinalWealthWithBestOffer, POffers, PResponses, DOffers, DResponses);
            DisputeArises = gameDefinition.Options.MyGameDisputeGenerator.PotentialDisputeArises(gameDefinition, DisputeGeneratorActions);
            PChangeWealth = outcome.PChangeWealth;
            DChangeWealth = outcome.DChangeWealth;
            PFinalWealth = outcome.PFinalWealth;
            DFinalWealth = outcome.DFinalWealth;
            PWelfare = outcome.PWelfare;
            DWelfare = outcome.DWelfare;
            TrialOccurs = outcome.TrialOccurs;
            NumChips = outcome.NumChips;
        }

        public override void RecalculateGameOutcome()
        {
            CalculateGameOutcome();
        }
    }
}
