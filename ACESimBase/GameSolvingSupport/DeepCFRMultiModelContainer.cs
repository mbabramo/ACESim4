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

        int ReservoirCapacity;
        long ReservoirSeed;
        double DiscountRate;
        object LockObj = new object(); 
        Func<IRegression> RegressionFactory;

        public DeepCFRMultiModelContainer(DeepCFRMultiModelMode mode, int reservoirCapacity, long reservoirSeed, double discountRate, Func<IRegression> regressionFactory, List<Decision> decisions)
        {
            Mode = mode;
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
            RegressionFactory = regressionFactory;
            List<IGrouping<byte, (Decision item, int decisionIndex)>> groupedDecisions = decisions.Select((item, index) => (item, index)).Where(x => x.item.IsChance == false).GroupBy(x => GetModelGroupingKey(Mode, x.item, (byte)x.index)).ToList();
            ModelIndexForDecisionIndex = new int[decisions.Count];
            for (int groupedModelIndex = 0; groupedModelIndex < groupedDecisions.Count; groupedModelIndex++)
            {
                IGrouping<byte, (Decision item, int decisionIndex)> group = (IGrouping<byte, (Decision item, int decisionIndex)>)groupedDecisions[groupedModelIndex];
                foreach (var item in group)
                    ModelIndexForDecisionIndex[item.decisionIndex] = groupedModelIndex;
                var decisionsInGroup = group.Select(x => (x.item, (byte) x.decisionIndex)).ToList();
                Models[groupedModelIndex] = new DeepCFRModel(decisionsInGroup, ReservoirCapacity, ReservoirSeed, DiscountRate, RegressionFactory);
            }
        }

        public DeepCFRMultiModelContainer(DeepCFRMultiModelMode mode, int reservoirCapacity, long reservoirSeed, double discountRate, Func<IRegression> regressionFactory, IEnumerable<DeepCFRModel> models, int[] modelIndexForDecisionIndex)
        {
            Mode = mode;
            ReservoirCapacity = reservoirCapacity;
            ReservoirSeed = reservoirSeed;
            DiscountRate = discountRate;
            RegressionFactory = regressionFactory;
            Models = models.ToArray();
            ModelIndexForDecisionIndex = modelIndexForDecisionIndex.ToArray();
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
                DeepCFRMultiModelMode.PlayerSpecific => currentDecision.PlayerNumber,
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
    }
}
