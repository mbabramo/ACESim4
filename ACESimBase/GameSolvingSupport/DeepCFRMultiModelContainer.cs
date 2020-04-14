using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRMultiModelContainer<T>
    {
        public Dictionary<T, DeepCFRModel> Models = new Dictionary<T, DeepCFRModel>();

        int ReservoirCapacity;
        long ReservoirSeed;
        double DiscountRate;
        object LockObj = new object(); 
        Func<IRegression> RegressionFactory;

        public DeepCFRMultiModelContainer(int reservoirCapacity, long reservoirSeed, double discountRate, Func<IRegression> regressionFactory)
        {
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
            RegressionFactory = regressionFactory;
        }

        public IEnumerable<DeepCFRModel> EnumerateModels() => Models.OrderBy(x => x.Key).Select(x => x.Value);
        public IEnumerable<(T key, DeepCFRModel model)> EnumerateModelsWithKey() => Models.OrderBy(x => x.Key).Select(x => (x.Key, x.Value));

        private void AddModelIfNecessary(T identifier, Func<string> modelName)
        {
            if (!Models.ContainsKey(identifier))
            {
                lock (LockObj)
                {
                    if (!Models.ContainsKey(identifier))
                    {
                        Models[identifier] = new DeepCFRModel(modelName(), ReservoirCapacity, ReservoirSeed, DiscountRate, RegressionFactory);
                    }
                }
            }
        }

        public DeepCFRModel GetModel(T identifier, Func<string> modelName)
        {
            AddModelIfNecessary(identifier, modelName);
            return Models[identifier];
        }
        public void SetModel(T identifier, Func<string> modelName, DeepCFRModel model)
        {
            Models[identifier] = model;
        }
    }
}
