using ACESim;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Reporting;
using ACESimBase.Util.Serialization;
using ACESimBase.Util.Statistical;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevelopmentStatus = ACESim.StrategiesDeveloperBase.DevelopmentStatus;

namespace ACESimBase.GameSolvingSupport.Execution;

/// <summary>Stored-scenario compatibility filtering and reporting. Preserves the existing heuristics; not a population equilibrium solver.</summary>
public sealed class ScenarioCorrelationWorkflow
{
    private readonly StrategiesDeveloperBase developer;
    private readonly WorkflowFiles files;
    public ScenarioCorrelationWorkflow(StrategiesDeveloperBase developer)
    {
        this.developer = developer ?? throw new ArgumentNullException(nameof(developer));
        files = new WorkflowFiles(developer);
    }

    private int GetRememberedStatusesIndex(int overallScenarioIndex) => developer.RememberedStatuses.TakeWhile(x => x.ScenarioIndex != overallScenarioIndex).Count();

    private DevelopmentStatus GetRememberedStatus(int overallScenarioIndex) => developer.RememberedStatuses[GetRememberedStatusesIndex(overallScenarioIndex)];

    public void ReportRememberedScenarios()
    {
        foreach (var r in developer.RememberedStatuses)
            TabbedText.WriteLine(r.ToString());
    }

    public void ReportRememberedScenarios(IEnumerable<int> scenarioIndices)
    {
        foreach (int i in scenarioIndices)
            TabbedText.WriteLine(GetRememberedStatus(i).ToString());
    }

    public void RememberScenarioForCorrelatedEquilibrium(int overallScenarioIndex)
    {
        if (developer.EvolutionSettings.RecordPastValues_AtEndOfScenarioOnly)
        {
            if (!developer.EvolutionSettings.RecordPastValues)
                throw new Exception("Must set RecordPastValues");
            foreach (var informationSet in developer.InformationSets)
            {
                informationSet.PastValuesCumulativeStrategyDiscounts.Add(0); // all 0's indicates that each is equal
                informationSet.RecordProbabilitiesAsPastValues();
            }

            developer.RememberedStatuses.Add(developer.Status.DeepCopy());
            if (developer.EvolutionSettings.CheckCorrelatedEquilibriumIncompatibilitiesAlongWay)
            {
                int incompatibilityCount = 0;
                int mostRecentIndex = developer.InformationSets.First().LastPastValueIndexRecorded;
                for (int i = 0; i < mostRecentIndex; i++) // each index before most recent
                {
                    (bool someoneSwitchesToMostRecent, bool someoneSwitchesFromMostRecent) = CheckForCompatibilityInCorrelatedEquilibrium(i, mostRecentIndex);
                    bool isIncompatible = someoneSwitchesFromMostRecent || someoneSwitchesToMostRecent;
                    if (isIncompatible)
                    {
                        int earlierScenarioIndex = developer.RememberedStatuses[i].ScenarioIndex;
                        developer.ScenarioIncompatibilities.AddIncompability(earlierScenarioIndex, overallScenarioIndex, iHatesJ: someoneSwitchesToMostRecent, jHatesI: someoneSwitchesFromMostRecent); // i.e., we're interpreting the hated one as the one switched to
                        incompatibilityCount++;
                    }
                }

                TabbedText.WriteLine($"Scenario index {overallScenarioIndex} incompabilities: {incompatibilityCount}");
            }
        }

        if (developer.EvolutionSettings.SerializeResults && !(developer is PlaybackOnly))
        {
            SerializeScenario(overallScenarioIndex);
        }
    }

    private bool IsCompatibleWithAllAlreadyInCorrelatedEquilibrium(int i, IEnumerable<int> js)
    {
        return js.All(j => AreCompatibleInCorrelatedEquilibrium(i, j));
    }

    private bool IsCompatibleWithAllAlreadyInCorrelatedEquilibrium_CachedIfPossible(int i, IEnumerable<int> js)
    {
        return js.All(j => AreCompatibleInCorrelatedEquilibrium_CachedIfPossible(i, j));
    }

    private bool AreCompatibleInCorrelatedEquilibrium_CachedIfPossible(int i, int j)
    {
        if (developer.ScenarioIncompatibilities.Tracked(i, j))
            return developer.ScenarioIncompatibilities.IsKnownIncompatible(i, j);
        var result = AreCompatibleInCorrelatedEquilibrium(i, j);
        developer.ScenarioIncompatibilities.AddIncompability(i, j, result, result); // disregard basis of incompatibility here
        return result;
    }

