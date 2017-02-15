using System;
using System.Collections.Generic;

namespace ACESim
{
    public interface IStrategyComponent : ISerializationPrep
    {
        double CalculateOutputForInputs(List<double> inputs);
        Decision Decision { get; set; }
        IStrategyComponent DeepCopy();
        void DevelopStrategyComponent();
        int Dimensions { get; set; }
        EvolutionSettings EvolutionSettings { get; set; }
        bool InitialDevelopmentCompleted { get; set; }
        bool IsCurrentlyBeingDeveloped { get; set; }
        string Name { get; set; }
        Strategy OverallStrategy { get; set; }
    }
}
