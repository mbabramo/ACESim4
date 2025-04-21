namespace ACESimBase.Util.Statistical
{
    public enum RegressionTechniques
    {
        NeuralNetworkNetRegression,
        OLS,
        FastForest, // i.e., DART
        Experimental,
        FastTree,
        SDCA,
        FastTreeTweedie,
        OnlineGradientDescent,
        LightGbm,
        LbfgsPoissonRegression,
        Gam,
    }
}
