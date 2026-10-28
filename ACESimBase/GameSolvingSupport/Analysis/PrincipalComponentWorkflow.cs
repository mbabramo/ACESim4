using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Execution;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.GameSolvingSupport.Statistical;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Parallelization;
using ACESimBase.Util.Randomization;
using ACESimBase.Util.Reporting;
using ACESimBase.Util.Serialization;
using ACESimBase.Util.Statistical;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.Analysis;

/// <summary>Owns PCA observation persistence, utility-surrogate fitting, candidate evaluation and reporting. Calls the developer's virtual model hooks so each solver keeps its representation.</summary>
public sealed class PrincipalComponentWorkflow
{
    private readonly StrategiesDeveloperBase developer;
    private readonly WorkflowFiles files;
    public PrincipalComponentWorkflow(StrategiesDeveloperBase developer)
    {
        this.developer = developer ?? throw new ArgumentNullException(nameof(developer));
        files = new WorkflowFiles(developer);
    }

    public async Task PostIterationWorkForPrincipalComponentsAnalysis(int iteration, ReportCollection reportCollection)
    {
        if (developer.EvolutionSettings.PCA_PerformPrincipalComponentAnalysis)
        {
            if (iteration == 1)
            {
                developer.InitializePreparationForPCA();
            }

            if (iteration >= developer.EvolutionSettings.PCA_FirstIterationToSaveAsPCAObservation && iteration % developer.EvolutionSettings.PCA_SavePCAObservationEveryNIterationsAfterFirst == 0)
            {
                float[][] perPlayerModels = developer.GetCurrentModelVariablesForPCA();
                developer.ModelDataSavedForPCA.Add(perPlayerModels);
            }

            if (iteration == developer.EvolutionSettings.TotalIterations && developer.GameDefinition.NumScenarioPermutations == 1) // if there are multiple scenarios, we wait for all to be complete
            {
                TabbedText.HideConsoleProgressString();
                await PerformPrincipalComponentAnalysis(true);
                await BuildPCAModelsForSpecifiedScenarios(reportCollection, "report", new int[] { 0 });
                TabbedText.ShowConsoleProgressString();
            }
        }
    }

    public Task SavePCAModelData()
    {
        int numModels, totalBytesNeeded;
        GetPCAModelCount(out numModels, out totalBytesNeeded);
        var buffer = new byte[totalBytesNeeded];
        int numBytesCopied = 0;
        for (int m = 0; m < numModels; m++)
            for (int p = 0; p < developer.NumNonChancePlayers; p++)
            {
                float[] src = developer.ModelDataSavedForPCA[m][p];
                int numBytesToCopy = src.Length * sizeof(float);
                Buffer.BlockCopy(src, 0, buffer, numBytesCopied, numBytesToCopy);
                numBytesCopied += numBytesToCopy;
            }

        TabbedText.WriteLine("Saving PCA data for scenario " + developer.OverallScenarioIndex);
        string path, filename;
        files.GetPathAndFilenameForScenario(developer.OverallScenarioIndex, out path, out filename);
        AzureBlob.SaveByteArrayToFileOrAzure(buffer, path, "pcadata", filename + ".pcd", developer.EvolutionSettings.SaveToAzureBlob);
        developer.ModelDataSavedForPCA = new List<float[][]>();
        return Task.CompletedTask;
    }

    private void GetPCAModelCount(out int numModels, out int totalBytesNeeded)
    {
        var currentModel = developer.GetCurrentModelVariablesForPCA();
        numModels = developer.ModelDataSavedForPCA.Count();
        totalBytesNeeded = developer.ModelDataSavedForPCA.Sum(m => m.Sum(p => p.Length)) * sizeof(float);
    }

