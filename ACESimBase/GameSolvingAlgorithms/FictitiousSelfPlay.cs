using ACESimBase.Util;
using ACESimBase.Util.ArrayProcessing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public partial class FictitiousSelfPlay : StrategiesDeveloperBase
    {

        public FictitiousSelfPlay(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new FictitiousSelfPlay(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }

        public override async Task<string> RunAlgorithm(string reportName)
        {
            string reportString = null;
            StrategiesDeveloperStopwatch.Reset();
            for (int iteration = 2; iteration <= EvolutionSettings.TotalVanillaCFRIterations; iteration++)
            {
                reportString = await FictitiousSelfPlayIteration(iteration);
            }
            return reportString;
        }

        private async Task<string> FictitiousSelfPlayIteration(int iteration)
        {
            StrategiesDeveloperStopwatch.Start();

            IterationNumDouble = iteration;
            IterationNum = iteration;

            double lambda2 = 1.0 / IterationNumDouble;

            string reportString = null;
            double[] lastUtilities = new double[NumNonChancePlayers];

            CalculateBestResponse();

            Parallel.ForEach(InformationSets, informationSet => informationSet.UpdateAverageStrategyForFictitiousPlay(lambda2));

            StrategiesDeveloperStopwatch.Stop();

            reportString = await GenerateReports(iteration,
                () =>
                    $"Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            return reportString;
        }
    }
}
