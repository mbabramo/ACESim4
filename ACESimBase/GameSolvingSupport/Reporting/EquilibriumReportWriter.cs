using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using System.Diagnostics;
using ACESimBase.GameSolvingSupport.Policies;
using ACESimBase.Util.Debugging;

namespace ACESimBase.GameSolvingSupport.Reporting;

/// <summary>The existing article report protocol, separated from solver/training ownership.
/// -Corr pools matched profiles equally; -Avg is the historical behavioral arithmetic average.
/// Neither recovery counts nor future population-selection weights alter this protocol.</summary>
public sealed class EquilibriumReportWriter
{
    private readonly StrategiesDeveloperBase developer;
    public EquilibriumReportWriter(StrategiesDeveloperBase developer) => this.developer = developer ?? throw new ArgumentNullException(nameof(developer));
    [SupportedOSPlatform("windows")]
    public async Task ProcessIdentifiedEquilibria(ReportCollection reportCollection, List<double[]> equilibria, bool multipleEquilibriaPossible)
    {
        if (developer.EvolutionSettings.CreateEquilibriaFile)
            LegacyEquilibriumFiles.Write(LegacyEquilibriumFiles.PathFor(developer), equilibria);
        await GenerateReportsFromEquilibria(equilibria, reportCollection, multipleEquilibriaPossible);
        if (equilibria.Any())
            LegacyEquilibriumPolicies.ApplyCurrent(developer, equilibria.First());
    }

