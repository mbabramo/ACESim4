using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace ACESim
{
    [Serializable]
    [XmlInclude(typeof(SmoothingOptionsWithPresmoothing)), XmlInclude(typeof(RPROPSmoothingOptions)), XmlInclude(typeof(GRNNSmoothingOptions)), XmlInclude(typeof(NearestNeighborSmoothingOptions)), XmlInclude(typeof(RegressionBasedSmoothingOptions))]
    public abstract class SmoothingOptions
    {
        public int SizeOfOversamplingSample;
        public int SizeOfSuccessReplicationSample;
        public bool StartSuccessReplicationFromScratch;
        public double SkipIfSuccessAttemptRateFallsBelow;
        public int MaxFailuresToRemember;

        public abstract IStrategyComponent GetStrategyComponent(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name);

        public virtual int NumNearestNeighborsToCalculate()
        {
            return 1;
        }
    }

    [Serializable]
    public class DirectScoreEstimationSmoothingOptions : SmoothingOptions
    {
        public int NumRefinementPasses;
        public int NumIterationsScoreEstimationNetworkMainSet;
        public int NumIterationsScoreEstimationNetworkValidationSet;
        public int NumIterationsOutputEstimationNetworkMainSet;
        public int NumIterationsOutputEstimationNetworkValidationSet;
        public double InitialWeightOnRandomStrategy; // the weight to be placed on a random number for the second refinement
        public double WeightOnRandomStrategyMultiplier; // the multiplier for this weight for each subsequent refinement
        public int EpochsScoreEstimationNetwork;
        public int ValidateEveryNEpochsScoreEstimationNetwork;
        public int HiddenLayerSizeScoreEstimationNetwork;
        public int EpochsOutputEstimationNetwork;
        public int ValidateEveryNEpochsOutputEstimationNetwork;
        public int HiddenLayerSizeOutputEstimationNetwork;
        public bool AutomaticallyGeneratePlots;

        public override IStrategyComponent GetStrategyComponent(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
        {
            return new DirectScoreEstimation(overallStrategy, dimensions, evolutionSettings, decision, name);
        }
    }

    [Serializable]
    public class NeuralNetworkWithoutPresmoothingOptions : SmoothingOptions
    {
        public int FirstHiddenLayerNeurons;
        public int SecondHiddenLayerNeurons;
        public int Epochs;

        public override IStrategyComponent GetStrategyComponent(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
        {
            return new NeuralNetworkStrategyComponent(overallStrategy, dimensions, evolutionSettings, decision, name);
        }
    }

    [Serializable]
    [XmlInclude(typeof(RPROPSmoothingOptions)), XmlInclude(typeof(GRNNSmoothingOptions)), XmlInclude(typeof(NearestNeighborSmoothingOptions)), XmlInclude(typeof(RegressionBasedSmoothingOptions))]
    public abstract class SmoothingOptionsWithPresmoothing : SmoothingOptions
    {
        public double PreliminaryOptimizationPrecision; // The desired precision from preliminary optimization, as a fraction of the strategy bounds distance
        public int MemoryLimitForIterations; // The maximum number of iterations that we can keep track of at a time (approximately)
        public bool AutomaticallyGeneratePlots;
        public bool SwitchToDirectEstimationWhenScoreRepresentsCorrectAnswer;
    }

    [Serializable]
    public class NearestNeighborSmoothingOptions : SmoothingOptionsWithPresmoothing
    {
        public override IStrategyComponent GetStrategyComponent(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
        {
            //if (decision.ScoreRepresentsCorrectAnswer && SwitchToDirectEstimationWhenScoreRepresentsCorrectAnswer)
            //    return new DirectScoreEstimation(overallStrategy, dimensions, evolutionSettings, decision, name);
            return new OptimizePointsAndSmooth(overallStrategy, dimensions, evolutionSettings, decision, name);
        }
    }

    [Serializable]
    public class RegressionBasedSmoothingOptions : SmoothingOptionsWithPresmoothing
    {
        public int SmoothingRepetitions;
        public int SmoothingRestoreOriginalPointsForFirstNRepetitions; // if this is less than SmoothingRepetitions, we will restore the original points instead of using the interpolated points; this allows the derivatives to be refined based on the updated values of the other derivatives but not by changing the points
        public int MaximumDerivativeOrder; // 0 to do smoothing without derivatives, 1 to use first derivatives only, etc.
        public int NearbyNeighborsToWeighInSmoothing;
        public int NearbyNeighborsToWeighInPlayMode;
        public int NumObsInOptimizeWeightingRegression;
        public int NumPointsPerWeightingRegressionObs;
        public bool CreateRegularGridAfterRegressionBasedSmoothing;
        public int RegularGridPoints;


        public override IStrategyComponent GetStrategyComponent(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
        {
            if (decision.ScoreRepresentsCorrectAnswer && SwitchToDirectEstimationWhenScoreRepresentsCorrectAnswer)
                return new DirectScoreEstimation(overallStrategy, dimensions, evolutionSettings, decision, name);
            return new RegressionBasedSmoothing(overallStrategy, dimensions, evolutionSettings, decision, name);
        }

        public override int NumNearestNeighborsToCalculate()
        {
            return NearbyNeighborsToWeighInSmoothing;
        }
    }

    [Serializable]
    public class RPROPSmoothingOptions : SmoothingOptionsWithPresmoothing
    {
        public int FirstHiddenLayerNeurons;
        public int SecondHiddenLayerNeurons;
        public int Epochs;
        public int TestValidationSetEveryNEpochs;


        public override IStrategyComponent GetStrategyComponent(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
        {
            if (decision.ScoreRepresentsCorrectAnswer && SwitchToDirectEstimationWhenScoreRepresentsCorrectAnswer)
                return new DirectScoreEstimation(overallStrategy, dimensions, evolutionSettings, decision, name);
            return new RPROPSmoothing(overallStrategy, dimensions, evolutionSettings, decision, name);
        }
    }

    [Serializable]
    public class GRNNSmoothingOptions : SmoothingOptionsWithPresmoothing
    {
        public bool LimitCalculationToNearestNeighbors;
        public int NearbyNeighborsToConsider;
        public bool SpeedUpWithApproximateNearestNeighbors;

        public override IStrategyComponent GetStrategyComponent(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
        {
            if (decision.ScoreRepresentsCorrectAnswer && SwitchToDirectEstimationWhenScoreRepresentsCorrectAnswer)
                return new DirectScoreEstimation(overallStrategy, dimensions, evolutionSettings, decision, name);
            return new GRNNSmoothing(overallStrategy, dimensions, evolutionSettings, decision, name);
        }

        public override int NumNearestNeighborsToCalculate()
        {
            if (LimitCalculationToNearestNeighbors)
                return NearbyNeighborsToConsider;
            return 1;
        }
    }
}
