using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    /// <summary>Structural illustrations, not equilibrium runs. Continuous merits stay integrated out.</summary>
    public static class ArticleGameTreeDiagrams
    {
        public sealed record Diagram(string FileStem, string Latex, string Description);

        public static LitigGameOptions CreateOptions(bool simplified)
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits);
            var options = (LitigGameOptions)launcher.GetDefaultSingleGameOptions();
            options.Name = "Article illustrative two-signal two-offer game";
            options.NumLiabilitySignals = 2;
            options.NumOffers = 2;
            options.CollapseChanceDecisions = true; // Continuous Q is integrated, not discretized.
            options.CollapseAlternativeEndings = simplified;
            options.VariableSettings["Number of Signals"] = "2";
            options.VariableSettings["Number of Offers"] = "2";
            return options;
        }

        public static async Task<GeneralizedVanilla> InitializeAsync(bool simplified)
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits);
            var settings = new EvolutionSettings
            {
                Algorithm = GameApproximationAlgorithm.GeneralizedVanilla,
                ParallelOptimization = false,
                CreateEFGFile = false,
                CreateEquilibriaFile = false,
                GenerateManualReports = false,
                GenerateReportsByPlaying = false,
            };
            var options = CreateOptions(simplified);
            // Initialize and enumerate only. No strategy optimization or production reports.
            return (GeneralizedVanilla)await launcher.GetInitializedDevelper(options, options.Name, settings);
        }

        public static ConstructGameTreeInformationSetInfo Collect(StrategiesDeveloperBase developer)
        {
            var tree = new ConstructGameTreeInformationSetInfo(developer.GameDefinition);
            developer.TreeWalk_Tree(tree);
            tree.CollectTreeInfo(developer.GameDefinition.DecisionsExecutionOrder, false);
            return tree;
        }

        public static bool AfterDefendantSignal(ConstructGameTreeInformationSetInfo.GamePointNode node) =>
            node.EdgeFromParent?.parentDecisionByteCode == (byte)LitigGameDecisions.DLiabilitySignal;

        public static bool StartOfBargaining(ConstructGameTreeInformationSetInfo.GamePointNode node) =>
            !node.anyNode.IsUtilitiesNode &&
            node.anyNode.Decision.DecisionByteCode == (byte)LitigGameDecisions.PAbandon;

        public static string Label(ConstructGameTreeInformationSetInfo.EdgeInfo edge, GameDefinition definition)
        {
            string name = (LitigGameDecisions)edge.parentDecisionByteCode switch
            {
                LitigGameDecisions.PLiabilitySignal => "P signal",
                LitigGameDecisions.DLiabilitySignal => "D signal",
                LitigGameDecisions.PAbandon => "P exit if no settlement",
                LitigGameDecisions.DDefault => "D exit if no settlement",
                LitigGameDecisions.CourtDecisionLiability => "Court finding",
                _ => edge.parentName,
            };
            return name + ": " + definition.GetActionString(edge.action, edge.parentDecisionByteCode);
        }

        public static async Task<IReadOnlyList<Diagram>> GenerateAsync()
        {
            var results = new List<Diagram>();
            foreach (bool simplified in new[] { false, true })
            {
                var developer = await InitializeAsync(simplified);
                var tree = Collect(developer);
                string suffix = simplified ? " simplified" : "";
                string legend = "Continuous-merits baseline; illustrative two-signal, two-offer grid. " +
                    "C = chance; repeated P/D numbers identify the same information set. " +
                    "Only chance probabilities are shown; no equilibrium is asserted. ";
                string endings = simplified
                    ? "Terminal pairs are expected final wealth (P, D), with terminal lotteries integrated out."
                    : "Terminal pairs are final wealth (P, D); court and mutual-exit lotteries are explicit.";
                Diagram CreateDiagram(
                    string stem,
                    Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> exclude,
                    Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> include,
                    string description) =>
                    new Diagram(stem,
                        tree.GenerateTikzDiagram(exclude, include, false,
                            edgeLabel: edge => Label(edge, developer.GameDefinition)),
                        description);
                results.Add(CreateDiagram("game tree 2x2x2" + suffix,
                    null, null, legend + endings));
                if (!simplified)
                    results.Add(CreateDiagram("game tree 2x2x2 beginning",
                        AfterDefendantSignal, null, legend +
                            "Private signal bins are represented by 0.25 and 0.75; play continues at the ellipses. " +
                            "Only one beginning view is needed because simplifying terminal lotteries does not change it."));
                results.Add(CreateDiagram("game tree 2x2x2 end" + suffix,
                    null, StartOfBargaining, legend +
                        "Subtree after both signals equal 0.25, filing, and answering. " +
                        "Exit choices take effect only if offers fail to settle. " + endings));
            }
            return results;
        }

        public static async Task WriteSourcesAsync(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            foreach (var diagram in await GenerateAsync())
            {
                await File.WriteAllTextAsync(Path.Combine(outputDirectory, diagram.FileStem + ".tex"), diagram.Latex);
                await File.WriteAllTextAsync(Path.Combine(outputDirectory, diagram.FileStem + ".txt"),
                    diagram.Description + "\n");
            }
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "README.md"), Readme.ReplaceLineEndings("\n"));
        }

        public const string Readme = """
            # Game-tree illustrations

            These five structural diagrams are generated from the revised continuous-merits baseline in
            ACESim4, using two signal bins per party, two court outcomes, and two offer values.
            They illustrate the extensive form; they are not solved equilibria or the
            ten-signal, ten-/fifteen-offer production results.

            ## Model and interpretation

            - Q is uniform on [0, 1], and true liability conditional on Q is Bernoulli(Q).
              Q is integrated with the baseline's 64-point Gauss-Legendre quadrature.
              The quadrature points are not game-tree states.
            - Both party signal standard deviations and the court standard deviation are 0.2.
              Party signal bins and offers are represented by 0.25 and 0.75; the court has
              two findings (not liable / liable). A court finding is not true liability.
            - Both initial wealth levels are 10. Damages are 1. Each party's total
              litigation cost is 0.30: 0.15 before bargaining and 0.15 at trial.
              There is no fee shifting and both parties are risk neutral.
            - Filing and answering remain endogenous. Exit commitments are simultaneous,
              as are offers: the drawn order does not imply observation. Repeated P/D
              node numbers identify the same information set, including across signal branches.
              A Yes exit commitment operates only if the offers do not settle.
            - Chance probabilities are rounded to three decimal places, payoffs to four.
              No behavioral probabilities are displayed: the initializer's arbitrary
              strategy is not presented as an equilibrium.
            - Payoff pairs are (plaintiff final wealth, defendant final wealth).
              Simplified leaves give expected wealth, not a realized monetary allocation.
              These pairs are not the article's net-outcome-fidelity measure.

            ## The five structural views

            - game tree 2x2x2.pdf: all actions, with court and mutual-exit lotteries explicit.
            - game tree 2x2x2 simplified.pdf: the same game with terminal lotteries integrated out.
            - game tree 2x2x2 beginning.pdf stops after private signals, at the filing information sets.
              There is no separate simplified beginning: continuous merits are integrated out
              in both versions, and the terminal-lottery switch does not affect this prefix.
            - The two end PDFs show the first bargaining subtree: both signals are 0.25,
              and the plaintiff has filed and the defendant has answered.
            - The legacy 2x2x2 filenames are retained to avoid unnecessary link changes.
              They do not mean that continuous merits now has two discrete states.

            ## Regeneration

            In ACESim4, run dotnet run --project LitigCharts -c Release -- diagrams game-trees
            --config <article repository>/article-diagrams.json (as one command).
            This invokes the existing C# tree walker and TikZ generator, compiles all five
            LaTeX sources with LuaLaTeX, and generates matching PNG previews.
            Explanatory prose is in a matching .txt file for each diagram, not inside the PDF.
            Only node/branch labels, probabilities, and payoff pairs appear in the diagrams.
            No production settings, equilibrium files, or production results are modified.
            The .tex sources are retained here so the figures can also be compiled directly.

            ## Worked equilibrium path

            The separate worked equilibrium path figure uses the ten-signal, ten-offer
            production equilibrium, not the structural illustrations' arbitrary strategies.
            See worked equilibrium path.txt and worked equilibrium paths.request.json.
            Regenerate it with the same command using the worked-path target.
            LitigCharts.WorkedPathDiagram owns the layout and emits self-contained LaTeX;
            the .tex is a generated output, not a separately maintained template.
            Use worked-path-data for extraction alone or all for all article diagrams.
            See LitigCharts/README.md for compile-only, sources-only, list, and output-root modes.
            """;
    }
}
