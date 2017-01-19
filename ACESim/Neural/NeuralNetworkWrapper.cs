using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Encog.Util.Arrayutil;
using Encog.Neural.Networks;
using Encog.Neural.Networks.Layers;
using Encog.ML.Factory;
using Encog.ML.Data;
using Encog.ML.Data.Basic;
using Encog.ML.Train;
using Encog.Neural.Networks.Training.Propagation;
using Encog.Neural.Networks.Training.Propagation.Resilient;
using System.Diagnostics;
using Encog.Neural.Networks.Training.Anneal;
using Encog.Neural.Networks.Training;
using Encog.Neural.Pattern;
using Encog.Neural.RBF;
using Encog.Neural.RBF.Training;
using Encog.ML.Factory.Train;
using Encog.ML;
using Encog.Neural.Rbf.Training;
using Encog.Engine.Network.Activation;
using Encog.MathUtil.Randomize;
using Encog.Neural.Networks.Training.Genetic;

namespace ACESim
{
    [Serializable]
    public class NeuralNetworkWrapper
    {
        NeuralNetworkData NeuralNetworkData;
        NeuralNetworkTrainingData ValidationData;
        BasicNetwork Network;
        double OutputMin;
        double OutputMax;
        int FirstHiddenLayer;
        int SecondHiddenLayer;
        public bool IsTrained = false;
        public TrainingInfo TrainingInfo;
        double HiInputs, LoInputs, HiOutputs, LoOutputs;

        public NeuralNetworkWrapper()
        {
            // for serialization
        }

        public NeuralNetworkWrapper(NeuralNetworkData neuralNetworkData, NeuralNetworkTrainingData optionalValidationData, int firstHiddenLayer, int secondHiddenLayer, TrainingInfo trainingInfo, bool reportResults = false, bool cleanUp = true)
        {
            NeuralNetworkData = neuralNetworkData;
            ValidationData = optionalValidationData;
            TrainingInfo = trainingInfo;
            FirstHiddenLayer = firstHiddenLayer;
            SecondHiddenLayer = secondHiddenLayer;

            PrepareNormalization();
            SetUpNetworkAndTrain();

            if (reportResults)
                ReportResults();
            if (cleanUp)
                NeuralNetworkData.CleanUpData();
        }

        private void PrepareNormalization()
        {
            NeuralNetworkData.CalculateMinAndMax();
            TrainingInfo.GetNormalizationRanges(out HiInputs, out LoInputs, out HiOutputs, out LoOutputs);
            NeuralNetworkData.PrepareNormalization(HiInputs, LoInputs, HiOutputs, LoOutputs);
        }

        public NeuralNetworkWrapper DeepCopy()
        {
            return new NeuralNetworkWrapper() { NeuralNetworkData = NeuralNetworkData.DeepCopy(), Network = ((BasicNetwork)Network).Clone<BasicNetwork>(), OutputMin = OutputMin, OutputMax = OutputMax, FirstHiddenLayer = FirstHiddenLayer, SecondHiddenLayer = SecondHiddenLayer, IsTrained = IsTrained, TrainingInfo = null, HiInputs = HiInputs, LoInputs = LoInputs, HiOutputs = HiOutputs, LoOutputs = LoOutputs };
        }

        private void SetUpNetworkAndTrain()
        {
            SetUpNetwork(); 
            Train();
        }

        private void Train()
        {
            IMLTrain train = NeuralNetworkData.GetTrainingMethod(TrainingInfo, Network);
            Train(train, ValidationTestOK);
        }

        public void ContinueTrainingWithNewData(double[][] newArrayFormInputs, double[][] newArrayFormOutputs)
        {
            lastResult = null; // reset validation
            validationTestingDisabled = true; // since we're going to repeatedly train with new data, we shouldn't worry about validation
            if (NeuralNetworkData is NeuralNetworkTrainingData)
            {
                ((NeuralNetworkTrainingData)NeuralNetworkData).ArrayFormInputs = newArrayFormInputs;
                ((NeuralNetworkTrainingData)NeuralNetworkData).ArrayFormOutputs = newArrayFormOutputs; 
                ((NeuralNetworkTrainingData)NeuralNetworkData).CompleteNormalization();
            }
            else
                throw new NotImplementedException();
            Train();
        }

        private void SetUpNetwork()
        {
            //if (TrainingInfo is TrainingInfoForRBF)
            //{
                // Network = new RBFNetwork(NeuralNetworkData.Dimensions, FirstHiddenLayer, 1, Encog.MathUtil.RBF.RBFEnum.Gaussian);
            //}
            //else
            {
                Network = new BasicNetwork();
                ((BasicNetwork)Network).AddLayer(new BasicLayer(null, true, NeuralNetworkData.Dimensions));
                ((BasicNetwork)Network).AddLayer(new BasicLayer(TrainingInfo.GetActivationFunction(), true, FirstHiddenLayer));
                if (SecondHiddenLayer > 0)
                    ((BasicNetwork)Network).AddLayer(new BasicLayer(TrainingInfo.GetActivationFunction(), true, SecondHiddenLayer));
                ((BasicNetwork)Network).AddLayer(new BasicLayer(TrainingInfo.GetActivationFunction(), false, 1));
                ((BasicNetwork)Network).Structure.FinalizeStructure();
                (new ConsistentRandomizer(-1, 1)).Randomize(Network);
            }
            //Network.Reset();
        }

        private void Train(IMLTrain train, Func<bool> validationOK)
        {
            //Network.Reset();
            TrainingInfo.Train(train, validationOK);
            train.FinishTraining();
            IsTrained = true;
            Network = TrainingInfo.GetNetworkAfterTraining();
        }

        object calcLock = new object();
        public double CalculateResult(List<double> unnormalizedInputs)
        {
            if (NeuralNetworkData.NormalizationFieldOutput == null)
                NeuralNetworkData.PrepareNormalization(HiInputs, LoInputs, HiOutputs, LoOutputs);

            double[] normalizedInputs = unnormalizedInputs.Select((y, j) => NeuralNetworkData.NormalizationFields[j].Normalize(y)).ToArray();
            BasicMLData inputData = new BasicMLData(normalizedInputs); 
            IMLData output;
            lock (calcLock)
            {
                output = Network.Compute(inputData); // this is not threadsafe, unfortunately, so we must lock; alternative would be to clone the network (e.g., by using ThreadSafe<Network>)
            }
            double denorm = NeuralNetworkData.NormalizationFieldOutput.DeNormalize(output[0]);
            return denorm;
        }

        public double CalculateValidationError()
        {
            if (ValidationData == null)
                return 0;
            else
            {
                double totalError = 0;
                for (int i = 0; i < ValidationData.ArrayFormInputs.Count(); i++)
                    totalError += Math.Abs(CalculateResult(ValidationData.ArrayFormInputs[i].ToList()) - ValidationData.ArrayFormOutputs[i][0]);
                return totalError / (double)ValidationData.ArrayFormInputs.Count();
            }
        }

        bool validationTestingDisabled = false;
        double? lastResult;
        public bool ValidationTestOK()
        {
            if (validationTestingDisabled)
                return true;
            double validationError = CalculateValidationError();
            bool OK = lastResult == null || lastResult >= validationError;
            lastResult = validationError;
            return OK;
        }

        private void ReportResults()
        {
            NeuralNetworkData.ReportResults(CalculateResult);
        }
    }


}
