using System.Collections.Generic;

namespace ACESim
{

    public record MultiSignalStructure(
        bool IncludeHiddenLiability,
        bool IncludeDefendantSignal,
        bool IncludePlaintiffSignal,
        bool DefendantSignalFirst,
        int NumLiabilityLevels,
        int NumSignalLevels,
        byte[] HiddenLiabilityObservers,
        byte[] DefendantSignalObservers,
        byte[] PlaintiffSignalObservers,
        bool HiddenLiabilityUneven,
        bool DefendantSignalUneven,
        bool PlaintiffSignalUneven);

/// <summary>
/// Contract for dispute generators that reveal one or more private liability-quality
/// signals (and optionally a hidden liability draw) before the rest of the litigation
/// game.  The concrete class supplies the structure and probability distributions;
/// the base class builds the corresponding Decision nodes.
/// </summary>
public interface IMultiLitigationQualitySignalDisputeGenerator : ILitigGameDisputeGenerator
    {
        /* ─────── Structural description of the early-signal phase ─────── */

        /// <summary>Return a value object describing which signal nodes exist, their sizes,
        /// observers, ordering, and whether their outcome distributions are uniform.</summary>
        MultiSignalStructure GetSignalStructure();

        /* ─────── Distribution hooks for the early signals ─────── */

        /// <remarks>Called only when <see cref="MultiSignalStructure.IncludeHiddenLiability" /> is true.</remarks>
        double[] GetLiabilityLevelProbabilities();

        /// <summary>Marginal distribution of the first private signal (P-sig or D-sig).</summary>
        double[] GetFirstSignalProbabilities();

        /// <summary>Conditional distribution of the second private signal given the first outcome.</summary>
        double[] GetSecondSignalProbabilities(byte firstSignalOutcome);

        /// <summary>Optional public / court signal conditional on both private signals.</summary>
        double[] GetCourtSignalProbabilities(byte pSignal, byte dSignal);

        /* ─────── Inversion support (mirrors Exogenous generator) ─────── */
        bool ProvidesInvertedCalculations { get; }

        /// <remarks>Subclasses that set <see cref="ProvidesInvertedCalculations"/> to true
        /// should override <see cref="MultiLitigationQualitySignalDisputeGeneratorBase.SetupInvertedCalculators"/>
        /// and the nine ILitigGameDisputeGenerator inversion methods.</remarks>
    }
}
