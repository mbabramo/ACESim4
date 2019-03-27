using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim.Util
{ 
    
    [Serializable]
    public abstract class UtilityCalculator
    {
        
        public double InitialWealth;

        public abstract double GetSubjectiveUtilityForWealthLevel(double laterWealth);
        public abstract double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility);
        public double GetDeltaFromSpecifiedWealthProducingSpecifiedSubjectiveUtility(double specifiedWealth, double subjectiveUtility)
        {
            return GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(subjectiveUtility) + (InitialWealth - specifiedWealth);
        }
        public double GetDeltaFromSpecifiedWealthProducingSpecifiedDeltaSubjectiveUtility(double specifiedWealth, double deltaSubjectiveUtility)
        {
            return GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(GetSubjectiveUtilityForWealthLevel(specifiedWealth) + deltaSubjectiveUtility) + (InitialWealth - specifiedWealth);
        }
    }

    [Serializable]
    public class RiskNeutralUtilityCalculator : UtilityCalculator
    {
        public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
        {
            return laterWealth;
        }

        public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
        {
            return subjectiveUtility - InitialWealth;
        }
    }

    [Serializable]
    public class LogRiskAverseUtilityCalculator : UtilityCalculator
    {
        // Note: It doesn't matter what the logarithm base is; that won't affect the degree of risk aversion, which is affected only by changing the initial wealth.

        public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
        {
            if (laterWealth < 0.000000001)
                laterWealth = 0.000000001; // this allows us to avoid an exception when testing actions that would have disastrous consequences
            return Math.Log(laterWealth);
        }

        public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
        {
            return Math.Exp(subjectiveUtility) - InitialWealth;
        }
    }

    public class QuadraticUtilityRiskAverseUtilityCalculator : UtilityCalculator
    {
        public double B; // coefficient for constant absolute risk aversion, should be on the order of magnitude of 1 / laterWealth. As alpha goes up, risk aversion increases.

        public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
        {
            if (1 - 2 * B * laterWealth <= 0)
                throw new Exception("Invalid use of quadratic utility.");
            return laterWealth - B * laterWealth * laterWealth;
        }

        public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
        {
            // -b*(InitialWealth + delta)^2 + (initialWealth + delta) = SubjectiveUtility
            double originalUtility = GetSubjectiveUtilityForWealthLevel(InitialWealth);
            if (subjectiveUtility > originalUtility)
                return (1.0 + Math.Sqrt(1.0 - 4 * B * subjectiveUtility) - (2 * B * InitialWealth)) / 2 * B;
            else
                return (1.0 - Math.Sqrt(1.0 - 4 * B * subjectiveUtility) - (2 * B * InitialWealth)) / 2 * B;
        }
    }

    [Serializable]
    public class CARARiskAverseUtilityCalculator : UtilityCalculator
    {
        public double Alpha; // coefficient for constant absolute risk aversion, should be on the order of magnitude of 1 / laterWealth. As alpha goes up, risk aversion increases.
            
        public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
        {
            return -Math.Exp(-Alpha * laterWealth);
        }

        public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
        {
            return -Math.Log(subjectiveUtility) / Alpha - InitialWealth;
        }
    }

    [Serializable]
    public class LossAverseUtilityCalculator : UtilityCalculator
    {
        public double MaxSubjectiveLossAsMultipleOfSubjectiveGain;
        public double GainNeededToGetToHalfOfSubjectiveMaxGain;
        public double LossNeededToGetToHalfOfSubjectiveMaxLoss;
        
        public double WealthReferencePoint;

        public double CalculateSubjectiveUtilityBasedOnGainOrLoss(double actualGainOrLoss)
        {
            if (LossNeededToGetToHalfOfSubjectiveMaxLoss < 0 || GainNeededToGetToHalfOfSubjectiveMaxGain < 0 || MaxSubjectiveLossAsMultipleOfSubjectiveGain <= 0)
                throw new Exception("Invalid loss aversion parameters.");
            return HyperbolicTangentCurve.GetYValueTwoSided(0.0, 0.0, -1.0 * MaxSubjectiveLossAsMultipleOfSubjectiveGain, 1.0, 0 - LossNeededToGetToHalfOfSubjectiveMaxLoss, -0.5, GainNeededToGetToHalfOfSubjectiveMaxGain, 0.5, actualGainOrLoss);
        }

        public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
        {
            if (WealthReferencePoint == 0)
                WealthReferencePoint = InitialWealth;
            return CalculateSubjectiveUtilityBasedOnGainOrLoss(laterWealth - WealthReferencePoint);
        }


        public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
        {
            return HyperbolicTangentCurve.GetXValueTwoSided(0.0, 0.0, -1.0 * MaxSubjectiveLossAsMultipleOfSubjectiveGain, 1.0, 0 - LossNeededToGetToHalfOfSubjectiveMaxLoss, -0.5, GainNeededToGetToHalfOfSubjectiveMaxGain, 0.5, subjectiveUtility);
        }
    }
}
