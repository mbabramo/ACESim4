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
    public partial class CounterfactualRegretMinimization
    {

        public async Task<string> SolveFictitiousSelfPlay()
        {
            string reportString = null;
            InitializeInformationSets();
            HedgeVanillaIterationStopwatch.Reset();
            for (int iteration = 2; iteration <= EvolutionSettings.TotalVanillaCFRIterations; iteration++)
            {
                if (iteration == EvolutionSettings.TotalVanillaCFRIterations)
                    EvolutionSettings.UseAcceleratedBestResponse = false; // DEBUG -- remove
                reportString = await FictitiousSelfPlayIteration(iteration);
            }
            return reportString;
        }

        private async Task<string> FictitiousSelfPlayIteration(int iteration)
        {
            HedgeVanillaIterationStopwatch.Start();

            HedgeVanillaIteration = iteration;
            HedgeVanillaIterationInt = iteration;

            double lambda2 = 1.0 / HedgeVanillaIteration;

            string reportString = null;
            double[] lastUtilities = new double[NumNonChancePlayers];

            CalculateBestResponse();

            Parallel.ForEach(InformationSets, informationSet => informationSet.UpdateAverageStrategyForFictitiousPlay(lambda2));

            HedgeVanillaIterationStopwatch.Stop();

            reportString = await GenerateReports(iteration,
                () =>
                    $"Iteration {iteration} Overall milliseconds per iteration {((HedgeVanillaIterationStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            return reportString;
        }
    }
}