    public Task RecoverSavedPCAModelData()
    {
        var currentModel = developer.GetCurrentModelVariablesForPCA();
        int[] numFloatElementsPerPlayer = currentModel.Select(x => x.Length).ToArray();
        developer.ModelDataSavedForPCA = new List<float[][]>();
        for (int scenarioIndex = 0; scenarioIndex < developer.GameDefinition.NumScenarioPermutations; scenarioIndex++)
        {
            string path, filename;
            files.GetPathAndFilenameForScenario(scenarioIndex, out path, out filename);
            byte[] bytes = AzureBlob.GetByteArrayFromFileOrAzure(path, "pcadata", filename + ".pcd", developer.EvolutionSettings.SaveToAzureBlob);
            int numBytesCopied = 0;
            int modelIndex = 0;
            while (numBytesCopied < bytes.Length)
            {
                var particularModel = new float[developer.NumNonChancePlayers][];
                for (int p = 0; p < developer.NumNonChancePlayers; p++)
                {
                    float[] modelDataForPlayer = new float[numFloatElementsPerPlayer[p]];
                    int numBytesToCopy = modelDataForPlayer.Length * sizeof(float);
                    Buffer.BlockCopy(bytes, numBytesCopied, modelDataForPlayer, 0, numBytesToCopy);
                    numBytesCopied += numBytesToCopy;
                    particularModel[p] = modelDataForPlayer;
                }

                developer.ModelDataSavedForPCA.Add(particularModel);
                modelIndex++;
            }
        }

        return Task.CompletedTask;
    }

    public async Task RecoverSavedPCAModelDataAndPerformAnalysis(ReportCollection reportCollection, string masterReportName)
    {
        if (developer.EvolutionSettings.PCA_PerformPrincipalComponentAnalysis)
        {
            await RecoverSavedPCAModelData();
            await PerformPrincipalComponentAnalysis(true);
            await BuildPCAModelsForSpecifiedScenarios(reportCollection, masterReportName, developer.GameDefinition.PostWarmupScenarioIndices);
        }
    }

    private async Task PerformPrincipalComponentAnalysis(bool saveResults)
    {
        Stopwatch s = new Stopwatch();
        s.Start();
        developer.PCAResultsForEachPlayer = new PrincipalComponentsAnalysis[developer.NumNonChancePlayers];
        for (byte p = 0; p < developer.NumNonChancePlayers; p++)
            developer.PCAResultsForEachPlayer[p] = PerformPrincipalComponentAnalysis(p);
        if (saveResults)
            await SavePCAResults();
        //await LoadReducedFormStrategies();
        await developer.ProcessPrincipalComponentsResults();
        TabbedText.WriteLine($"Total time performing PCA and creating compound strategy {s.ElapsedMilliseconds} ms");
    // await BuildModelPredictingUtilitiesBasedOnPrincipalComponents(reportCollection);
    }

    private Task SavePCAResults()
    {
        for (byte p = 0; p < developer.NumNonChancePlayers; p++)
        {
            string path, filename;
            files.GetPathAndFilenameForOptionSet($"p{p}.pcr", out path, out filename);
            AzureBlob.SerializeToFileOrAzure(developer.PCAResultsForEachPlayer[p], path, "pcadata", filename, developer.EvolutionSettings.SaveToAzureBlob);
            developer.PCAResultsForEachPlayer[p] = PerformPrincipalComponentAnalysis(p);
        }

        return Task.CompletedTask;
    }

    private async Task LoadPCAResults(ReportCollection reportCollection, string masterReportName)
    {
        developer.PCAResultsForEachPlayer = new PrincipalComponentsAnalysis[developer.NumNonChancePlayers];
        TabbedText.WriteLine("Loading PCA results");
        for (byte p = 0; p < developer.NumNonChancePlayers; p++)
        {
            string path, filename;
            files.GetPathAndFilenameForOptionSet($"p{p}.pcr", out path, out filename);
            developer.PCAResultsForEachPlayer[p] = (PrincipalComponentsAnalysis)AzureBlob.GetSerializedObjectFromFileOrAzure(path, "pcadata", filename, developer.EvolutionSettings.SaveToAzureBlob);
        }

        TabbedText.WriteLine("Processing PCA results");
        await developer.ProcessPrincipalComponentsResults();
        TabbedText.WriteLine($"Building model based on PCA results");
        await BuildPCAModelsForSpecifiedScenarios(reportCollection, masterReportName, developer.GameDefinition.PostWarmupScenarioIndices);
    }

    public PrincipalComponentsAnalysis PerformPrincipalComponentAnalysis(byte playerIndex)
    {
        int numVariablesInModel = developer.ModelDataSavedForPCA.First()[playerIndex].Length;
        int numVersionsOfModel = developer.ModelDataSavedForPCA.Count();
        double[,] originalData = new double[numVersionsOfModel, numVariablesInModel];
        for (int i = 0; i < numVersionsOfModel; i++)
            for (int j = 0; j < numVariablesInModel; j++)
                originalData[i, j] = developer.ModelDataSavedForPCA[i][playerIndex][j];
        PrincipalComponentsAnalysis stats = new PrincipalComponentsAnalysis(originalData, developer.EvolutionSettings.PCA_NumPrincipalComponents, developer.EvolutionSettings.PCA_Precision);
        return stats;
    }

