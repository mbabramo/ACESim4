using ACESim;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Randomization;
using ACESimBase.Util.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport.Analysis;

/// <summary>Collects model-quality observations and crossovers, and writes their data files. Persisted backing fields remain on the developer for checkpoint compatibility.</summary>
public sealed class ModelSuccessTracker
{
    private readonly StrategiesDeveloperBase developer;
    public ModelSuccessTracker(StrategiesDeveloperBase developer)
    {
        this.developer = developer ?? throw new ArgumentNullException(nameof(developer));
    }

    public void ConsiderModelSuccessTrackingForIteration(int iteration)
    {
        if (developer.EvolutionSettings.ModelSuccessTracking == false || iteration < developer.EvolutionSettings.ModelSuccessTracking_StartingIteration)
            return;
        if (developer.TrackedModels == null)
        {
            //if (EvolutionSettings.BestResponseEveryMIterations != 1)
            //    throw new Exception("Must set best response every iteration for model success tracking"); // could be changed -- just need to figure out total number of iterations
            int totalPlayerActions = developer.InformationSets.Sum(x => x.NumPossibleActions);
            const int utilityAndBestResponseDataPerPlayer = 3; // utility, utility after best response, improvement
            developer.TrackedModelWidth = totalPlayerActions - developer.InformationSets.Count() /* exclude last probability for each info set */ + 2 * utilityAndBestResponseDataPerPlayer + 1 /* avg adjusted best response improvement */;
            // generate perfectly mixed strategies, which can be a starting point */
            List<double> perfectlyMixed = new List<double>();
            foreach (var informationSet in developer.InformationSets)
            {
                int numActions = informationSet.NumPossibleActions;
                double perfectlyMixedProbability = 1.0 / numActions;
                int numExcludingLast = numActions - 1;
                for (int a = 1; a <= numExcludingLast; a++)
                {
                    perfectlyMixed.Add(perfectlyMixedProbability);
                }
            }

            string perfectlyMixedString = String.Join(",", perfectlyMixed.Select(x => x.ToString()).ToArray());
            // continuing with setting up model success tracking
            int numIterations = developer.EvolutionSettings.TotalIterations;
            int adjNumIterations = numIterations - developer.EvolutionSettings.ModelSuccessTracking_StartingIteration;
            //if (adjNumIterations <= 0)
            //    throw new Exception();
            //ModelSuccessTracked = new float[adjNumIterations, ModelSuccessTrackingDataPerIteration];
            developer.TrackedModels = new List<float[]>();
            developer.TrackedModelCount = 0;
        }

        AddModelSuccessTrackingDataForIteration();
        //TabbedText.WriteLine(ModelSuccessEntriesSaved.ToString());
        if (developer.TrackedModelCount == developer.EvolutionSettings.ModelSuccessTrackingSaveAfterNItems)
        {
            for (int i = 0; i < developer.EvolutionSettings.ModelSuccessTrackingAdditionalToGenerate; i++)
                GenerateCrossoverForModelSuccessTracking(i, (int)developer.EvolutionSettings.ModelSuccessTrackingSaveAfterNItems);
            SaveModelSuccessTrackingData();
        }
    }

    private void AddModelSuccessTrackingDataForIteration()
    {
        int colIndex = 0;
        float[] dataForIteration = new float[developer.TrackedModelWidth];
        foreach (var informationSet in developer.InformationSets)
        {
            int numActions = informationSet.NumPossibleActions;
            int numExcludingLast = numActions - 1;
            for (int a = 1; a <= numExcludingLast; a++)
            {
                double probability = Math.Round(informationSet.GetCurrentProbability((byte)a, false), 3); // TODO: Make sure all the probabilities add up to 1. Maybe only round at the extremes (<0.01, >0.99) and always balance it out, so that we round the same number high and low.
                dataForIteration[colIndex++] = (float)probability;
            }
        }

        dataForIteration[colIndex++] = (float)developer.Status.UtilitiesOverall[0];
        dataForIteration[colIndex++] = (float)developer.Status.UtilitiesOverall[1];
        dataForIteration[colIndex++] = (float)developer.Status.BestResponseUtilities[0];
        dataForIteration[colIndex++] = (float)developer.Status.BestResponseUtilities[1];
        dataForIteration[colIndex++] = (float)developer.Status.BestResponseImprovementAdj[0];
        dataForIteration[colIndex++] = (float)developer.Status.BestResponseImprovementAdj[1];
        dataForIteration[colIndex++] = (float)developer.Status.BestResponseImprovementAdjAvg;
        developer.TrackedModels.Add(dataForIteration);
        developer.TrackedModelCount++;
    }

