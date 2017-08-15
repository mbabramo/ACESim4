using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ForbiddenActDisputeGeneratorModuleProgress : DisputeGeneratorModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public ForbiddenActDisputeGeneratorInputs ForbiddenActInputs;
        public bool DoesAct;
        public double EvidentiaryStrengthIfDidNotDoIt;
        public double EvidentiaryStrengthIfDidIt;
        public double NetSocialCost;


        static ConcurrentQueue<ForbiddenActDisputeGeneratorModuleProgress> RecycledForbiddenActDisputeGeneratorModuleProgressQueue = new ConcurrentQueue<ForbiddenActDisputeGeneratorModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledForbiddenActDisputeGeneratorModuleProgressQueue.Enqueue(this);
            }
        }

        public static new ForbiddenActDisputeGeneratorModuleProgress GetRecycledOrAllocate()
        {
            ForbiddenActDisputeGeneratorModuleProgress recycled = null;
            RecycledForbiddenActDisputeGeneratorModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new ForbiddenActDisputeGeneratorModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            ForbiddenActInputs = null; // torecycle
            DoesAct = false;
            EvidentiaryStrengthIfDidNotDoIt = 0;
            EvidentiaryStrengthIfDidIt = 0;
            NetSocialCost = 0;
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            ForbiddenActDisputeGeneratorModuleProgress copy = new ForbiddenActDisputeGeneratorModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(ForbiddenActDisputeGeneratorModuleProgress copy)
        {
            copy.ForbiddenActInputs = ForbiddenActInputs;
            copy.DoesAct = DoesAct;
            copy.EvidentiaryStrengthIfDidNotDoIt = EvidentiaryStrengthIfDidNotDoIt;
            copy.EvidentiaryStrengthIfDidIt = EvidentiaryStrengthIfDidIt;
            copy.NetSocialCost = NetSocialCost;

            base.CopyFieldInfo(copy);
        }

        public override object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
            switch (variableNameForReport)
            {
                case "PrivateBenefitOfAct":
                    found = true;
                    return ForbiddenActInputs.PrivateBenefitOfAct;

                case "SocialCostOfAct":
                    found = true;
                    return ForbiddenActInputs.SocialCostOfAct;

                default:
                    break;
            }
            return base.GetNonFieldValueForReport(variableNameForReport, out found);
        }
    }
}
