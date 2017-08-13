using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class MyGameProgress : GameProgress
    {
        public byte LitigationQualityDiscrete;
        public double LitigationQualityUniform;
        public byte PSignalDiscrete;
        public byte DSignalDiscrete;
        public double PSignalUniform;
        public double DSignalUniform;
        public int BargainingRoundsComplete;
        public List<double> POffers;
        public List<bool> PResponses;
        public List<double> DOffers;
        public List<bool> DResponses;
        public bool CaseSettles;
        public double? SettlementValue;
        public bool TrialOccurs;
        public bool PWinsAtTrial;
        public double PInitialWealth;
        public double DInitialWealth;
        public double DamagesAlleged = 1.0;
        public double PChangeWealth;
        public double DChangeWealth;
        public double PFinalWealth;
        public double DFinalWealth;
        public double PWelfare;
        public double DWelfare;

        public double? PFirstOffer => (double?)POffers?.FirstOrDefault() ?? null;
        public double? DFirstOffer => (double?)DOffers?.FirstOrDefault() ?? null;
        public bool? PFirstResponse => (bool?)PResponses?.FirstOrDefault() ?? null;
        public bool? DFirstResponse => (bool?)DResponses?.FirstOrDefault() ?? null;
        public double? PLastOffer => (double?)POffers?.LastOrDefault() ?? null;
        public double? DLastOffer => (double?)DOffers?.LastOrDefault() ?? null;
        public bool? PLastResponse => (bool?)PResponses?.LastOrDefault() ?? null;
        public bool? DLastResponse => (bool?)DResponses?.LastOrDefault() ?? null;
        public bool BothPlayersHaveCompletedRound => POffers?.Count() == DResponses?.Count() && DOffers?.Count() == PResponses?.Count();
        public bool RoundIsComplete(bool playersMovingSimultaneously, bool pGoesFirstIfNotSimultaneous)
        {
            return (POffers?.Count() ?? 0) + (PResponses?.Count() ?? 0) == (DOffers?.Count() ?? 0) + (DResponses?.Count() ?? 0);
            // NOTE: The following code doesn't work right when we have a simultaneous move round followed by an offer-response round. Maybe we shouldn't be mixing them.
            //if (playersMovingSimultaneously)
            //    return POffers != null && DOffers != null && POffers.Count() == DOffers.Count();
            //else if (pGoesFirstIfNotSimultaneous)
            //    return POffers != null && DResponses != null && POffers.Count() == DResponses.Count();
            //else
            //    return DOffers != null && PResponses != null && DOffers.Count() == PResponses.Count();
        }
        public void UpdateProgress(MyGameDefinition gameDefinition)
        {
            bool playersMovingSimultaneously = gameDefinition.BargainingRoundsSimultaneous[BargainingRoundsComplete];
            bool pGoesFirstIfNotSimultaneous = gameDefinition.BargainingRoundsPGoesFirstIfNotSimultaneous[BargainingRoundsComplete];
            if (!RoundIsComplete(playersMovingSimultaneously, pGoesFirstIfNotSimultaneous))
                return;
            BargainingRoundsComplete++;
            CaseSettles = SettlementReached(playersMovingSimultaneously, pGoesFirstIfNotSimultaneous);
            if (CaseSettles)
                SetSettlementValue(playersMovingSimultaneously, pGoesFirstIfNotSimultaneous);
            if (CaseSettles)
                GameComplete = true;
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
                SettlementValue = (PLastOffer + DLastOffer) / 2.0;
            else if (pGoesFirstIfNotSimultaneous)
                SettlementValue = PLastOffer;
            else
                SettlementValue = DLastOffer;
        }

        public override GameProgress DeepCopy()
        {
            MyGameProgress copy = new MyGameProgress();

            // copy.GameComplete = this.GameComplete;
            base.CopyFieldInfo(copy);
            copy.LitigationQualityDiscrete = LitigationQualityDiscrete;
            copy.PSignalDiscrete = PSignalDiscrete;
            copy.DSignalDiscrete = DSignalDiscrete;
            copy.LitigationQualityUniform = LitigationQualityUniform;
            copy.PSignalUniform = PSignalUniform;
            copy.DSignalUniform = DSignalUniform;
            copy.BargainingRoundsComplete = BargainingRoundsComplete;
            copy.POffers = POffers == null ? null : POffers.ToList();
            copy.DOffers = DOffers == null ? null : DOffers.ToList();
            copy.PResponses = PResponses == null ? null : PResponses.ToList();
            copy.DResponses = DResponses == null ? null : DResponses.ToList();
            copy.CaseSettles = CaseSettles;
            copy.SettlementValue = SettlementValue;
            copy.TrialOccurs = TrialOccurs;
            copy.PWinsAtTrial = PWinsAtTrial;
            copy.PInitialWealth = PInitialWealth;
            copy.DInitialWealth = DInitialWealth;
            copy.DamagesAlleged = DamagesAlleged;
            copy.PChangeWealth = PChangeWealth;
            copy.DChangeWealth = DChangeWealth;
            copy.PFinalWealth = PFinalWealth;
            copy.DFinalWealth = DFinalWealth;
            copy.PWelfare = PWelfare;
            copy.DWelfare = DWelfare;

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
