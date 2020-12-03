using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACESim
{
    public interface IStrategiesDeveloper : ISerializationPrep
    {
        List<Strategy> Strategies { get; set; }
        IStrategiesDeveloper DeepCopy();
        Task<ReportCollection> DevelopStrategies(string optionSetName, int? restrictToScenarioIndex, string masterReportName);
        Task<ReportCollection> RunAlgorithm(string optionSetName);
        EvolutionSettings EvolutionSettings { get; set; }

        GameDefinition GameDefinition { get; set; }

        IGameFactory GameFactory { get; set; }

        GamePlayer GamePlayer { get; set; }
        HistoryNavigationInfo Navigation { get; set; }
        InformationSetLookupApproach LookupApproach { get; set; }

        void Reinitialize();
    }
}
