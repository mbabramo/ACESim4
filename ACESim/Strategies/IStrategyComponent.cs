using System;

namespace ACESim
{
    public interface IStrategyComponent : ISerializationPrep
    {
        double CalculateOutputForInputs(System.Collections.Generic.List<double> inputs);
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
