﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class MyGameProgress : GameProgress
    {
        public double LitigationQuality;
        public byte PSignal;
        public byte DSignal;
        public double PSignalUniform;
        public double DSignalUniform;
        public List<double> POffers;
        public List<bool> PResponses;
        public List<double> DOffers;
        public List<bool> DResponses;
        public bool CaseSettles;
        public double? SettlementValue;
        public bool PWinsAtTrial;
        public double PWelfare;
        public double DWelfare;

        public double? PFirstOffer => (double?)POffers.FirstOrDefault() ?? null;
        public double? DFirstOffer => (double?)DOffers.FirstOrDefault() ?? null;
        public bool? PFirstResponse => (bool?)PResponses.FirstOrDefault() ?? null;
        public bool? DFirstResponse => (bool?)DResponses.FirstOrDefault() ?? null;
        public double? PLastOffer => (double?)POffers.LastOrDefault() ?? null;
        public double? DLastOffer => (double?)DOffers.LastOrDefault() ?? null;
        public bool? PLastResponse => (bool?)PResponses.LastOrDefault() ?? null;
        public bool? DLastResponse => (bool?)DResponses.LastOrDefault() ?? null;
        public bool BothPlayersHaveCompletedRound => POffers?.Count() == DResponses?.Count() && DOffers?.Count() == PResponses?.Count();
        public bool RoundIsComplete(bool playersMovingSimultaneously, bool pGoesFirstIfNotSimultaneous)
        {
            if (playersMovingSimultaneously)
                return POffers != null && DOffers != null && POffers.Count() == DOffers.Count();
            else if (pGoesFirstIfNotSimultaneous)
                return POffers != null && DResponses != null && POffers.Count() == DResponses.Count();
            else
                return DOffers != null && PResponses != null && DOffers.Count() == PResponses.Count();
        }
        public void CheckSettlement(bool playersMovingSimultaneously, bool pGoesFirstIfNotSimultaneous)
        {
            CaseSettles = SettlementReached(playersMovingSimultaneously, pGoesFirstIfNotSimultaneous);
            if (CaseSettles)
                SetSettlementValue(playersMovingSimultaneously, pGoesFirstIfNotSimultaneous);
        }
        private bool SettlementReached(bool playersMovingSimultaneously, bool pGoesFirstIfNotSimultaneous)
        {
            if (!RoundIsComplete(playersMovingSimultaneously, pGoesFirstIfNotSimultaneous))
                return false;
            if (playersMovingSimultaneously)
                return DLastOffer > PLastOffer;
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
            copy.LitigationQuality = LitigationQuality;
            copy.PSignal = PSignal;
            copy.DSignal = DSignal;
            copy.PSignalUniform = PSignalUniform;
            copy.DSignalUniform = DSignalUniform;
            copy.POffers = POffers.ToList();
            copy.DOffers = DOffers.ToList();
            copy.PResponses = PResponses.ToList();
            copy.DResponses = DResponses.ToList();
            copy.CaseSettles = CaseSettles;
            copy.SettlementValue = SettlementValue;
            copy.PWinsAtTrial = PWinsAtTrial;
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
