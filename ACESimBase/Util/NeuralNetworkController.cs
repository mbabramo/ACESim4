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

        public async Task TrainNeuralNetwork((float[] X, float Y)[] data, CostFunctionType costFunctionType, int epochs, int numHiddenLayers)
        {
            debug; // make it a List of X, then eliminate duplicates and expand shorter ones
            var data2 = data.Select(d => (d.X, new float[] { d.Y })).ToArray();
            await TrainNeuralNetwork(data2, costFunctionType, epochs, numHiddenLayers);
        }

        private async Task TrainNeuralNetwork((float[] X, float[] Y)[] data, CostFunctionType costFunctionType, int epochs, int numHiddenLayers)
        {
            int neuronsCount = (int)(0.67 * data.First().X.Length); // heuristic -- 2/3 of number of inputs
            int minNeuronsCount = 10;
            if (neuronsCount < minNeuronsCount)
                neuronsCount = minNeuronsCount;
            LayerFactory[] layerFactories = new LayerFactory[numHiddenLayers + 1];
            for (int i = 0; i < numHiddenLayers; i++)
                layerFactories[i] = NetworkLayers.FullyConnected(neuronsCount, ActivationType.ReLU);
            layerFactories[numHiddenLayers] = NetworkLayers.FullyConnected(1, ActivationType.Tanh, costFunctionType);
            StoredNetwork = NetworkManager.NewSequential(TensorInfo.Linear(data.First().X.Length), layerFactories);
            const float proportionForTraining = 0.8f;
            int numSamplesForTraining = (int)(data.Length * proportionForTraining);
            int numSamplesForValidation = data.Length - numSamplesForTraining;
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
            //var lastTrainingReport = trainingResult.TestReports.Last();
            //var testDataResults = data.Skip(numSamplesForTraining + numSamplesForValidation).Select(d => (StoredNetwork.Forward(d.X).First(), d.Y.First())).ToList();
            //var examples = data.Skip(numSamplesForTraining + numSamplesForValidation).Take(15).Select(d => $"{string.Join(",", d.X)} => {StoredNetwork.Forward(d.X).Single()} (correct: {d.Y.Single()})");
        }

        public float GetResult(float[] x) => StoredNetwork.Forward(x)[0];
    }
}
