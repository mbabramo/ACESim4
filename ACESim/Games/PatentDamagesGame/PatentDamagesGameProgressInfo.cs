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
            copy.InventorEstimatesInventionValue = InventorEstimatesInventionValue == null ? null : InventorEstimatesInventionValue.ToList();
            copy.InventorEntryDecisions = InventorEntryDecisions == null ? null : InventorEntryDecisions.ToList();
            copy.InventorTryToInventDecisions = InventorTryToInventDecisions == null ? null : InventorTryToInventDecisions.ToList();
            copy.InventorSpendDecisions = InventorSpendDecisions == null ? null : InventorSpendDecisions.ToList();
            copy.InventorSucceedsAtInvention = InventorSucceedsAtInvention == null ? null : InventorSucceedsAtInvention.ToList();
            copy.WinnerOfPatent = WinnerOfPatent;
            copy.InventorUtility = InventorUtility;

            copy.GameComplete = this.GameComplete;

            base.CopyFieldInfo(copy);

            return copy;
        }


    }
}
