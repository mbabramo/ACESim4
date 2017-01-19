using ACESim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

// adapted from http://dynamicnotions.blogspot.com/2008/09/training-neural-networks-using-back.html

namespace SimpleBackprop
{
    public static class SimpleBackpropTest
    {
        public static void DoTest()
        {
            Network theNetwork = new Network(2, 10);
            theNetwork.TrainWithDynamicData(10000000, GetData);
            theNetwork.MeasureErrorWithDynamicData(10000, GetData);
        }

        public static Data GetData()
        {
            double firstInput = RandomGenerator.NextDouble();
            double secondInput = RandomGenerator.NextDouble();
            double output = ObfuscationGame.ObfuscationCorrectAnswer.Calculate(firstInput, secondInput);
            return new Data() { Inputs = new double[] { firstInput, secondInput }, Output = output };
        }


        public static void DoTest2()
        {
            Network theNetwork = new Network(3, 5);
            theNetwork.DynamicTraining(GetData2NoNoise, 0.5, 10000, 10000, 10000, GetData2NoNoise);
        }


        public static Data GetData2()

        {
            double firstInput = RandomGenerator.NextDouble();
            double secondInput = RandomGenerator.NextDouble();
            double randomGuess = RandomGenerator.NextDouble();
            double correctAnswer = ObfuscationGame.ObfuscationCorrectAnswer.Calculate(firstInput, secondInput);
            double output = (randomGuess - correctAnswer) * (randomGuess - correctAnswer);
            // return new Data() { Inputs = new double[] { 10.0 * firstInput, 40.0 * secondInput - 234.0, 17.0 * randomGuess + 12.0 }, Output = output + RandomGenerator.NextDouble() - 0.5 };
            return new Data() { Inputs = new double[] { firstInput, secondInput, randomGuess }, Output = output + RandomGenerator.NextDouble() - 0.5 };
        }

        public static Data GetData2NoNoise()
        {
            double firstInput = RandomGenerator.NextDouble();
            double secondInput = RandomGenerator.NextDouble();
            double randomGuess = RandomGenerator.NextDouble();
            double correctAnswer = ObfuscationGame.ObfuscationCorrectAnswer.Calculate(firstInput, secondInput);
            double output = (randomGuess - correctAnswer) * (randomGuess - correctAnswer);
            return new Data() { Inputs = new double[] { firstInput, secondInput, randomGuess }, Output = output };
        }

    }

    public class Network
    {

        private int _hiddenDims = 2;        // Number of hidden neurons.

        private int _inputDims = 2;        // Number of input neurons.

        private int _iteration;            // Current training iteration.

        private int _restartAfter = 2000;   // Restart training if iterations exceed this.

        private Layer _hidden;              // Collection of hidden neurons.

        private Layer _inputs;              // Collection of input neurons.

        private List<Data> _data;    // Collection of training patterns.

        private Neuron _output;            // Output neuron.

        private Random _rnd = new Random(); // Global random number generator.

        Normalizer Normalization; // Normalizer

        public Network(int inputNeurons, int hiddenNeurons)
        {
            _hiddenDims = hiddenNeurons;
            _inputDims = inputNeurons;
            Initialise();
        }

        public Network()
        {
            Initialise();
        }

        public void NormalizationSetup(Func<Data> dataLoader)
        {
            Normalization = new Normalizer();
            const int numToDo = 500;
            double[][] inputs = new double[numToDo][], outputs = new double[numToDo][];
            for (int i = 0; i < numToDo; i++)
            {
                Data datum = dataLoader();
                inputs[i] = datum.Inputs;
                outputs[i] = new double[] { datum.Output };
            }

            Normalization.SetInitialMapping(_inputDims, inputs, outputs );
        }

