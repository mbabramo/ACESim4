using ACESim;
using ACESimBase.GameSolvingSupport.Evaluation;
using ACESimBase.GameSolvingSupport.Policies;
using ACESimBase.GameSolvingSupport.Reporting;
using ACESimBase.Games.LitigGame.ManualReports;
using LitigCharts;

namespace ArticleReplication;

/// <summary>Article-specific accounting over a joint distribution's terminal paths.
/// Truth-conditional denominators are aggregated before division, as in SavedProfileWelfare.</summary>
public static class PopulationOutcomes
{
    public sealed record Result(double[] SolverUtilities, double[] NativeUtilities, double Filing,
        double AnsweringUnconditional, double? AnsweringGivenFiling, double Trial, double Settlement,
        TruthMappingReplay.Measures Welfare, double TruthMass, double TotalProbability, double MaximumAccountingResidual);

    public static Result Evaluate(StrategiesDeveloperBase developer, PolicyGameTree tree, LitigGameOptions options,
        PlayerPolicy[] plaintiffs, PlayerPolicy[] defendants, double[] weights, double[] expectedValues)
    {
        // Keep the published evaluator's supported model boundary explicit.
        if (options.DamagesMax != 1 || options.DamagesMultiplier != 1 || options.NumPotentialBargainingRounds != 1 ||
            !options.IncludeAgreementToBargainDecisions || options.PerPartyCostsLeadingUpToBargainingRound != 0 || options.RoundSpecificBargainingCosts != null)
            throw new NotSupportedException("Expected the article's one-round unit-damages agreement game.");
        double prior = options.LitigGameDisputeGenerator switch
        {
            LitigGameUniformQualityDisputeGenerator continuous => Prior(continuous),
            LitigGameExogenousDirectSignalDisputeGenerator binary => binary.ExogenousProbabilityTrulyLiable,
            _ => throw new NotSupportedException("Unsupported article truth model.")
        };
        static double Prior(LitigGameUniformQualityDisputeGenerator generator)
        { var q = generator.GetPriorQualityForReporting(); return TruthMappingMath.Integrate(q.Quality, q.Weights, 1); }
        double mass = 0, truth = 0, filing = 0, answering = 0, trial = 0, settlement = 0, cost = 0, accounting = 0;
        double trueShortfall = 0, falseBurden = 0, trueBurden = 0, trueError = 0, falseError = 0;
        var native = new double[2];
        JointPolicyOutcomeReplay.Visit(developer, tree, plaintiffs, defendants, weights, (progress, w) =>
                {
                    var p = (LitigGameProgress)progress;
                    if (!double.IsFinite(w) || w < 0 || p.AlternativeEndings != null || p.IsTrulyLiable == null)
                        throw new InvalidDataException("Expected expanded positive-weight article outcomes.");
                    mass += w; filing += w * (p.PFiles ? 1 : 0); answering += w * (p.DAnswers ? 1 : 0);
                    trial += w * (p.TrialOccurs ? 1 : 0); settlement += w * (p.CaseSettles ? 1 : 0);
                    native[0] += w * p.PWelfare; native[1] += w * p.DWelfare; cost += w * p.TotalExpensesIncurred;
                    double payment = SavedProfileWelfare.Payment(p);
                    double computedCost = options.CostsMultiplier * ((p.PFiles ? options.PFilingCost : 0) + (p.DAnswers ? options.DAnswerCost : 0) + (p.TrialOccurs ? options.PTrialCosts + options.DTrialCosts : 0));
                    accounting = Math.Max(accounting, Math.Max(Math.Abs(p.TotalExpensesIncurred - computedCost), Math.Abs(p.PChangeWealth + p.DChangeWealth + computedCost)));
                    if (p.IsTrulyLiable == true)
                    { truth += w; trueShortfall += w * p.FalseNegativeShortfall; trueBurden += w * p.FalsePositiveExpenditures; trueError += w * (1 - payment); }
                    else { falseBurden += w * p.FalsePositiveExpenditures; falseError += w * payment; }
                });
        if (Math.Abs(mass - 1) > 1e-9 || truth <= 0 || truth >= mass || accounting > 1e-10)
            throw new ArithmeticException("Invalid joint outcome accounting or truth mass.");
        // Custom-result filing/answering/trial fields in the compiled solver tree must
        // agree with the independent native progress replay, including collapsed paths.
        foreach (var (actual, expected) in new[] { (filing, expectedValues[2]), (answering, expectedValues[3]), (trial, expectedValues[4]) })
            if (Math.Abs(actual - expected) > 1e-8) throw new ArithmeticException("Solver-tree and native outcome rates differ.");
        var welfare = new TruthMappingReplay.Measures(prior * trueShortfall / truth, (1 - prior) * falseBurden / (mass - truth),
            prior * trueBurden / truth, prior * trueError / truth + (1 - prior) * falseError / (mass - truth), cost);
        return new(expectedValues.Take(2).ToArray(), native, filing, answering, filing > 0 ? answering / filing : null, trial, settlement,
            welfare, truth, mass, accounting);
    }
}
