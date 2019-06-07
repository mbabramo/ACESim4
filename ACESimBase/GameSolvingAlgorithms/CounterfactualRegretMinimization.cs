using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Threading;
using ACESimBase.Util;
using ACESimBase.GameSolvingSupport;

namespace ACESim
{
    [Serializable]
    public partial class CounterfactualRegretMinimization : StrategiesDeveloperBase
    {

        #region Options

        bool TraceCFR = false;

        bool ShouldEstimateImprovementOverTime = false;
        const int NumRandomGamePlaysForEstimatingImprovement = 1000;


        #endregion

        #region Construction

        public CounterfactualRegretMinimization()
        {
            Navigation = Navigation.WithGameStateFunction(GetGameState);
        }

        public CounterfactualRegretMinimization(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition)
        {
            Strategies = existingStrategyState;
            EvolutionSettings = evolutionSettings;
            GameDefinition = gameDefinition;
            GameFactory = GameDefinition.GameFactory;
            NumNonChancePlayers = (byte) GameDefinition.Players.Count(x => !x.PlayerIsChance);
            NumChancePlayers = (byte) GameDefinition.Players.Count(x => x.PlayerIsChance);
        }

        public IStrategiesDeveloper DeepCopy()
        {
            return new CounterfactualRegretMinimization()
            {
                Strategies = Strategies.Select(x => x.DeepCopy()).ToList(),
                EvolutionSettings = EvolutionSettings,
                GameDefinition = GameDefinition,
                GameFactory = GameFactory,
                Navigation = Navigation,
                LookupApproach = LookupApproach
            };
        }

        public async Task<string> DevelopStrategies(string reportName)
        {
            string report = null;
            Initialize();
            switch (EvolutionSettings.Algorithm)
            {
                case GameApproximationAlgorithm.AverageStrategySampling:
                    await SolveAvgStrategySamplingCFR();
                    break;
                case GameApproximationAlgorithm.GibsonProbing:
                    report = await SolveGibsonProbingCFR();
                    break;
                case GameApproximationAlgorithm.ExploratoryProbing:
                    report = await SolveExploratoryProbingCFR(reportName);
                    break;
                case GameApproximationAlgorithm.ModifiedGibsonProbing:
                    report = await SolveModifiedGibsonProbingCFR(reportName);
                    break;
                case GameApproximationAlgorithm.HedgeProbing:
                    report = await SolveHedgeProbingCFR(reportName);
                    break;
                case GameApproximationAlgorithm.HedgeVanilla:
                    report = await SolveHedgeVanillaCFR();
                    break;
                case GameApproximationAlgorithm.Vanilla:
                    report = await SolveVanillaCFR();
                    break;
                case GameApproximationAlgorithm.PureStrategyFinder:
                    await FindPureStrategies();
                    break;
                case GameApproximationAlgorithm.FictitiousSelfPlay:
                    report = await SolveFictitiousSelfPlay();
                    break;
                default:
                    throw new NotImplementedException();
            }
            if (EvolutionSettings.SerializeResults)
                StrategySerialization.SerializeStrategies(Strategies.ToArray(), "serstat.sst");
            return report;
        }

        #endregion
    }
}
