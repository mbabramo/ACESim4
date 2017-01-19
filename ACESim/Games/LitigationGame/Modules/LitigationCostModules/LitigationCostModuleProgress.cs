using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class LitigationCostModuleProgress : GameModuleProgress
    {
        /* NOTE: Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */

        public double PAnticipatedTrialExpenses;
        public double DAnticipatedTrialExpenses;
        public double PTotalExpenses;
        public double DTotalExpenses;
        public double PTrialExpenses;
        public double DTrialExpenses;
        public double PInvestigationExpenses;
        public double DInvestigationExpenses;
        public int NumberInvestigativeRounds;
        public double PlaintiffLawyerNetUtility;
        public LitigationCostInputs LitigationCostInputs;

        public override void CleanAfterRecycling()
        {
            PAnticipatedTrialExpenses = 0;
            DAnticipatedTrialExpenses = 0;
            PTrialExpenses = 0;
            DTrialExpenses = 0;
            PInvestigationExpenses = 0;
            DInvestigationExpenses = 0;
            PTotalExpenses = 0;
            DTotalExpenses = 0;
            NumberInvestigativeRounds = 0;
            PlaintiffLawyerNetUtility = 0;
            LitigationCostInputs = new LitigationCostInputs();
            base.CleanAfterRecycling();
        }


        public override GameModuleProgress DeepCopy()
        {
            LitigationCostModuleProgress copy = new LitigationCostModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(LitigationCostModuleProgress copy)
        {
            copy.PAnticipatedTrialExpenses = PAnticipatedTrialExpenses;
            copy.DAnticipatedTrialExpenses = DAnticipatedTrialExpenses;
            copy.PInvestigationExpenses = PInvestigationExpenses;
            copy.DInvestigationExpenses = DInvestigationExpenses;
            copy.PTrialExpenses = PTrialExpenses;
            copy.DTrialExpenses = DTrialExpenses;
            copy.PTotalExpenses = PTotalExpenses;
            copy.DTotalExpenses = DTotalExpenses;
            copy.LitigationCostInputs = LitigationCostInputs.Clone();
            base.CopyFieldInfo(copy);
        }

        internal void CalculateTotalExpensesForReporting()
        {
            PTotalExpenses = PTrialExpenses + PInvestigationExpenses;
            DTotalExpenses = DTrialExpenses + DInvestigationExpenses;
        }

    }
}
