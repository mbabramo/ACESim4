using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class PatentDamagesGameProgress : GameProgress
    {
        public List<double> InventorEstimatesInventionValue;
        public List<bool> InventorEntryDecisions;
        public bool MainInventorEnters => InventorEntryDecisions.First();
        public double ForecastAfterEntry; 
        public List<bool> InventorTryToInventDecisions;
        public bool MainInventorTries => InventorTryToInventDecisions.First();
        public double ForecastAfterInvestment;
        public List<double> InventorSpendDecisions;
        public List<bool> InventorSucceedsAtInvention;
        public int? WinnerOfPatent = null;
        public bool InventionOccurs => WinnerOfPatent != null;
        public double? Price;
        public bool InadvertentInfringement;
        public bool PriceAccepted;
        public bool IntentionalInfringement;
        public bool InventionUsed => InadvertentInfringement || PriceAccepted || IntentionalInfringement;
        public double? Damages;
        public double AmountPaid => PriceAccepted ? Price ?? 0 : (InventionUsed ? (double) Damages : 0);

        public double InventorUtility;
        public double UserUtility;
        public double SocialWelfare;

        public override GameProgress DeepCopy()
        {
            PatentDamagesGameProgress copy = new PatentDamagesGameProgress();
            copy.InventorEstimatesInventionValue = InventorEstimatesInventionValue == null ? null : InventorEstimatesInventionValue.ToList();
            copy.InventorEntryDecisions = InventorEntryDecisions == null ? null : InventorEntryDecisions.ToList();
            copy.ForecastAfterEntry = ForecastAfterEntry;
            copy.InventorTryToInventDecisions = InventorTryToInventDecisions == null ? null : InventorTryToInventDecisions.ToList();
            copy.InventorSpendDecisions = InventorSpendDecisions == null ? null : InventorSpendDecisions.ToList();
            copy.ForecastAfterInvestment = ForecastAfterInvestment;
            copy.InventorSucceedsAtInvention = InventorSucceedsAtInvention == null ? null : InventorSucceedsAtInvention.ToList();
            copy.WinnerOfPatent = WinnerOfPatent;
            copy.Price = Price;
            copy.InadvertentInfringement = InadvertentInfringement;
            copy.PriceAccepted = PriceAccepted;
            copy.IntentionalInfringement = IntentionalInfringement;
            copy.Damages = Damages;
            copy.InventorUtility = InventorUtility;
            copy.UserUtility = UserUtility;
            copy.SocialWelfare = SocialWelfare;


            copy.GameComplete = this.GameComplete;

            base.CopyFieldInfo(copy);

            return copy;
        }


    }
}
