using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRMultiModel<T>
    {
        public Dictionary<T, DeepCFRModel> Models = new Dictionary<T, DeepCFRModel>();

        int ReservoirCapacity;
        long ReservoirSeed;
        double DiscountRate;
        object LockObj = new object();

        public DeepCFRMultiModel(int reservoirCapacity, long reservoirSeed, double discountRate)
        {
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
        }

        public IEnumerable<DeepCFRModel> EnumerateModels() => Models.OrderBy(x => x.Key).Select(x => x.Value);

        private void AddModelIfNecessary(T identifier)
        {
            if (!Models.ContainsKey(identifier))
            {
                lock (LockObj)
                {
                    if (!Models.ContainsKey(identifier))
                    {
                        Models[identifier] = new DeepCFRModel(ReservoirCapacity, ReservoirSeed, DiscountRate);
                    }
                }
            }
        }

        public byte ChooseAction(T identifier, double randomValue, DeepCFRIndependentVariables independentVariables, byte maxActionValue, byte numActionsToSample)
        {
            AddModelIfNecessary(identifier);
            return Models[identifier].ChooseAction(randomValue, independentVariables, maxActionValue, numActionsToSample);
        }

        public void AddPendingObservation(T identifier, DeepCFRObservation observation)
        {
            AddModelIfNecessary(identifier);
            Models[identifier].AddPendingObservation(observation);
        }

        public int[] CountPendingObservationsTarget(int iteration) => EnumerateModels().Select(x => x.CountPendingObservationsTarget(iteration)).ToArray();

        public bool AllMeetPendingObservationsTarget(int[] target)
        {
            if (target == null || target.Length == 0)
                return Models.Any() && Models.All(x => x.Value.PendingObservations.Count() > ReservoirCapacity);
            bool result = EnumerateModels().Select(x => x.PendingObservations.Count()).Zip(target, (poc, t) => poc > t).All(x => x == true);
            return result;
        }

        public async Task CompleteIteration(int deepCFR_Epochs)
        {
            //await Parallelizer.ForEachAsync(EnumerateModels(), m => m.CompleteIteration(deepCFR_Epochs));
            foreach (var model in EnumerateModels())
                await model.CompleteIteration(deepCFR_Epochs);
        }
    }
}