    public List<double>[] GetRandomPrincipalComponentWeightsForEachPlayer(int randomIndex, double multiplier)
    {
        return Enumerable.Range(0, developer.NumNonChancePlayers).Select(x => GetRandomPrincipalComponentWeights((byte)x, new ConsistentRandomSequenceProducer(randomIndex, 1_000_000 * x), multiplier)).ToArray();
    }

    public List<double> GetRandomPrincipalComponentWeights(byte playerIndex, ConsistentRandomSequenceProducer randomizer, double multiplier)
    {
        var stats = developer.PCAResultsForEachPlayer[playerIndex];
        int numPrincipalComponents = developer.EvolutionSettings.PCA_NumPrincipalComponents;
        var result = Enumerable.Range(0, numPrincipalComponents).Select(principalComponent => multiplier * InvNormal.Calculate(randomizer.GetDoubleAtIndex(principalComponent)) * stats.principalComponentStdevs[principalComponent]).ToList();
        return result;
    }

    public List<double> GetRandomPrincipalComponentWeights(byte playerIndex, double[] randomizerValues, double multiplier)
    {
        var stats = developer.PCAResultsForEachPlayer[playerIndex];
        int numPrincipalComponents = developer.EvolutionSettings.PCA_NumPrincipalComponents;
        var result = Enumerable.Range(0, numPrincipalComponents).Select(principalComponent => multiplier * InvNormal.Calculate(randomizerValues[principalComponent]) * stats.principalComponentStdevs[principalComponent]).ToList();
        return result;
    }

    public List<double>[] GetRandomPrincipalComponentWeights(byte playerIndex, double[][] randomizerValues, double multiplier)
    {
        int numToGet = randomizerValues.GetLength(0);
        return Enumerable.Range(0, numToGet).Select(x => GetRandomPrincipalComponentWeights(playerIndex, randomizerValues[x], multiplier)).ToArray();
    }

    private async Task BuildPCAModelsForSpecifiedScenarios(ReportCollection reportCollection, string masterReportName, int[] scenarioIndices)
    {
        if (developer.EvolutionSettings.PCA_BuildModelToPredictUtilitiesBasedOnPrincipalComponents)
        {
            for (int s = 0; s < scenarioIndices.Length; s++)
            {
                await BuildModelPredictingUtilitiesBasedOnPrincipalComponents(reportCollection, masterReportName, scenarioIndices, s);
            }
        }
    }

    private async Task BuildModelPredictingUtilitiesBasedOnPrincipalComponents(ReportCollection reportCollection, string masterReportName, int[] scenarioIndices, int s)
    {
        int numUtilitiesToCalculateToBuildModel = developer.EvolutionSettings.PCA_NumUtilitiesToCalculateToBuildModel;
        TabbedText.WriteLine("");
        TabbedText.WriteLine($"Post-warmup option set {s + 1} of {developer.GameDefinition.NumPostWarmupPossibilities}");
        int scenarioIndex = scenarioIndices[s];
        developer.ReinitializeForScenario(scenarioIndex, false, true /* b/c if this is first, we might have set the scenario for just one iteration without actually calling ReinitializeForScenario */);
        List<ModelPredictingUtilitiesDatum> data = await GetDataForModelPredictingUtilities(numUtilitiesToCalculateToBuildModel);
        await CompleteUtilitiesModelBuild(data);
        if (developer.EvolutionSettings.PCA_AssessModelsToPredictUtilitiesFromPrincipalComponents)
            await AssessModelsToPredictUtilitiesFromPrincipalComponents();
        await UsePCAToEvaluateEquilibria(reportCollection, masterReportName);
    }

