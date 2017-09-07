﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;

namespace ACESim
{
    public static class MyGameOptionsGenerator
    {


        public static MyGameOptions PerfectInformation()
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
                PFilingCost = 0,
                DAnswerCost = 0,
                PNoiseStdev = 0.001,
                DNoiseStdev = 0.001,
                CourtNoiseStdev = 0.1,
                PTrialCosts = 1000,
                DTrialCosts = 1000,
                IncludeAgreementToBargainDecisions = false,
                PerPartyCostsLeadingUpToBargainingRound = 1000,
                AllowAbandonAndDefaults = false,
                LoserPays = false,
                LoserPaysAfterAbandonment = true,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumPotentialBargainingRounds = 4,
                ForgetEarlierBargainingRounds = true,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = true,
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false, true, false, true, false },
                IncludeSignalsReport = false,
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            return options;
        }

        public static MyGameOptions Standard()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 10,
                ActionIsNoiseNotSignal = true,
                NumSignals = 10,
                NumNoiseValues = 10,
                NumOffers = 10,
                PFilingCost = 3000,
                DAnswerCost = 3000,
                PNoiseStdev = 0.05,
                DNoiseStdev = 0.05,
                CourtNoiseStdev = 0.1,
                PTrialCosts = 25000,
                DTrialCosts = 25000,
                IncludeAgreementToBargainDecisions = true,
                PerPartyCostsLeadingUpToBargainingRound = 5000,
                AllowAbandonAndDefaults = true,
                LoserPays = false,
                LoserPaysAfterAbandonment = true,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumPotentialBargainingRounds = 3,
                ForgetEarlierBargainingRounds = true,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = true,
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false, true, false, true, false },
                IncludeSignalsReport = false,                 
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
                IncludeAgreementToBargainDecisions = false,
                PerPartyCostsLeadingUpToBargainingRound = 1000,
                AllowAbandonAndDefaults = true,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumPotentialBargainingRounds = 1,
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
