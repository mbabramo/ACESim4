using System;
using System.Collections.Generic;

namespace ACESim
{
    public interface IStrategiesDeveloper : ISerializationPrep
    {
        List<Strategy> Strategies { get; set; }
        IStrategiesDeveloper DeepCopy();
        void DevelopStrategies();
        EvolutionSettings EvolutionSettings { get; set; }
    }
}