    private async Task CompleteUtilitiesModelBuild(List<ModelPredictingUtilitiesDatum> data)
    {
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        developer.ModelsToPredictUtilitiesFromPrincipalComponents = new RegressionController[developer.NumNonChancePlayers];
        for (byte p = 0; p < developer.NumNonChancePlayers; p++)
        {
            developer.ModelsToPredictUtilitiesFromPrincipalComponents[p] = new RegressionController(developer.EvolutionSettings.RegressionFactory());
            var dataConverted = data.Select(x => x.Convert(p)).ToArray();
            await developer.ModelsToPredictUtilitiesFromPrincipalComponents[p].Regress(dataConverted);
        }

        stopwatch.Stop();
        TabbedText.WriteLine($"Utilities model build in {stopwatch.ElapsedMilliseconds} ms");
    }

    private async Task<List<ModelPredictingUtilitiesDatum>> GetDataForModelPredictingUtilities(int numUtilitiesToCalculateToBuildModel)
    {
        Stopwatch outerStopwatch = new Stopwatch();
        outerStopwatch.Start();
        StatCollectorArray utilitiesStats = new StatCollectorArray();
        StatCollectorArray customStatsStats = new StatCollectorArray();
        StatCollector timeStats = new StatCollector();
        List<ModelPredictingUtilitiesDatum> data = new List<ModelPredictingUtilitiesDatum>();
        for (int utilIndex = 0; utilIndex < numUtilitiesToCalculateToBuildModel; utilIndex++)
        {
            Stopwatch innerStopwatch = new Stopwatch();
            innerStopwatch.Start();
            List<double>[] principalComponentWeightsForEachPlayer = GetRandomPrincipalComponentWeightsForEachPlayer(utilIndex * 737, 1.5);
            string principalComponentWeightsString = String.Join("; ", Enumerable.Range(0, developer.NumNonChancePlayers).Select(x => x.ToString() + ": " + principalComponentWeightsForEachPlayer[x].ToSignificantFigures(3)));
            bool printAndTimeEachCalculation = false;
            await developer.SetModelToPrincipalComponentWeights(principalComponentWeightsForEachPlayer);
            (double[] compoundUtilities, double[] customStats) = await developer.PCA_UtilitiesAndCustomResultAverage(utilIndex * numUtilitiesToCalculateToBuildModel, printAndTimeEachCalculation);
            data.Add(new ModelPredictingUtilitiesDatum(principalComponentWeightsForEachPlayer, compoundUtilities));
            innerStopwatch.Stop();
            timeStats.Add(innerStopwatch.ElapsedMilliseconds);
            utilitiesStats.Add(compoundUtilities);
            customStatsStats.Add(customStats);
            if (printAndTimeEachCalculation)
                TabbedText.WriteLine($"Data input {utilIndex}: {principalComponentWeightsString} utilities: {compoundUtilities.ToSignificantFigures(6)} customStats (same for each post-warmup scenario): {customStats.ToSignificantFigures(6)}"); // same for each post-warmup scenario because here we are using the same principal component weights to detemrine strategy; the only thing that's changing is utilities.
        }

        TabbedText.WriteLine($"Average ms per each of {numUtilitiesToCalculateToBuildModel} utilities to calculate: {timeStats.Average().ToSignificantFigures(3)} (total time: {outerStopwatch.ElapsedMilliseconds})");
        TabbedText.WriteLine($"utilities {utilitiesStats.ToString()} custom stats {customStatsStats.ToString()}");
        return data;
    }

    public Task<(double[] compoundUtilities, double[] customStats)> PCA_UtilitiesAndCustomResultAverage(int randSeed, bool reportResults = false)
    {
        (double[] utilities, double[] customStats) ultimateResult = default;
        if (reportResults)
            TabbedText.Write($"Calculating utilities");
        Stopwatch s = new Stopwatch();
        s.Start();
        bool useAcceleratedBestResponseForCalculation = true;
        if (developer.EvolutionSettings.UseAcceleratedBestResponse && useAcceleratedBestResponseForCalculation)
        {
            var abrResult = developer.ExecuteAcceleratedBestResponse(false);
            double[] utilities = abrResult.Select(x => x.utilityResult).ToArray();
            double[] customStats = abrResult.First().customResult.AsDoubleArray();
            ultimateResult = (utilities, customStats);
        }
        else
        {
            var calculator = new UtilitiesAndCustomResultTreeCalculation(false);
            var treeWalkResult = developer.TreeWalk_Tree(calculator, true);
            ultimateResult = (treeWalkResult.utilities, treeWalkResult.customResult.AsDoubleArray());
        }

        if (reportResults)
            TabbedText.WriteLine($"({ultimateResult.utilities.ToSignificantFigures(6)}; customResults: {ultimateResult.customStats.ToSignificantFigures(6)} time {s.ElapsedMilliseconds} ms");
        return Task.FromResult(ultimateResult);
    }

