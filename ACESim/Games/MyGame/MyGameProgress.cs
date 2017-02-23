using System;
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
        public double POffer;
        public double DOffer;
        public bool CaseSettles;
        public double? SettlementValue;
        public byte CourtSignal;
        public bool PWinsAtTrial;
        public double PWelfare;
        public double DWelfare;

        public override GameProgress DeepCopy()
        {
            MyGameProgress copy = new MyGameProgress();

            // copy.GameComplete = this.GameComplete;
            base.CopyFieldInfo(copy);
            copy.LitigationQuality = LitigationQuality;
            copy.PSignal = PSignal;
            copy.DSignal = DSignal;
            copy.POffer = POffer;
            copy.DOffer = DOffer;
            copy.CaseSettles = CaseSettles;
            copy.SettlementValue = SettlementValue;
            copy.CourtSignal = CourtSignal;
            copy.PWinsAtTrial = PWinsAtTrial;
            copy.PWelfare = PWelfare;
            copy.DWelfare = DWelfare;

            return copy;
        }
    }
}