    private bool AreCompatibleInCorrelatedEquilibrium(int i, int j)
    {
        (bool someoneSwitchesFromIToJ, bool someoneSwitchesFromJToI) = CheckForCompatibilityInCorrelatedEquilibrium(i, j);
        return !someoneSwitchesFromIToJ && !someoneSwitchesFromJToI;
    }

    private (bool someoneSwitchesFromIToJ, bool someoneSwitchesFromJToI) CheckForCompatibilityInCorrelatedEquilibrium(int i, int j)
    {
        double[] p0PlayingJ_p1PlaysI = developer.GetUtilitiesForPastValueCombination(j, i);
        double[] p1PlayingJ_p0PlaysI = developer.GetUtilitiesForPastValueCombination(i, j);
        double p0InI = developer.RememberedStatuses[i].UtilitiesOverall[0];
        double p1InI = developer.RememberedStatuses[i].UtilitiesOverall[1];
        double p0InJ = developer.RememberedStatuses[j].UtilitiesOverall[0];
        double p1InJ = developer.RememberedStatuses[j].UtilitiesOverall[1];
        double p0IfSwitchingStrategyFromIToJ = p0PlayingJ_p1PlaysI[0];
        double p1IfSwitchingStrategyFromIToJ = p1PlayingJ_p0PlaysI[1];
        double p0IfSwitchingStrategyFromJToI = p1PlayingJ_p0PlaysI[0];
        double p1IfSwitchingStrategyFromJToI = p0PlayingJ_p1PlaysI[1];
        bool someoneSwitchesFromIToJ = p0IfSwitchingStrategyFromIToJ > p0InI || p1IfSwitchingStrategyFromIToJ > p1InI;
        bool someoneSwitchesFromJToI = p0IfSwitchingStrategyFromJToI > p0InJ || p1IfSwitchingStrategyFromJToI > p1InJ;
        return (someoneSwitchesFromIToJ, someoneSwitchesFromJToI);
    }

    public async Task<ReportCollection> FinalizeCorrelatedEquilibrium()
    {
        if (developer.EvolutionSettings.PCA_PerformPrincipalComponentAnalysis)
            return new ReportCollection(); // using a different approach to finding correlated equilibrium there
        if (developer.EvolutionSettings.CheckCorrelatedEquilibriumIncompatibilitiesAlongWay)
            ReduceToCorrelatedEquilibrium_BasedOnPredeterminedIncompatibilities();
        else
            ReduceToCorrelatedEquilibrium_DeterminingIncompatibilitiesNow();
        ReportCollection reportCollection = new ReportCollection();
        await developer.GenerateMainReports(() => "correq", "-Corr", reportCollection, new List<ActionStrategies>() { ActionStrategies.CorrelatedEquilibrium });
        return reportCollection;
    }

