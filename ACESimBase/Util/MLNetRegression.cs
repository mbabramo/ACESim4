using Microsoft.ML;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.ML.AutoML;
using ACESim;
using System.Linq;
using Microsoft.ML.Data;
using System.Threading.Tasks;
using Microsoft.ML.Trainers;
using Microsoft.Extensions.ObjectPool;

namespace ACESimBase.Util
{
    public partial class MLNetRegression : IRegression
    {
        SchemaDefinition Schema;
        MLContext Context;
        ITransformer Transformer;
        FactoryCreatableObjectPool<PredictionEngine<MLNetDatum, MLNetPrediction>> PredictionEnginePool; // we need to have a separate prediction engine for each thread, so we put the prediction engines in a pool. even then, repeated access of the pool can be slow, so we can create an IRegressionMachine that locally caches a prediction engine taken from a pool
        RegressionTechniques Technique;

        public MLNetRegression(RegressionTechniques technique)
        {
            Technique = technique;
        }

        public IEstimator<ITransformer> GetEstimator(IDataView trainDataView) => Technique switch
        {
            RegressionTechniques.OLS => Context.Regression.Trainers.Ols(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features), nameof(MLNetDatum.Weight)),
            RegressionTechniques.FastForest => Context.Regression.Trainers.FastForest(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features), nameof(MLNetDatum.Weight)),
            RegressionTechniques.FastTree => Context.Regression.Trainers.FastTree(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features), nameof(MLNetDatum.Weight)),
            RegressionTechniques.FastTreeTweedie => Context.Regression.Trainers.FastTreeTweedie(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features), nameof(MLNetDatum.Weight)),
            // RegressionTechniques.OnlineGradientDescent => Context.Regression.Trainers.OnlineGradientDescent(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features)), // omitted because no weight column
            RegressionTechniques.LightGbm => Context.Regression.Trainers.LightGbm(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features), nameof(MLNetDatum.Weight)),
            RegressionTechniques.LbfgsPoissonRegression => Context.Regression.Trainers.LbfgsPoissonRegression(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features), nameof(MLNetDatum.Weight)),
            RegressionTechniques.Gam => Context.Regression.Trainers.Gam(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features), nameof(MLNetDatum.Weight)),
            RegressionTechniques.SDCA => Context.Regression.Trainers.Sdca(new SdcaRegressionTrainer.Options() { LabelColumnName = nameof(MLNetDatum.Label), FeatureColumnName = nameof(MLNetDatum.Features), ExampleWeightColumnName = nameof(MLNetDatum.Weight) }),
            RegressionTechniques.Experimental => ChooseEstimatorExperimentally(Context, trainDataView),
            _ => throw new NotImplementedException(),
        };

        public class MLNetDatum
        {
            /// <summary>
            /// The dependent variable (Y)
            /// </summary>
            public float Label { get; set; }
            /// <summary>
            /// The independent variables (X)
            /// </summary>
            public float[] Features { get; set; }
            /// <summary>
            /// The weight of the observation
            /// </summary>
            public float Weight { get; set; }
        }

        public class MLNetPrediction
        {
            public float Score { get; set; }
        }

        public void InitializeSchemaDefinitionIfNecessary(float[] X, float[] Y)
        {
            if (Schema != null)
                return;
            Schema = SchemaDefinition.Create(typeof(MLNetDatum));
            if (Y.Length != 1)
                throw new Exception();
            Schema[nameof(MLNetDatum.Label)].ColumnType =
                NumberDataViewType.Single;
            Schema[nameof(MLNetDatum.Features)].ColumnType = new VectorDataViewType(NumberDataViewType.Single, X.Length);
        }

        public IDataView DatumToDataView(float[] X, float[] Y, float W) => ArrayToDataView(Context, new (float[] X, float[] Y, float W)[] { (X, Y, W) });

        public IDataView ArrayToDataView(MLContext mlContext, (float[] X, float[] Y, float W)[] data)
        {
            var datum = data.First();
            InitializeSchemaDefinitionIfNecessary(datum.X, datum.Y);
            var myData = data.Select(d => new MLNetDatum() { Label = d.Y[0], Features = d.X, Weight = d.W }).ToArray();
            return mlContext.Data.LoadFromEnumerable(myData, Schema);
        }

        public Task Regress((float[] X, float[] Y, float W)[] data)
        {
            Context = new MLContext(0); // specify random seed for reproducibility
            IDataView trainDataView = ArrayToDataView(Context, data);
            IEstimator<ITransformer> estimator = GetEstimator(trainDataView);
            
            Transformer = estimator.Fit(trainDataView);

            bool applyCrossValidation = false; 
            if (applyCrossValidation)
            {
                var transformedData = Transformer.Transform(trainDataView);
                var cvResults = Context.Regression.CrossValidate(transformedData, estimator, numberOfFolds: 5);
            }

            PredictionEnginePool = new FactoryCreatableObjectPool<PredictionEngine<MLNetDatum, MLNetPrediction>>(() => GetNewPredictionEngine());
            return Task.CompletedTask;
        }

        private IEstimator<ITransformer> ChooseEstimatorExperimentally(MLContext mlContext, IDataView trainDataView)
        {
            var experimentSettings = new RegressionExperimentSettings();
            experimentSettings.MaxExperimentTimeInSeconds = 10;
            experimentSettings.OptimizingMetric = RegressionMetric.RSquared;
            experimentSettings.Trainers.Clear();
            experimentSettings.Trainers.Add(RegressionTrainer.FastTree);
            RegressionExperiment experiment = mlContext.Auto().CreateRegressionExperiment(experimentSettings);
            ExperimentResult<RegressionMetrics> experimentResult = experiment.Execute(trainDataView, new ColumnInformation() { ExampleWeightColumnName = nameof(MLNetDatum.Weight), LabelColumnName = nameof(MLNetDatum.Label) }); // Do we need a FeatureColumnName? Doesn't seem to exist as an option.
            RegressionMetrics metrics = experimentResult.BestRun.ValidationMetrics;
            Transformer = experimentResult.BestRun.Model;
            IEstimator<ITransformer> estimator = experimentResult.BestRun.Estimator;
            return estimator;
        }

        private PredictionEngine<MLNetDatum, MLNetPrediction> GetNewPredictionEngine()
        {
            return Context.Model.CreatePredictionEngine<MLNetDatum, MLNetPrediction>(Transformer, false, Schema, null);
        }

        public PredictionEngine<MLNetDatum, MLNetPrediction> GetPredictionEngine()
        {
            var result = PredictionEnginePool.GetObject();
            return result;
        }
        public void ReturnPredictionEngineToPool(PredictionEngine<MLNetDatum, MLNetPrediction> predictionEngine)
        {
            PredictionEnginePool.Return(predictionEngine);
        }

        public class MLNetRegressionMachine : IRegressionMachine
        {
            public PredictionEngine<MLNetDatum, MLNetPrediction> PredictionEngine;

            public MLNetRegressionMachine(PredictionEngine<MLNetDatum, MLNetPrediction> predictionEngine)
            {
                PredictionEngine = predictionEngine;
            }

            public float[] GetResults(float[] x)
            {
                MLNetPrediction prediction = new MLNetPrediction();
                PredictionEngine.Predict(new MLNetDatum() { Features = x }, ref prediction);
                return new float[] { prediction.Score };
            }
        }

        // The following is the local caching mechanism. The idea is that we don't have to keep using the pool (which may be slow).

        public IRegressionMachine GetRegressionMachine()
        {
            PredictionEngine<MLNetDatum, MLNetPrediction> predictionEngine = GetPredictionEngine();
            MLNetRegressionMachine regressionMachine = new MLNetRegressionMachine(predictionEngine);
            return regressionMachine;
        }

        public void ReturnRegressionMachine(IRegressionMachine regressionMachine)
        {
            MLNetRegressionMachine machine = (MLNetRegressionMachine)regressionMachine;
            ReturnPredictionEngineToPool(machine.PredictionEngine);
        }

        public float[] GetResults(float[] x, IRegressionMachine regressionMachine = null)
        {
            bool createRegressionMachine = regressionMachine == null;
            if (createRegressionMachine)
                regressionMachine = GetRegressionMachine();
            var results = regressionMachine.GetResults(x);
            if (createRegressionMachine)
                ReturnRegressionMachine(regressionMachine);
            return results;
        }

        public string GetTrainingResultString()
        {
            return "";
        }
    }
}