    public async Task UsePCAToEvaluateEquilibria(ReportCollection reportCollection, string masterReportName)
    {
        if (developer.NumNonChancePlayers != 2)
            throw new NotSupportedException();
        TabbedText.WriteLine($"Search for equilibria with PCA");
        int numStrategyChoicesPerPlayer = developer.EvolutionSettings.PCA_NumStrategyChoicesPerPlayer;
        double[,] player0Utilities, player1Utilities;
        List<double>[] principalComponentsWeightsForPlayer0, principalComponentsWeightsForPlayer1;
        GetEstimatedUtilitiesForStrategyChoices(numStrategyChoicesPerPlayer, out player0Utilities, out player1Utilities, out principalComponentsWeightsForPlayer0, out principalComponentsWeightsForPlayer1);
        var nashResults = await UsePCAToEvaluateNashEquilibria(player0Utilities, player1Utilities, principalComponentsWeightsForPlayer0, principalComponentsWeightsForPlayer1, reportCollection, masterReportName);
        await GenerateReportsAfterPCA(reportCollection, developer.GameDefinition.OptionSetName + "-" + developer.GameDefinition.GetNameForScenario(), "bestnash", masterReportName);
        await UsePCAToEvaluateCorrelatedEquilibria(player0Utilities, player1Utilities, principalComponentsWeightsForPlayer0, principalComponentsWeightsForPlayer1, reportCollection, nashResults.distanceFromNash, 10_000, 100, masterReportName);
    }

    private async Task UsePCAToEvaluateCorrelatedEquilibria(double[,] player0Utilities, double[,] player1Utilities, List<double>[] principalComponentsWeightsForPlayer0, List<double>[] principalComponentsWeightsForPlayer1, ReportCollection reportCollection, double[,] distanceFromNash, int maxNumCandidatesToConsider, int numAttemptsToGetBiggerEquilibria, string masterReportName)
    {
        if (!developer.EvolutionSettings.ConstructCorrelatedEquilibrium)
            return;
        TabbedText.WriteLine($"Correlated equilibria");
        List<(int player0Strategy, int player1Strategy)> correlatedEquilibriumStrategies = PureStrategiesFinder.GetCorrelatedEquilibrium_OrderingByApproxNashValue(player0Utilities, player1Utilities, distanceFromNash);
        List<List<double>[]> correlatedEquilibrium = correlatedEquilibriumStrategies.Select(x => (new List<double>[2] { principalComponentsWeightsForPlayer0[x.player0Strategy], principalComponentsWeightsForPlayer1[x.player1Strategy] })).ToList();
        ConsistentRandomSequenceProducer randomSequenceProducer = new ConsistentRandomSequenceProducer(6_012_345); // arbitrary sequence
        for (int i = 0; i < numAttemptsToGetBiggerEquilibria; i++)
        {
            List<(int player0Strategy, int player1Strategy)> candidateEquilibriumStrategies = PureStrategiesFinder.GetCorrelatedEquilibrium_OrderingByFarthestDistanceFromAdmittees_StartingWithRandomStrategy(player0Utilities, player1Utilities, distanceFromNash, maxNumCandidatesToConsider, randomSequenceProducer);
            List<List<double>[]> candidateEquilibrium = candidateEquilibriumStrategies.Select(x => (new List<double>[2] { principalComponentsWeightsForPlayer0[x.player0Strategy], principalComponentsWeightsForPlayer1[x.player1Strategy] })).ToList();
            int numOfEquilibriaWithinCorrelatedCandidate = candidateEquilibrium.Count();
            if (numOfEquilibriaWithinCorrelatedCandidate > correlatedEquilibrium.Count())
            {
                correlatedEquilibriumStrategies = candidateEquilibriumStrategies;
                correlatedEquilibrium = candidateEquilibrium;
            }
        }

        WriteBasicEquilibriaInfo(correlatedEquilibriumStrategies, correlatedEquilibrium, distanceFromNash);
        await PCA_SeparateReportsForEachEquilibrium(correlatedEquilibrium, "corr", masterReportName);
    }