    private void SetCurrentProbabilitiesManually(double[] probabilities)
    {
        int i = 0;
        foreach (var informationSet in developer.InformationSets)
        {
            double[] probabilitiesForInformationSet = probabilities.Skip(i).Take(informationSet.NumPossibleActions).Select(x => Math.Max(x, 0)).ToArray();
            double initialSum = probabilitiesForInformationSet.Sum();
            if (initialSum > 0)
            {
                probabilitiesForInformationSet = probabilitiesForInformationSet.Select(x => x / initialSum).ToArray();
                i += informationSet.NumPossibleActions;
                informationSet.SetCurrentProbabilities(probabilitiesForInformationSet);
            }
        }
    }

    public void GenerateCrossoverForModelSuccessTracking(int randomizationIndex, int numPotentialSources)
    {
        // For each information set, we want to pick two random of our saved items and then determine how to weight them.
        // We need to know the index
        developer.InformationSets.ForEach(x =>
        {
            x.CreateBackup();
            long revisedRandomizationIndex = 9949L * x.InformationSetNodeNumber + randomizationIndex;
            ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(revisedRandomizationIndex);
            int savedStrategyIndexFirst = r.NextInt(numPotentialSources);
            int savedStrategyIndexSecond;
            do
            {
                savedStrategyIndexSecond = r.NextInt(numPotentialSources);
            }
            while (savedStrategyIndexFirst == savedStrategyIndexSecond);
            double weightOnFirst = r.NextDouble();
            int indexOfFirstAction = x.OverallIndexAmongActions - x.InformationSetNodeNumber; // subtract node number to account for not including last probability
            for (byte a = 1; a <= x.NumPossibleActions - 1; a++)
            {
                int actionIndex = indexOfFirstAction + a - 1;
                double probabilityFirst = developer.TrackedModels[savedStrategyIndexFirst][actionIndex];
                double probabilitySecond = developer.TrackedModels[savedStrategyIndexSecond][actionIndex];
                double probabilityWeighted = probabilityFirst * weightOnFirst + probabilitySecond * (1 - weightOnFirst);
                x.SetCurrentAndAverageStrategyValues(a, probabilityWeighted, probabilityWeighted);
            }
        });
        developer.ExecuteAcceleratedBestResponse(false);
        var result = developer.CompleteAcceleratedBestResponse();
        AddModelSuccessTrackingDataForIteration();
        developer.InformationSets.ForEach(x =>
        {
            x.RestoreBackup();
        });
    }

    public void SaveModelSuccessTrackingData()
    {
        TabbedText.WriteLine($"Writing model success data ({developer.TrackedModels.Count} x {developer.TrackedModels[0].Length})");
        string path = FolderFinder.GetFolderToWriteTo("Strategies").FullName;
        string fullPath = Path.Combine(path, "result.h5");
        var matrix = developer.TrackedModels.SelectMany((r, i) => r.Select((val, j) => new { i, j, val })).Aggregate(new float[developer.TrackedModels.Count, developer.TrackedModels[0].Length], (acc, x) =>
        {
            acc[x.i, x.j] = x.val;
            return acc;
        });
        HDF5Writer.WriteArrayToFile(matrix, fullPath);
        string fullPathNpy = Path.Combine(path, "result.npy");
        SaveAsNpy(matrix, fullPathNpy);
    }

    static void LoadH5Data()
    {
        // TODO -- not working
        string path = FolderFinder.GetFolderToWriteTo("Strategies").FullName;
        string fullPath = Path.Combine(path, "input.h5");
        var result = HDF5Writer.ReadHDF5ToList(fullPath);
    }

    static void SaveAsNpy(float[,] matrix, string filePath)
    {
        using (var fs = new FileStream(filePath, FileMode.Create))
        using (var writer = new BinaryWriter(fs))
        {
            // Write the magic string
            writer.Write((byte)0x93);
            writer.Write(Encoding.ASCII.GetBytes("NUMPY"));
            // Write version
            writer.Write((byte)1); // Major version
            writer.Write((byte)0); // Minor version
            // Construct the header
            string header = $"{{'descr': '<f4', 'fortran_order': False, 'shape': ({matrix.GetLength(0)}, {matrix.GetLength(1)}), }}";
            // Pad header to be multiple of 16
            int paddingLength = 16 - ((10 + header.Length) % 16);
            header += new string (' ', paddingLength);
            writer.Write((ushort)header.Length);
            writer.Write(Encoding.ASCII.GetBytes(header));
            // Write matrix data
            for (int i = 0; i < matrix.GetLength(0); i++)
            {
                for (int j = 0; j < matrix.GetLength(1); j++)
                {
                    writer.Write(matrix[i, j]);
                }
            }
        }
    }
}
