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

namespace ACESimBase.Util
{
    public partial class MLNetRegression : IRegression
    {
        SchemaDefinition Schema;
        MLContext Context;
        ITransformer Transformer; 
        PredictionEngine<MLNetDatum, MLNetPrediction> PredictionEngine;
        RegressionTechniques Technique;

        public MLNetRegression(RegressionTechniques technique)
        {
            Technique = technique;
        }

        public IEstimator<ITransformer> GetEstimator(IDataView trainDataView) => Technique switch
        {
            RegressionTechniques.OLS => Context.Regression.Trainers.Ols(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features)),
            RegressionTechniques.FastForest => Context.Regression.Trainers.FastForest(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features)),
            RegressionTechniques.FastTree => Context.Regression.Trainers.FastTree(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features)),
            RegressionTechniques.FastTreeTweedie => Context.Regression.Trainers.FastTreeTweedie(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features)),
            RegressionTechniques.OnlineGradientDescent => Context.Regression.Trainers.OnlineGradientDescent(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features)),
            RegressionTechniques.LightGbm => Context.Regression.Trainers.LightGbm(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features)),
            RegressionTechniques.LbfgsPoissonRegression => Context.Regression.Trainers.LbfgsPoissonRegression(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features)),
            RegressionTechniques.Gam => Context.Regression.Trainers.Gam(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features)),
            RegressionTechniques.SDCA => Context.Regression.Trainers.Sdca(new SdcaRegressionTrainer.Options() { LabelColumnName = nameof(MLNetDatum.Label), FeatureColumnName = nameof(MLNetDatum.Features) }),
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
            Schema[nameof(MLNetDatum.Label)].ColumnType = NumberDataViewType.Single;
            Schema[nameof(MLNetDatum.Features)].ColumnType = new VectorDataViewType(NumberDataViewType.Single, X.Length);
        }

        public IDataView DatumToDataView(float[] X, float[] Y = null) => ArrayToDataView(Context, new (float[] X, float[] Y)[] { (X, Y) });

        public IDataView ArrayToDataView(MLContext mlContext, (float[] X, float[] Y)[] data)
        {
            var datum = data.First();
            InitializeSchemaDefinitionIfNecessary(datum.X, datum.Y);
            var myData = data.Select(d => new MLNetDatum() { Label = d.Y[0], Features = d.X }).ToArray();
            return mlContext.Data.LoadFromEnumerable(myData, Schema);
        }

        public Task Regress((float[] X, float[] Y)[] data)
        {
            Context = new MLContext(0); // specify random seed for reproducibility
            IDataView trainDataView = ArrayToDataView(Context, data);
            IEstimator<ITransformer> estimator = GetEstimator(trainDataView);
            Transformer = estimator.Fit(trainDataView);
            PredictionEngine = Context.Model.CreatePredictionEngine<MLNetDatum, MLNetPrediction>(Transformer, false, Schema, null);
            return Task.CompletedTask;
        }

        private IEstimator<ITransformer> ChooseEstimatorExperimentally(MLContext mlContext, IDataView trainDataView)
        {
            var experimentSettings = new RegressionExperimentSettings();
            experimentSettings.MaxExperimentTimeInSeconds = 10;
            experimentSettings.OptimizingMetric = RegressionMetric.RSquared;
            //experimentSettings.Trainers.Clear();
            //experimentSettings.Trainers.Add(RegressionTrainer.FastTree);
            RegressionExperiment experiment = mlContext.Auto().CreateRegressionExperiment(experimentSettings);
            ExperimentResult<RegressionMetrics> experimentResult = experiment.Execute(trainDataView);
            RegressionMetrics metrics = experimentResult.BestRun.ValidationMetrics;
            Transformer = experimentResult.BestRun.Model;
            IEstimator<ITransformer> estimator = experimentResult.BestRun.Estimator;
            return estimator;
        }

        public float[] GetResults(float[] x)
        {
            MLNetPrediction prediction = new MLNetPrediction();
            PredictionEngine.Predict(new MLNetDatum() { Features = x }, ref prediction);
            return new float[] { prediction.Score };
            //IDataView transformed = Transformer.Transform(DatumToDataView(x));
            //var result = Context.Data.CreateEnumerable<MLNetPrediction>(transformed, reuseRowObject: false).First();
            //return result.DependentVariables;
        }

        public string GetTrainingResultString()
        {
            return "";
        }
    }
}
