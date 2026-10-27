using System;

namespace ACESimBase.GameSolvingSupport.Settings
{
    public enum PredictiveCFRVariant { CFRPlus, PCFRPlus, DCFRPlus, PDCFRPlus }

    /// <summary>Paper algorithms from https://arxiv.org/abs/2404.13891.
    /// These settings are independent of the legacy GeneralizedVanilla discounting,
    /// trembles, pruning, sampling, annealing, and traversal-flavor settings.</summary>
    [Serializable]
    public sealed class PredictiveCFRSettings
    {
        public PredictiveCFRVariant Variant = PredictiveCFRVariant.PDCFRPlus;
        public double? Alpha;
        public double? Gamma;
        public double? AveragingPower;
        public bool AlternatingUpdates = true;
        public bool ReportCurrentStrategy = false;

        public bool Discounted => Variant == PredictiveCFRVariant.DCFRPlus || Variant == PredictiveCFRVariant.PDCFRPlus;
        public bool Predictive => Variant == PredictiveCFRVariant.PCFRPlus || Variant == PredictiveCFRVariant.PDCFRPlus;
        public double EffectiveAlpha => Alpha ?? (Variant == PredictiveCFRVariant.PDCFRPlus ? 2.3 : 1.5);
        public double EffectiveGamma => Gamma ?? (Variant == PredictiveCFRVariant.PDCFRPlus ? 5 : 4);
        public double EffectiveAveragingPower => AveragingPower ?? (Variant == PredictiveCFRVariant.PCFRPlus ? 2 : 1);

        public PredictiveCFRSettings DeepCopy() => (PredictiveCFRSettings)MemberwiseClone();

        public void Validate()
        {
            if (!Enum.IsDefined(Variant)) throw new ArgumentOutOfRangeException(nameof(Variant));
            if (!double.IsFinite(EffectiveAlpha) || EffectiveAlpha <= 0) throw new ArgumentOutOfRangeException(nameof(Alpha));
            if (!double.IsFinite(EffectiveGamma) || EffectiveGamma < 0) throw new ArgumentOutOfRangeException(nameof(Gamma));
            if (!double.IsFinite(EffectiveAveragingPower) || EffectiveAveragingPower < 0) throw new ArgumentOutOfRangeException(nameof(AveragingPower));
        }

        // Use the paper's +1 denominator. An early reference implementation used +1.5 for DCFR+.
        public double RegretDiscount(int iteration) => iteration <= 0 ? 0 : 1.0 / (1.0 + Math.Pow(iteration, -EffectiveAlpha));
    }
}
