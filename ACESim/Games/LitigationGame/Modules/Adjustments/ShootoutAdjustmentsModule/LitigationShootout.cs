using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class LitigationShootout
    {
        // if damages are uncertain, then the damages used should be the amount claimed by the party making the settlement offer. The opposing party would then also have the right to accede to the damages request.
        public double SettlementOffer;
        public bool SettlementOfferIsPlaintiffs;
        public double MultiplierOfDamagesForShootout = 1.0;
        public double DiscountFactor = 1.0;

        public double NetPaymentFromDToP(bool pWins, double damagesAsDetermined, bool ignoreFixedCost = false)
        {
            // E.g., If plaintiff gives settlement offer of $80, the plaintiff is saying that the plaintiff's right is worth more than $80. In the extreme case, plaintiff thus must pay $80 to get an amount equal to the original damages. This gives plaintiff an incentive to announce an amount as low as possible, even though ordinarily in settlement, plaintiff has an incentive to be tough in the opposite direction. If it is only defendant's option to make plaintiff pay, then the discount factor should probably be <= 1, so that defendant only exercises the option when the plaintiff's settlement announcement is way off.
            // If defendant gives a settlement offer of $20, then defendant is saying that the plaintiff's right is worth no more than $20. In the extreme case, plaintiff may then spend $20 to get an amount equal to the original damages. This gives the defendant an incentive to say a number as high as possible, the opposite of defendant's ordinary incentive to be tough in settlement. If it is only the plaintiff's option to take advantage of defendant's settlement offer to make the defendant pay, then the discount factor should probably be >= 1, so we divide by the DiscountFactor specified.
            // Ordinarily, when D wins, damagesAsDetermined = 0. In this case, this routine should simply return the negative of the amount that the plaintiff must pay to defendant in the shootout. However, if D is entitled to something for victory (i.e., there is a side bet), then damages will be set to a negative number, reflecting the magnitude of the side bet. For example, it might be -$1000, with the negative sign representing that this is money the plaintiff will pay to the defendant rather than the other way around. There will still be a fixed payment from P to D, but the -$1000 must also be multiplied by the shootout multiplier.

            double fixedCostFromPToD = ignoreFixedCost ? 0 : SettlementOffer * MultiplierOfDamagesForShootout;
            if (SettlementOfferIsPlaintiffs)
                fixedCostFromPToD *= DiscountFactor;
            else
                fixedCostFromPToD /= DiscountFactor;
            return damagesAsDetermined * MultiplierOfDamagesForShootout - fixedCostFromPToD;
        }
    }
}