        public void DynamicTraining(Func<Data> dataLoader, double initialLearnRate, int numRepetitions, int numDataPointsPerRepetition, int numDataPointsPerErrorMeasure, Func<Data> separateFuncForError = null)
        {
            NormalizationSetup(dataLoader);

            double learnRate = initialLearnRate;
            for (int r = 0; r < numRepetitions; r++)
            {
                Debug.WriteLine("Learn rate: " + learnRate);

                double hiLearnRate = learnRate * 1.1;
                double loLearnRate = learnRate * 0.9;

                SetLearnRate(loLearnRate);
                TrainWithDynamicData(numDataPointsPerRepetition, dataLoader);
                double loLearnRateErr = MeasureErrorWithDynamicData(numDataPointsPerErrorMeasure, separateFuncForError ?? dataLoader);

                SetLearnRate(hiLearnRate);
                TrainWithDynamicData(numDataPointsPerRepetition, dataLoader);
                double hiLearnRateErr = MeasureErrorWithDynamicData(numDataPointsPerErrorMeasure, dataLoader);

                learnRate = (hiLearnRateErr < loLearnRateErr) ? hiLearnRate : loLearnRate;
                learnRate = ConstrainToRange.Constrain(learnRate, 0, 1.0);

                //theNetwork.SetLearnRate(learnRate);
            }
        }

        public void TrainWithDynamicData(long repetitions, Func<Data> dataLoader)
        {
            for (long r = 0; r < repetitions; r++)
            {
                Data datum = Normalization.Normalize(dataLoader());
                double delta = datum.Output - Calculate(datum);
                AdjustWeights(delta);
            }
        }

        public double MeasureErrorWithDynamicData(int repetitions, Func<Data> dataLoader)
        {
            StatCollector s = new StatCollector();

            for (int r = 0; r < repetitions; r++)
            {
                Data datum = Normalization.Normalize(dataLoader());
                double delta =  Normalization.DenormalizeOutput(datum.Output) - Normalization.DenormalizeOutput(Calculate(datum));
                s.Add(Math.Abs(delta));
            }

            Debug.WriteLine("Error: " + s.Average());
            return s.Average();
        }

        public void SetLearnRate(double newLearnRate)
        {
            _hidden.SetLearnRate(newLearnRate);
            _inputs.SetLearnRate(newLearnRate);
        }

        public void SetData(List<Data> theData)
        {
            _data = theData;
        }

        public void Train(int repetitions)
        {
            for (int r = 0; r < repetitions; r++)
            {
                foreach (Data datum in _data)
                {
                    double delta = datum.Output - Calculate(datum);
                    AdjustWeights(delta);
                }
            };
        }


        private double Calculate(Data pattern)
        {
            for (int i = 0; i < pattern.Inputs.Length; i++)
            {
                _inputs[i].Output = pattern.Inputs[i];
            }

            foreach (Neuron neuron in _hidden)
            {

                neuron.Activate();

            }

            _output.Activate();

            return _output.Output;

        }



        private void AdjustWeights(double delta)
        {

            _output.AdjustWeights(delta);

            foreach (Neuron neuron in _hidden)
            {

                neuron.AdjustWeights(_output.ErrorFeedback(neuron));

            }

        }



        private void Initialise()
        {

            _inputs = new Layer(_inputDims);

            _hidden = new Layer(_hiddenDims, _inputs, _rnd);

            _output = new Neuron(_hidden, _rnd);

            _iteration = 0;

            Console.WriteLine("Network Initialised");

        }

    }



    public class Layer : List<Neuron>
    {

        public Layer(int size)
        {

            for (int i = 0; i < size; i++)

                base.Add(new Neuron());

        }



        public Layer(int size, Layer layer, Random rnd)
        {

            for (int i = 0; i < size; i++)

                base.Add(new Neuron(layer, rnd));

        }

        public void SetLearnRate(double newLearnRate)
        {
            this.ForEach(x => x.SetLearnRate(newLearnRate));
        }

    }



    public class Neuron
    {

        private double _bias;                       // Bias value.

        private double _error;                      // Sum of error.

        private double _input;                      // Sum of inputs.

        private double _lambda = 6;                // Steepness of sigmoid curve.

        private double _learnRate = 0.5;          // Learning rate.

        private double _output = double.MinValue;   // Preset value of neuron.

        private List<Weight> _weights;              // Collection of weights to inputs.



        public Neuron() { }


