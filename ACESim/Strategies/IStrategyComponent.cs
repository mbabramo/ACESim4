using System;
using System.Collections.Generic;

namespace ACESim
{
    public interface IStrategyComponent : ISerializationPrep
    {
        Strategy OverallStrategy { get; set; }
        IStrategyComponent DeepCopy();
        void DevelopStrategyComponent();
        EvolutionSettings EvolutionSettings { get; set; }
        string Name { get; set; }
    }
}
