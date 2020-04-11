using Microsoft.ML;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.ML;
using Microsoft.ML.AutoML;
using ACESim;
using System.Linq;
using Microsoft.ML.Data;
using System.Threading.Tasks;

namespace ACESimBase.Util
{
    public class MLNetRegression : IRegression
    {
        public static IDataView ArrayToDataView((float[] X, float[] Y)[] data)
        {
            var properties = new List<DynamicTypeProperty>();
            for (int y = 0; y < data.First().Y.Length)
                properties.Add(new DynamicTypeProperty($"Y{y}", typeof(float)));
            for (int y = 0; y < data.First().X.Length)
                properties.Add(new DynamicTypeProperty($"X{y}", typeof(float)));

            var dynamicType = DynamicType.CreateDynamicType(properties);
            var schema = SchemaDefinition.Create(dynamicType);

            // create dynamic list
            var dynamicList = DynamicType.CreateDynamicList(dynamicType);

            // get an action that will add to the list
            var addAction = DynamicType.GetAddAction(dynamicList);

            foreach (var datum in data)
            {
                object[] toAdd = new object[datum.Y.Length + datum.X.Length];
                for (int i = 0; i < datum.Y.Length; i++)
                    toAdd[i] = datum.Y[i];
                for (int i = 0; i < datum.X.Length; i++)
                    toAdd[datum.Y.Length + i] = datum.X[i];
                addAction.Invoke(toAdd);
            }

            var mlContext = new MLContext();
            var dataType = mlContext.Data.GetType();
            var loadMethodGeneric = dataType.GetMethods().First(method => method.Name == "LoadFromEnumerable" && method.IsGenericMethod);
            var loadMethod = loadMethodGeneric.MakeGenericMethod(dynamicType);
            var dataView = (IDataView)loadMethod.Invoke(mlContext.Data, new[] { dynamicList, schema });

            return dataView;
        }


        public Task Regress((float[] X, float[] Y)[] data)
        {
            MLContext mlContext = new MLContext();
            IDataView trainDataView = ArrayToDataView(data);
            var experimentSettings = new RegressionExperimentSettings();
            experimentSettings.MaxExperimentTimeInSeconds = 10;
            experimentSettings.OptimizingMetric = RegressionMetric.RSquared;
            //experimentSettings.Trainers.Clear();
            //experimentSettings.Trainers.Add(RegressionTrainer.FastTree);
            RegressionExperiment experiment = mlContext.Auto().CreateRegressionExperiment(experimentSettings); 
            ExperimentResult<RegressionMetrics> experimentResult = experiment.Execute(trainDataView); 
            RegressionMetrics metrics = experimentResult.BestRun.ValidationMetrics;
            var model = experimentResult.BestRun.Model;
            experimentResult.BestRun.
        }

        public float[] GetResults(float[] x)
        {
            throw new NotImplementedException();
        }

        public string GetTrainingResultString()
        {
            throw new NotImplementedException();
        }
    }
}
