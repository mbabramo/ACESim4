using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class PatentDamagesGameProgressInfo : GameProgress
    {
        public List<double> InventorEstimatesInventionValue;
        public List<bool> InventorEntryDecisions;
        public bool MainInventorEnters => InventorEntryDecisions.First();
        public List<bool> InventorTryToInventDecisions;
        public bool MainInventorTries => InventorTryToInventDecisions.First();
        public List<double> InventorSpendDecisions;
        public List<bool> InventorSucceedsAtInvention;
        public int? WinnerOfPatent = null;
        public double InventorUtility;

        public override GameProgress DeepCopy()
        {
            PatentDamagesGameProgressInfo copy = new PatentDamagesGameProgressInfo();
            copy.InventorEstimatesInventionValue = InventorEstimatesInventionValue.ToList();
            copy.InventorEntryDecisions = InventorEntryDecisions.ToList();
            copy.InventorTryToInventDecisions = InventorTryToInventDecisions.ToList();
            copy.InventorSpendDecisions = InventorSpendDecisions.ToList();
            copy.WinnerOfPatent = WinnerOfPatent;
            copy.InventorUtility = InventorUtility;

            copy.GameComplete = this.GameComplete;

            base.CopyFieldInfo(copy);

            return copy;
        }


    }
}
