using ACESim;
using ACESimBase.Util.Debugging;
using System;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.Execution;

/// <summary>Owns the existing initialization/scenario/solve/postprocessing lifecycle. Solver mathematics and override hooks stay on the developer.</summary>
public sealed class StrategyDevelopmentWorkflow
{
    private readonly StrategiesDeveloperBase developer;
    public StrategyDevelopmentWorkflow(StrategiesDeveloperBase developer)
    {
        this.developer = developer ?? throw new ArgumentNullException(nameof(developer));
    }

    public async Task<ReportCollection> DevelopStrategies(string optionSetName, int? restrictToScenarioIndex, string masterReportName)
    {
        developer.DevelopmentStartedAt = DateTime.Now;
        developer.MasterReportName = masterReportName;
        if (developer.EvolutionSettings.SkipAltogetherIfEquilibriaFileAlreadyExists && developer.EquilibriaFileAlreadyExists())
        {
            TabbedText.WriteLine($"{optionSetName}: Equilibria file already exists. Skipping equilibria generation.");
            return null;
        }

        ReportCollection reportCollection = new ReportCollection();
        {
            await developer.Initialize();
            bool constructCorrelatedEquilibrium = developer.GameDefinition.NumScenarioPermutations > 1 && developer.EvolutionSettings.ConstructCorrelatedEquilibrium;
            bool anyScenarioOK = false;
            if (restrictToScenarioIndex == -1)
            {
                anyScenarioOK = true; // it doesn't matter what the scenario is, because we're just doing a single iteration to get initialization complete
                restrictToScenarioIndex = 0;
            }

            int startingScenarioIndex = 0;
            int numScenarios = developer.GameDefinition.NumScenarioPermutations;
            if (restrictToScenarioIndex is int restriction)
            {
                // We will be restricting to a particular scenario when using scenarios and developing individual scenarios each in their own distributed process. This can permit us to generate PCA data for particular scenarios.
                // If restrictToScenarioIndex is null, then we will run through all of the scenarios. Of course, if there is only 1 scenario, then this won't make a difference.
                startingScenarioIndex = restriction;
                numScenarios = 1;
            }

            for (developer.OverallScenarioIndex = startingScenarioIndex; developer.OverallScenarioIndex < startingScenarioIndex + numScenarios; developer.OverallScenarioIndex++)
            {
                string optionSetInfo = $@"Option set {optionSetName}";
                string scenarioFullName = optionSetInfo;
                if (developer.GameDefinition.NumScenarioPermutations > 1)
                {
                    developer.ReinitializeForScenario(developer.OverallScenarioIndex, developer.GameDefinition.UseDifferentWarmup);
                    scenarioFullName = developer.GameDefinition.GetNameForScenario_WithOpponentWeight();
                    if (anyScenarioOK)
                        optionSetInfo += $" (scenario irrelevant -- loading only)";
                    else
                        optionSetInfo += $" (scenario index {developer.OverallScenarioIndex} (total scenarios: {developer.GameDefinition.NumScenarioPermutations}) = {scenarioFullName})";
                }

                developer.Status.ScenarioIndex = developer.OverallScenarioIndex;
                developer.Status.ScenarioName = scenarioFullName;
                TabbedText.WriteLineEvenIfDisabled(optionSetInfo);
            }

            ReportCollection reportToAddToCollection = await developer.RunAlgorithm(optionSetName);
            if (developer.EvolutionSettings.PCA_PerformPrincipalComponentAnalysis)
            {
                if (!anyScenarioOK)
                    await developer.PrincipalComponents.SavePCAModelData();
            }
            else if (constructCorrelatedEquilibrium)
            { // if doing correlated eq & PCA, we just save PCA data.
                developer.ScenarioCorrelation.RememberScenarioForCorrelatedEquilibrium(developer.OverallScenarioIndex);
                if ((developer.OverallScenarioIndex + 1) % developer.EvolutionSettings.ReduceCorrelatedEquilibriumEveryNScenariosIfCheckingAlongWay == 0 && developer.EvolutionSettings.CheckCorrelatedEquilibriumIncompatibilitiesAlongWay)
                    developer.ScenarioCorrelation.ReduceToCorrelatedEquilibrium_BasedOnPredeterminedIncompatibilities();
            }
            else
            {
                reportCollection.Add(reportToAddToCollection, false, true); // if constructing correlated equilibrium, we ignore the interim reports
            }

            if (restrictToScenarioIndex == null)
                await developer.PrincipalComponents.RecoverSavedPCAModelDataAndPerformAnalysis(reportCollection, masterReportName); // i.e., we've gone through all scenarios all at once (if there are more than one) and so now we should perform PCA. If we are distributing the scenarios across processes, then we will execute this through a later task instead.
            if (constructCorrelatedEquilibrium)
                reportCollection = await developer.ScenarioCorrelation.FinalizeCorrelatedEquilibrium();
        }

        DateTime finished = DateTime.Now;
        TimeSpan timeSpan = finished - developer.DevelopmentStartedAt;
        developer.Status.OverallCalculationTime = (int)timeSpan.TotalSeconds;
        return reportCollection;
    }
}
