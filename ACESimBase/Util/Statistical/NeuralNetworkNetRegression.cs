using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

namespace ACESimBase.Util.Statistical
{
    public class NeuralNetworkNetRegression : IRegression
    {

        public int NumSamplesForTesting;
        INeuralNetwork StoredNetwork;
        public DatasetEvaluationResult LastTrainingReport;
        public int Epochs;
        public int NumHiddenLayers;
        public int NeuronsPerHiddenLayer;

        public NeuralNetworkNetRegression(int epochs, int numHiddenLayers, int neuronsPerHiddenLayer)
        {
            Epochs = epochs;
            NumHiddenLayers = numHiddenLayers;
            NeuronsPerHiddenLayer = neuronsPerHiddenLayer;
        }

        public float[] GetResults(float[] x, IRegressionMachine regressionMachine)
        {
            // NOTE: We ignore the regression machine mechanism used by MLNetRegression here, because (I think) StoredNetwork is thread safe.
            float[] result = StoredNetwork.Forward(x);
            return result;
        }

        public async Task Regress((float[] X, float[] Y, float W)[] dataWithWeights)
        {
            // NOTE: NeuralNetwork.Net does not support regression weights. 
            (float[] X, float[] Y)[] data = dataWithWeights.Select(d => (d.X, d.Y)).ToArray();
            LayerFactory[] layerFactories = new LayerFactory[NumHiddenLayers + 1];
            for (int i = 0; i < NumHiddenLayers; i++)
                layerFactories[i] = NetworkLayers.FullyConnected(NeuronsPerHiddenLayer, ActivationType.ReLU);
            layerFactories[NumHiddenLayers] = NetworkLayers.FullyConnected(1, ActivationType.Tanh, CostFunctionType.Quadratic);
            StoredNetwork = NetworkManager.NewSequential(TensorInfo.Linear(data.First().X.Length), layerFactories);
            int numForTesting = 0; // change to add testing
            const float validationProportion = 0.1f; // applies to items not for testing
            int numSamplesForTraining = (int)Math.Round((1.0 - validationProportion) * (data.Length - numForTesting));
            int numSamplesForValidation = data.Length - numForTesting - numSamplesForTraining;
            NumSamplesForTesting = data.Length - numSamplesForTraining - numSamplesForValidation;
            const int batchSize = 1_000;
            ITrainingDataset trainingData = DatasetLoader.Training(data.Take(numSamplesForTraining), batchSize);
            var validationData = DatasetLoader.Validation(data.Skip(numSamplesForTraining).Take(numSamplesForValidation), 0.005f, 10);
            ITestDataset testData = numForTesting == 0 ? null : numSamplesForTraining + numSamplesForValidation == data.Length ? null : DatasetLoader.Test(data.Skip(numSamplesForTraining + numSamplesForValidation));
            void TrackBatchProgress(BatchProgress progress)
            {
            }
            TrainingSessionResult trainingResult = await NetworkManager.TrainNetworkAsync(StoredNetwork,
                trainingData,
                TrainingAlgorithms.RMSProp(),
                Epochs,
                0,
                TrackBatchProgress,
                testDataset: testData);
            LastTrainingReport = trainingResult.TestReports.LastOrDefault();
            //var testDataResults = data.Skip(numSamplesForTraining + numSamplesForValidation).Select(d => (StoredNetwork.Forward(d.X).First(), d.Y.First())).ToList();
            //var examples = data.Skip(numSamplesForTraining + numSamplesForValidation).Take(15).Select(d => $"{string.Join(",", d.X)} => {StoredNetwork.Forward(d.X).Single()} (correct: {d.Y.Single()})");
        }

        public string GetTrainingResultString()
        {
            double avgCost = LastTrainingReport.Cost / (double)NumSamplesForTesting;
            return $"Avgcost: {avgCost}";
        }
    }
}
