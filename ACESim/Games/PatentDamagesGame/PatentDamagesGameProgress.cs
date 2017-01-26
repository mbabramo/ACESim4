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
        public double FirstInventorWinsPatent = 0;
        public bool InventionOccurs;
        public double? InventorOffer;
        public double? UserOffer;
        public double? AgreedOnPrice;
        public bool InadvertentInfringement;
        public bool AgreementReached;
        public bool IntentionalInfringement;
        public bool InventionUsed;
        public double? WinnerPredictionUserWelfareChangeIntentionalInfringement;
        public double? UserPredictedWelfareChangeIntentionalInfringement;
        public double DamagesPaid;
        public double? HypotheticalDamages;
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
            copy.FirstInventorWinsPatent = FirstInventorWinsPatent;
            copy.InventionOccurs = InventionOccurs;
            copy.InventorOffer = InventorOffer;
            copy.UserOffer = UserOffer;
            copy.AgreedOnPrice = AgreedOnPrice;
            copy.InadvertentInfringement = InadvertentInfringement;
            copy.AgreementReached = AgreementReached;
            copy.IntentionalInfringement = IntentionalInfringement;
            copy.InventionUsed = InventionUsed;
            copy.WinnerPredictionUserWelfareChangeIntentionalInfringement = WinnerPredictionUserWelfareChangeIntentionalInfringement;
            copy.UserPredictedWelfareChangeIntentionalInfringement = UserPredictedWelfareChangeIntentionalInfringement;
            copy.DamagesPaid = DamagesPaid;
            copy.HypotheticalDamages = HypotheticalDamages;
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
