using System;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    public static class WelfareRuleBehaviorDecomposition
    {
        public sealed record Component(double AmericanWithAmericanProfile, double CompleteWithAmericanProfile,
            double AmericanWithCompleteProfile, double CompleteWithCompleteProfile,
            double MechanicalRuleEffect, double BehavioralEffect, double TotalDifference, double Residual);

        /// <summary>Symmetric two-factor decomposition, complete minus American.</summary>
        public static Component Calculate(double aa, double ca, double ac, double cc)
        {
            if (!double.IsFinite(aa) || !double.IsFinite(ca) || !double.IsFinite(ac) || !double.IsFinite(cc))
                throw new ArgumentException("All four rule/profile evaluations must be finite.");
            double mechanical = 0.5 * ((ca - aa) + (cc - ac));
            double behavior = 0.5 * ((ac - aa) + (cc - ca));
            double total = cc - aa;
            return new(aa, ca, ac, cc, mechanical, behavior, total, total - mechanical - behavior);
        }
    }
}
