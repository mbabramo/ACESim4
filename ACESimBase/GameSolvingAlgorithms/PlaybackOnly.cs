using ACESimBase.Util;
using ACESimBase.Util.ArrayProcessing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public partial class PlaybackOnly : StrategiesDeveloperBase
    {

        public PlaybackOnly(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new PlaybackOnly(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            StrategiesDeveloperStopwatch.Reset();
            string path = FolderFinder.GetFolderToWriteTo("Strategies").FullName;
            string filename = GameDefinition.OptionSetName + "-" + EvolutionSettings.SerializeResultsPrefixPlus(GameDefinition.CurrentOverallScenarioIndex, GameDefinition.NumScenarioPermutations);
            if (EvolutionSettings.SerializeInformationSetDataOnly)
            {
                StrategySerialization.DeserializeInformationSets(InformationSets, path, filename, EvolutionSettings.SaveToAzureBlob);
            }
            else
                Strategies = StrategySerialization.DeserializeStrategies(path, filename, EvolutionSettings.SaveToAzureBlob).ToList();
            var correctScenarioInfo = GameDefinition.GetScenarioIndexAndWeightValues(GameDefinition.CurrentOverallScenarioIndex, false);
            if (correctScenarioInfo.indexInPostWarmupScenarios != GameDefinition.CurrentOverallScenarioIndex)
            {
                ReinitializeForScenario(correctScenarioInfo.indexInPostWarmupScenarios, false);
            }
            Status.IterationNum = EvolutionSettings.ReportEveryNIterations ?? 0;
            ReportCollection reportCollection = null;
            bool reportForEachPastValue = false;
            if (reportForEachPastValue)
            {
                int num = EvolutionSettings.RecordPastValues_NumberToRecord;
                for (int i = 0; i < num; i++)
                {
                    foreach (var informationSet in InformationSets)
                        informationSet.SetAverageStrategyToPastValue(i);
                    var result = await GenerateReports(Status.IterationNum, () => "Replayed report");
                    if (i == 0)
                        reportCollection = result;
                    else
                        reportCollection.Add(result);
                    foreach (var informationSet in InformationSets)
                        informationSet.RestoreBackup();
                }
            }
            else
                reportCollection = await GenerateReports(Status.IterationNum, () => "Replayed report");

            return reportCollection;
        }

    }
}
