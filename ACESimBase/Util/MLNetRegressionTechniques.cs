namespace ACESimBase.Util
{
    public partial class MLNetRegression
    {
        public enum MLNetRegressionTechniques
        {
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
}
