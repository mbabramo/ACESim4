using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRMultiModelContainer
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
            List<IGrouping<byte, (Decision item, int decisionIndex)>> groupedDecisions = decisions.Select((item, index) => (item, index)).GroupBy(x => GetModelGroupingKey(Mode, x.item, (byte)x.index)).ToList();
            ModelIndexForDecisionIndex = new int[decisions.Count];
            for (int groupedModelIndex = 0; groupedModelIndex < groupedDecisions.Count; groupedModelIndex++)
            {
                IGrouping<byte, (Decision item, int decisionIndex)> group = (IGrouping<byte, (Decision item, int decisionIndex)>)groupedDecisions[groupedModelIndex];
                foreach (var item in group)
                    ModelIndexForDecisionIndex[item.decisionIndex] = groupedModelIndex;
                var decisionsInGroup = group.ToArray();
                List<string> modelNames = decisionsInGroup.Select(x => x.item.Name).ToHashSet().OrderBy(x => x).ToList();
                List<byte> playerNumbers = decisionsInGroup.Select(x => x.item.PlayerNumber).ToHashSet().OrderBy(x => x).ToList();
                List<byte> decisionByteCodes = decisionsInGroup.Select(x => x.item.DecisionByteCode).ToHashSet().OrderBy(x => x).ToList();
                List<byte> decisionIndices = decisionsInGroup.Select(x => (byte) x.decisionIndex).ToHashSet().OrderBy(x => x).ToList();
                Models[groupedModelIndex] = new DeepCFRModel(playerNumbers, modelNames, decisionByteCodes, decisionIndices, ReservoirCapacity, ReservoirSeed, DiscountRate, RegressionFactory);
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

        public DeepCFRModel this[byte decisionIndex] => Models[ModelIndexForDecisionIndex[decisionIndex]];
    }
}
