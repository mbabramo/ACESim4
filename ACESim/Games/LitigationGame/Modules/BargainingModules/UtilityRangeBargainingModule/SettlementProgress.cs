using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public abstract class SettlementProgress
    {
        public bool BlockSettlementDuringOptimization;

        public abstract bool CompleteSettlementReached();

        public abstract double? CompleteSettlementAmount();

        public abstract double? OverallSuccessOfPlaintiff();

        public abstract SettlementProgress DeepCopy();
    }


    [Serializable]
    public class GlobalSettlementProgress : SettlementProgress
    {
        public bool GlobalSettlementAchieved;
        public double? AgreedUponPaymentAsProportion;
        public double? OriginalDamagesClaim;

        public override bool CompleteSettlementReached()
        {
            return GlobalSettlementAchieved && !BlockSettlementDuringOptimization;
        }

        public override double? CompleteSettlementAmount()
        {
            return AgreedUponPaymentAsProportion * OriginalDamagesClaim;
        }

        public override double? OverallSuccessOfPlaintiff()
        {
            return AgreedUponPaymentAsProportion;
        }

        public override SettlementProgress DeepCopy()
        {
 	        return new GlobalSettlementProgress() { AgreedUponPaymentAsProportion = AgreedUponPaymentAsProportion, BlockSettlementDuringOptimization = BlockSettlementDuringOptimization, GlobalSettlementAchieved = GlobalSettlementAchieved, OriginalDamagesClaim = OriginalDamagesClaim };
        }
    }

    [Serializable]
    public class ProbabilityAndMagnitudeSettlementProgress : SettlementProgress
    {
        private double? _AgreedUponProbability;
        public double? AgreedUponProbability { get { return _AgreedUponProbability; } }
        public double? AgreedUponDamagesProportion;
        public double? OriginalDamagesClaim;

        public void TryToSetProposedAgreedUponProbability(double agreedUponProbability, UtilityRangeBargainingModuleSettings settings)
        {
            bool reject = (settings.RejectHighProbabilitySettlements && agreedUponProbability > settings.HighProbabilityThreshold) || (settings.RejectLowProbabilitySettlements && agreedUponProbability < settings.LowProbabilityThreshold);
            if (!reject)
                _AgreedUponProbability = agreedUponProbability;
        }

        public override bool CompleteSettlementReached()
        {
            return AgreedUponDamagesProportion != null && AgreedUponProbability != null && !BlockSettlementDuringOptimization;
        }

        public override double? CompleteSettlementAmount()
        {
            return AgreedUponProbability * AgreedUponDamagesProportion * OriginalDamagesClaim;
        }

        public override double? OverallSuccessOfPlaintiff()
        {
            return AgreedUponDamagesProportion * AgreedUponProbability;
        }
        
        public override SettlementProgress DeepCopy()
        {
 	        return new ProbabilityAndMagnitudeSettlementProgress() {  BlockSettlementDuringOptimization = BlockSettlementDuringOptimization, AgreedUponDamagesProportion = AgreedUponDamagesProportion, _AgreedUponProbability = AgreedUponProbability, OriginalDamagesClaim = OriginalDamagesClaim };
        }
    }
}
