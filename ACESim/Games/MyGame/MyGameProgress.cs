using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    class MyGameProgress : GameProgress
    {
        public double LitigationQuality;
        public int PlaintiffSignal;
        public int DefendantSignal;
        public double PlaintiffOffer;
        public double DefendantOffer;

        public override GameProgress DeepCopy()
        {
            MyGameProgress copy = new MyGameProgress();

            // copy.GameComplete = this.GameComplete;
            base.CopyFieldInfo(copy);
            copy.LitigationQuality = LitigationQuality;
            copy.PlaintiffSignal = PlaintiffSignal;
            copy.DefendantSignal = DefendantSignal;
            copy.PlaintiffOffer = PlaintiffOffer;
            copy.DefendantOffer = DefendantOffer;

            return copy;
        }
    }
}
