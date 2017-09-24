using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class MyGameProgress : GameProgress
    {
        public bool DisputeArises;
        public bool IsTrulyLiable;
        public byte LitigationQualityDiscrete;
        public double? LitigationQualityUniform;
        public byte PNoiseDiscrete;
        public byte DNoiseDiscrete;
        public byte PSignalDiscrete;
        public byte DSignalDiscrete;
        public double PSignalUniform;
        public double DSignalUniform;
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
        public double FalsePositiveExpenditures;
        public double FalseNegativeShortfall;
        public double TotalExpensesIncurred;
        public double PreDisputeSWelfare;
        public double PInitialWealth;
        public double DInitialWealth;
        public double? DamagesAlleged;
        public double PChangeWealth;
        public double DChangeWealth;
        public double? PFinalWealthWithBestOffer;
        public double? DFinalWealthWithBestOffer;
        public double PFinalWealth;
        public double DFinalWealth;
        public double PWelfare;
        public double DWelfare;
        public MyGameDisputeGeneratorActions DisputeGeneratorActions;
        public MyGamePretrialActions PretrialActions;

        public override string ToString()
        {
            return
                $"DisputeArises {DisputeArises} IsTrulyLiable {IsTrulyLiable} LitigationQualityDiscrete {LitigationQualityDiscrete} LitigationQualityUniform {LitigationQualityUniform} PSignalDiscrete {PSignalDiscrete} DSignalDiscrete {DSignalDiscrete} PSignalUniform {PSignalUniform} DSignalUniform {DSignalUniform} PFiles {PFiles} DAnswers {DAnswers} BargainingRoundsComplete {BargainingRoundsComplete} PLastAgreesToBargain {PLastAgreesToBargain} DLastAgreesToBargain {DLastAgreesToBargain} PLastOffer {PLastOffer} DLastOffer {DLastOffer} CaseSettles {CaseSettles} SettlementValue {SettlementValue} PAbandons {PAbandons} DDefaults {DDefaults} TrialOccurs {TrialOccurs} PWinsAtTrial {PWinsAtTrial} PFinalWealthWithBestOffer {PFinalWealthWithBestOffer} DFinalWealthWithBestOffer {DFinalWealthWithBestOffer} PFinalWealth {PFinalWealth} DFinalWealth {DFinalWealth} PWelfare {PWelfare} DWelfare {DWelfare} FalsePositiveExpenditures {FalsePositiveExpenditures} FalseNegativeShortfall {FalseNegativeShortfall} TotalExpensesIncurred {TotalExpensesIncurred} ";
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
        public bool BothPlayersHaveCompletedRound => POffers?.Count() == DResponses?.Count() && DOffers?.Count() == PResponses?.Count();
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
                    var pMissedOpportunity = MyGame.CalculateGameOutcome(gameDefinition, DisputeGeneratorActions, PretrialActions, PInitialWealth, DInitialWealth, (double) DamagesAlleged, PFiles, PAbandons, DAnswers, DDefaults, (double) DLastOffer * (double)DamagesAlleged, true /* ignored */, (byte) (BargainingRoundsComplete + 1), null, null);
                    if (pMissedOpportunity.PFinalWealth > PFinalWealthWithBestOffer || PFinalWealthWithBestOffer == null)
                        PFinalWealthWithBestOffer = pMissedOpportunity.PFinalWealth;
                }
                if (playersMovingSimultaneously || pGoesFirstIfNotSimultaneous)
                { // plaintiff has made an offer this round
                    var dMissedOpportunity = MyGame.CalculateGameOutcome(gameDefinition, DisputeGeneratorActions, PretrialActions, PInitialWealth, DInitialWealth, (double)DamagesAlleged, PFiles, PAbandons, DAnswers, DDefaults, (double)PLastOffer * (double)DamagesAlleged, true /* ignored */, (byte)(BargainingRoundsComplete + 1), null, null);
                    if (dMissedOpportunity.DFinalWealth > DFinalWealthWithBestOffer || DFinalWealthWithBestOffer == null)
                        DFinalWealthWithBestOffer = dMissedOpportunity.DFinalWealth;
                }
            }
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
                SettlementValue = (PLastOffer + DLastOffer) * (double)DamagesAlleged / 2.0;
            else if (pGoesFirstIfNotSimultaneous)
                SettlementValue = PLastOffer * (double)DamagesAlleged;
            else
                SettlementValue = DLastOffer * (double)DamagesAlleged;
        }

        public override GameProgress DeepCopy()
        {
            MyGameProgress copy = new MyGameProgress();

            // copy.GameComplete = this.GameComplete;
            base.CopyFieldInfo(copy);
            copy.DisputeArises = DisputeArises;
            copy.IsTrulyLiable = IsTrulyLiable;
            copy.LitigationQualityDiscrete = LitigationQualityDiscrete;
            copy.PNoiseDiscrete = PNoiseDiscrete;
            copy.DNoiseDiscrete = DNoiseDiscrete;
            copy.PSignalDiscrete = PSignalDiscrete;
            copy.DSignalDiscrete = DSignalDiscrete;
            copy.LitigationQualityUniform = LitigationQualityUniform;
            copy.PSignalDiscrete = PSignalDiscrete;
            copy.DSignalDiscrete = DSignalDiscrete;
            copy.PSignalUniform = PSignalUniform;
            copy.DSignalUniform = DSignalUniform;
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
            copy.PInitialWealth = PInitialWealth;
            copy.DInitialWealth = DInitialWealth;
            copy.DamagesAlleged = DamagesAlleged;
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
            copy.PreDisputeSWelfare = PreDisputeSWelfare;
            copy.DisputeGeneratorActions = DisputeGeneratorActions;
            copy.PretrialActions = PretrialActions;

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
    }
}
