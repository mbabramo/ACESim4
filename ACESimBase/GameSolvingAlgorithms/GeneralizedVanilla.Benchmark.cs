using System;
using ACESimBase.GameSolvingSupport.FastCFR;
using ACESimBase.GameSolvingSupport.SolverSpecificSupport;

namespace ACESim
{
    public partial class GeneralizedVanilla
    {
        /// <summary>One legacy simultaneous update, with no reports, cache writes, stopping rule, or warmup.</summary>
        public void RunBenchmarkIteration(int iteration)
        {
            if (EvolutionSettings.CFRBR || EvolutionSettings.PruneOnOpponentStrategy)
                throw new NotSupportedException("The standalone benchmark requires full traversal without CFRBR or pruning.");
            Status.IterationNumDouble = iteration;
            Status.IterationNum = iteration;
            CalculateDiscountingAdjustments();
            ActionStrategy = ActionStrategies.CurrentProbability;
            if (EvolutionSettings.GeneralizedVanillaFlavor == GeneralizedVanillaFlavor.Fast)
            {
                Fast_EnsureBuilder();
                for (byte player = 0; player < NumNonChancePlayers; player++)
                {
                    var context = Fast_Builder.InitializeIteration(player, GameDefinition.CurrentOverallScenarioIndex, null);
                    context.IterationNumber = iteration;
                    context.ReachSelf = context.ReachOpp = context.ReachChance = context.SamplingCorrection = 1.0;
                    Fast_Builder.Root.Go(ref context);
                    Fast_Builder.CopyTalliesIntoBackingNodes();
                }
            }
            else if (EvolutionSettings.GeneralizedVanillaFlavor == GeneralizedVanillaFlavor.Regular)
            {
                var results = new GeneralizedVanillaUtilities[NumNonChancePlayers];
                for (byte player = 0; player < NumNonChancePlayers; player++)
                    GeneralizedVanillaCFRIteration_OptimizePlayer(iteration, results, player);
            }
            else
                throw new NotSupportedException("Benchmark legacy flavor must be Fast or Regular.");
            UpdateInformationSets(iteration);
        }
    }
}
