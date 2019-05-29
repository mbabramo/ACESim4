//using ACESimBase.Util;
//using ACESimBase.Util.ArrayProcessing;
//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;

//namespace ACESim
//{
//    public partial class CounterfactualRegretMinimization
//    {

//        public async Task<string> SolveXFP()
//        {
//            string reportString = null;
//            InitializeInformationSets();
//            for (int iteration = 1; iteration <= EvolutionSettings.TotalVanillaCFRIterations; iteration++)
//            {
//                reportString = await HedgeVanillaCFRIteration(iteration);
//            }
//            return reportString;
//        }

//        private async Task<string> XFPIteration(int iteration)
//        {
//            HedgeVanillaIteration = iteration;
//            HedgeVanillaIterationInt = iteration;
//            CalculateDiscountingAdjustments();

//            string reportString = null;
//            double[] lastUtilities = new double[NumNonChancePlayers];

//            ActionStrategy = ActionStrategies.NormalizedHedge;

//            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
//            {
//                XFPIteration_OptimizePlayer(iteration, playerBeingOptimized);
//            }
//            MiniReport(iteration, results);

//            UpdateInformationSets(iteration);

//            reportString = await GenerateReports(iteration,
//                () =>
//                    $"Iteration {iteration} Overall milliseconds per iteration {((HedgeVanillaIterationStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
//            return reportString;
//        }
//    }
//}
