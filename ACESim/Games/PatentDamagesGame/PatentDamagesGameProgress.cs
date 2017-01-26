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
        public bool MainInventorEnters;
        public bool SomeoneEnters;
        public int NumberEntrants;
        public double ForecastAfterEntry; 
        public List<bool> InventorTryToInventDecisions;
        public bool MainInventorTries;
        public bool SomeoneTries;
        public int NumberTrying;
        public double ForecastAfterInvestment;
        public List<double> InventorSpendDecisions;
        public double AverageSpendingOfEntrants;
        public List<bool> InventorSucceedsAtInvention;
        public int? WinnerOfPatent = null;
        public bool InventionOccurs;
        public double? Price;
        public bool InadvertentInfringement;
        public bool PriceAccepted;
        public bool IntentionalInfringement;
        public bool InventionUsed;
        public double? Damages;
        public double AmountPaid;

        public double AverageInventorUtility;
        public double MainInventorUtility;
        public double UserUtility;
        public double SocialWelfare;
        public double PrivateWelfare;

        public override GameProgress DeepCopy()
        {
            PatentDamagesGameProgress copy = new PatentDamagesGameProgress();
            copy.InventorEstimatesInventionValue = InventorEstimatesInventionValue == null ? null : InventorEstimatesInventionValue.ToList();
            copy.InventorEntryDecisions = InventorEntryDecisions == null ? null : InventorEntryDecisions.ToList();
            copy.MainInventorEnters = MainInventorEnters;
            copy.SomeoneEnters = SomeoneEnters;
            copy.NumberEntrants = NumberEntrants;
            copy.ForecastAfterEntry = ForecastAfterEntry;
            copy.InventorTryToInventDecisions = InventorTryToInventDecisions == null ? null : InventorTryToInventDecisions.ToList();
            copy.MainInventorTries = MainInventorTries;
            copy.SomeoneTries = SomeoneTries;
            copy.NumberTrying = NumberTrying;
            copy.ForecastAfterInvestment = ForecastAfterInvestment;
            copy.InventorSpendDecisions = InventorSpendDecisions == null ? null : InventorSpendDecisions.ToList();
            copy.AverageSpendingOfEntrants = AverageSpendingOfEntrants;
            copy.InventorSucceedsAtInvention = InventorSucceedsAtInvention == null ? null : InventorSucceedsAtInvention.ToList();
            copy.WinnerOfPatent = WinnerOfPatent;
            copy.InventionOccurs = InventionOccurs;
            copy.Price = Price;
            copy.InadvertentInfringement = InadvertentInfringement;
            copy.PriceAccepted = PriceAccepted;
            copy.IntentionalInfringement = IntentionalInfringement;
            copy.InventionUsed = InventionUsed;
            copy.Damages = Damages;
            copy.AmountPaid = AmountPaid;
            copy.AverageInventorUtility = AverageInventorUtility;
            copy.MainInventorUtility = MainInventorUtility;
            copy.UserUtility = UserUtility;
            copy.SocialWelfare = SocialWelfare;
            copy.PrivateWelfare = PrivateWelfare;


            copy.GameComplete = this.GameComplete;

            base.CopyFieldInfo(copy);

            return copy;
        }


    }
}
