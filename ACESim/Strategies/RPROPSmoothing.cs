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
    public class RPROPSmoothing : OptimizePointsAndSmooth
    {
        internal NeuralNetworkWrapper NeuralNetworkController;

        public RPROPSmoothing()
        {
        }

         public RPROPSmoothing(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
             : base(overallStrategy, dimensions, evolutionSettings, decision, name)
        {
        }

         public override IStrategyComponent DeepCopy()
         {
             RPROPSmoothing copy = new RPROPSmoothing()
             {
             };
             SetCopyFields(copy);
             return copy;
         }

         public override void SetCopyFields(IStrategyComponent copy)
         {
             RPROPSmoothing copyCast = (RPROPSmoothing)copy;
             copyCast.NeuralNetworkController = NeuralNetworkController.DeepCopy();
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

        public void CreateNeuralNetworkApproximation(bool updateProgressStep = true)
        {
            SaveOriginalValues();
            CopyPreSmoothedValuesToPostSmoothed();

            CreateNeuralNetworkApproximation_BackPropagation();

            foreach (var smoothingPoint in SmoothingSetPointInfos)
                if (smoothingPoint.eligible)
                    smoothingPoint.postSmoothingValue = CalculateOutputForInputs(smoothingPoint.decisionInputs);
            // Since we're not calling the regular smoothing routine, let's do this here.
            if (updateProgressStep)
                SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Develop strategy component substep " + Name);
            // CopyPostSmoothedValuesToPreSmoothed();
        }


        private void CreateNeuralNetworkApproximation_BackPropagation()
        {
            var arrayFormInputs = SmoothingSetPointInfos.Where(x => x.eligible).Select(y => y.decisionInputs.ToArray()).ToArray();
            var arrayFormOutputs = SmoothingSetPointInfos.Where(x => x.eligible).Select(y => new double[] { y.preSmoothingValue }).ToArray();
            NeuralNetworkTrainingData trainingData = new NeuralNetworkTrainingData(Dimensions, arrayFormInputs, arrayFormOutputs);

            NeuralNetworkTrainingData validationData = null;
            if (EvolutionSettings.SmoothingPointsValidationSet != null &&  EvolutionSettings.SmoothingPointsValidationSet.CreateValidationSet)
            {
                var arrayFormInputsValidationSet = SmoothingSetPointInfosValidationSet.Where(x => x.eligible).Select(y => y.decisionInputs.ToArray()).ToArray();
                var arrayFormOutputsValidationSet = SmoothingSetPointInfosValidationSet.Where(x => x.eligible).Select(y => new double[] { y.preSmoothingValue }).ToArray();
                validationData = new NeuralNetworkTrainingData(Dimensions, arrayFormInputsValidationSet, arrayFormOutputsValidationSet);
            }

            TrainingInfo trainingInfo = new TrainingInfo() { Epochs = ((RPROPSmoothingOptions)(EvolutionSettings.SmoothingOptions)).Epochs, Technique = TrainingTechnique.ResilientPropagation, ValidateEveryNEpochs = ((RPROPSmoothingOptions)(EvolutionSettings.SmoothingOptions)).TestValidationSetEveryNEpochs };
            NeuralNetworkController = new NeuralNetworkWrapper(trainingData, validationData, ((RPROPSmoothingOptions)(EvolutionSettings.SmoothingOptions)).FirstHiddenLayerNeurons, ((RPROPSmoothingOptions)(EvolutionSettings.SmoothingOptions)).SecondHiddenLayerNeurons, trainingInfo);
        }

        internal override double CalculateOutputForInputsNotZeroDimensions(List<double> inputs)
        {
            if (NeuralNetworkController != null && NeuralNetworkController.IsTrained)
                return NeuralNetworkController.CalculateResult(inputs);
            return InterpolateOutputForPointUsingNearestNeighborOnly(inputs);
        }

        internal override void SmoothingSteps()
        {
            ReportSmoothingInfo();
            CreateNeuralNetworkApproximation();
        }


        internal override void FreeUnnecessaryStorageAfterSmoothingComplete()
        {
            SmoothingSetPointInfos = null;
            //kdTree.FreeKDTreeMemory();
            KDTreeForInputs = null;
            SmoothingSetPointInfosValidationSet = null;
            StorageForSmoothingSetValidation = null;
            SmoothingSetPointInfosMainSet = null;
            SmoothingSetPointInfosValidationSet = null;
        }

    }
}
