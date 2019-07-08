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
            string filename = Path.Combine(FolderFinder.GetFolderToWriteTo("Strategies").FullName, EvolutionSettings.SerializeResultsPrefixPlus(GameDefinition.CurrentScenarioIndex, GameDefinition.NumScenariosToDevelop));
            if (EvolutionSettings.SerializeInformationSetDataOnly)
            {
                StrategySerialization.DeserializeInformationSets(InformationSets, filename);
            }
            else
                Strategies = StrategySerialization.DeserializeStrategies(filename).ToList();
            IterationNum = EvolutionSettings.ReportEveryNIterations ?? 0;
            ReportCollection reportCollection = await GenerateReports(IterationNum, () => "Replayed report");

            return reportCollection;
        }

    }
}
