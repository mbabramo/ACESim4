using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRMultiModel
    {
        DeepCFRMultiModelMode Mode;
        int ReservoirCapacity;
        long ReservoirSeed;
        double DiscountRate;
        object LockObj = new object();

        public DeepCFRModel UnifiedModel;
        public DeepCFRMultiModelContainer<byte> PlayerSpecificModels = null;
        public DeepCFRMultiModelContainer<(byte playerIndex, byte decisionByteCode)> DecisionSpecificModels = null;
        /// <summary>
        /// Factory to create a regression processor
        /// </summary>
        Func<IRegression> RegressionFactory;

        byte? StateFrozenForPlayer;

        public DeepCFRMultiModel(DeepCFRMultiModelMode mode, int reservoirCapacity, long reservoirSeed, double discountRate, Func<IRegression> regressionFactory)
        {
            Mode = mode;
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
            RegressionFactory = regressionFactory;
            switch (Mode)
            {
                case DeepCFRMultiModelMode.Unified:
                    UnifiedModel = new DeepCFRModel("Unified", ReservoirCapacity, ReservoirSeed, DiscountRate, RegressionFactory);
                    break;
                case DeepCFRMultiModelMode.PlayerSpecific:
                    PlayerSpecificModels = new DeepCFRMultiModelContainer<byte>(ReservoirCapacity, ReservoirSeed, DiscountRate, RegressionFactory);
                    break;
                case DeepCFRMultiModelMode.DecisionSpecific:
                    DecisionSpecificModels = new DeepCFRMultiModelContainer<(byte playerIndex, byte decisionByteCode)>(ReservoirCapacity, ReservoirSeed, DiscountRate, RegressionFactory);
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

        public void SetModel(Decision decision, DeepCFRModel model)
        {
            switch (Mode)
            {
                case DeepCFRMultiModelMode.Unified:
                    UnifiedModel = model;
                    break;
                case DeepCFRMultiModelMode.PlayerSpecific:
                    PlayerSpecificModels.SetModel(decision.PlayerNumber, () => $"Player {decision.PlayerNumber}", model);
                    break;
                case DeepCFRMultiModelMode.DecisionSpecific:
                    DecisionSpecificModels.SetModel((decision.PlayerNumber, decision.DecisionByteCode), () => $"Decision {decision.Name}", model);
                    break;
            }
        }

        public IEnumerable<DeepCFRModel> EnumerateModels() => Mode switch
        {
            DeepCFRMultiModelMode.Unified => new DeepCFRModel[] { UnifiedModel },
            DeepCFRMultiModelMode.PlayerSpecific => PlayerSpecificModels.EnumerateModels(),
            DeepCFRMultiModelMode.DecisionSpecific => DecisionSpecificModels.EnumerateModels(),
            _ => throw new NotImplementedException(),
        };

        public IEnumerable<DeepCFRModel> EnumerateModelsForPlayer(byte playerIndex) => Mode switch
        {
            DeepCFRMultiModelMode.Unified => throw new NotSupportedException("Cannot enumerate per-player model when using unified models"), // thus, we would need to do switch to player-specific to do best response approximation with this
            DeepCFRMultiModelMode.PlayerSpecific => PlayerSpecificModels.EnumerateModelsWithKey().Where(x => x.key == playerIndex).Select(x => x.model),
            DeepCFRMultiModelMode.DecisionSpecific => DecisionSpecificModels.EnumerateModelsWithKey().Where(x => x.key.playerIndex == playerIndex).Select(x => x.model),
            _ => throw new NotImplementedException(),
        };

        public IEnumerable<(byte playerIndex, DeepCFRModel model)> EnumerateModelsWithPlayerInfo() => Mode switch
        {
            DeepCFRMultiModelMode.Unified => throw new NotSupportedException("Cannot enumerate per-player model when using unified models"), // thus, we would need to do switch to player-specific to do best response approximation with this
            DeepCFRMultiModelMode.PlayerSpecific => PlayerSpecificModels.EnumerateModelsWithKey().Select(x => (x.key, x.model)),
            DeepCFRMultiModelMode.DecisionSpecific => DecisionSpecificModels.EnumerateModelsWithKey().Select(x => (x.key.playerIndex, x.model)),
            _ => throw new NotImplementedException(),
        };

        public void RememberStateForPlayer(byte playerIndex)
        {
            if (StateFrozenForPlayer != null)
                throw new NotImplementedException("Cannot freeze state for both players.");
            StateFrozenForPlayer = playerIndex;
            foreach (DeepCFRModel model in EnumerateModelsForPlayer(playerIndex))
            {
                model.RememberObservations();
            }
        }

        public async Task RecallRememberedStateForPlayer(byte playerIndex)
        {
            foreach (DeepCFRModel model in EnumerateModelsForPlayer(playerIndex))
            {
                model.RecallRememberedObservations();
                await model.BuildModel(); // must rebuild the model -- alternative would be to duplicate the model, but this would require more work
            }
        }

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

        public int[] CountPendingObservationsTarget(int iteration) => StateFrozenForPlayer == null ? CountPendingObservationsTarget_Ordinary(iteration) : CountPendingObservationsTarget_OnePlayerFrozen();

        private int[] CountPendingObservationsTarget_Ordinary(int iteration) => EnumerateModels().Select(x => x.CountPendingObservationsTarget(iteration)).ToArray();

        // if one player is frozen, then we want to replace NONE of that player's observations, but we want to replace ALL of the other player's observations.
        debug; // we actually need to check whether to add the observation
        public int[] CountPendingObservationsTarget_OnePlayerFrozen() => EnumerateModelsWithPlayerInfo().Select(x => x.playerIndex == StateFrozenForPlayer ? 0 : x.model.Observations.Capacity).ToArray();

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
}
