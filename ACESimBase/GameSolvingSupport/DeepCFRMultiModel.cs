using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public enum DeepCFRMultiModelMode
    {
        Unified,
        PlayerSpecific,
        DecisionSpecific
    }

    public class DeepCFRMultiModel
    {
        DeepCFRMultiModelMode Mode;
        int ReservoirCapacity;
        long ReservoirSeed;
        double DiscountRate;
        int Epochs, HiddenLayers;
        object LockObj = new object();

        public DeepCFRModel UnifiedModel;
        public DeepCFRMultiModelContainer<byte> PlayerSpecificModels = null;
        public DeepCFRMultiModelContainer<(byte playerIndex, byte decisionByteCode)> DecisionSpecificModels = null;

        public DeepCFRMultiModel(DeepCFRMultiModelMode mode, int reservoirCapacity, long reservoirSeed, double discountRate, int epochs, int hiddenLayers)
        {
            Mode = mode;
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
            Epochs = epochs;
            HiddenLayers = hiddenLayers;
            switch (Mode)
            {
                case DeepCFRMultiModelMode.Unified:
                    UnifiedModel = new DeepCFRModel("Unified", ReservoirCapacity, ReservoirSeed, DiscountRate, HiddenLayers, Epochs);
                    break;
                case DeepCFRMultiModelMode.PlayerSpecific:
                    PlayerSpecificModels = new DeepCFRMultiModelContainer<byte>(ReservoirCapacity, ReservoirSeed, DiscountRate, HiddenLayers, Epochs);
                    break;
                case DeepCFRMultiModelMode.DecisionSpecific:
                    DecisionSpecificModels = new DeepCFRMultiModelContainer<(byte playerIndex, byte decisionByteCode)>(ReservoirCapacity, ReservoirSeed, DiscountRate, HiddenLayers, Epochs);
                    break;
            }
        }

        public DeepCFRModel GetModel(Decision decision) => Mode switch
        {
            DeepCFRMultiModelMode.Unified => UnifiedModel,
            DeepCFRMultiModelMode.PlayerSpecific => PlayerSpecificModels.GetModel(decision.PlayerNumber, () => $"Player {decision.PlayerNumber}"),
            DeepCFRMultiModelMode.DecisionSpecific => DecisionSpecificModels.GetModel((decision.PlayerNumber, decision.DecisionByteCode), () => $"Decision {decision.Name}"),
            _ => throw new NotImplementedException(),
        };

        public IEnumerable<DeepCFRModel> EnumerateModels() => Mode switch
        {
            DeepCFRMultiModelMode.Unified => new DeepCFRModel[] { UnifiedModel },
            DeepCFRMultiModelMode.PlayerSpecific => PlayerSpecificModels.EnumerateModels(),
            DeepCFRMultiModelMode.DecisionSpecific => DecisionSpecificModels.EnumerateModels(),
            _ => throw new NotImplementedException(),
        };


        public byte ChooseAction(Decision decision, double randomValue, DeepCFRIndependentVariables independentVariables, byte maxActionValue, byte numActionsToSample, double probabilityUniformRandom)
        {
            var model = GetModel(decision);
            return ChooseAction(model, randomValue, independentVariables, maxActionValue, numActionsToSample, probabilityUniformRandom);
        }

        public static byte ChooseAction(DeepCFRModel model, double randomValue, DeepCFRIndependentVariables independentVariables, byte maxActionValue, byte numActionsToSample, double probabilityUniformRandom)
        {
            // turn one random draw into two independent random draws
            double rand1 = Math.Floor(randomValue * 10_000) / 10_000;
            double rand2 = (randomValue - rand1) * 10_000;
            if (rand1 < probabilityUniformRandom)
                return DeepCFRModel.ChooseActionAtRandom(rand2, maxActionValue);
            var result = model.ChooseAction(rand2, independentVariables, maxActionValue, numActionsToSample);
            if (result > numActionsToSample)
                throw new Exception("Internal error. Invalid action choice.");
            return result;
        }

        public void AddPendingObservation(Decision decision, DeepCFRObservation observation)
        {
            var model = GetModel(decision);
            AddPendingObservation(model, observation);
        }

        public static void AddPendingObservation(DeepCFRModel model, DeepCFRObservation observation)
        {
            model.AddPendingObservation(observation);
        }

        public int[] CountPendingObservationsTarget(int iteration) => EnumerateModels().Select(x => x.CountPendingObservationsTarget(iteration)).ToArray();

        public bool AllMeetInitialPendingObservationsTarget(int initialTarget)
        {
            bool result = EnumerateModels().All(x => x.PendingObservations.Count() >= initialTarget);
            return result;
        }

        public bool AllMeetPendingObservationsTarget(int[] target)
        {
            bool result = EnumerateModels().Select(x => x.PendingObservations.Count()).Zip(target, (poc, t) => poc >= t).All(x => x == true);
            return result;
        }

        public async Task CompleteIteration(int deepCFR_Epochs)
        {
            //await Parallelizer.ForEachAsync(EnumerateModels(), m => m.CompleteIteration(deepCFR_Epochs));
            foreach (var model in EnumerateModels())
                await model.CompleteIteration();
        }
    }

    public class DeepCFRMultiModelContainer<T>
    {
        public Dictionary<T, DeepCFRModel> Models = new Dictionary<T, DeepCFRModel>();

        int ReservoirCapacity;
        long ReservoirSeed;
        double DiscountRate;
        int Epochs, HiddenLayers;
        object LockObj = new object();

        public DeepCFRMultiModelContainer(int reservoirCapacity, long reservoirSeed, double discountRate, int hiddenLayers, int epochs)
        {
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
            HiddenLayers = hiddenLayers;
            Epochs = epochs;
        }

        public IEnumerable<DeepCFRModel> EnumerateModels() => Models.OrderBy(x => x.Key).Select(x => x.Value);

        private void AddModelIfNecessary(T identifier, Func<string> modelName)
        {
            if (!Models.ContainsKey(identifier))
            {
                lock (LockObj)
                {
                    if (!Models.ContainsKey(identifier))
                    {
                        Models[identifier] = new DeepCFRModel(modelName(), ReservoirCapacity, ReservoirSeed, DiscountRate, HiddenLayers, Epochs);
                    }
                }
            }
        }

        public DeepCFRModel GetModel(T identifier, Func<string> modelName)
        {
            AddModelIfNecessary(identifier, modelName);
            return Models[identifier];
        }
    }
}
