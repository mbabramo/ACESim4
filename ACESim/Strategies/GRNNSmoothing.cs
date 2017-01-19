using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using ACESim.Util;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PriorityQueue;

namespace ACESim
{
    [Serializable]
    public class GRNNSmoothing : OptimizePointsAndSmooth
    {
        internal GRNN GeneralRegressionNeuralNetwork;

        public GRNNSmoothing()
        {
        }

        public GRNNSmoothing(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
            : base(overallStrategy, dimensions, evolutionSettings, decision, name)
        {
        }

        public override IStrategyComponent DeepCopy()
        {
            GRNNSmoothing copy = new GRNNSmoothing()
            {
            };
            SetCopyFields(copy);
            return copy;
        }

        public override void PreSerialize()
        {
            base.PreSerialize();
            if (GeneralRegressionNeuralNetwork != null)
                GeneralRegressionNeuralNetwork.PreSerialize();
        }

        public override void UndoPreSerialize()
        {
            base.UndoPreSerialize();
            if (GeneralRegressionNeuralNetwork != null)
                GeneralRegressionNeuralNetwork.PostDeserialize();
        }

        public override void SetCopyFields(IStrategyComponent copy)
        {
            GRNNSmoothing copyCast = (GRNNSmoothing)copy;
            if (GeneralRegressionNeuralNetwork != null)
                copyCast.GeneralRegressionNeuralNetwork = GeneralRegressionNeuralNetwork.DeepCopy();
            base.SetCopyFields(copyCast);
        }

        public override void SetCopyFieldsBaseOnly(IStrategyComponent copy)
        {
            base.SetCopyFields(copy);
        }

        internal override int CountSubstepsFromSmoothingItself()
        {
            return 1;
        }

        internal void CreateNeuralNetworkApproximation()
        {
            SaveOriginalValues();
            CopyPreSmoothedValuesToPostSmoothed();

            CreateNeuralNetworkApproximation_GRNN();

            foreach (var smoothingPoint in SmoothingSetPointInfos)
                if (smoothingPoint.eligible)
                    smoothingPoint.postSmoothingValue = CalculateOutputForInputs(smoothingPoint.decisionInputs);
            // Since we're not calling the regular smoothing routine, let's do this here.
            SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Develop strategy component substep " + Name);
            // CopyPostSmoothedValuesToPreSmoothed();
        }

        internal override double CalculateOutputForInputsNotZeroDimensions(List<double> inputs)
        {
            if (GeneralRegressionNeuralNetwork != null)
                return GeneralRegressionNeuralNetwork.CalculateOutput(inputs.ToArray(), null, ((GRNNSmoothingOptions)EvolutionSettings.SmoothingOptions).SpeedUpWithApproximateNearestNeighbors);
            return InterpolateOutputForPointUsingNearestNeighborOnly(inputs);
        }

        private void CreateNeuralNetworkApproximation_GRNN()
        {
            List<int> ineligiblePoints = SmoothingSetPointInfos.Where(x => !x.eligible).Select((item, index) => index).ToList();
            List<double[]> basisFunctionCenters = SmoothingSetPointInfos.Where(x => x.eligible).Select(y => y.decisionInputs.ToArray()).ToList();
            List<double> targetOutputs = SmoothingSetPointInfos.Where(x => x.eligible).Select(y => y.preSmoothingValue).ToList();

            List<double> weights = SmoothingSetPointInfos.Where(x => x.eligible).Select(y => (double) y.pointsInRunningSetCount).ToList();
            List<List<int>> nearestNeighbors = null;
            if (((GRNNSmoothingOptions)EvolutionSettings.SmoothingOptions).LimitCalculationToNearestNeighbors)
            {
                nearestNeighbors = new List<List<int>>();
                foreach (var eligiblePoint in SmoothingSetPointInfos.Where(x => x.eligible))
                {
                    List<int> nearestNeighborsOfThisPoint = new List<int>();
                    // at this point, all nearest neighbors listed are eligible points,
                    // but their indices are among all points, including ineligible ones, so we 
                    // must reduce their indices appropriately
                    foreach (var originalNeighborIndex in eligiblePoint.nearestNeighbors)
                        nearestNeighborsOfThisPoint.Add(originalNeighborIndex - ineligiblePoints.Count(x => x < originalNeighborIndex));
                    nearestNeighbors.Add(nearestNeighborsOfThisPoint);
                }
            }
            //GeneralRegressionNeuralNetwork = new GRNNGradient(basisFunctionCenters, targetOutputs, nearestNeighbors, null, 1, 1);
           GeneralRegressionNeuralNetwork = new GRNN(basisFunctionCenters, targetOutputs, weights, nearestNeighbors, null);
        }

        public void RecoverPartialSmoothingSetPointInfosFromGRNN()
        {
            // We recover just enough information so that we can use it for another purpose, such as to do some other type of smoothing.
            bool useResultOfGRNNSmoothingAsPreSmoothingBasis = true; // this makes sense for conversion to RPROP, since we otherwise would have no way of taking into account the weights, and also because RPROP doesn't do as well with noisy data
            List<double[]> basisFunctionCenters = GeneralRegressionNeuralNetwork.ClusterCenters; 
            List<double> targetOutputs = GeneralRegressionNeuralNetwork.ClusterOutputs;
            List<double> weights = GeneralRegressionNeuralNetwork.ClusterWeights;
            int count = basisFunctionCenters.Count;
            SmoothingSetPointInfos = new List<SmoothingSetPointInfo>();
            for (int i = 0; i < count; i++)
            {
                SmoothingSetPointInfos.Add(new SmoothingSetPointInfo() { 
                    decisionInputs = basisFunctionCenters[i].ToList(), 
                    preSmoothingValue = useResultOfGRNNSmoothingAsPreSmoothingBasis ? CalculateOutputForInputs(basisFunctionCenters[i].ToList()) : targetOutputs[i], 
                    pointsInRunningSetCount = useResultOfGRNNSmoothingAsPreSmoothingBasis ? 1 : (int)weights[i],
                    eligible = true
                });
            }
        }

        public override RPROPSmoothing ConvertToRPROPSmoothing()
        {
            RecoverPartialSmoothingSetPointInfosFromGRNN();
            return base.ConvertToRPROPSmoothing();
        }

        internal override void SmoothingSteps()
        {
            ReportSmoothingInfo();
            CreateNeuralNetworkApproximation();

            //if (OverallStrategy.DecisionNumber == 3)
            //{
            //    const int nStep = 20;
            //    foreach (var s in smoothingSetPointInfos.OrderBy(x => x.decisionInputs[0]).Where((x, i) => i % nStep == 0))
            //        Debug.WriteLine(s.decisionInputs[0] + " ======> " + s.preSmoothingValue);
            //}
        }

        internal override void FreeUnnecessaryStorageAfterSmoothingComplete()
        {
            KDTreeForInputs = null;
            SmoothingSetPointInfos = null;
            StorageForSmoothingSetMainSet = null;
            StorageForSmoothingSetValidation = null;
            SmoothingSetPointInfosMainSet = null;
            SmoothingSetPointInfosValidationSet = null; 
            SmoothingSetPointInfosBeforeNarrowing = null;
        }
    }
}
