using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class PatentDamagesGameProgress : GameProgress
    {
        public bool HighValue;
        public List<double> InventorEstimatesHighestInventionValue;
        public List<bool> InventorEntryDecisions;
        public bool MainInventorEnters;
        public bool SomeoneEnters;
        public int NumberEntrants;
        public double ForecastAfterEntry; 
        public List<bool> InventorTryToInventDecisions;
        public bool MainInventorTries;
        public bool SomeoneTries;
        public int NumberTrying;
        public List<double> InventorSpendDecisions;
        public double MainInventorSpendMultiple;
        public double TotalSpendingIncludingEntry;
        public double TotalResearchSpending;
        public double? AverageSpendingOfTriers;
        public double[] ProbabilityInventingSuccessfully;
        public double[] ProbabilityWinningPatent;
        public double ProbabilitySomeoneWins;
        public double ProbabilityFirstInventorWins;
        public List<bool> InventorSucceedsAtInvention;
        public int? WinnerOfPatent = null;
        public double FirstInventorWinsPatent = 0;
        public bool InventionOccurs;

        public double? InventorSetPrice;
        public double? CourtSetPrice;
        public double? UserAnticipatedPrice;
        public double ProportionUsingProduct;
        public double ProportionIntentionallyInfringing;
        public double ProportionInadvertentlyInfringing;
        public double? AverageGrossUserBenefit;
        public double? AverageUserCost;
        public double? PerUserReceipts;
        public double TotalReceipts;

        public double AverageInventorUtility;
        public double DeltaInventorsUtility;
        public double MainInventorUtility;
        public double UserUtility;
        public double SocialWelfare;
        public double PrivateWelfare;

        public override GameProgress DeepCopy()
        {
            PatentDamagesGameProgress copy = new PatentDamagesGameProgress();
            copy.HighValue = HighValue;
            copy.InventorEstimatesHighestInventionValue = InventorEstimatesHighestInventionValue == null ? null : InventorEstimatesHighestInventionValue.ToList();
            copy.InventorEntryDecisions = InventorEntryDecisions == null ? null : InventorEntryDecisions.ToList();
            copy.MainInventorEnters = MainInventorEnters;
            copy.SomeoneEnters = SomeoneEnters;
            copy.NumberEntrants = NumberEntrants;
            copy.ForecastAfterEntry = ForecastAfterEntry;
            copy.InventorTryToInventDecisions = InventorTryToInventDecisions == null ? null : InventorTryToInventDecisions.ToList();
            copy.MainInventorTries = MainInventorTries;
            copy.SomeoneTries = SomeoneTries;
            copy.NumberTrying = NumberTrying;
            copy.InventorSpendDecisions = InventorSpendDecisions == null ? null : InventorSpendDecisions.ToList();
            copy.MainInventorSpendMultiple = MainInventorSpendMultiple;
            copy.TotalSpendingIncludingEntry = TotalSpendingIncludingEntry;
            copy.TotalResearchSpending = TotalResearchSpending;
            copy.AverageSpendingOfTriers = AverageSpendingOfTriers;
            copy.ProbabilityInventingSuccessfully = ProbabilityInventingSuccessfully == null ? null : ProbabilityInventingSuccessfully.ToArray();
            copy.ProbabilityWinningPatent = ProbabilityWinningPatent == null ? null : ProbabilityWinningPatent.ToArray();
            copy.ProbabilitySomeoneWins = ProbabilitySomeoneWins;
            copy.ProbabilityFirstInventorWins = ProbabilityFirstInventorWins;
            copy.InventorSucceedsAtInvention = InventorSucceedsAtInvention == null ? null : InventorSucceedsAtInvention.ToList();
            copy.WinnerOfPatent = WinnerOfPatent;
            copy.FirstInventorWinsPatent = FirstInventorWinsPatent;
            copy.InventionOccurs = InventionOccurs;
            copy.InventorSetPrice = InventorSetPrice;
            copy.CourtSetPrice = CourtSetPrice;
            copy.UserAnticipatedPrice = UserAnticipatedPrice;
            copy.ProportionUsingProduct = ProportionUsingProduct;
            copy.AverageGrossUserBenefit = AverageGrossUserBenefit;
            copy.AverageUserCost = AverageUserCost;
            copy.PerUserReceipts = PerUserReceipts;
            copy.TotalReceipts = TotalReceipts;
            copy.AverageInventorUtility = AverageInventorUtility;
            copy.DeltaInventorsUtility = DeltaInventorsUtility;
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
