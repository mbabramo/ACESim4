using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LitigCharts;

/// <summary>Unrounded established monetary measures on one expanded profile replay.</summary>
public static class SavedProfileWelfare
{
    public sealed record Result(TruthMappingReplay.Measures Headline, double ModelTruthPrior,
        double ReplayedTruthMass, double TotalProbability, double MaximumAccountingResidual);
    public static double Payment(LitigGameProgress p) => !p.PFiles || p.PAbandons ? 0 :
        !p.DAnswers || p.DDefaults ? p.LitigGameOptions.DamagesMax*p.LitigGameOptions.DamagesMultiplier :
        p.SettlementValue ?? (p.PWinsAtTrial ? p.DamagesAwarded : 0);

    public static Result Evaluate(LitigGameOptions options, IEnumerable<(GameProgress theProgress, double weight)> saved)
    {
        if (options.DamagesMax != 1 || options.DamagesMultiplier != 1 || options.NumDamagesStrengthPoints != 1 ||
            options.NumPotentialBargainingRounds != 1 || !options.IncludeAgreementToBargainDecisions ||
            options.PerPartyCostsLeadingUpToBargainingRound != 0 || options.RoundSpecificBargainingCosts != null ||
            options.PFilingCost_PortionSavedIfDDoesntAnswer != 0)
            throw new NotSupportedException("Expected the declared one-round, unit-damages agreement game.");
        double prior;
        if (options.LitigGameDisputeGenerator is LitigGameUniformQualityDisputeGenerator continuous)
        {
            var quadrature = continuous.GetPriorQualityForReporting();
            prior = TruthMappingMath.Integrate(quadrature.Quality, quadrature.Weights, 1);
        }
        else if (options.LitigGameDisputeGenerator is LitigGameExogenousDirectSignalDisputeGenerator binary)
            prior = binary.ExogenousProbabilityTrulyLiable;
        else throw new NotSupportedException("Unsupported final article truth distribution.");
        var rows = saved.Select(x => (p: (LitigGameProgress)x.theProgress, x.weight)).ToArray();
        if (rows.Length == 0 || rows.Any(x => !double.IsFinite(x.weight) || x.weight <= 0 || x.p.AlternativeEndings != null || x.p.IsTrulyLiable == null))
            throw new InvalidDataException("Expected positive-weight fully expanded terminal rows.");
        double mass = rows.Sum(x => x.weight), liable = rows.Where(x => x.p.IsTrulyLiable == true).Sum(x => x.weight);
        double nonliable = rows.Where(x => x.p.IsTrulyLiable == false).Sum(x => x.weight);
        if (Math.Abs(mass-1) > 1e-10 || liable <= 0 || nonliable <= 0 || prior <= 0 || prior >= 1)
            throw new InvalidDataException("Undefined truth-conditional welfare or invalid replay mass.");
        double accounting = 0;
        foreach (var row in rows)
        {
            var p = row.p;
            double cost = options.CostsMultiplier*((p.PFiles ? options.PFilingCost : 0) +
                (p.DAnswers ? options.DAnswerCost : 0) + (p.TrialOccurs ? options.PTrialCosts+options.DTrialCosts : 0));
            double[] values = { p.PChangeWealth, p.DChangeWealth, p.TotalExpensesIncurred,
                p.FalseNegativeShortfall, p.FalsePositiveExpenditures, Payment(p) };
            if (values.Any(x => !double.IsFinite(x)) || Payment(p) < 0 || Payment(p) > 1)
                throw new InvalidDataException("Invalid unrounded monetary outcome.");
            accounting = Math.Max(accounting, Math.Max(Math.Abs(p.TotalExpensesIncurred-cost),
                Math.Abs(p.PChangeWealth+p.DChangeWealth+cost)));
        }
        if (accounting > 1e-10) throw new InvalidDataException("Terminal monetary accounting failed.");
        double T(Func<LitigGameProgress,double> f) => rows.Where(x => x.p.IsTrulyLiable == true).Sum(x => x.weight*f(x.p))/liable;
        double F(Func<LitigGameProgress,double> f) => rows.Where(x => x.p.IsTrulyLiable == false).Sum(x => x.weight*f(x.p))/nonliable;
        return new(new(prior*T(p => p.FalseNegativeShortfall), (1-prior)*F(p => p.FalsePositiveExpenditures),
            prior*T(p => p.FalsePositiveExpenditures), prior*T(p => 1-Payment(p))+(1-prior)*F(Payment),
            rows.Sum(x => x.weight*x.p.TotalExpensesIncurred)), prior, liable, mass, accounting);
    }
}
