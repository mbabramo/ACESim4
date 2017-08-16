using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class MyGameRunner
    {
        public static MyGameProgress PlayMyGameOnce(MyGameOptions options,
            Func<Decision, byte> actionsOverride)
        {
            MyGameDefinition gameDefinition = new MyGameDefinition();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);

            GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition);
            MyGameProgress gameProgress = (MyGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);

            return gameProgress;
        }

        public static void EvolveMyGame()
        {
            MyGameDefinition gameDefinition = new MyGameDefinition();
            var options = MyGameOptionsGenerator.DEBUG_TestOptions();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);
            EvolutionSettings evolutionSettings = new EvolutionSettings()
            {
                MaxParallelDepth = 2,
                ParallelOptimization = true,

                Algorithm = GameApproximationAlgorithm.ExplorativeProbing,
                TotalAvgStrategySamplingCFRIterations = 10000000,
                TotalProbingCFRIterations = 100000,
                TotalVanillaCFRIterations = 100000000,

                ReportEveryNIterations = 1000,
                BestResponseEveryMIterations = EvolutionSettings.EffectivelyNever,

                UseEpsilonOnPolicyForOpponent = true,
                FirstOpponentEpsilonValue = 0.5,
                LastOpponentEpsilonValue = 0.05,
                LastOpponentEpsilonIteration = 10000,
                MaxOneEpsilonExploration = true,
            };
            CounterfactualRegretMaximization developer = new CounterfactualRegretMaximization(starterStrategies, evolutionSettings, gameDefinition);
            developer.DevelopStrategies();
        }
    }
}
