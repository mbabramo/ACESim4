using ACESim;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        bool UsingShortcutForSymmetricGames;

        public DeepCFRMultiModelContainer Models = null;
        public GameDefinition GameDefinition;
        public List<Decision> Decisions;

        /// <summary>
        /// Factory to create a regression processor
        /// </summary>
        Func<IRegression> RegressionFactory;

        #region Model initialization and access

        public DeepCFRMultiModel()
        {

        }

        public DeepCFRMultiModel(GameDefinition gameDefinition, DeepCFRMultiModelMode mode, int[] reservoirCapacity, long reservoirSeed, double discountRate, bool usingShortcutForSymmetricGames, Func<IRegression> regressionFactory)
        {
            Mode = mode;
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
            UsingShortcutForSymmetricGames = usingShortcutForSymmetricGames;
            RegressionFactory = regressionFactory;
            GameDefinition = gameDefinition;
            Decisions = gameDefinition.DecisionsExecutionOrder.ToList();
            if (ReservoirCapacity.Count() != Decisions.Count())
                throw new Exception();
            Models = new DeepCFRMultiModelContainer(Mode, ReservoirCapacity, ReservoirSeed, DiscountRate, RegressionFactory, Decisions);
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

        public DeepCFRMultiModel DeepCopyObservationsOnly(byte? limitToPlayerIndex)
        {
            return new DeepCFRMultiModel()
            {
                Mode = Mode,
                ReservoirCapacity = ReservoirCapacity.ToArray(),
                ReservoirSeed = ReservoirSeed,
                DiscountRate = DiscountRate,
                Models = Models?.DeepCopyObservationsOnly(limitToPlayerIndex),
            };
        }

        public void IntegrateOtherMultiModel(DeepCFRMultiModel other)
        {
            Models.IntegrateOtherContainer(other.Models);
        }

        public DeepCFRModel GetModelForDecisionIndex(byte decisionIndex) => Models[decisionIndex];

        public IEnumerable<DeepCFRModel> EnumerateModels(byte? playerIndex = null, byte? decisionIndex = null) => playerIndex == null ? Models : FilterModels((byte) playerIndex, decisionIndex);

        public IEnumerable<DeepCFRModel> FilterModels(byte playerIndex, byte? decisionIndex) => Models.Where(x => x != null && x.PlayerNumbers.Contains(playerIndex) && (decisionIndex == null || x.DecisionIndices.Contains((byte) decisionIndex)));

        public IEnumerable<DeepCFRModel> FilterModelsExcept(byte playerIndex, byte? decisionIndex) => Models.Where(x => x != null && !(x.PlayerNumbers.Contains(playerIndex) && (decisionIndex == null || x.DecisionIndices.Contains((byte)decisionIndex))));

        #endregion

        #region Cached regression machine

        public IRegressionMachine GetParticularRegressionMachineForLocalUse(byte decisionIndex) => Models.First(x => x.DecisionIndices.Contains(decisionIndex)).GetRegressionMachine();

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
            if (UsingShortcutForSymmetricGames && decision.PlayerIndex == 1)
            {
                var symmetricIndependentVariables = independentVariables.DeepCopyWithSymmetricInformationSet(GameDefinition);
                byte player0DecisionIndex = (byte) (decisionIndex - 1);
                Decision player0Decision = GameDefinition.DecisionsExecutionOrder[player0DecisionIndex];
                byte actionFromEarlierDecision = ChooseAction(player0Decision, player0DecisionIndex, regressionMachineForDecision /* NOTE: This is the regression machine for player 0 decision if this is symmetric */, randomValue, symmetricIndependentVariables, maxActionValue, numActionsToSample, probabilityUniformRandom, ref onPolicyProbabilities);
                bool reverse = decision.SymmetryMap.decision == SymmetryMapOutput.ReverseAction;
                byte actionToChoose;
                if (reverse)
                    actionToChoose = (byte)(decision.NumPossibleActions - actionFromEarlierDecision + 1);
                else
                    actionToChoose = actionFromEarlierDecision;
                return actionToChoose;
            }
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

        public double[] GetRegretMatchingProbabilities(Decision decision, byte decisionIndex, DeepCFRIndependentVariables independentVariables, IRegressionMachine regressionMachineForDecision)
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
            if (UsingShortcutForSymmetricGames && decision.PlayerIndex == 1)
            {
                double[] previous = GetExpectedRegretsForAllActions(GameDefinition.DecisionsExecutionOrder[decisionIndex - 1], (byte) (decisionIndex - 1), independentVariables.DeepCopyWithSymmetricInformationSet(GameDefinition), regressionMachineForDecision /* will be for correct decision */);
                if (decision.SymmetryMap.decision == SymmetryMapOutput.ReverseAction)
                    previous = previous?.Reverse().ToArray();
                return previous;
            }
            var model = GetModelForDecisionIndex(decisionIndex);
            if (model.IterationsProcessed == 0)
                return null;
            double[] results = new double[decision.NumPossibleActions];
            for (byte a = 1; a <= decision.NumPossibleActions; a++)
                results[a - 1] = model.GetPredictedRegretForAction(independentVariables, a, regressionMachineForDecision);
            return results;
        }

        /// <summary>
        /// /// This produces genotypes based on a baseline multimodel. That is, it uses the observations that were used
        /// to generate the baseline multimodel and it calculates the regrets that would obtain in the current multimodel.
        /// Thus, any given multimodel can be typed as a series of numbers. We can then reduce the dimensionality of these
        /// numbers, assuming that we have a fair number of genotypes (but possibly far fewer than the number of
        /// observations in the baseline multimodel), by using a tool such as Principal Components Analysis.
        /// </summary>
        /// <param name="baseline"></param>
        /// <param name="numNonChancePlayers"></param>
        /// <returns></returns>
        public float[][] GetGenotypes(DeepCFRMultiModel baseline, byte numNonChancePlayers)
        {
            float[][] result = new float[numNonChancePlayers][];
            for (byte p = 0; p < numNonChancePlayers; p++)
                result[p] = GetGenotype(baseline, p);
            return result;
        }

        /// <summary>
        /// Returns a genotype for a single player.
        /// </summary>
        /// <param name="baseline"></param>
        /// <returns></returns>
        private float[] GetGenotype(DeepCFRMultiModel baseline, byte? playerIndex)
        {
            return GetExpectedRegretsForMultiModelBaseline(baseline, playerIndex).Select(x => (float)x).ToArray();
        }

        private IEnumerable<double> GetExpectedRegretsForMultiModelBaseline(DeepCFRMultiModel baseline, byte? playerIndex)
        {
            var matchedModels = EnumerateModels(playerIndex).Zip(baseline.EnumerateModels(playerIndex), (currentModel, baselineModel) => (currentModel, baselineModel)).ToList();
            // TODO: Improve performance by parallelizing, doing each model on a separate thread
            foreach (var match in matchedModels)
            {
                IEnumerable<double> expectedRegrets = match.currentModel.GetPredictedRegretForObservations(match.baselineModel.Observations);
                foreach (double expectedRegret in expectedRegrets)
                    yield return expectedRegret;
            }
        }

        public void SetExpectedRegretsForObservations(byte? playerIndex, double[] regrets)
        {
            int regretIndex = 0;
            var models = EnumerateModels(playerIndex);
            foreach (var model in models)
            {
                var observations = model.Observations;
                foreach (var observation in observations)
                {
                    observation.SampledRegret = regrets[regretIndex++];
                }
            }
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
                else
                    return ReservoirCapacity.Select((item, index) => (item, index)).Where(x => Decisions[x.index].IsChance == false).Select(x => x.item).ToArray();

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

        public Task CompleteIteration(bool parallel) => ProcessObservations(true, parallel);

        public async Task ProcessObservations(bool addPendingObservations, bool parallel)
        {
            var models = EnumerateModels().Where(x => x != null).Select((item, index) => (item, index)).ToArray();
            string[] text = new string[models.Length];
            if (parallel)
                await Parallelizer.ForEachAsync(models, async m =>
                {
                    text[m.Item2] = await m.Item1.ProcessObservations(addPendingObservations);
                });
            else
            {
                foreach (var (model, index) in models)
                    text[index] = await model.ProcessObservations(addPendingObservations);
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

        public async Task PrepareForBestResponseIterations(bool doParallel, double capacityMultiplier)
        {
            var models = EnumerateModels().ToList();
            await Parallelizer.GoAsync(doParallel, 0, models.Count(), m =>
            {
                var model = models[(int) m];
                return model.PrepareForBestResponseIterations(capacityMultiplier);
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
                model.ResumeRegretMatching();
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
