using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ChangeSimulationSettingContainer : IChangeSimulationSettingPermutator
    {
        public bool IsConcurrent = false;
        public List<ChangeSimulationSettings> ChangeSimulationSettingsList;
        public List<ChangeSimulationSettingGenerator> ChangeSimulationSettingGeneratorList;
        public List<ChangeSimulationSettingContainer> ChangeSimulationSettingContainerList;

        public List<List<Setting>> GenerateAll()
        {

            List<List<List<Setting>>> fromChangeSimulation = ChangeSimulationSettingsList.Select(x => x.GenerateAll()).ToList();
            List<List<List<Setting>>> fromChangeSimulationGenerator = ChangeSimulationSettingGeneratorList.Select(x => x.GenerateAll()).ToList();
            List<List<List<Setting>>> fromChangeSimulationContainer = ChangeSimulationSettingContainerList.Select(x => x.GenerateAll()).ToList();

            List<List<List<Setting>>> combinedLists = new List<List<List<Setting>>>();
            combinedLists.AddRange(fromChangeSimulation);
            combinedLists.AddRange(fromChangeSimulationGenerator);
            combinedLists.AddRange(fromChangeSimulationContainer);

            if (IsConcurrent)
            {
                List<List<Setting>> crossProduct = new List<List<Setting>>();
                // Note that each list within the combinedLists is a set of consecutive simulations to run.
                // We need to get one List<setting> from each of the combined lists, for all permutations.
                List<int> numberInEachDoubleList = combinedLists.Select(x => x.Count).ToList();
                List<List<int>> permutations = GetPermutations(numberInEachDoubleList);
                foreach (List<int> permutation in permutations)
                {
                    List<Setting> singleExecution = new List<Setting>();
                    for (int p = 0; p < permutation.Count(); p++)
                    {
                        singleExecution.AddRange(combinedLists[p][permutation[p] - 1]);
                    }
                    crossProduct.Add(singleExecution);
                }
                return crossProduct;
            }
            else
            {
                List<List<Setting>> consecutiveLists = new List<List<Setting>>();
                foreach (var x in combinedLists)
                    consecutiveLists.AddRange(x);
                return consecutiveLists;
            }
        }

        private List<List<int>> GetPermutations(List<int> listOfNumbersToPickFrom)
        {
            return PermutationMaker.GetPermutations(listOfNumbersToPickFrom);
        }

    }
}
