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
    public abstract class NeuralNetworkData
    {
        public double[] OriginalInputsMin;
        public double[] OriginalInputsMax;
        public double UltimateOutputMin;
        public double UltimateOutputMax;
        public int Dimensions;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public NormalizedField[] NormalizationFields;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public NormalizedField NormalizationFieldOutput;

        public NeuralNetworkData(int dimensions)
        {
            Dimensions = dimensions;
        }

        public abstract NeuralNetworkData DeepCopy();

        internal abstract void CalculateMinAndMax();

        public virtual void PrepareNormalization(double hiInputsToActivationFunction, double loInputsToActivationFunction, double hiOutputsFromActivationFunction, double loOutputsFromActivationFunction)
        {
            NormalizationFields = new NormalizedField[Dimensions];
            for (int d = 0; d < Dimensions; d++)
            {
                NormalizationFields[d] = new NormalizedField(NormalizationAction.Normalize, d.ToString(), OriginalInputsMax[d], OriginalInputsMin[d], hiInputsToActivationFunction, loInputsToActivationFunction);
            }
            NormalizationFieldOutput = new NormalizedField(NormalizationAction.Normalize, "output", UltimateOutputMax, UltimateOutputMin, hiOutputsFromActivationFunction, loOutputsFromActivationFunction);
        }

        public abstract IMLTrain GetTrainingMethod(TrainingInfo trainingInfo, BasicNetwork network);

        public virtual void CleanUpData()
        {
        }

        public virtual void ReportResults(Func<List<double>, double> CalculateResults)
        {
        }
    }

    [Serializable]
    public class NeuralNetworkTrainingData : NeuralNetworkData
    {
        public double[][] ArrayFormInputs;
        public double[][] ArrayFormOutputs;
        public double[][] ArrayFormInputsNormalized;
        public double[][] ArrayFormOutputsNormalized;

        public NeuralNetworkTrainingData(int dimensions, double[][] arrayFormInputs, double[][] arrayFormOutputs)
            : base(dimensions)
        {
            ArrayFormInputs = arrayFormInputs;
            ArrayFormOutputs = arrayFormOutputs;
        }

        public override NeuralNetworkData DeepCopy()
        {
            var data = new NeuralNetworkTrainingData(Dimensions, ArrayFormInputs == null ? null : ArrayFormInputs.Select(x => x.ToArray()).ToArray(), ArrayFormOutputs == null ? null : ArrayFormOutputs.Select(x => x.ToArray()).ToArray());
            data.ArrayFormInputsNormalized = ArrayFormInputsNormalized == null ? null : ArrayFormInputsNormalized.Select(x => x.ToArray()).ToArray();
            data.ArrayFormOutputsNormalized = ArrayFormOutputsNormalized == null ? null : ArrayFormOutputsNormalized.Select(x => x.ToArray()).ToArray();
            data.OriginalInputsMin = OriginalInputsMin == null ? null : OriginalInputsMin.ToArray();
            data.OriginalInputsMax = OriginalInputsMax == null ? null : OriginalInputsMax.ToArray();
            return data;
        }

        internal override void CalculateMinAndMax()
        {
            OriginalInputsMax = new double[Dimensions];
            OriginalInputsMin = new double[Dimensions];
            for (int d = 0; d < Dimensions; d++)
            {
                OriginalInputsMin[d] = ArrayFormInputs.Min(x => x[d]);
                OriginalInputsMax[d] = ArrayFormInputs.Max(x => x[d]);
            }
            UltimateOutputMin = ArrayFormOutputs.Min(x => x[0]);
            UltimateOutputMax = ArrayFormOutputs.Max(x => x[0]);
            if (OriginalInputsMin.Any(x => double.IsNaN(x)) || OriginalInputsMax.Any(x => double.IsNaN(x)) || double.IsNaN(UltimateOutputMin) || double.IsNaN(UltimateOutputMax))
                throw new Exception("Problem here.");
        }

        public override void PrepareNormalization(double hiInputsToActivationFunction, double loInputsToActivationFunction, double hiOutputsFromActivationFunction, double loOutputsFromActivationFunction)
        {
            base.PrepareNormalization(hiInputsToActivationFunction, loInputsToActivationFunction, hiOutputsFromActivationFunction, loOutputsFromActivationFunction);
            CompleteNormalization();
        }

        public void CompleteNormalization()
        {
            if (ArrayFormInputs != null && ArrayFormOutputs != null)
            {
                ArrayFormInputsNormalized = ArrayFormInputs.Select((x, i) => x.Select((y, j) => NormalizationFields[j].Normalize(y)).ToArray()).ToArray();
                ArrayFormOutputsNormalized = ArrayFormOutputs.Select((x, i) => x.Select((y, j) => NormalizationFieldOutput.Normalize(y)).ToArray()).ToArray();
            }
        }

        public override IMLTrain GetTrainingMethod(TrainingInfo trainingInfo, BasicNetwork network)
        {
            BasicMLDataSet trainingSet = new BasicMLDataSet(ArrayFormInputsNormalized, ArrayFormOutputsNormalized);
            return trainingInfo.GetTrainingMethodForTrainingSet(network, trainingSet);
        }

        public override void CleanUpData()
        {
            ArrayFormInputs = null;
            ArrayFormOutputs = null;
            ArrayFormInputsNormalized = null;
            ArrayFormOutputsNormalized = null;
        }

        public override void ReportResults(Func<List<double>, double> calculateResultFunc)
        {
            var combined = ArrayFormInputs.Zip(ArrayFormOutputs, (x, y) => new { Input = x, Ideal = y });
            StatCollector coll = new StatCollector();
            foreach (var c in combined)
            {
                List<double> unnormalizedInputs = c.Input.ToList();
                double normalizedOutput = calculateResultFunc(unnormalizedInputs);
                //Debug.WriteLine(String.Join(",", unnormalizedInputs.ToArray()) + " ==> " + normalizedOutput + " (ideal: " + c.Ideal.First() + ")");
                coll.Add(Math.Abs(normalizedOutput - c.Ideal.First()));
            }
            Debug.WriteLine("Average error: " + coll.Average() + " Standard deviation: " + coll.StandardDeviation());
        }

    }


    [Serializable]
    public class NeuralNetworkDynamicDataProcessor : NeuralNetworkData
    {
        public ICalculateScore ScoreCalculator;
        public BasicNetwork Network;

        public NeuralNetworkDynamicDataProcessor(int dimensions, double[] inputsMin, double[] inputsMax, double outputMin, double outputMax, ICalculateScore scoreCalculator)
            : base(dimensions)
        {
            OriginalInputsMin = inputsMin;
            OriginalInputsMax = inputsMax;
            UltimateOutputMin = outputMin;
            UltimateOutputMax = outputMax;
            ScoreCalculator = scoreCalculator;
        }

        public override NeuralNetworkData DeepCopy()
        {
            return new NeuralNetworkDynamicDataProcessor(Dimensions, OriginalInputsMin.ToArray(), OriginalInputsMax.ToArray(), UltimateOutputMin, UltimateOutputMax, null /* not necessarily copiable */);
        }

        internal override void CalculateMinAndMax()
        {
        }

        public override IMLTrain GetTrainingMethod(TrainingInfo trainingInfo, BasicNetwork network)
        {
            Network = network;
            return trainingInfo.GetTrainingMethodForScoreCalculator(network, ScoreCalculator);
        }

        public override void ReportResults(Func<List<double>, double> calculateResultFunc)
        {
            const int numRepetitions = 100;
            double total = 0;
            for (int i = 0; i < numRepetitions; i++)
                total += ScoreCalculator.CalculateScore(Network);
            double avg = total / (double)numRepetitions;
            Debug.WriteLine("Average score: " + avg);
        }

        public double ComputeNetworkBeingDeveloped(List<double> unnormalizedInputs)
        {
            double[] normalizedInputs = unnormalizedInputs.Select((x, j) => NormalizationFields[j].Normalize(x)).ToArray();
            BasicMLData input = new BasicMLData(normalizedInputs);
            IMLData output = Network.Compute(input);
            double outputDenormalized = NormalizationFieldOutput.DeNormalize(output[0]);
            return outputDenormalized;
        }

    }
}
