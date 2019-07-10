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

        public override async Task<ReportCollection> RunAlgorithm(string reportName)
        {
            StrategiesDeveloperStopwatch.Reset();
            string filename = Path.Combine(FolderFinder.GetFolderToWriteTo("Strategies").FullName, EvolutionSettings.SerializeResultsPrefixPlus(GameDefinition.BaselineScenarioIndex, GameDefinition.NumScenariosToDevelop));
            if (EvolutionSettings.SerializeInformationSetDataOnly)
            {
                StrategySerialization.DeserializeInformationSets(InformationSets, filename);
            }
            else
                Strategies = StrategySerialization.DeserializeStrategies(filename).ToList();
            int correctCurrentScenarioIndex = GameDefinition.GetScenarioIndex(GameDefinition.BaselineScenarioIndex, false);
            if (correctCurrentScenarioIndex != GameDefinition.CurrentScenarioIndex)
            {
                ReinitializeForScenario(GameDefinition.BaselineScenarioIndex, false);
            }
            IterationNum = EvolutionSettings.ReportEveryNIterations ?? 0;
            ReportCollection reportCollection = null;
            bool reportForEachPastValue = true; // DEBUG
            if (reportForEachPastValue)
            {
                int num = EvolutionSettings.RecordPastValues_NumberToRecord;
                for (int i = 0; i < num; i++)
                {
                    foreach (var informationSet in InformationSets)
                        informationSet.SetAverageStrategyToPastValue(i);
                    var result = await GenerateReports(IterationNum, () => "Replayed report");
                    if (i == 0)
                        reportCollection = result;
                    else
                        reportCollection.Add(result);
                    foreach (var informationSet in InformationSets)
                        informationSet.RestoreBackup();
                }
            }
            else
                reportCollection = await GenerateReports(IterationNum, () => "Replayed report");

            return reportCollection;
        }

    }
}