    public async Task<(List<(int player0Strategy, int player1Strategy)> nashEquilibriaStrategies, List<List<double>[]> nashEquilibriaPrincipalComponents, double[,] distanceFromNash)> UsePCAToEvaluateNashEquilibria(double[,] player0Utilities, double[,] player1Utilities, List<double>[] principalComponentsWeightsForPlayer0, List<double>[] principalComponentsWeightsForPlayer1, ReportCollection reportCollection, string masterReportName)
    {
        TabbedText.WriteLine($"Nash equilibria");
        List<(int player0Strategy, int player1Strategy)> nashEquilibriaStrategies = PureStrategiesFinder.ComputeNashEquilibria(player0Utilities, player1Utilities, false);
        List<List<double>[]> nashEquilibriaPrincipalComponents = nashEquilibriaStrategies.Select(x => new List<double>[2] { principalComponentsWeightsForPlayer0[x.player0Strategy], principalComponentsWeightsForPlayer1[x.player1Strategy] }).ToList();
        var approximateNash = PureStrategiesFinder.GetApproximateNashEquilibrium(player0Utilities, player1Utilities, out double nashDistance);
        if (!nashEquilibriaPrincipalComponents.Any())
        {
            TabbedText.WriteLine($"No Nash equilibrium found. Finding best approximate Nash equilibrium (distance from Nash: {nashDistance}).");
            nashEquilibriaPrincipalComponents.Add(new List<double>[2] { principalComponentsWeightsForPlayer0[approximateNash.player0Strategy], principalComponentsWeightsForPlayer1[approximateNash.player1Strategy] });
            nashEquilibriaStrategies.Add((approximateNash.player0Strategy, approximateNash.player1Strategy));
        }

        nashEquilibriaPrincipalComponents = nashEquilibriaPrincipalComponents.OrderByDescending(eq => GetSumOfPlayerUtilitiesInEquilibrium(eq)).ToList();
        await PCA_SeparateReportsForEachEquilibrium(nashEquilibriaPrincipalComponents, "nash", masterReportName);
        var nashEquilibriumToPick = nashEquilibriaPrincipalComponents.First();
        await developer.SetModelToPrincipalComponentWeights(nashEquilibriumToPick);
        WriteBasicEquilibriaInfo(nashEquilibriaStrategies, nashEquilibriaPrincipalComponents, approximateNash.distanceFromNash);
        return (nashEquilibriaStrategies, nashEquilibriaPrincipalComponents, approximateNash.distanceFromNash);
    }

    private async Task PCA_SeparateReportsForEachEquilibrium(List<List<double>[]> equilibria, string equilibriumType, string masterReportName)
    {
        if (developer.EvolutionSettings.PCA_SeparateReportPerEquilibrium)
        {
            for (int e = 0; e < equilibria.Count; e++)
            {
                List<double>[] eq = equilibria[e];
                await developer.SetModelToPrincipalComponentWeights(eq);
                ReportCollection singleReport = new ReportCollection();
                string scenarioName = developer.GameDefinition.OptionSetName + "-" + developer.GameDefinition.GetNameForScenario();
                string scenarioEquilibriumName = equilibriumType + "-" + e.ToString();
                await GenerateReportsAfterPCA(singleReport, scenarioName, scenarioEquilibriumName, masterReportName);
            }
        }
    }

    private async Task GenerateReportsAfterPCA(ReportCollection reportCollection, string scenarioName, string scenarioEquilibriumName, string masterReportName)
    {
        developer.GameDefinition.ScenarioEquilibriumName = scenarioEquilibriumName;
        string reportSuffix = scenarioEquilibriumName == null ? $"-{scenarioName}" : $"-{scenarioName}-{scenarioEquilibriumName}";
        Func<string> prefaceFn = () => $"{masterReportName}{reportSuffix}";
        await developer.GenerateBestResponseAndMainReports(prefaceFn, reportSuffix, reportCollection, developer.EvolutionSettings.PCA_BestResponseAfterPCA, developer.EvolutionSettings.PCA_ReportsAfterPCA);
        AzureBlob.WriteTextToFileOrAzure("results", Launcher.ReportFolder(), prefaceFn() + ".csv", true, reportCollection.csvReports.First(), developer.EvolutionSettings.SaveToAzureBlob);
        developer.GameDefinition.ScenarioEquilibriumName = null;
        TabbedText.WriteLine($"Results from model used to generate report:"); // can be useful as a check to make sure report corresponds approximately to model
        var resultAverage = await developer.PCA_UtilitiesAndCustomResultAverage(0, true);
    }

