namespace ACESim
{
    public enum GameApproximationAlgorithm
    {
        RegretMatching, // primary for GeneralizeVanilla
        DeepCFR,
        SequenceForm,
        MultiplicativeWeights,
        PureStrategyFinder,
        FictitiousPlay,
        BestResponseDynamics,
        GreedyFictitiousPlay,
        GeneticAlgorithm,
        Vanilla,
        GibsonProbing,
        ModifiedGibsonProbing,
        AverageStrategySampling,
        PlaybackOnly,
    }
}