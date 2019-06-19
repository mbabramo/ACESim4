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
        public bool AddNoiseToBestResponses = false;
        public bool ReportOnBestResponse = false; // not yet implemented
        public bool BestBecomesResult = true;  
        public double BestExploitability = int.MaxValue; // initialize to worst possible score (i.e., highest possible exploitability)

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

            //double lambda2 = 1.0 / IterationNumDouble;

            string reportString = null;
            double[] lastUtilities = new double[NumNonChancePlayers];

            CalculateBestResponse(true);

            if (BestBecomesResult && iteration >= 3)
            {
                double exploitability = BestResponseImprovement.Sum();
                if (exploitability < BestExploitability)
                {
                    Parallel.ForEach(InformationSets, informationSet => informationSet.CreateBackup());
                    BestExploitability = exploitability;
                }
                if (iteration == EvolutionSettings.TotalVanillaCFRIterations)
                {
                    Parallel.ForEach(InformationSets, informationSet => informationSet.RestoreBackup());
                }
            }

            if (AddNoiseToBestResponses)
                Parallel.ForEach(InformationSets, informationSet => informationSet.AddNoiseToBestResponse(0.10, iteration));

            if (!EvolutionSettings.ParallelOptimization)
            foreach (var informationSet in InformationSets)
                informationSet.MoveAverageStrategyTowardBestResponse(iteration, EvolutionSettings.TotalVanillaCFRIterations);
            else
                Parallel.ForEach(InformationSets, informationSet => informationSet.MoveAverageStrategyTowardBestResponse(iteration, EvolutionSettings.TotalVanillaCFRIterations));

            StrategiesDeveloperStopwatch.Stop();

            reportString = await GenerateReports(iteration,
                () =>
                    $"Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");

            return reportString;
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