        public Neuron(Layer inputs, Random rnd)
        {

            _weights = new List<Weight>();

            foreach (Neuron input in inputs)
            {

                Weight w = new Weight();

                w.Input = input;

                w.Value = rnd.NextDouble() * 2 - 1;

                _weights.Add(w);

            }

        }

        public void SetLearnRate(double newRate)
        {
            _learnRate = newRate;
        }

        public void Activate()
        {

            _input = 0;

            foreach (Weight w in _weights)
            {

                _input += w.Value * w.Input.Output;

            }

        }



        public double ErrorFeedback(Neuron input)
        {

            Weight w = _weights.Find(delegate(Weight t) { return t.Input == input; });

            return _error * Derivative * w.Value;

        }



        public void AdjustWeights(double value)
        {

            _error = value;

            for (int i = 0; i < _weights.Count; i++)
            {

                _weights[i].Value += _error * Derivative * _learnRate * _weights[i].Input.Output;

            }

            _bias += _error * Derivative * _learnRate;

        }



        private double Derivative
        {

            get
            {

                double activation = Output;

                return activation * (1 - activation);

            }

        }



        public double Output
        {

            get
            {

                if (_output != double.MinValue)
                {

                    return _output;

                }

                return 1 / (1 + Math.Exp(-_lambda * (_input + _bias)));

            }

            set
            {

                _output = value;

            }

        }

    }



    public class Data
    {
        public double[] Inputs;
        public double Output;

    }

    public class Normalizer
    {
        public int Dimensions;
        public double[] OriginalInputsMin;
        public double[] OriginalInputsMax;
        public double UltimateOutputMin;
        public double UltimateOutputMax;
        public const double HighestSampleInputMapsTo = 0.9, LowestSampleInputMapsTo = 0.1, HighestSampleOutputMapsTo = 0.9, LowestSampleOutputMapsTo = 0.1;

        public void SetInitialMapping(int dimensions, double[][] sampleInputs, double[][] sampleOutputs)
        {
            Dimensions = dimensions;
            OriginalInputsMax = new double[Dimensions];
            OriginalInputsMin = new double[Dimensions];
            for (int d = 0; d < Dimensions; d++)
            {
                OriginalInputsMin[d] = sampleInputs.Min(x => x[d]);
                OriginalInputsMax[d] = sampleInputs.Max(x => x[d]);
            }
            UltimateOutputMin = sampleOutputs.Min(x => x[0]);
            UltimateOutputMax = sampleOutputs.Max(x => x[0]);
            if (OriginalInputsMin.Any(x => double.IsNaN(x)) || OriginalInputsMax.Any(x => double.IsNaN(x)) || double.IsNaN(UltimateOutputMin) || double.IsNaN(UltimateOutputMax))
                throw new Exception("Problem here.");
        }

        public double Map(double originalValue, double highestSource, double highestTarget, double lowestSource, double lowestTarget)
        {
            return (originalValue - lowestSource) * (highestTarget - lowestTarget) / (highestSource - lowestSource) + lowestTarget;
        }

        public Data Normalize(double[] inputs, double output)
        {
            double[] mappedInputs = inputs.Select((x, i) => ConstrainToRange.Constrain(
                    Map(x, OriginalInputsMax[i], HighestSampleInputMapsTo, OriginalInputsMin[i], LowestSampleInputMapsTo),
                    0.0, 1.0
                )).ToArray();
            double mappedOutput = ConstrainToRange.Constrain(Map(output, UltimateOutputMax, HighestSampleOutputMapsTo, UltimateOutputMin, LowestSampleOutputMapsTo), 0.0, 1.0);
            return new Data() { Inputs = mappedInputs, Output = mappedOutput };
        }

        public Data Normalize(Data unnormalized)
        {
            return Normalize(unnormalized.Inputs, unnormalized.Output);
        }

        public double DenormalizeOutput(double normalizedOutput)
        {
            return Map(normalizedOutput, HighestSampleOutputMapsTo, UltimateOutputMax, LowestSampleOutputMapsTo, UltimateOutputMin);
        }
    }



    public class Weight
    {

        public Neuron Input;

        public double Value;

    }
}
