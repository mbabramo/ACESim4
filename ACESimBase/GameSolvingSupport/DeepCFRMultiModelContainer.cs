using ACESim;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRMultiModelContainer : IEnumerable<DeepCFRModel>
    {
        DeepCFRMultiModelMode Mode;
        private DeepCFRModel[] Models;
        public int[] ModelIndexForDecisionIndex;

        int[] ReservoirCapacity;
        long ReservoirSeed;
        double DiscountRate;
        object LockObj = new object(); 
        Func<IRegression> RegressionFactory;

        public DeepCFRMultiModelContainer(DeepCFRMultiModelMode mode, int[] reservoirCapacity, long reservoirSeed, double discountRate, Func<IRegression> regressionFactory, List<Decision> decisions)
        {
            Mode = mode;
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
            RegressionFactory = regressionFactory;
            ModelIndexForDecisionIndex = new int[decisions.Count];
            for (int i = 0; i < decisions.Count; i++)
                ModelIndexForDecisionIndex[i] = 255; // indicate emptiness
            List<IGrouping<byte, (Decision item, int decisionIndex)>> groupedDecisions = decisions.Select((item, index) => (item, index)).Where(x => !x.item.IsChance).GroupBy(x => GetModelGroupingKey(Mode, x.item, (byte)x.index)).ToList();
            Models = new DeepCFRModel[groupedDecisions.Count];
            for (int groupedModelIndex = 0; groupedModelIndex < groupedDecisions.Count; groupedModelIndex++)
            {
                IGrouping<byte, (Decision item, int decisionIndex)> group = (IGrouping<byte, (Decision item, int decisionIndex)>)groupedDecisions[groupedModelIndex];
                int? reservoirCapacityForDecisionIndex = null;
                foreach (var item in group)
                {
                    ModelIndexForDecisionIndex[item.decisionIndex] = groupedModelIndex;
                    if (reservoirCapacityForDecisionIndex == null)
                        reservoirCapacityForDecisionIndex = reservoirCapacity[item.decisionIndex];
                    else if (reservoirCapacityForDecisionIndex != reservoirCapacity[item.decisionIndex])
                        throw new Exception("If not grouping decisions by decision index, then each decision must have same reservoir capacity.");
                }
                var decisionsInGroup = group.Select(x => (x.item, (byte) x.decisionIndex)).ToList();
                Models[groupedModelIndex] = new DeepCFRModel(decisionsInGroup, (int) reservoirCapacityForDecisionIndex, ReservoirSeed, DiscountRate, RegressionFactory);
            }
        }

        public DeepCFRMultiModelContainer(DeepCFRMultiModelMode mode, int[] reservoirCapacity, long reservoirSeed, double discountRate, Func<IRegression> regressionFactory, IEnumerable<DeepCFRModel> models, int[] modelIndexForDecisionIndex)
        {
            Mode = mode;
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
            RegressionFactory = regressionFactory;
            Models = models.ToArray();
            ModelIndexForDecisionIndex = modelIndexForDecisionIndex.ToArray();
        }

        public DeepCFRMultiModelContainer DeepCopyObservationsOnly(byte? limitToPlayerIndex)
        {
            var models = EnumerateModels().Select(x =>
            {
                if (limitToPlayerIndex == null || x.PlayerIndices.Contains((byte) limitToPlayerIndex))
                    return x.DeepCopyObservationsOnly();
                return null;
            }).ToArray();
            var result = new DeepCFRMultiModelContainer(Mode, ReservoirCapacity, ReservoirSeed, DiscountRate, RegressionFactory, models, ModelIndexForDecisionIndex);
            return result;
        }

        public DeepCFRMultiModelContainer DeepCopyForPlaybackOnly()
        {
            var models = EnumerateModels().Select(x => x.DeepCopyForPlaybackOnly()).ToArray();
            var result = new DeepCFRMultiModelContainer(Mode, ReservoirCapacity, ReservoirSeed, DiscountRate, RegressionFactory, models, ModelIndexForDecisionIndex);
            return result;
        }

        private static byte GetModelGroupingKey(DeepCFRMultiModelMode mode, Decision currentDecision, byte decisionIndex)
        {
            return mode switch
            {
                DeepCFRMultiModelMode.Unified => (byte)0,
                DeepCFRMultiModelMode.PlayerSpecific => currentDecision.PlayerIndex,
                DeepCFRMultiModelMode.DecisionTypeSpecific => currentDecision.DecisionByteCode,
                DeepCFRMultiModelMode.DecisionSpecific => decisionIndex,
                _ => throw new NotImplementedException()
            };
        }

        public IEnumerable<DeepCFRModel> EnumerateModels() => Models;

        public IEnumerator<DeepCFRModel> GetEnumerator()
        {
            foreach (var model in Models)
                yield return model;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public DeepCFRModel this[byte decisionIndex] => Models[ModelIndexForDecisionIndex[decisionIndex]];

        public void IntegrateOtherContainer(DeepCFRMultiModelContainer other)
        {
            int numModels = Models.Length;
            if (other.Models.Length != numModels)
                throw new Exception("Inconsistent number of models");
            for (byte i = 0; i < numModels; i++)
                if (other.Models[i] != null)
                {
                    //Because the following is commented out, the other container will take precedence over this one.
                    //if (Models[i] != null)
                    //    throw new Exception("Both contain same model.");
                    Models[i] = other.Models[i];
                }
        }

        public List<byte> GetDecisionIndices()
        {
            HashSet<byte> hs = new HashSet<byte>();
            foreach (var model in Models)
                foreach (byte decisionIndex in model.DecisionIndices)
                    hs.Add(decisionIndex);
            return hs.OrderBy(x => x).ToList();
        }
    }
}
