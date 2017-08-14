using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim;
using System.Threading;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using System.IO;
using System.Runtime.Serialization;
using System.Diagnostics;
using ACESim.Util;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Queue;

namespace ACESim
{
    public static class StartRunning
    {
        public static void StartMyGame()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 5,
                NumSignals = 5,
                NumOffers = 5,
                PNoiseStdev = 0.01, DNoiseStdev = 0.01,
                PTrialCosts = 5000, DTrialCosts = 5000,
                PerPartyBargainingRoundCosts = 1000,
                DeltaOffersOptions = new DeltaOffersOptions()
                { 
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumBargainingRounds = 1,
                ForgetEarlierBargainingRounds = true,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = true,
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false, true },
                IncludeSignalsReport = true
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() {InitialWealth = options.PInitialWealth};
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            MyGameDefinition gameDefinition = new MyGameDefinition();
            gameDefinition.Setup(options);
            EvolutionSettings evolutionSettings = new EvolutionSettings()
            {
                MaxParallelDepth = 2,
                ParallelOptimization = true,
                Algorithm = CRMAlgorithm.Probing,
                TotalAvgStrategySamplingCFRIterations = 100000000,
                TotalProbingCFRIterations = 100000,
                TotalVanillaCFRIterations = 100000000,
                ReportEveryNIterations = 10000,
                BestResponseEveryMIterations = EvolutionSettings.EffectivelyNever
            };
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition, evolutionSettings);
            CRMDevelopment developer =
                new CRMDevelopment(starterStrategies, evolutionSettings, gameDefinition);
            developer.DevelopStrategies();
        }
    }

    class Program
    {


        static void Main(string[] args)
        {
            string baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\MyGame";
            string strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
            StartRunning.StartMyGame();
        }
    }
}