    private void WriteBasicEquilibriaInfo(List<(int player0Strategy, int player1Strategy)> equilibriaStrategies, List<List<double>[]> equilibriaPrincipalComponents, double[,] distanceFromNash, bool removeRedundantEquilibria = true)
    {
        HashSet<string> redundantEquilibria = new HashSet<string>();
        for (int i = 0; i < equilibriaPrincipalComponents.Count; i++)
        {
            List<double>[] equilibrium = (List<double>[])equilibriaPrincipalComponents[i];
            (int player0Strategy, int player1Strategy) = equilibriaStrategies[i];
            (double player0Utility, double player1Utility) = GetPredictedPlayerUtilitiesInEquilibrium(equilibrium);
            double approximateNashDistance = distanceFromNash[player0Strategy, player1Strategy];
            string principalComponentWeightsString = string.Join("; ", Enumerable.Range(0, developer.NumNonChancePlayers).Select(x => x.ToString() + ": " + equilibrium[x].ToSignificantFigures(3)));
            string hashToCheckRedundancy0 = $"{equilibrium[0].ToSignificantFigures(10)} {player0Utility.ToSignificantFigures(10)} {player1Utility.ToSignificantFigures(10)}";
            string hashToCheckRedundancy1 = $"{equilibrium[1].ToSignificantFigures(10)} {player0Utility.ToSignificantFigures(10)} {player1Utility.ToSignificantFigures(10)}";
            if (!redundantEquilibria.Contains(hashToCheckRedundancy0) && !redundantEquilibria.Contains(hashToCheckRedundancy1))
            {
                TabbedText.WriteLine($"eq for principal components {principalComponentWeightsString}; utility {player0Utility.ToSignificantFigures(10)}, {player1Utility.ToSignificantFigures(10)} {(approximateNashDistance > 0 ? "Distance from Nash " + approximateNashDistance.ToSignificantFigures(4) : "")}");
                redundantEquilibria.Add(hashToCheckRedundancy0);
                redundantEquilibria.Add(hashToCheckRedundancy1);
            }
        }
    }

    private double GetSumOfPlayerUtilitiesInEquilibrium(List<double>[] equilibrium)
    {
        var utilities = GetPredictedPlayerUtilitiesInEquilibrium(equilibrium);
        return utilities.player0Utility + utilities.player1Utility;
    }

    private (double player0Utility, double player1Utility) GetPredictedPlayerUtilitiesInEquilibrium(List<double>[] equilibrium)
    {
        var datum = new ModelPredictingUtilitiesDatum(equilibrium, null);
        var model = developer.ModelsToPredictUtilitiesFromPrincipalComponents[0];
        double player0Utility = model.GetResult(datum.Convert().X, null);
        model = developer.ModelsToPredictUtilitiesFromPrincipalComponents[1];
        double player1Utility = model.GetResult(datum.Convert().X, null);
        return (player0Utility, player1Utility);
    }

