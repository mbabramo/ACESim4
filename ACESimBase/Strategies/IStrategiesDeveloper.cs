using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACESim
{
    public interface IStrategiesDeveloper : ISerializationPrep
    {
        List<Strategy> Strategies { get; set; }
        IStrategiesDeveloper DeepCopy();
        Task<string> DevelopStrategies(string reportName);
        EvolutionSettings EvolutionSettings { get; set; }
    }
}
