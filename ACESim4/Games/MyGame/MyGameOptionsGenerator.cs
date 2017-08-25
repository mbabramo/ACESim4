using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;

namespace ACESim
{
    public static class MyGameOptionsGenerator
    {
        public static MyGameOptions SingleBargainingRound_5Points_LowNoise()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 5,
                NumSignals = 5,
                NumNoiseValues = 5,
                ActionIsNoiseNotSignal = false,
                NumOffers = 5,
                PNoiseStdev = 0.01,
                DNoiseStdev = 0.01,
                PTrialCosts = 5000,
                DTrialCosts = 5000,
                PerPartyBargainingRoundCosts = 1000,
                AllowAbandonAndDefaults = true,
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
                PGoesFirstIfNotSimultaneous = new List<bool> {true, false, true, false, true},
                IncludeSignalsReport = true
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() {InitialWealth = options.PInitialWealth};
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() {InitialWealth = options.DInitialWealth};
            return options;
        }


        public static MyGameOptions SingleBargainingRound()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 10,
                ActionIsNoiseNotSignal = false,
                NumSignals = 10,
                NumNoiseValues = 10,
                NumOffers = 10,
                PNoiseStdev = 0.1,
                DNoiseStdev = 0.1,
                PTrialCosts = 15000,
                DTrialCosts = 15000,
                PerPartyBargainingRoundCosts = 1000,
                AllowAbandonAndDefaults = true,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumBargainingRounds = 4,
                ForgetEarlierBargainingRounds = true,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = true,
                PGoesFirstIfNotSimultaneous = new List<bool> { },
                IncludeSignalsReport = true
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            return options;
        }

        public static MyGameOptions TwoSimultaneousBargainingRounds()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 5,
                NumSignals = 5,
                NumOffers = 5,
                NumNoiseValues = 5,
                ActionIsNoiseNotSignal = false,
                PNoiseStdev = 0.2,
                DNoiseStdev = 0.2,
                PTrialCosts = 20000,
                DTrialCosts = 20000,
                PerPartyBargainingRoundCosts = 1000,
                AllowAbandonAndDefaults = false,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumBargainingRounds = 2,
                ForgetEarlierBargainingRounds = true,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = true,
                PGoesFirstIfNotSimultaneous = new List<bool> { },
                IncludeSignalsReport = true
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            return options;
        }

        
        public static MyGameOptions TwoAlternatingOffers()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 5,
                NumSignals = 5,
                NumOffers = 5,
                NumNoiseValues = 5,
                ActionIsNoiseNotSignal = false,
                PNoiseStdev = 0.1,
                DNoiseStdev = 0.1,
                PTrialCosts = 5000,
                DTrialCosts = 5000,
                PerPartyBargainingRoundCosts = 1000,
                AllowAbandonAndDefaults = true,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumBargainingRounds = 2,
                ForgetEarlierBargainingRounds = true,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = false,
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false },
                IncludeSignalsReport = true
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            return options;
        }


        public static MyGameOptions FourBargainingRounds_PerfectInformation()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 10,
                NumSignals = 10,
                NumNoiseValues = 10,
                ActionIsNoiseNotSignal = false,
                NumOffers = 10,
                PNoiseStdev = 0.0001,
                DNoiseStdev = 0.0001,
                PTrialCosts = 5000,
                DTrialCosts = 5000,
                PerPartyBargainingRoundCosts = 1000,
                AllowAbandonAndDefaults = true,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumBargainingRounds = 4,
                ForgetEarlierBargainingRounds = true, // true makes things much faster
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = true,
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false },
                IncludeSignalsReport = true
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            return options;
        }


        public static MyGameOptions ActionIsNoise_10Points_1Round()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 10,
                NumSignals = 10, // include signals for < 0 and > 1
                NumNoiseValues = 10,
                ActionIsNoiseNotSignal = true,
                NumOffers = 10,
                PNoiseStdev = 0.1,
                DNoiseStdev = 0.1,
                CourtNoiseStdev = 0.5,
                PTrialCosts = 20000,
                DTrialCosts = 20000,
                PerPartyBargainingRoundCosts = 1000,
                AllowAbandonAndDefaults = true,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumBargainingRounds = 1,
                ForgetEarlierBargainingRounds = true, // true makes things much faster
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = true,
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false },
                IncludeSignalsReport = true
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            return options;
        }
    }
}
