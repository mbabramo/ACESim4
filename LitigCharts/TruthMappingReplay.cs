using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LitigCharts;

/// <summary>Truth remapping of complete saved-profile replay, without solver or game changes.</summary>
public static class TruthMappingReplay
{
    public sealed record Measures(double MeritoriousPlaintiffShortfall, double NonliableDefendantBurden,
        double LiableDefendantExcessBurden, double GrossOutcomeError, double RealLitigationExpenditures);
    public sealed record Mapping(double Exponent, double ModelTruthPrior, double ReplayedTruthMass,
        Measures Headline, Measures DirectPopulationIntegrals, double[] TruthConditionalMeans);
    public sealed record Replay(string Method, int SavedTruthRows, int MarginalizedHistories,
        double TotalProbability, double MaximumTruthSplitResidual, double MaximumMonetaryInvarianceResidual,
        double MaximumAccountingResidual, Mapping[] Mappings);

    private static void Near(double a, double b, string label, double tolerance = 1e-10)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || Math.Abs(a-b) > tolerance)
            throw new InvalidDataException($"{label}: {a:R} versus {b:R}.");
    }
    private static double Payment(LitigGameProgress p) => SavedProfileWelfare.Payment(p);
    private static string Bits(double x) => BitConverter.DoubleToInt64Bits(x).ToString("X16", CultureInfo.InvariantCulture);

    public static Replay Evaluate(LitigGameOptions options,
        IEnumerable<(GameProgress theProgress, double weight)> savedProgresses, double[] exponents)
    {
        if (options.LitigGameDisputeGenerator is not LitigGameUniformQualityDisputeGenerator generator)
            throw new NotSupportedException("Latent-Q truth remapping is not applicable to direct binary signals.");
        if (options.DamagesMax != 1 || options.DamagesMultiplier != 1 || options.NumDamagesStrengthPoints != 1 ||
            options.NumPotentialBargainingRounds != 1 || !options.IncludeAgreementToBargainDecisions)
            throw new NotSupportedException("This analysis requires the declared unit-damages, one-round agreement game.");
        if (exponents == null || exponents.Length == 0 || exponents.Distinct().Count() != exponents.Length ||
            !exponents.Contains(1) || exponents.Any(k => !double.IsFinite(k) || k <= 0))
            throw new ArgumentException("Supply distinct positive exponents including the identity map.");
        var rows = savedProgresses.Select(x => (p: (LitigGameProgress)x.theProgress, x.weight)).ToArray();
        if (rows.Length == 0 || rows.Any(x => !double.IsFinite(x.weight) || x.weight <= 0 || x.p.AlternativeEndings != null || x.p.IsTrulyLiable == null))
            throw new InvalidDataException("Supply positive-weight replay rows after terminal lotteries and truth splits have been expanded exactly once.");
        Near(rows.Sum(x => x.weight), 1, "Whole-profile probability");
        // Preserve the complete played action history and the realized terminal lottery,
        // while removing only the ex-post true/false copy identity. Monetary outcomes
        // deliberately stay out of the key so a truth-dependent payoff is detected.
        string Key(LitigGameProgress p) => JsonSerializer.Serialize(new {
            p.ActionsToPlayString, p.PLiabilitySignalDiscrete, p.DLiabilitySignalDiscrete, p.CLiabilitySignalDiscrete,
            p.PFiles, p.DAnswers, p.PReadyToAbandon, p.DReadyToDefault, p.PAbandons, p.DDefaults,
            p.PAgreesToBargain, p.DAgreesToBargain, p.POffers, p.DOffers, p.CaseSettles, p.SettlementValue,
            p.TrialOccurs, p.PWinsAtTrial, p.DamagesAwarded
        });
        var groups = rows.GroupBy(x => Key(x.p)).ToArray();
        var sums = exponents.Select(_ => new double[8]).ToArray();
        double splitResidual = 0, moneyResidual = 0, accountingResidual = 0;
        foreach (var group in groups)
        {
            double weight = group.Sum(x => x.weight);
            var p = group.First().p;
            double liableMass = group.Where(x => x.p.IsTrulyLiable == true).Sum(x => x.weight);
            var trueRows = group.Where(x => x.p.IsTrulyLiable == true).ToArray();
            var falseRows = group.Where(x => x.p.IsTrulyLiable == false).ToArray();
            if (trueRows.Length == 0 || falseRows.Length == 0)
                throw new InvalidDataException("A marginalized history is missing a baseline truth copy.");
            foreach (var row in group)
            {
                moneyResidual = Math.Max(moneyResidual, new[] { Math.Abs(row.p.PChangeWealth-p.PChangeWealth),
                    Math.Abs(row.p.DChangeWealth-p.DChangeWealth), Math.Abs(row.p.PWelfare-p.PWelfare),
                    Math.Abs(row.p.DWelfare-p.DWelfare) }.Max());
                if (Bits(row.p.PChangeWealth) != Bits(p.PChangeWealth) || Bits(row.p.DChangeWealth) != Bits(p.DChangeWealth) ||
                    Bits(row.p.PWelfare) != Bits(p.PWelfare) || Bits(row.p.DWelfare) != Bits(p.DWelfare))
                    throw new InvalidDataException("Truth changes strategic payoffs; remapping cannot reuse this equilibrium.");
            }
            var posterior = generator.GetPosteriorQualityForReporting(p.PLiabilitySignalDiscrete,
                p.DLiabilitySignalDiscrete, p.CLiabilitySignalDiscrete);
            double baselineTruth = TruthMappingMath.Integrate(posterior.Quality, posterior.Weights, 1);
            splitResidual = Math.Max(splitResidual, Math.Abs(liableMass/weight-baselineTruth));
            Near(liableMass/weight, baselineTruth, "Baseline truth split");
            // Use the existing progress welfare definitions on each Boolean truth copy.
            double shortfall = trueRows[0].p.FalseNegativeShortfall;
            double liableBurden = trueRows[0].p.FalsePositiveExpenditures;
            double nonliableBurden = falseRows[0].p.FalsePositiveExpenditures;
            double payment = Payment(p), realCost = p.TotalExpensesIncurred;
            if (payment < 0 || payment > 1) throw new InvalidDataException("Base damages payment outside [0,1].");
            double stageCost = options.CostsMultiplier * ((p.PFiles ? options.PFilingCost : 0) +
                (p.DAnswers ? options.DAnswerCost : 0) + (p.TrialOccurs ? options.PTrialCosts + options.DTrialCosts : 0));
            accountingResidual = Math.Max(accountingResidual, Math.Max(Math.Abs(realCost-stageCost),
                Math.Abs(p.PChangeWealth+p.DChangeWealth+realCost)));
            for (int m = 0; m < exponents.Length; m++)
            {
                double truth = TruthMappingMath.Integrate(posterior.Quality, posterior.Weights, exponents[m]);
                double t = weight*truth, f = weight*(1-truth);
                sums[m][0] += t; sums[m][1] += f;
                sums[m][2] += t*shortfall; sums[m][3] += f*nonliableBurden; sums[m][4] += t*liableBurden;
                sums[m][5] += t*(1-payment); sums[m][6] += f*payment; sums[m][7] += weight*realCost;
            }
        }
        Near(accountingResidual, 0, "Terminal accounting");
        var prior = generator.GetPriorQualityForReporting();
        var maps = exponents.Select((k,i) => {
            var s = sums[i]; Near(s[0]+s[1], 1, "Mapped population mass");
            if (s[0] <= 0 || s[1] <= 0) throw new InvalidDataException("Undefined truth-conditional welfare.");
            double pi = TruthMappingMath.Integrate(prior.Quality, prior.Weights, k);
            double[] conditional = { s[2]/s[0], s[3]/s[1], s[4]/s[0], s[5]/s[0], s[6]/s[1] };
            return new Mapping(k, pi, s[0],
                new(pi*conditional[0], (1-pi)*conditional[1], pi*conditional[2], pi*conditional[3]+(1-pi)*conditional[4], s[7]),
                new(s[2],s[3],s[4],s[5]+s[6],s[7]), conditional);
        }).ToArray();
        // Reproduce the established headline convention: configured truth prior times
        // truth-conditional means. Also retain direct integrals and the discretization
        // difference in truth mass; neither is silently substituted for the other.
        var identity = maps.Single(m => m.Exponent == 1);
        double oldTrueMass = rows.Where(x => x.p.IsTrulyLiable == true).Sum(x => x.weight);
        double oldFalseMass = rows.Where(x => x.p.IsTrulyLiable == false).Sum(x => x.weight);
        Near(identity.Headline.MeritoriousPlaintiffShortfall,
            identity.ModelTruthPrior*rows.Sum(x => x.weight*x.p.FalseNegativeShortfall)/oldTrueMass, "Identity plaintiff welfare");
        Near(identity.Headline.NonliableDefendantBurden,
            (1-identity.ModelTruthPrior)*rows.Where(x => x.p.IsTrulyLiable == false).Sum(x => x.weight*x.p.FalsePositiveExpenditures)/oldFalseMass, "Identity nonliable defendant welfare");
        Near(identity.Headline.LiableDefendantExcessBurden,
            identity.ModelTruthPrior*rows.Where(x => x.p.IsTrulyLiable == true).Sum(x => x.weight*x.p.FalsePositiveExpenditures)/oldTrueMass, "Identity liable defendant welfare");
        Near(identity.Headline.GrossOutcomeError,
            identity.ModelTruthPrior*rows.Where(x => x.p.IsTrulyLiable == true).Sum(x => x.weight*(1-Payment(x.p)))/oldTrueMass +
            (1-identity.ModelTruthPrior)*rows.Where(x => x.p.IsTrulyLiable == false).Sum(x => x.weight*Payment(x.p))/oldFalseMass,
            "Identity gross outcome error");
        Near(identity.Headline.RealLitigationExpenditures, rows.Sum(x => x.weight*x.p.TotalExpensesIncurred), "Identity real costs");
        return new("Marginalize baseline truth copies within full action histories; integrate each map over posterior quadrature; retain configured-prior headline weighting and direct population integrals separately.",
            rows.Length, groups.Length, rows.Sum(x => x.weight), splitResidual, moneyResidual, accountingResidual, maps);
    }
}
