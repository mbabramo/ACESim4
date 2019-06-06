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
            for (int iteration = 1; iteration <= EvolutionSettings.TotalVanillaCFRIterations; iteration++)
            {
                reportString = await FictitiousSelfPlayIteration(iteration);
            }
            return reportString;
        }

        private async Task<string> FictitiousSelfPlayIteration(int iteration)
        {
            HedgeVanillaIteration = iteration;
            HedgeVanillaIterationInt = iteration;

            double lambda2 = 1.0 / HedgeVanillaIteration;

            string reportString = null;
            double[] lastUtilities = new double[NumNonChancePlayers];

            ExecuteAcceleratedBestResponse(true);
            Parallel.ForEach(InformationSets, informationSet => informationSet.UpdateAverageStrategyForFictitiousPlay(lambda2));

            MiniReport(iteration, null);

            reportString = await GenerateReports(iteration,
                () =>
                    $"Iteration {iteration} Overall milliseconds per iteration {((HedgeVanillaIterationStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            return reportString;
        }
    }
}