    public void ReduceToCorrelatedEquilibrium_DeterminingIncompatibilitiesNow()
    {
        // Basic strategy is to take items one at a time, finding items that are furthest separated based on the utility scores. We can just start with the highest utility item or we can pick at random, and we can do it many times so that we can average results of different correlated equilibria.
        // Could we still find some way to take out a strategy admitted to the equilibrium because there is a better one? If a new strategy comes along that is inconsistent only with one strategy, then we should perhaps keep them both (or all) so that we can figure out later which one to dump. But this would get very complicated quickly as we have multiple versions of each member of the equilibrium, and it's not clear that we get a much better equilibrium.
        int numCandidates = developer.RememberedStatuses.Count;
        bool[] consideredItems = null;
        developer.ScenarioIncompatibilities = new IncompabilityTracker();
        double p0AvgUtility = developer.RememberedStatuses.Average(x => x.UtilitiesOverall[0]);
        double p0Stdev = SummaryStatistics.Stdev(developer.RememberedStatuses.Select(x => x.UtilitiesOverall[0]));
        double p1AvgUtility = developer.RememberedStatuses.Average(x => x.UtilitiesOverall[1]);
        double p1Stdev = SummaryStatistics.Stdev(developer.RememberedStatuses.Select(x => x.UtilitiesOverall[1]));
        List<double> p0Z = developer.RememberedStatuses.Select(x => (x.UtilitiesOverall[0] - p0AvgUtility) / p0Stdev).ToList();
        List<double> p1Z = developer.RememberedStatuses.Select(x => (x.UtilitiesOverall[1] - p1AvgUtility) / p1Stdev).ToList();
        List<(double p0, double p1)> p0p1Z = p0Z.Zip(p1Z, (p0, p1) => (p0, p1)).ToList();
        List<double> sumSqZ = p0p1Z.Select(x => x.p0 + x.p1).ToList();
        List<int> includedItems = null, rejectedItems = null;
        double GetSqDistance(int i, int j)
        {
            return ((p0Z[i] - p0Z[j]) * (p0Z[i] - p0Z[j]) + (p1Z[i] - p1Z[j]) * (p1Z[i] - p1Z[j]));
        }

        int GetItemToConsider()
        {
            double highestTotalSqDistance = 0;
            int indexWithHighestTotalSqDistance = -1;
            for (int i = 0; i < consideredItems.Length; i++)
            {
                if (!consideredItems[i])
                {
                    double totalSqDistance = 0;
                    foreach (int includedItem in includedItems)
                        totalSqDistance += GetSqDistance(i, includedItem);
                    if (totalSqDistance > highestTotalSqDistance)
                    {
                        highestTotalSqDistance = totalSqDistance;
                        indexWithHighestTotalSqDistance = i;
                    }
                }
            }

            return indexWithHighestTotalSqDistance;
        }

        int highestUtilityItem = Enumerable.Range(0, numCandidates).Select((candidate, index) => (sumSqZ[candidate], index)).OrderByDescending(y => y.Item1).First().index; // item with highest sum of utilities, represented in z scores
        int[] itemsToTryAsInitial = developer.EvolutionSettings.ConstructCorrelatedEquilibriumMultipleTimesExPost ? Enumerable.Range(0, numCandidates).Take(developer.EvolutionSettings.MaxNumCorrelatedEquilibriaToConstruct).ToArray() : new int[]
        {
            highestUtilityItem
        };
        int mostItemsInAnyCorrelatedEquilibrium = 0;
        List<int> biggestCorrelatedEquilibrium = null;
        StatCollectorArray meta1 = new StatCollectorArray(), meta2 = new StatCollectorArray();
        StatCollector meta3 = new StatCollector();
        foreach (int initialItem in itemsToTryAsInitial)
        {
            TabbedText.WriteLine($"Building correlated equilibrium starting with {initialItem}");
            includedItems = new List<int>();
            includedItems.Add(initialItem);
            consideredItems = new bool[numCandidates];
            consideredItems[initialItem] = true;
            while (consideredItems.Any(x => x == false))
            {
                int itemToConsider = GetItemToConsider();
                if (IsCompatibleWithAllAlreadyInCorrelatedEquilibrium_CachedIfPossible(itemToConsider, includedItems))
                    includedItems.Add(itemToConsider);
                consideredItems[itemToConsider] = true;
            }

            StatCollectorArray a = new StatCollectorArray();
            foreach (var includedItem in includedItems.Select(x => developer.RememberedStatuses[x].CustomResult.AsDoubleArray()))
                a.Add(includedItem);
            TabbedText.WriteLine($"Average custom values among included: {String.Join(",", a.Average().Select(x => x.ToSignificantFigures(3)).ToArray())}");
            meta1.Add(a.Average().ToArray());
            a = new StatCollectorArray();
            foreach (var includedItem in includedItems.Select(x => developer.RememberedStatuses[x].UtilitiesOverall))
                a.Add(includedItem);
            meta2.Add(a.Average().ToArray());
            TabbedText.WriteLine($"Average utilities among included: {String.Join(",", a.Average().Select(x => x.ToSignificantFigures(3)).ToArray())}");
            int numberItemsIncluded = includedItems.Count();
            TabbedText.WriteLine($"Number of items {numberItemsIncluded}");
            meta3.Add(numberItemsIncluded);
            if (numberItemsIncluded > mostItemsInAnyCorrelatedEquilibrium)
            {
                mostItemsInAnyCorrelatedEquilibrium = numberItemsIncluded;
                biggestCorrelatedEquilibrium = includedItems.ToList();
            }
        }

        if (developer.EvolutionSettings.ConstructCorrelatedEquilibriumMultipleTimesExPost)
        {
            TabbedText.WriteLine($"Average custom values overall: {String.Join(",", meta1.Average().Select(x => x.ToSignificantFigures(3)).ToArray())}");
            TabbedText.WriteLine($"Average utilities overall: {String.Join(",", meta1.Average().Select(x => x.ToSignificantFigures(3)).ToArray())}");
            TabbedText.WriteLine($"Stdev custom values overall: {String.Join(",", meta1.StandardDeviation().Select(x => x.ToSignificantFigures(3)).ToArray())}");
            TabbedText.WriteLine($"Stdev utilities overall: {String.Join(",", meta1.StandardDeviation().Select(x => x.ToSignificantFigures(3)).ToArray())}");
            TabbedText.WriteLine($"Number items included average {meta3.Average().ToSignificantFigures(3)} stdev {meta3.StandardDeviation().ToSignificantFigures(3)}");
        }

        includedItems = biggestCorrelatedEquilibrium.ToList();
        TabbedText.WriteLine($"Chosen correlated equilibrium: {String.Join(",", includedItems.ToArray())}");
        rejectedItems = Enumerable.Range(0, numCandidates).Where(x => !includedItems.Contains(x)).ToList();
        var rejectedItemsScenarioIndices = rejectedItems.Select(x => developer.RememberedStatuses[x].ScenarioIndex).ToArray();
        //NOTE: The commented out code can be used to calculate compatibility of items with neighbors, and correlation of this with exploitability.
        //int[] GetClosestItems(int i, int numClosestItems)
        //{
        //    return Enumerable.Range(0, consideredItems.Length).Where(j => j != i).OrderBy(j => GetSqDistance(i, j)).Take(numClosestItems).ToArray();
        //}
        //(int iWouldSwitchToNeighbor, int neighborWouldSwitchToI) CompatibilityWithNeighbors(int i, int numClosestItems)
        //{
        //    int[] neighbors = GetClosestItems(i, numClosestItems);
        //    int iWouldSwitchToNeighbor = 0;
        //    int neighborWouldSwitchToI = 0;
        //    foreach (int neighbor in neighbors)
        //    {
        //        var result = CheckForCompatibilityInCorrelatedEquilibrium(i, neighbor);
        //        if (result.someoneSwitchesFromIToJ)
        //            iWouldSwitchToNeighbor++;
        //        if (result.someoneSwitchesFromJToI)
        //            neighborWouldSwitchToI++;
        //    }
        //    return (iWouldSwitchToNeighbor, neighborWouldSwitchToI);
        //}
        //double ComputeCoeff(double[] values1, double[] values2)
        //{
        //    var avg1 = values1.Average();
        //    var avg2 = values2.Average();
        //    var sum1 = values1.Zip(values2, (x1, y1) => (x1 - avg1) * (y1 - avg2)).Sum();
        //    var sumSqr1 = values1.Sum(x => Math.Pow((x - avg1), 2.0));
        //    var sumSqr2 = values2.Sum(y => Math.Pow((y - avg2), 2.0));
        //    var result = sum1 / Math.Sqrt(sumSqr1 * sumSqr2);
        //    return result;
        //}
        //void CheckNeighborCompatibility(int numNeighbors)
        //{
        //    TabbedText.WriteLine($"Checking compatibility with {numNeighbors} neighbors");
        //    List<(double exploitability, int numSwitchesToNeighbor, int numSwitchesFromNeighbor)> results = new List<(double exploitability, int numSwitchesToNeighbor, int numSwitchesFromNeighbor)>();
        //    for (int i = 0; i < consideredItems.Length; i++)
        //    {
        //        double exploitability = RememberedStatuses[i].BestResponseImprovementAdjAvg;
        //        var compatibility = CompatibilityWithNeighbors(i, numNeighbors);
        //        results.Add((exploitability, compatibility.iWouldSwitchToNeighbor, compatibility.neighborWouldSwitchToI));
        //    }
        //    results = results.OrderBy(x => x.exploitability).ToList();
        //    int k = 0;
        //    foreach (var result in results)
        //        TabbedText.WriteLine($"{k++}: {result.exploitability} switch to neighbor: {result.numSwitchesToNeighbor} switch from neighbor: {result.numSwitchesFromNeighbor}");
        //    double correlationToNeighbor = ComputeCoeff(results.Select(x => x.exploitability).ToArray(), results.Select(x => (double)x.numSwitchesToNeighbor).ToArray());
        //    double correlationFromNeighbor = ComputeCoeff(results.Select(x => x.exploitability).ToArray(), results.Select(x => (double)x.numSwitchesFromNeighbor).ToArray());
        //    double correlationSum = ComputeCoeff(results.Select(x => x.exploitability).ToArray(), results.Select(x => (double)x.numSwitchesToNeighbor + (double)x.numSwitchesFromNeighbor).ToArray());
        //    double correlationDifference = ComputeCoeff(results.Select(x => x.exploitability).ToArray(), results.Select(x => (double)x.numSwitchesToNeighbor - (double)x.numSwitchesFromNeighbor).ToArray());
        //    TabbedText.WriteLine($"Correlation between exploitability and switches to {numNeighbors} neighbors: {correlationToNeighbor} switches from neighbor {correlationFromNeighbor} sum {correlationSum} difference {correlationDifference}\r\n");
        //}
        //CheckNeighborCompatibility(1);
        //CheckNeighborCompatibility(3);
        //CheckNeighborCompatibility(7);
        //CheckNeighborCompatibility(20);
        ReportRememberedScenarios(includedItems);
        foreach (var removingScenarioIndex in rejectedItemsScenarioIndices)
        {
            RemovePastValueRecordedIndex(removingScenarioIndex);
        }
    }

