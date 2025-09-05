namespace ACESim
{
    public enum GameApproximationAlgorithm
    {
        GeneralizedVanilla, // primary for GeneralizeVanilla
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