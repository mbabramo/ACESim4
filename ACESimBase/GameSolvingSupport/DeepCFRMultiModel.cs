using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRMultiModel
    {
        DeepCFRMultiModelMode Mode;
        int ReservoirCapacity;
        long ReservoirSeed;
        double DiscountRate;

        public DeepCFRModel UnifiedModel;
        public DeepCFRMultiModelContainer<byte> PlayerSpecificModels = null;
        public DeepCFRMultiModelContainer<(byte playerIndex, byte decisionByteCode)> DecisionSpecificModels = null;
        /// <summary>
        /// Factory to create a regression processor
        /// </summary>
        Func<IRegression> RegressionFactory;

        byte? DeterminingBestResponseOfPlayer;

        #region Model initialization and access

        public DeepCFRMultiModel()
        {

        }

        public DeepCFRMultiModel(DeepCFRMultiModelMode mode, int reservoirCapacity, long reservoirSeed, double discountRate, Func<IRegression> regressionFactory)
        {
            Mode = mode;
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
            RegressionFactory = regressionFactory;
            InitializeModels();
        }

        public DeepCFRMultiModel DeepCopyForPlaybackOnly()
        {
            return new DeepCFRMultiModel()
            {
                Mode = Mode,
                ReservoirCapacity = ReservoirCapacity,
                ReservoirSeed = ReservoirSeed,
                DiscountRate = DiscountRate,
                UnifiedModel = UnifiedModel,
                PlayerSpecificModels = PlayerSpecificModels?.DeepCopyForPlaybackOnly(),
                DecisionSpecificModels = DecisionSpecificModels?.DeepCopyForPlaybackOnly(),
                DeterminingBestResponseOfPlayer = DeterminingBestResponseOfPlayer
            };
        }

        private void InitializeModels()
        {
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

        public DeepCFRModel GetModel(Decision decision)
        {
            if (decision.IsChance)
                throw new Exception("Should only model non-chance decisions");
            return GetModel(decision.PlayerNumber, decision.DecisionByteCode, decision.Name);
        }

        public DeepCFRModel GetModel(byte playerNumber, byte decisionByteCode, string name) => Mode switch
        { 
            DeepCFRMultiModelMode.Unified => UnifiedModel,
            DeepCFRMultiModelMode.PlayerSpecific => PlayerSpecificModels.GetModel(playerNumber, () => $"Player {playerNumber}"),
            DeepCFRMultiModelMode.DecisionSpecific => DecisionSpecificModels.GetModel((playerNumber, decisionByteCode), () => $"Decision {name}"),
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

        public IEnumerable<DeepCFRModel> EnumerateModelsForPlayersBesides(byte playerIndex) => Mode switch
        {
            DeepCFRMultiModelMode.Unified => throw new NotSupportedException("Cannot enumerate per-player model when using unified models"), // thus, we would need to do switch to player-specific to do best response approximation with this
            DeepCFRMultiModelMode.PlayerSpecific => PlayerSpecificModels.EnumerateModelsWithKey().Where(x => x.key != playerIndex).Select(x => x.model),
            DeepCFRMultiModelMode.DecisionSpecific => DecisionSpecificModels.EnumerateModelsWithKey().Where(x => x.key.playerIndex != playerIndex).Select(x => x.model),
            _ => throw new NotImplementedException(),
        };

        public IEnumerable<(byte playerIndex, DeepCFRModel model)> EnumerateModelsWithPlayerInfo() => Mode switch
        {
            DeepCFRMultiModelMode.Unified => throw new NotSupportedException("Cannot enumerate per-player model when using unified models"), // thus, we would need to do switch to player-specific to do best response approximation with this
            DeepCFRMultiModelMode.PlayerSpecific => PlayerSpecificModels.EnumerateModelsWithKey().Select(x => (x.key, x.model)),
            DeepCFRMultiModelMode.DecisionSpecific => DecisionSpecificModels.EnumerateModelsWithKey().Select(x => (x.key.playerIndex, x.model)),
            _ => throw new NotImplementedException(),
        };

        #endregion

        #region Cached regression machine

        // helper method
        IEnumerable<TSource> DistinctBy<TSource, TKey>
(IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            HashSet<TKey> seenKeys = new HashSet<TKey>();
            foreach (TSource element in source)
            {
                if (seenKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }

        public Dictionary<byte, IRegressionMachine> GetRegressionMachinesForLocalUse(List<Decision> decisions)
        {

            var distinctDecisions = DistinctBy<Decision, byte>(decisions.Where(x => !x.IsChance), d => d.DecisionByteCode);
            Dictionary<byte, IRegressionMachine> result = distinctDecisions.Select(d => (d, GetModel(d))).ToDictionary(dm => dm.d.DecisionByteCode, dm => dm.Item2.GetRegressionMachine());
            return result;
        }

        public void ReturnRegressionMachines(List<Decision> decisions, Dictionary<byte, IRegressionMachine> machines)
        {
            var distinctDecisions = DistinctBy<Decision, byte>(decisions.Where(x => !x.IsChance), d => d.DecisionByteCode);
            foreach (Decision d in distinctDecisions)
            {
                GetModel(d).ReturnRegressionMachine(machines[d.DecisionByteCode]);
            }
        }

        #endregion

        #region Choosing actions

        public byte ChooseAction(Decision decision, IRegressionMachine regressionMachineForDecision, double randomValue, DeepCFRIndependentVariables independentVariables, byte maxActionValue, byte numActionsToSample, double probabilityUniformRandom, ref double[] onPolicyProbabilities)
        {
            var model = GetModel(decision);
            byte action = ChooseAction(model, regressionMachineForDecision, randomValue, independentVariables, maxActionValue, numActionsToSample, probabilityUniformRandom, ref onPolicyProbabilities);
            if (onPolicyProbabilities == null) // i.e., if we make decision off-policy, then we still want to return the probabilities
                onPolicyProbabilities = GetRegretMatchingProbabilities(independentVariables, decision, regressionMachineForDecision);
            return action;
        }

        private static byte ChooseAction(DeepCFRModel model, IRegressionMachine regressionMachine, double randomValue, DeepCFRIndependentVariables independentVariables, byte maxActionValue, byte numActionsToSample, double probabilityUniformRandom, ref double[] onPolicyProbabilities)
        {
            // turn one random draw into two independent random draws
            double rand1 = Math.Floor(randomValue * 10_000) / 10_000;
            double rand2 = (randomValue - rand1) * 10_000;
            if (rand1 < probabilityUniformRandom)
                return DeepCFRModel.ChooseActionAtRandom(rand2, maxActionValue);
            var result = model.ChooseAction(rand2, independentVariables, maxActionValue, numActionsToSample, regressionMachine, ref onPolicyProbabilities);
            if (result > numActionsToSample)
                throw new Exception("Internal error. Invalid action choice.");
            return result;
        }

        public double[] GetRegretMatchingProbabilities(DeepCFRIndependentVariables independentVariables, Decision decision, IRegressionMachine regressionMachineForDecision)
        {
            
            double[] regrets = GetExpectedRegretsForAllActions(independentVariables, decision, regressionMachineForDecision);
            double positiveRegretsSum = 0;
            for (byte a = 1; a <= decision.NumPossibleActions; a++)
                if (regrets?[a - 1] > 0)
                    positiveRegretsSum += regrets[a - 1];
            double[] probabilities = new double[decision.NumPossibleActions];
            if (positiveRegretsSum == 0)
            {
                double constantProbability = 1.0 / (double)decision.NumPossibleActions;
                for (byte a = 1; a <= decision.NumPossibleActions; a++)
                    probabilities[a - 1] = constantProbability;
            }
            else
            {
                for (byte a = 1; a <= decision.NumPossibleActions; a++)
                {
                    double regret = regrets[a - 1];
                    if (regret <= 0)
                        probabilities[a - 1] = 0;
                    else
                        probabilities[a - 1] = regret / positiveRegretsSum;
                }
            }
            return probabilities;
        }

        public double[] GetExpectedRegretsForAllActions(DeepCFRIndependentVariables independentVariables, Decision decision, IRegressionMachine regressionMachineForDecision)
        {
            var model = GetModel(decision);
            if (model.IterationsProcessed == 0)
                return null;
            double[] results = new double[decision.NumPossibleActions];
            for (byte a = 1; a <= decision.NumPossibleActions; a++)
                results[a - 1] = model.GetPredictedRegretForAction(independentVariables, a, regressionMachineForDecision);
            return results;
        }

        #endregion

        #region Pending observations

        public void AddPendingObservation(Decision decision, DeepCFRObservation observation)
        {
            var model = GetModel(decision);
            AddPendingObservation(model, observation);
        }

        public static void AddPendingObservation(DeepCFRModel model, DeepCFRObservation observation)
        {
            model.AddPendingObservation(observation);
        }

        public bool ObservationsNeeded(Decision decision)
        {
            var model = GetModel(decision);
            return model.ObservationsNeeded();
        }

        public int[] CountPendingObservationsTarget(int iteration) => EnumerateModels().Select(x => x.UpdateAndCountPendingObservationsTarget(iteration)).ToArray();


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

        #endregion

        public async Task CompleteIteration(bool parallel)
        {
            var models = EnumerateModels().Select((item, index) => (item, index)).ToArray();
            string[] text = new string[models.Length];
            if (parallel)
                await Parallelizer.ForEachAsync(models, async m =>
                {
                    text[m.Item2] = await m.Item1.CompleteIteration();
                });
            else
            {
                foreach (var (model, index) in models)
                    text[index] = await model.CompleteIteration();
            }
            foreach (string s in text)
            {
                var result = Regex.Split(s, "\r\n|\r|\n");
                foreach (string r in result)
                    if (s.Trim() != "")
                        TabbedText.WriteLine(r);
            }
        }

        #region Best response

        public void StartDeterminingBestResponse(byte playerIndex)
        {
            if (DeterminingBestResponseOfPlayer != null)
                throw new NotImplementedException("Already determining best response.");
            DeterminingBestResponseOfPlayer = playerIndex;
            foreach (DeepCFRModel model in EnumerateModelsForPlayer(playerIndex))
            {
                model.StartDeterminingBestResponse();
            }
            foreach (DeepCFRModel model in EnumerateModelsForPlayersBesides(playerIndex))
            {
                model.FreezeState();
            }
        }

        public async Task EndDeterminingBestResponse(byte playerIndex)
        {
            foreach (DeepCFRModel model in EnumerateModelsForPlayer(playerIndex))
            {
                await model.EndDeterminingBestResponse();
            }
            foreach (DeepCFRModel model in EnumerateModelsForPlayersBesides(playerIndex))
            {
                model.UnfreezeState();
            }
            DeterminingBestResponseOfPlayer = null;
        }

        #endregion
    }
}
