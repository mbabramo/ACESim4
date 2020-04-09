using ACESim;
using NeuralNetworkNET.APIs;
using NeuralNetworkNET.APIs.Delegates;
using NeuralNetworkNET.APIs.Enums;
using NeuralNetworkNET.APIs.Interfaces;
using NeuralNetworkNET.APIs.Interfaces.Data;
using NeuralNetworkNET.APIs.Results;
using NeuralNetworkNET.APIs.Structs;
using NeuralNetworkNET.Networks.Cost;
using NeuralNetworkNET.SupervisedLearning.Progress;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util
{
    public class NeuralNetworkController
    {
        INeuralNetwork StoredNetwork;
        bool Normalize = true;
        (float MinX, float MaxX)[] Ranges;
        float?[] IndependentVariableConstant;
        int NumConstantIndependentVariables;
        float MinY, MaxY;

        public float[] NormalizeIndependentVars(float[] x)
        {
            float[] result = new float[x.Length - NumConstantIndependentVariables];
            int constantsSoFar = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (IndependentVariableConstant[i] != null)
                    constantsSoFar++;
                else
                {
                    float item = x[i];
                    // TODO: Allow for extrapolation by allowing to exceed min-max range
                    if (item < Ranges[i].MinX)
                        item = Ranges[i].MinX;
                    if (item > Ranges[i].MaxX)
                        item = Ranges[i].MaxX;
                    result[i - constantsSoFar] = (item - Ranges[i].MinX) / (Ranges[i].MaxX - Ranges[i].MinX);
                }
            }
            return result;
        }
        public float NormalizeDependentVar(float y) => (y - MinY) / (MaxY - MinY);
        public float[] DenormalizeIndependentVars(float[] x)
        {
            float[] result = new float[x.Length + NumConstantIndependentVariables];
            int constantsSoFar = 0;
            for (int i = 0; i < result.Length; i++)
            {
                if (IndependentVariableConstant[i] is float constantValue)
                {
                    constantsSoFar++;
                    result[i] = constantValue;
                }
                else
                {
                    float item = x[i - constantsSoFar];
                    result[i] = Ranges[i].MinX + item * (Ranges[i].MaxX - Ranges[i].MinX);
                }
            }
            return result;
        }
        public float DenormalizeDependentVar(float y) => MinY + y * (MaxY - MinY);

        public async Task TrainNeuralNetwork((float[] X, float Y)[] data, CostFunctionType costFunctionType, int epochs, int numHiddenLayers)
        {
            (float[] X, float[] Y)[] data2 = data.Select(d => (d.X, new float[] { d.Y })).ToArray();
            if (Normalize)
            {
                int numItems = data2.Count();
                int lengthX = data2.First().Item1.Length;
                Ranges = new (float MinX, float MaxX)[lengthX];
                IndependentVariableConstant = new float?[lengthX];
                for (int xIndex = 0; xIndex < lengthX; xIndex++)
                {
                    Ranges[xIndex].MinX = data2.Min(d => d.X[xIndex]);
                    Ranges[xIndex].MaxX = data2.Max(d => d.X[xIndex]);
                    if (Ranges[xIndex].MinX == Ranges[xIndex].MaxX)
                        IndependentVariableConstant[xIndex] = Ranges[xIndex].MinX;
                }
                NumConstantIndependentVariables = IndependentVariableConstant.Where(x => x != null).Count();
                MinY = data2.Min(d => d.Y[0]);
                MaxY = data2.Max(d => d.Y[0]);
                for (int i = 0; i < numItems; i++)
                {
                    data2[i].X = NormalizeIndependentVars(data2[i].X);
                    float yUnnormalized = data2[i].Y[0];
                    data2[i].Y[0] = NormalizeDependentVar(yUnnormalized);
                }
                bool createString = false;
                if (createString)
                {
                    StringBuilder s = new StringBuilder();
                    for (int i = 0; i < numItems; i++)
                    {
                        s.AppendLine(data2[i].Y[0] + "," + String.Join(",", data2[i].X));
                    }
                }
            }
            await TrainNeuralNetwork(data2, costFunctionType, epochs, numHiddenLayers);
        }

        private async Task TrainNeuralNetwork((float[] X, float[] Y)[] data, CostFunctionType costFunctionType, int epochs, int numHiddenLayers)
        {
            int neuronsCount = (int)(0.67 * data.First().X.Length); // heuristic -- 2/3 of number of inputs
            int minNeuronsCount = 30;
            if (neuronsCount < minNeuronsCount)
                neuronsCount = minNeuronsCount;
            LayerFactory[] layerFactories = new LayerFactory[numHiddenLayers + 1];
            for (int i = 0; i < numHiddenLayers; i++)
                layerFactories[i] = NetworkLayers.FullyConnected(neuronsCount, ActivationType.ReLU);
            layerFactories[numHiddenLayers] = NetworkLayers.FullyConnected(1, ActivationType.Tanh, costFunctionType);
            StoredNetwork = NetworkManager.NewSequential(TensorInfo.Linear(data.First().X.Length), layerFactories);
            int numForTesting = 10_000;
            if (data.Length < numForTesting + 100) // DEBUG
                throw new Exception();
            const float validationProportion = 0.1f; // applies to items not for testing
            int numSamplesForTraining = (int)((1.0 - validationProportion) * (data.Length - numForTesting));
            int numSamplesForValidation = data.Length - numForTesting - numSamplesForTraining;
            int numSamplesForTesting = data.Length - numSamplesForTraining - numSamplesForValidation;
            const int batchSize = 1_000;
            ITrainingDataset trainingData = DatasetLoader.Training(data.Take(numSamplesForTraining), batchSize);
            var validationData = DatasetLoader.Validation(data.Skip(numSamplesForTraining).Take(numSamplesForValidation), 0.005f, 10);
            ITestDataset testData = numSamplesForTraining + numSamplesForValidation == data.Length ? null : DatasetLoader.Test(data.Skip(numSamplesForTraining + numSamplesForValidation));
            void TrackBatchProgress(BatchProgress progress)
            {
            }
            TrainingSessionResult trainingResult = await NetworkManager.TrainNetworkAsync(StoredNetwork,
                trainingData,
                TrainingAlgorithms.RMSProp(),
                epochs,
                0,
                TrackBatchProgress,
                testDataset: testData);
            var lastTrainingReport = trainingResult.TestReports.Last();
            TabbedText.Write($"Cost {lastTrainingReport.Cost}"); // / (double) numSamplesForTesting} ");
            var testDataResults = data.Skip(numSamplesForTraining + numSamplesForValidation).Select(d => (StoredNetwork.Forward(d.X).First(), d.Y.First())).ToList();
            var examples = data.Skip(numSamplesForTraining + numSamplesForValidation).Take(15).Select(d => $"{string.Join(",", d.X)} => {StoredNetwork.Forward(d.X).Single()} (correct: {d.Y.Single()})");
        }

        public float GetResult(float[] x)
        {
            if (Normalize)
                x = NormalizeIndependentVars(x);
            float result = StoredNetwork.Forward(x)[0];
            if (Normalize)
                result = DenormalizeDependentVar(result);
            return result;
        }
    }
}
