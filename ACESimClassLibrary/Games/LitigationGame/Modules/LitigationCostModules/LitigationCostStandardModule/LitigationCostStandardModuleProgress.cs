using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class LitigationCostStandardModuleProgress : LitigationCostModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public LitigationCostStandardInputs LCSInputs { get { return (LitigationCostStandardInputs)LitigationCostInputs; } }


        static ConcurrentQueue<LitigationCostStandardModuleProgress> RecycledLitigationCostStandardModuleProgressQueue = new ConcurrentQueue<LitigationCostStandardModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledLitigationCostStandardModuleProgressQueue.Enqueue(this);
            }
        }

        public static new LitigationCostStandardModuleProgress GetRecycledOrAllocate()
        {
            LitigationCostStandardModuleProgress recycled = null;
            RecycledLitigationCostStandardModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new LitigationCostStandardModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            LitigationCostStandardModuleProgress copy = new LitigationCostStandardModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(LitigationCostStandardModuleProgress copy)
        {
            base.CopyFieldInfo(copy);
        }

        public override object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
            switch (variableNameForReport)
            {
                case "PInvestigationExpensesIfDispute":
                    found = true;
                    return LCSInputs == null ? null : (double?) LCSInputs.PInvestigationExpensesIfDispute;
                case "DInvestigationExpensesIfDispute":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.DInvestigationExpensesIfDispute;
                case "PMarginalInvestigationExpensesAfterEachMiddleBargainingRound":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.PMarginalInvestigationExpensesAfterEachMiddleBargainingRound;
                case "DMarginalInvestigationExpensesAfterEachMiddleBargainingRound":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.DMarginalInvestigationExpensesAfterEachMiddleBargainingRound;
                case "PMarginalInvestigationExpensesAfterFirstBargainingRound":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.PMarginalInvestigationExpensesAfterFirstBargainingRound;
                case "DMarginalInvestigationExpensesAfterFirstBargainingRound":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.DMarginalInvestigationExpensesAfterFirstBargainingRound;
                case "PMarginalInvestigationExpensesAfterLastBargainingRound":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.PMarginalInvestigationExpensesAfterLastBargainingRound;
                case "DMarginalInvestigationExpensesAfterLastBargainingRound":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.DMarginalInvestigationExpensesAfterLastBargainingRound;
                case "PSettlementFailureCostPerBargainingRound":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.PSettlementFailureCostPerBargainingRound;
                case "DSettlementFailureCostPerBargainingRound":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.DSettlementFailureCostPerBargainingRound;
                case "CommonTrialExpenses":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.CommonTrialExpenses;
                case "PAdditionalTrialExpenses":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.PAdditionalTrialExpenses;
                case "DAdditionalTrialExpenses":
                    found = true;
                    return LCSInputs == null ? null : (double?)LCSInputs.DAdditionalTrialExpenses;
                case "PMinusDTrialExpenses":
                    found = true;
                    return LCSInputs == null ? null : (double?)(LCSInputs.PAdditionalTrialExpenses - LCSInputs.DAdditionalTrialExpenses);
                default:
                    break;
            }
            found = false;
            return null;
        }

    }
}
