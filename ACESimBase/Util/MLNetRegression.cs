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
    public class MLNetRegression : IRegression
    {
        SchemaDefinition Schema;
        MLContext Context;
        ITransformer Transformer; 
        PredictionEngine<MLNetDatum, MLNetPrediction> PredictionEngine;

        public class MLNetDatum
        {
            /// <summary>
            /// The dependent variables (Y)
            /// </summary>
            public float[] DependentVariables { get; set; }
            /// <summary>
            /// The independent variables (X)
            /// </summary>
            public float[] Features { get; set; }
        }

        public class MLNetPrediction
        {
            public float[] DependentVariables { get; set; }
            // Score produced from the trainer.
            public float Score { get; set; }
        }

        public void InitializeSchemaDefinitionIfNecessary(float[] X, float[] Y)
        {
            if (Schema != null)
                return;
            Schema = SchemaDefinition.Create(typeof(MLNetDatum));
            Schema[nameof(MLNetDatum.DependentVariables)].ColumnType = new VectorDataViewType(NumberDataViewType.Single, Y.Length);
            Schema[nameof(MLNetDatum.Features)].ColumnType = new VectorDataViewType(NumberDataViewType.Single, X.Length);
        }

        public IDataView DatumToDataView(float[] X, float[] Y = null) => ArrayToDataView(Context, new (float[] X, float[] Y)[] { (X, Y) });

        public IDataView ArrayToDataView(MLContext mlContext, (float[] X, float[] Y)[] data)
        {
            var datum = data.First();
            InitializeSchemaDefinitionIfNecessary(datum.X, datum.Y);
            var myData = data.Select(d => new MLNetDatum() { DependentVariables = d.Y, Features = d.X }).ToArray();
            return mlContext.Data.LoadFromEnumerable(myData, Schema);
        }

        public Task Regress((float[] X, float[] Y)[] data)
        {
            Context = new MLContext();
            IDataView trainDataView = ArrayToDataView(Context, data);
            IEstimator<ITransformer> estimator = null;
            estimator = Context.Regression.Trainers.FastForest(nameof(MLNetDatum.DependentVariables), nameof(MLNetDatum.Features));
            // estimator = ChooseEstimatorExperimentally(mlContext, trainDataView);
            Transformer = estimator.Fit(trainDataView);
            PredictionEngine = Context.Model.CreatePredictionEngine<MLNetDatum, MLNetPrediction>(Transformer);
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
            return prediction.DependentVariables;
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
