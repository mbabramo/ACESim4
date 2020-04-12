using Microsoft.ML;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.ML.AutoML;
using ACESim;
using System.Linq;
using Microsoft.ML.Data;
using System.Threading.Tasks;

namespace ACESimBase.Util
{
    public partial class MLNetRegression : IRegression
    {
        SchemaDefinition Schema;
        MLContext Context;
        ITransformer Transformer; 
        PredictionEngine<MLNetDatum, MLNetPrediction> PredictionEngine;
        MLNetRegressionTechniques Technique = MLNetRegressionTechniques.FastForest;

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
            Context = new MLContext();
            IDataView trainDataView = ArrayToDataView(Context, data);
            IEstimator<ITransformer> estimator = GetEstimator(trainDataView);
            Transformer = estimator.Fit(trainDataView);
            PredictionEngine = Context.Model.CreatePredictionEngine<MLNetDatum, MLNetPrediction>(Transformer, false, Schema, null);
            return Task.CompletedTask;
        }

        public IEstimator<ITransformer> GetEstimator(IDataView trainDataView) => Technique switch
        {
            MLNetRegressionTechniques.OLS => Context.Regression.Trainers.Ols(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features)),
            MLNetRegressionTechniques.FastForest => Context.Regression.Trainers.FastForest(nameof(MLNetDatum.Label), nameof(MLNetDatum.Features), null, 2000, 10000, 1000),
            MLNetRegressionTechniques.Experimental => ChooseEstimatorExperimentally(Context, trainDataView),
            _ => throw new NotImplementedException(),
        };

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