    public void GetEstimatedUtilitiesForStrategyChoices(int numStrategyChoicesPerPlayer, out double[,] player0Utilities, out double[,] player1Utilities, out List<double>[] principalComponentsWeightsForPlayer0, out List<double>[] principalComponentsWeightsForPlayer1)
    {
        player0Utilities = new double[numStrategyChoicesPerPlayer, numStrategyChoicesPerPlayer];
        player1Utilities = new double[numStrategyChoicesPerPlayer, numStrategyChoicesPerPlayer];
        // Determine principal component weights for each strategy choice, making sure they are slightly spaced out. There is no deterministic algorithm for the "sphere packing problem" in n-dimensional space, but the SpacedOutPoints class should work in an approximate way.
        principalComponentsWeightsForPlayer0 = new List<double>[numStrategyChoicesPerPlayer];
        principalComponentsWeightsForPlayer1 = new List<double>[numStrategyChoicesPerPlayer];
        principalComponentsWeightsForPlayer0 = GetRandomPrincipalComponentWeights(0, new SpacedOutPoints(numStrategyChoicesPerPlayer, numStrategyChoicesPerPlayer * 10, Enumerable.Range(0, developer.EvolutionSettings.PCA_NumPrincipalComponents).Select(x => 1.0).ToArray(), new ConsistentRandomSequenceProducer(5_000_001)).CalculatePoints(), 1.0);
        principalComponentsWeightsForPlayer1 = GetRandomPrincipalComponentWeights(1, new SpacedOutPoints(numStrategyChoicesPerPlayer, numStrategyChoicesPerPlayer * 10, Enumerable.Range(0, developer.EvolutionSettings.PCA_NumPrincipalComponents).Select(x => 1.0).ToArray(), new ConsistentRandomSequenceProducer(5_000_002)).CalculatePoints(), 1.0);
        for (int i = 0; i < numStrategyChoicesPerPlayer; i++)
        {
            for (int j = 0; j < numStrategyChoicesPerPlayer; j++)
            {
                List<double>[] principalComponentWeightsForEachPlayer = new List<double>[2]
                {
                    principalComponentsWeightsForPlayer0[i],
                    principalComponentsWeightsForPlayer1[j]
                };
                var datum = new ModelPredictingUtilitiesDatum(principalComponentWeightsForEachPlayer, null);
                var model = developer.ModelsToPredictUtilitiesFromPrincipalComponents[0];
                player0Utilities[i, j] = model.GetResult(datum.Convert().X, null);
                model = developer.ModelsToPredictUtilitiesFromPrincipalComponents[1];
                player1Utilities[i, j] = model.GetResult(datum.Convert().X, null);
            //CompoundRegressionMachinesContainer.SpecifyWeightOnSupplementalMachines(principalComponentWeightsForEachPlayer, InverseNumStandardDeviationsForPrincipalComponentStrategy);
            }
        }
    }

    private async Task AssessModelsToPredictUtilitiesFromPrincipalComponents()
    {
        StatCollector s = new StatCollector();
        const int numToCompare = 100;
        double[][] actualArray = new double[developer.NumNonChancePlayers][], predictedArray = new double[developer.NumNonChancePlayers][];
        for (int p = 0; p < developer.NumNonChancePlayers; p++)
        {
            actualArray[p] = new double[numToCompare];
            predictedArray[p] = new double[numToCompare];
        }

        await Parallelizer.GoAsync(developer.EvolutionSettings.ParallelOptimization, 0, numToCompare, async j =>
        {
            List<double>[] principalComponentWeightsForEachPlayer = GetRandomPrincipalComponentWeightsForEachPlayer(((int)j) * 2011, 1.0);
            string principalComponentWeightsString = string.Join("; ", Enumerable.Range(0, developer.NumNonChancePlayers).Select(x => x.ToString() + ": " + principalComponentWeightsForEachPlayer[x].ToSignificantFigures(3)));
            ModelPredictingUtilitiesDatum datum = new ModelPredictingUtilitiesDatum(principalComponentWeightsForEachPlayer, null);
            await developer.SetModelToPrincipalComponentWeights(principalComponentWeightsForEachPlayer);
            (double[] actual, double[] customStats) = await developer.PCA_UtilitiesAndCustomResultAverage((int)j, reportResults: false);
            double[] predicted = new double[developer.NumNonChancePlayers];
            for (byte p = 0; p < developer.NumNonChancePlayers; p++)
            {
                var model = developer.ModelsToPredictUtilitiesFromPrincipalComponents[p];
                predicted[p] = model.GetResult(datum.Convert().X, null);
                predictedArray[p][j] = predicted[p];
                actualArray[p][j] = actual[p];
            }

            double[] absoluteDifference = actual.Zip(predicted, (first, second) => Math.Abs(first - second)).ToArray();
            double avgAbsoluteDifference = absoluteDifference.Average();
            s.Add(avgAbsoluteDifference);
            TabbedText.WriteLine($"Principal components {j}: {principalComponentWeightsString} ==> Prediction {predicted.ToSignificantFigures(6)} actual {actual.ToSignificantFigures(6)} avg_abs_diff {avgAbsoluteDifference.ToSignificantFigures(3)} ");
        });
        TabbedText.WriteLine($"Overall average absolute difference: " + s.Average().ToSignificantFigures(3));
        for (int p = 0; p < developer.NumNonChancePlayers; p++)
        {
            double coef = actualArray[p].CorrelationCoefficient(predictedArray[p]);
            TabbedText.WriteLine($"Correlation coefficient player {p}: {coef}");
        }
    }
}
