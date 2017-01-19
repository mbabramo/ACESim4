using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class DisputeGeneratorModuleProgress : GameModuleProgress
    {
        public bool DisputeGeneratorInitiated;
        public bool DisputeExists;
        public bool PShouldWin;
        public double EvidentiaryStrengthLiability;
        public double BaseProbabilityPWins;
        public double DamagesClaim;
        public double BaseDamagesIfPWins;
        public double BaseDamagesIfPWinsAsPctOfClaim;
        public double PrelitigationWelfareEffectOnD;
        public double PrelitigationWelfareEffectOnP;
        public double AdjustedErrorWeight;
        public double SocialLoss;


        static ConcurrentQueue<DisputeGeneratorModuleProgress> RecycledDisputeGeneratorModuleProgressQueue = new ConcurrentQueue<DisputeGeneratorModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledDisputeGeneratorModuleProgressQueue.Enqueue(this);
            }
        }

        public static new DisputeGeneratorModuleProgress GetRecycledOrAllocate()
        {
            DisputeGeneratorModuleProgress recycled = null;
            RecycledDisputeGeneratorModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new DisputeGeneratorModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            DisputeGeneratorInitiated = false;
            DisputeExists = false;
            PShouldWin = false;
            EvidentiaryStrengthLiability = 0;
            BaseProbabilityPWins = 0;
            DamagesClaim = 0;
            BaseDamagesIfPWins = 0;
            BaseDamagesIfPWinsAsPctOfClaim = 0;
            PrelitigationWelfareEffectOnD = 0;
            PrelitigationWelfareEffectOnP = 0;
            AdjustedErrorWeight = 0;
            SocialLoss = 0;
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            DisputeGeneratorModuleProgress copy = new DisputeGeneratorModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(DisputeGeneratorModuleProgress copy)
        {
            copy.DisputeGeneratorInitiated = DisputeGeneratorInitiated;
            copy.DisputeExists = DisputeExists;
            copy.PShouldWin = PShouldWin;
            copy.EvidentiaryStrengthLiability = EvidentiaryStrengthLiability;
            copy.BaseProbabilityPWins = BaseProbabilityPWins;
            copy.DamagesClaim = DamagesClaim;
            copy.PrelitigationWelfareEffectOnD = PrelitigationWelfareEffectOnD;
            copy.PrelitigationWelfareEffectOnP = PrelitigationWelfareEffectOnP;
            copy.BaseDamagesIfPWins = BaseDamagesIfPWins;
            copy.BaseDamagesIfPWinsAsPctOfClaim = BaseDamagesIfPWinsAsPctOfClaim;
            copy.SocialLoss = SocialLoss;
            copy.AdjustedErrorWeight = AdjustedErrorWeight;
            base.CopyFieldInfo(copy);
        }

        public override object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
            found = false;
            return null;
            
        }
    }
}