    [SupportedOSPlatform("windows")]
    public async Task GenerateReportsFromEquilibria(List<double[]> equilibria, ReportCollection reportCollection, bool multipleEquilibriaPossible)
    {
        bool includeAverageEquilibriumReport = multipleEquilibriaPossible;
        bool includeCorrelatedEquilibriumReport = multipleEquilibriaPossible;
        if (includeCorrelatedEquilibriumReport || developer.EvolutionSettings.UseExistingEquilibriaIfAvailable || developer.EvolutionSettings.GenerateManualReports)
            developer.SaveWeightedGameProgressesAfterEachReport = true;
        bool includeReportForFirstEquilibrium = true;
        bool includeReportForEachEquilibrium = true;
        int numEquilibria = equilibria.Count();
        var infoSets = developer.InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetNodeNumber).ToList();
        var infoSetNames = infoSets.Select(x => x.ToStringWithoutValues()).ToArray();
        for (int eqNum = 0; eqNum < numEquilibria; eqNum++)
        {
            bool isFirst = eqNum == 0;
            bool isLast = eqNum == numEquilibria - 1;
            var actionProbabilities = equilibria[eqNum];
            await ProcessEquilibrium(reportCollection, includeAverageEquilibriumReport, includeCorrelatedEquilibriumReport, includeReportForFirstEquilibrium, includeReportForEachEquilibrium, numEquilibria, infoSets, eqNum, isFirst, isLast, actionProbabilities);
        }
    }

    [SupportedOSPlatform("windows")]
    public async Task ProcessEquilibrium(ReportCollection reportCollection, bool includeAverageEquilibriumReport, bool includeCorrelatedEquilibriumReport, bool includeReportForFirstEquilibrium, bool includeReportForEachEquilibrium, int numEquilibria, List<InformationSetNode> infoSets, int eqNum, bool isFirst, bool isLast, double[] actionProbabilities)
    {
        LegacyEquilibriumPolicies.InstallAndAccumulateReportAverage(infoSets, actionProbabilities, eqNum, includeCorrelatedEquilibriumReport);

        developer.IdentifyPressureOnInformationSets();

        //double[] utils = GetAverageUtilities(false);
        //double[] maxPurifiedUtils = GetMaximumUtilitiesFromPurifiedStrategies();
        //if (Enumerable.Range(0, NumNonChancePlayers).Any(p => maxPurifiedUtils[p] > utils[p] + 0.001))
        //    TabbedText.WriteLine($"Maximum not achieved {String.Join(",", utils)} vs. {String.Join(",", maxPurifiedUtils)}");

        if ((includeReportForFirstEquilibrium && isFirst) || includeReportForEachEquilibrium)
        {
            await AddReportForEquilibrium(reportCollection, numEquilibria, eqNum);
        }
        if (isLast)
        {
            if (includeCorrelatedEquilibriumReport)
            {
                AddCorrelatedEquilibriumReport(reportCollection);
            }
            if (includeAverageEquilibriumReport)
            {
                await AddAverageEquilibriumReport(reportCollection);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public async Task AddReportForEquilibrium(ReportCollection reportCollection, int numEquilibria, int eqNum)
    {
        Stopwatch s = new Stopwatch();
        s.Start();
        developer.EvolutionSettings.ActionStrategiesToUseInReporting = new List<ActionStrategies>() { ActionStrategies.CurrentProbability }; // will use latest equilibrium
        bool priorCurrent = developer.EvolutionSettings.UseCurrentStrategyForBestResponse;
        bool priorAccelerated = developer.EvolutionSettings.UseAcceleratedBestResponse;
        bool priorRefinement = developer.EvolutionSettings.CalculatePerturbedBestResponseRefinement;
        bool priorRounding = developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse;
        // Keep cumulative paths for the separate correlated-profile report, but
        // an individual equilibrium's diagrams must see only its own paths.
        var cumulativeProgresses = developer.SavedWeightedGameProgresses;
        developer.SavedWeightedGameProgresses = new List<(GameProgress theProgress, double weight)>();
        try
        {
            // A running average of different equilibria need not itself be an equilibrium.
            // Its best-response diagnostic does not describe this individual profile.
            developer.EvolutionSettings.UseCurrentStrategyForBestResponse = true;
            developer.EvolutionSettings.UseAcceleratedBestResponse = true;
            developer.EvolutionSettings.CalculatePerturbedBestResponseRefinement = false;
            developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = false;
            var reportResult = await developer.ConsiderGeneratingReports(developer.EvolutionSettings.ReportEveryNIterations ?? 0,
                () =>
                    $"{developer.GameDefinition.OptionSetName}{(developer.EvolutionSettings.SequenceFormNumPriorsToUseToGenerateEquilibria > 1 ? $"-Eq{eqNum + 1}" : "")}",
                reportSuffix: numEquilibria > 1 ? $"-Eq{eqNum + 1}" : "");
            reportCollection.Add(reportResult, false, true);
        }
        finally
        {
            cumulativeProgresses.AddRange(developer.SavedWeightedGameProgresses);
            developer.SavedWeightedGameProgresses = cumulativeProgresses;
            developer.EvolutionSettings.UseCurrentStrategyForBestResponse = priorCurrent;
            developer.EvolutionSettings.UseAcceleratedBestResponse = priorAccelerated;
            developer.EvolutionSettings.CalculatePerturbedBestResponseRefinement = priorRefinement;
            developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = priorRounding;
        }
        TabbedText.WriteLine($"Elapsed milliseconds report for eq {eqNum + 1} of {numEquilibria}: {s.ElapsedMilliseconds}");
    }

    public void AddCorrelatedEquilibriumReport(ReportCollection reportCollection)
    {
        Stopwatch s = new Stopwatch();
        s.Start();
        var reportResult = developer.GenerateReportFromSavedWeightedGameProgresses(false);
        reportResult.AddReportSuffix($"-Corr");
        developer.PrintReportsToScreenIfNotSuppressed(reportResult);
        reportCollection.Add(reportResult, false, true);
        developer.GenerateManualReports($"-Corr");
        developer.SavedWeightedGameProgresses = new List<(GameProgress theProgress, double weight)>();
        TabbedText.WriteLine($"Elapsed milliseconds generating correlated equilibrium report: {s.ElapsedMilliseconds}");
    }

    [SupportedOSPlatform("windows")]
    public async Task AddAverageEquilibriumReport(ReportCollection reportCollection)
    {
        Stopwatch s = new Stopwatch();
        s.Start();
        developer.EvolutionSettings.ActionStrategiesToUseInReporting = new List<ActionStrategies>() { ActionStrategies.AverageStrategy };
        var reportResult = await developer.ConsiderGeneratingReports(developer.EvolutionSettings.ReportEveryNIterations ?? 0,
            () => $"{developer.GameDefinition.OptionSetName}-Avg", reportSuffix: "-Avg", suppressPrintTree: true);
        reportCollection.Add(reportResult, false, true);
        TabbedText.WriteLine($"Elapsed milliseconds generating average equilibrium report: {s.ElapsedMilliseconds}");
    }
}
