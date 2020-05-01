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
        int[] ReservoirCapacity;
        long ReservoirSeed;
        double DiscountRate;

        public DeepCFRMultiModelContainer Models = null;

        public List<Decision> Decisions;

        /// <summary>
        /// Factory to create a regression processor
        /// </summary>
        Func<IRegression> RegressionFactory;

        #region Model initialization and access

        public DeepCFRMultiModel()
        {

        }

        public DeepCFRMultiModel(List<Decision> decisionsInExecutionOrder, DeepCFRMultiModelMode mode, int[] reservoirCapacity, long reservoirSeed, double discountRate, Func<IRegression> regressionFactory)
        {
            Mode = mode;
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
            RegressionFactory = regressionFactory;
            Decisions = decisionsInExecutionOrder;
            if (ReservoirCapacity.Count() != decisionsInExecutionOrder.Count())
                throw new Exception();
            Models = new DeepCFRMultiModelContainer(Mode, ReservoirCapacity, ReservoirSeed, DiscountRate, RegressionFactory, decisionsInExecutionOrder);
        }

        public DeepCFRMultiModel DeepCopyForPlaybackOnly()
        {
            return new DeepCFRMultiModel()
            {
                Mode = Mode,
                ReservoirCapacity = ReservoirCapacity.ToArray(),
                ReservoirSeed = ReservoirSeed,
                DiscountRate = DiscountRate,
                Models = Models?.DeepCopyForPlaybackOnly(),
            };
        }

        public DeepCFRModel GetModelForDecisionIndex(byte decisionIndex) => Models[decisionIndex];

        public IEnumerable<DeepCFRModel> EnumerateModels() => Models;

        public IEnumerable<DeepCFRModel> FilterModels(byte playerIndex, byte? decisionIndex) => Models.Where(x => x.PlayerNumbers.Contains(playerIndex) && (decisionIndex == null || x.DecisionIndices.Contains((byte) decisionIndex)));

        public IEnumerable<DeepCFRModel> FilterModelsExcept(byte playerIndex, byte? decisionIndex) => Models.Where(x => !(x.PlayerNumbers.Contains(playerIndex) && (decisionIndex == null || x.DecisionIndices.Contains((byte)decisionIndex))));

        #endregion

        #region Cached regression machine

        public Dictionary<byte, IRegressionMachine> GetRegressionMachinesForLocalUse()
        {
            return Models.GetDecisionIndices().ToDictionary(x => x, x => Models[x].GetRegressionMachine());
        }

        public void ReturnRegressionMachines(Dictionary<byte, IRegressionMachine> regressionMachineDictionary)
        {
            foreach (var dictionaryEntry in regressionMachineDictionary)
                Models[dictionaryEntry.Key].ReturnRegressionMachine(dictionaryEntry.Value);
        }

        #endregion

        #region Choosing actions

        public byte ChooseAction(Decision decision, byte decisionIndex, IRegressionMachine regressionMachineForDecision, double randomValue, DeepCFRIndependentVariables independentVariables, byte maxActionValue, byte numActionsToSample, double probabilityUniformRandom, ref double[] onPolicyProbabilities)
        {
            var model = GetModelForDecisionIndex(decisionIndex);
            byte action = ChooseAction(model, regressionMachineForDecision, randomValue, independentVariables, maxActionValue, numActionsToSample, probabilityUniformRandom, ref onPolicyProbabilities);
            if (onPolicyProbabilities == null) // i.e., if we make decision off-policy, then we still want to return the probabilities
                onPolicyProbabilities = GetRegretMatchingProbabilities(decision, decisionIndex, independentVariables, regressionMachineForDecision);
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

        public double[] GetRegretMatchingProbabilities(Decision decision, byte decisionIndex, DeepCFRIndependentVariables independentVariables,  IRegressionMachine regressionMachineForDecision)
        {
            double[] regrets = GetExpectedRegretsForAllActions(decision, decisionIndex, independentVariables,  regressionMachineForDecision);
            double[] probabilities = new double[decision.NumPossibleActions];
            var model = GetModelForDecisionIndex(decisionIndex);
            if (regrets != null && (EvolutionSettings.DeepCFR_PredictUtilitiesNotRegrets || model.AlwaysChooseBestOption))
            {
                // AlwaysChooseBestOption: We have temporarily disabled regret matching for this model (as we may do with accelerated best response).
                // Just choose the best action.
                byte bestAction = 0;
                double bestRegrets = regrets[0];
                probabilities[0] = 1.0; 
                for (byte r = 1; r < regrets.Length; r++)
                {
                    if (regrets[r] > bestRegrets)
                    {
                        probabilities[bestAction] = 0; // previous best action no longer chosen
                        probabilities[r] = 1.0;
                        bestRegrets = regrets[r];
                        bestAction = r;
                    }
                    else
                        probabilities[r] = 0;
                }
                return probabilities;
            }
            double positiveRegretsSum = 0;
            for (byte a = 1; a <= decision.NumPossibleActions; a++)
                if (regrets?[a - 1] > 0)
                    positiveRegretsSum += regrets[a - 1];
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

        public double[] GetExpectedRegretsForAllActions(Decision decision, byte decisionIndex, DeepCFRIndependentVariables independentVariables, IRegressionMachine regressionMachineForDecision)
        {
            var model = GetModelForDecisionIndex(decisionIndex);
            if (model.IterationsProcessed == 0)
                return null;
            double[] results = new double[decision.NumPossibleActions];
            for (byte a = 1; a <= decision.NumPossibleActions; a++)
                results[a - 1] = model.GetPredictedRegretForAction(independentVariables, a, regressionMachineForDecision);
            return results;
        }

        #endregion

        #region Pending observations

        public void AddPendingObservation(Decision decision, byte decisionIndex, DeepCFRObservation observation)
        {
            var model = GetModelForDecisionIndex(decisionIndex);
            AddPendingObservation(model, observation);
        }

        public static void AddPendingObservation(DeepCFRModel model, DeepCFRObservation observation)
        {
            model.AddPendingObservation(observation);
        }

        public bool ObservationsNeeded(byte decisionIndex)
        {
            var model = GetModelForDecisionIndex(decisionIndex);
            return model.ObservationsNeeded();
        }


        public int[] CountPendingObservationsTarget(int iteration, bool isBestResponseIteration, bool includeChanceDecisions)
        {
            if (iteration == 1 && !isBestResponseIteration)
            {
                if (includeChanceDecisions)
                    return ReservoirCapacity;
                throw new NotImplementedException();
            }
            int[] resultWithoutChanceDecisions = EnumerateModels().Select(x => x.UpdateAndCountPendingObservationsTarget(iteration)).ToArray();
            if (includeChanceDecisions)
            {
                int[] resultWithChanceDecisions = new int[Decisions.Count()];
                int resultIndex = 0;
                for (int decisionIndex = 0; decisionIndex < Decisions.Count(); decisionIndex++)
                {
                    if (Decisions[decisionIndex].IsChance == false)
                        resultWithChanceDecisions[decisionIndex] = resultWithoutChanceDecisions[resultIndex++];
                }
                return resultWithChanceDecisions;
            }
            else
                return resultWithoutChanceDecisions;
        }

        public bool AllMeetPendingObservationsTarget(int[] target)
        {
            bool result = EnumerateModels()
                .Select(x => x.PendingObservations.Count())
                .Zip(target, (poc, t) => poc >= t)
                .All(x => x == true);
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
                    if (r.Trim() != "")
                        TabbedText.WriteLine(r);
            }
        }

        #region Best response

        public async Task PrepareForBestResponseIterations(bool doParallel)
        {
            var models = EnumerateModels().ToList();
            await Parallelizer.GoAsync(doParallel, 0, models.Count(), m =>
            {
                var model = models[(int) m];
                return model.PrepareForBestResponseIterations();
            });
        }

        public async Task ReturnToStateBeforeBestResponseIterations(bool doParallel)
        {
            var models = EnumerateModels().ToList();
            await Parallelizer.GoAsync(doParallel, 0, models.Count(), m =>
            {
                var model = models[(int)m];
                return model.ReturnToStateBeforeBestResponseIterations();
            });
        }

        public void StopRegretMatching(byte playerIndex, byte? decisionIndex)
        {
            foreach (DeepCFRModel model in FilterModels(playerIndex, decisionIndex))
            {
                model.StopRegretMatching();
            }
        }

        public void ResumeRegretMatching()
        {
            foreach (DeepCFRModel model in EnumerateModels())
            {
                model.StopRegretMatching();
            }
        }

        public void TargetBestResponse(byte playerIndex, byte? decisionIndex)
        {
            foreach (DeepCFRModel model in FilterModels(playerIndex, decisionIndex))
            {
                model.UnfreezeState();
            }
        }

        public void ConcludeTargetingBestResponse(byte playerIndex, byte? decisionIndex)
        {
            foreach (DeepCFRModel model in FilterModels(playerIndex, decisionIndex))
            {
                model.FreezeState();
            }
        }

        #endregion
    }
}
