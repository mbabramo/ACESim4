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
        Type dynamicType;
        SchemaDefinition Schema;

        public class MyData
        {
            public float[] Dependent { get; set; }
            public float[] Independent { get; set; }
        }

        public void InitializeSchemaDefinition((float[] X, float[] Y)[] data)
        {
            var exampleDatum = data.First();
            Schema = SchemaDefinition.Create(typeof(MyData));
            Schema["Dependent"].ColumnType = new VectorDataViewType(NumberDataViewType.Single, exampleDatum.Y.Length);
            Schema["Independent"].ColumnType = new VectorDataViewType(NumberDataViewType.Single, exampleDatum.X.Length);
        }

        public IDataView ArrayToDataView(MLContext mlContext, (float[] X, float[] Y)[] data)
        {
            var myData = data.Select(d => new MyData() { Dependent = d.Y, Independent = d.X }).ToArray();
            mlContext.Data.LoadFromEnumerable(myData, Schema);
        }

        public IDataView ArrayToDataView2((float[] X, float[] Y)[] data)
        {
            InitializeSchemaDefinitionIfNecessary(data);

            // create dynamic list
            IEnumerable<object> dynamicList = DynamicType.CreateDynamicList(dynamicType);

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
            var dataView = (IDataView)loadMethod.Invoke(mlContext.Data, new[] { dynamicList, Schema });

            return dataView;
        }

        private void InitializeSchemaDefinitionIfNecessary((float[] X, float[] Y)[] data)
        {
            if (dynamicType != null)
                return;
            List<DynamicTypeProperty> properties = new List<DynamicTypeProperty>();
            for (int y = 0; y < data.First().Y.Length; y++)
                properties.Add(new DynamicTypeProperty($"Y{y}", typeof(float)));
            for (int x = 0; x < data.First().X.Length; x++)
                properties.Add(new DynamicTypeProperty($"X{x}", typeof(float)));

            dynamicType = DynamicType.CreateDynamicType(properties);
            Schema = SchemaDefinition.Create(dynamicType);
        }

        public Task Regress((float[] X, float[] Y)[] data)
        {
            MLContext mlContext = new MLContext();
            mlContext.Data.CreateTextLoader();
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
            var DEBUG = experimentResult.BestRun.Estimator;
            model.Transform()
            model.Transform()
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
