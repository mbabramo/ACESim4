using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public  class InformationSetNodesMutationPrep
    {
        public List<InformationSetNode> sourceInformationSets;
        public List<double[]> averageStrategies;
        public float[] scratchSpace;
        public List<byte>[] includedActionsMinus1;
        public List<int> numActionsToVary;
        public int totalActionsToVary;
        public double changeDiscounting;

        public InformationSetNodesMutationPrep(List<InformationSetNode> sourceInformationSets, double changeSizeScale)
        {
            this.sourceInformationSets = sourceInformationSets;
            this.changeDiscounting = changeSizeScale;
            averageStrategies = sourceInformationSets.Select(x => x.CalculateAverageStrategiesAsArray()).ToList();
            scratchSpace = new float[averageStrategies.Max(x => x.Length)];
            includedActionsMinus1 = averageStrategies
                .Select(x => x.Select((item, index) => (item, index))
                    .Where(y => y.item > MinValueToKeep)
                    .Select(y => (byte)y.index)
                    .ToList())
                .ToArray();
            numActionsToVary = includedActionsMinus1.Select(x => x.Count() > 1 ? x.Count() : 0).ToList();
            totalActionsToVary = numActionsToVary.Sum();
        }

        public float[] PrepareMutations(ConsistentRandomSequenceProducer r)
        {
            float[] conciseForm = new float[totalActionsToVary];
            int inputIndex = 0;
            for (int infoSetIndex = 0; infoSetIndex < sourceInformationSets.Count(); infoSetIndex++)
            {
                byte numActions = (byte)averageStrategies[infoSetIndex].Length;
                CopyNontrivialAverageStrategiesToScratch(infoSetIndex, numActions);
                int numActionsToVaryAtInfoSet = numActionsToVary[infoSetIndex];
                if (numActionsToVaryAtInfoSet > 0)
                {
                    for (int j = 0; j < numActionsToVaryAtInfoSet; j++)
                    {
                        byte a = includedActionsMinus1[infoSetIndex][j];
                        double valueAtIndex = scratchSpace[a];
                        bool up = r.NextDouble() > 0.5;
                        double magnitude = r.NextDouble() * changeDiscounting;
                        double revisedValue = (1.0 - magnitude) * valueAtIndex + (magnitude) * (up ? 1.0 : 0.0);
                        scratchSpace[a] = (float)revisedValue;
                    }
                    float total = 0;
                    for (int a = 0; a < numActions; a++)
                        total += scratchSpace[a];
                    for (int a = 0; a < numActions; a++)
                        scratchSpace[a] /= total;
                    for (int j = 0; j < numActionsToVaryAtInfoSet; j++)
                    {
                        byte a = includedActionsMinus1[infoSetIndex][j];
                        float valueAtIndex = scratchSpace[a];
                        conciseForm[inputIndex++] = valueAtIndex;
                    }
                }
            }
            return conciseForm;
        }

        public const double MinValueToKeep = 0.01;

        private void CopyNontrivialAverageStrategiesToScratch(int infoSetIndex, byte numActions)
        {
            for (int a = 0; a < numActions; a++)
            {
                scratchSpace[a] = (float)averageStrategies[infoSetIndex][a];
                if (scratchSpace[a] < MinValueToKeep)
                    scratchSpace[a] = 0;
            }
        }

        public void ImplementMutations(float[] conciseForm)
        {
            int conciseFormIndex = 0;
            for (int infoSetIndex = 0; infoSetIndex < sourceInformationSets.Count(); infoSetIndex++)
            {
                InformationSetNode informationSet = sourceInformationSets[infoSetIndex];
                byte numActions = (byte)averageStrategies[infoSetIndex].Length;
                CopyNontrivialAverageStrategiesToScratch(infoSetIndex, numActions);
                int numActionsToVaryAtInfoSet = numActionsToVary[infoSetIndex];
                if (numActionsToVaryAtInfoSet > 0)
                {
                    byte nextActionToSet = 0;
                    for (byte j = 0; j < numActionsToVaryAtInfoSet; j++)
                    {
                        // find next action in concise form
                        byte a = (byte) (includedActionsMinus1[infoSetIndex][j] + 1);
                        // set skipped actions to 0
                        while (nextActionToSet < j)
                            informationSet.SetAverageStrategyForAction(nextActionToSet++, 0);
                        // set next action in concise form in information set
                        float revisedValue = conciseForm[conciseFormIndex++];
                        informationSet.SetAverageStrategyForAction(a, revisedValue);
                        nextActionToSet = (byte) (j + 1);
                    }
                }
                // normalize
                double total = 0;
                for (byte j = 1; j <= numActions; j++)
                    total += informationSet.GetAverageStrategy(j);
                for (byte j = 1; j <= numActions; j++)
                    informationSet.SetAverageStrategyForAction(j, informationSet.GetAverageStrategy(j)/total);
            }
        }
    }
}