    public void ReduceToCorrelatedEquilibrium_BasedOnPredeterminedIncompatibilities()
    {
        TabbedText.WriteLine($"Reducing correlated equilibrium...");
        //int[] candidateScenarioIndices = Incompabilities.GetOrdered(GameDefinition.NumScenarioPermutations, mostIncompatibleFirst: false, includeHaters: true, includeHated: false); // consider last the scenarios that the most other scenarios will want to switch from; remember that haters are the strategies we are switching from and hated are strategies we are switching to
        int[] candidateScenarioIndices = developer.ScenarioIncompatibilities.GetOrdered(developer.GameDefinition.NumScenarioPermutations, mostIncompatibleFirst: true, includeHaters: false, includeHated: true); // consider first the scenarios that the most other scenarios will want to switch to; remember that haters are the strategies we are switching from and hated are strategies we are switching to
        List<int> addedScenarioIndices = new List<int>();
        List<int> removedScenarioIndices = new List<int>();
        foreach (int candidate in candidateScenarioIndices)
        {
            if (!developer.ScenarioIncompatibilities.IsKnownIncompatibleWithAny(candidate, addedScenarioIndices))
                addedScenarioIndices.Add(candidate);
            else
                removedScenarioIndices.Add(candidate);
        }

        removedScenarioIndices = removedScenarioIndices.OrderByDescending(x => x).ToList();
        foreach (var removingScenarioIndex in removedScenarioIndices)
        {
            developer.ScenarioIncompatibilities.Remove(removingScenarioIndex);
            RemovePastValueRecordedIndex(removingScenarioIndex);
        }

        ReportRememberedScenarios();
        TabbedText.WriteLine($"...Completed reducing correlated equilibrium");
    }

    private void RemovePastValueRecordedIndex(int overallScenarioIndexToRemove)
    {
        int correspondingRememberedIndex = GetRememberedStatusesIndex(overallScenarioIndexToRemove);
        developer.RememberedStatuses.RemoveAt(correspondingRememberedIndex);
        developer.InformationSets.ForEach(x =>
        {
            x.PastValues.RemoveAt(correspondingRememberedIndex);
            x.PastValuesCumulativeStrategyDiscounts.RemoveAt(correspondingRememberedIndex);
            x.LastPastValueIndexRecorded--;
        });
    }

    private void SerializeScenario(int overallScenarioIndex)
    {
        try
        {
            string path, filename;
            files.GetPathAndFilenameForScenario(overallScenarioIndex, out path, out filename);
            if (developer.EvolutionSettings.SerializeInformationSetDataOnly)
                StrategySerialization.SerializeInformationSets(developer.InformationSets, path, filename, developer.EvolutionSettings.SaveToAzureBlob);
            else
                StrategySerialization.SerializeStrategies(developer.Strategies.ToArray(), path, filename, developer.EvolutionSettings.SaveToAzureBlob);
        }
        catch
        {
        // ignore failure to serialize strategies
        }
    }
}
