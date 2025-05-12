using ACESim;
using ACESimBase.Util.Debugging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms
{
    [Serializable]
    public partial class GeneticAlgorithm : StrategiesDeveloperBase
    {

        public GeneticAlgorithm(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new GeneticAlgorithm(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }

        GeneticPopulation Pop;

        public static void RunFromAnotherAlgorithm(List<InformationSetNode> informationSets, int numIterations, Func<(double exploitability, double[] utilities)> bestResponseCalculator)
        {
            GeneticPopulation pop = new GeneticPopulation(informationSets, bestResponseCalculator, randomize: false); 
            for (int iteration = 1; iteration <= numIterations; iteration++)
            {
                pop.Generation(iteration, numIterations);
                string reportString = $"{iteration}: {pop.Members[0].GameFitness}";
                TabbedText.WriteLine(reportString);
            }
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            ReportCollection reportCollection = new ReportCollection();
            Pop = new GeneticPopulation(InformationSets, CalculateBestResponseAndGetFitnessAndUtilities, randomize: true);
            StrategiesDeveloperStopwatch.Reset();

            for (int iteration = 1; iteration <= EvolutionSettings.TotalIterations; iteration++)
            {
                reportCollection = await GeneticIteration(iteration, EvolutionSettings.TotalIterations);
            }

            return reportCollection;
        }

        private async Task<ReportCollection> GeneticIteration(int iteration, int maxIteration)
        {
            Pop.Generation(iteration, maxIteration);

            string reportString = $"{iteration}: {Pop.Members[0].GameFitness}";
            TabbedText.WriteLine(reportString);

#pragma warning disable CA1416
            ReportCollection reportCollection = await ConsiderGeneratingReports(iteration,
                () =>
                    $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");

            return reportCollection;
        }
    }
}
