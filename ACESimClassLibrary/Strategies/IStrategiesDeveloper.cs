using System;
using System.Collections.Generic;

namespace ACESim
{
    public interface IStrategiesDeveloper : ISerializationPrep
    {
        List<Strategy> Strategies { get; set; }
        IStrategiesDeveloper DeepCopy();
        string DevelopStrategies(string reportName);
        EvolutionSettings EvolutionSettings { get; set; }
    }
}
