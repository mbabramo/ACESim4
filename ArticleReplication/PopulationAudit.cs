using ACESim;
using ACESimBase.GameSolvingSupport.Evaluation;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.Util.Debugging;
using System.Text.Json;

namespace ArticleReplication;

/// <summary>Distinguish a joint CCE from the Nash incentives of its product marginals.
/// Read archived policies and independently checked response utilities; never re-solve.</summary>
public static class PopulationAudit
{
    public static async Task Run(Dictionary<string, string> arguments)
    {
        if (arguments.Keys.Except(new[] { "run", "output" }, StringComparer.OrdinalIgnoreCase).Any()) throw new ArgumentException("Unknown population audit option.");
        string run = Path.GetFullPath(arguments["run"]), output = Path.GetFullPath(arguments["output"]);
        if (File.Exists(output)) throw new IOException("Choose a fresh audit file.");
        var plan = Files.Object(Path.Combine(run, "plan.json"));
        bool endogenous = plan["Model"]?.GetValue<string>() == "endogenous";
        var cases = plan["Cases"]!.AsArray();
        var selectors = plan["Selectors"]!.Deserialize<string[]>(Files.Json)!;
        var rows = new List<object>(); TabbedText.DisableOutput();
        foreach (var item in cases)
        {
            string caseId = item!["Id"]!.GetValue<string>();
            var developer = endogenous
                ? await EndogenousPopulationExperiment.Initialize(item.Deserialize<EndogenousPopulationExperiment.Case>(Files.Json)!)
                : await ArticleWorkedPathExtraction.InitializeAsync(FinalArticleCaseFactory.Create(item.Deserialize<FinalArticleCase>(Files.Json)!));
            var fingerprint = StrategicGameFingerprint.Capture(developer, roundChanceProbabilities: !endogenous); var tree = PolicyGameTree.Create(developer);
            foreach (var selector in selectors)
            {
                string directory = Path.Combine(run, caseId, selector);
                var saved = PopulationArchiveIO.Read(Path.Combine(directory, "population.json"));
                if (saved.Schema != "population-policy-archive-v1" || saved.GameHash != fingerprint.CompleteSha256 || !saved.Keys.SequenceEqual(tree.Layout.Select(x => x.Key))) throw new InvalidDataException("Changed population game/layout.");
                var policies = Enumerable.Range(0, 2).Select(p => saved.Policies.Where(x => x.Player == p).Select(x => PopulationExperiment.Restore(tree, x)).ToArray()).ToArray();
                var result = Files.ObjectOrCompressed(Path.Combine(directory, "result.json"));
                var q = result["JointWeights"]!.Deserialize<double[]>(Files.Json)!;
                int n = policies[0].Length, m = policies[1].Length;
                PolicyGameTree.ValidateDistribution(q, n * m);
                var marginals = new[] { new double[n], new double[m] };
                for (int i = 0; i < n; i++) for (int j = 0; j < m; j++) { marginals[0][i] += q[i * m + j]; marginals[1][j] += q[i * m + j]; }
                var product = tree.Evaluate(tree.Mix(policies[0], marginals[0]), tree.Mix(policies[1], marginals[1]));
                var oracle = Files.Object(Path.Combine(directory, "independent-oracle-check.json"));
                if (oracle["Passed"]?.GetValue<bool>() != true) throw new InvalidDataException("Missing independent full-game response check.");
                var best = oracle["ExistingArticleBestResponseUtilities"]!.Deserialize<double[]>(Files.Json)!;
                var joint = result["ExpectedValues"]!.Deserialize<double[]>(Files.Json)!;
                var cceGains = best.Select((b, p) => Math.Max(0, b - joint[p]) / tree.UtilityScale[p]).ToArray();
                var nashGains = best.Select((b, p) => Math.Max(0, b - product[p]) / tree.UtilityScale[p]).ToArray();
                double tv = 0; for (int i = 0; i < n; i++) for (int j = 0; j < m; j++) tv += Math.Abs(q[i * m + j] - marginals[0][i] * marginals[1][j]) / 2;
                double target = result["Settings"]!["MaximumNormalizedGain"]!.GetValue<double>();
                rows.Add(new { CaseId = caseId, Selector = selector, Status = result["Status"]!.GetValue<string>(), JointValues = joint,
                    ProductMarginalValues = product, NormalizedCceGains = cceGains, NormalizedProductNashGains = nashGains,
                    PolicyIndexCorrelationTotalVariation = tv, ProductIsApproximateNashAt1eMinus5 = nashGains.Max() <= 1e-5,
                    MaximumNormalizedGainTarget = target, ProductIsApproximateNashAtRunTolerance = nashGains.Max() <= target,
                    SourceResultSha256 = Files.Sha(PopulationArchiveIO.PhysicalPath(Path.Combine(directory, "result.json"))), ArchiveSha256 = Files.Sha(PopulationArchiveIO.PhysicalPath(Path.Combine(directory, "population.json"))) });
            }
        }
        Files.Save(output, new { Passed = true, Rows = rows, Explanation = "CCE deviations subtract joint payoff; Nash deviations subtract product-marginal payoff. Correlation across policy labels can be redundant; total variation alone does not establish a behavioral difference." });
    }
}
