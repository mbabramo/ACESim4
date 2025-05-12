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
    [Serializable]
    public partial class FictitiousPlay : StrategiesDeveloperBase
    {
        public bool AddNoiseToBestResponses = false;
        public bool ReportOnBestResponse = false; // not yet implemented

        public FictitiousPlay(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new FictitiousPlay(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            ReportCollection reportCollection = new ReportCollection();
            StrategiesDeveloperStopwatch.Reset();
            InitializeInformationSets();
            int iterationToReturnToBaselineScenario = GameDefinition.IterationsForWarmupScenario ?? -1;
            int startingIteration = 2;
            if (iterationToReturnToBaselineScenario < startingIteration)
                iterationToReturnToBaselineScenario = startingIteration;
            bool targetMet = false;
            for (int iteration = startingIteration; iteration <= EvolutionSettings.TotalIterations && !targetMet; iteration++)
            {
                if (iteration % 50 == 1 && EvolutionSettings.DynamicSetParallel)
                    DynamicallySetParallel();
                Status.IterationNum = iteration;
                var result = await FictitiousPlayIteration(iteration);
                reportCollection.Add(result);
                targetMet = Status.BestResponseTargetMet(EvolutionSettings.BestResponseTarget);
                if (iteration == iterationToReturnToBaselineScenario)
                {
                    ReinitializeForScenario(GameDefinition.CurrentOverallScenarioIndex, false);
                    ResetBestExploitability();
                }
            }
            return reportCollection;
        }

        private async Task<ReportCollection> FictitiousPlayIteration(int iteration)
        {
            StrategiesDeveloperStopwatch.Start();

            Status.IterationNumDouble = iteration;
            Status.IterationNum = iteration;

            //double lambda2 = 1.0 / IterationNumDouble;

            ReportCollection reportCollection = new ReportCollection();
            double[] lastUtilities = new double[NumNonChancePlayers];

            CalculateBestResponse(true);
            RememberBestResponseExploitabilityValues(iteration);

            if (AddNoiseToBestResponses)
                Parallel.ForEach(InformationSets, informationSet => informationSet.AddNoiseToBestResponse(0.10, iteration));

            double perturbation = 0; // NOTE: 0 perturbation seems necessary for fictitious play EvolutionSettings.Perturbation_BasedOnCurve(iteration, EvolutionSettings.TotalIterations);

            if (EvolutionSettings.BestResponseDynamics)
            {
                if (!EvolutionSettings.ParallelOptimization)
                    foreach (var informationSet in InformationSets)
                        informationSet.SetAverageStrategyToBestResponse(perturbation);
                else
                    Parallel.ForEach(InformationSets, informationSet => informationSet.SetAverageStrategyToBestResponse(perturbation));
            }
            else
            {
                if (!EvolutionSettings.ParallelOptimization)
                    foreach (var informationSet in InformationSets)
                        informationSet.MoveAverageStrategyTowardBestResponse(iteration, EvolutionSettings.TotalIterations, perturbation);
                else
                    Parallel.ForEach(InformationSets, informationSet => informationSet.MoveAverageStrategyTowardBestResponse(iteration, EvolutionSettings.TotalIterations, perturbation));
            }

            StrategiesDeveloperStopwatch.Stop();

#pragma warning disable CA1416
            var result = await ConsiderGeneratingReports(iteration,
                () =>
                    $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            reportCollection.Add(result);

            return reportCollection;
        }

        private void ZeroLowPorbabilities()
        {
            Parallel.ForEach(InformationSets, informationSet =>
            {
                informationSet.ZeroLowProbabilities(0.01);
            });
        }
    }
}
