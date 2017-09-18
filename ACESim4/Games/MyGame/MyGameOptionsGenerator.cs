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


        public static MyGameOptions PerfectInformation()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 10,
                ActionIsNoiseNotSignal = false,
                MyGameDisputeGenerator = null,
                ProbabilityTrulyLiable_LitigationQuality75 = 0.75,
                ProbabilityTrulyLiable_LitigationQuality90 = 0.90,
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
                LoserPaysMultiple = 1.0,
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


        public static MyGameOptions ReducedGame()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 3,
                ActionIsNoiseNotSignal = true,
                MyGameDisputeGenerator = null,
                ProbabilityTrulyLiable_LitigationQuality75 = 0.75,
                ProbabilityTrulyLiable_LitigationQuality90 = 0.90,
                NumSignals = 3,
                NumNoiseValues = 3,
                NumOffers = 3,
                PFilingCost = 5000,
                DAnswerCost = 5000,
                PNoiseStdev = 0.3,
                DNoiseStdev = 0.3,
                CourtNoiseStdev = 0.3,
                PTrialCosts = 15000,
                DTrialCosts = 15000,
                IncludeAgreementToBargainDecisions = false,
                PerPartyCostsLeadingUpToBargainingRound = 10000,
                AllowAbandonAndDefaults = false,
                LoserPays = false,
                LoserPaysMultiple = 1.0,
                LoserPaysAfterAbandonment = true,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumPotentialBargainingRounds = 1,
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


        public static MyGameOptions Temporary()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 5,
                ActionIsNoiseNotSignal = true,
                MyGameDisputeGenerator = null,
                ProbabilityTrulyLiable_LitigationQuality75 = 0.75,
                ProbabilityTrulyLiable_LitigationQuality90 = 0.90,
                NumSignals = 5,
                NumNoiseValues = 5,
                NumOffers = 5,
                PFilingCost = 5000,
                DAnswerCost = 5000,
                PNoiseStdev = 0.3,
                DNoiseStdev = 0.3,
                CourtNoiseStdev = 0.3,
                PTrialCosts = 15000,
                DTrialCosts = 15000,
                IncludeAgreementToBargainDecisions = true,
                PerPartyCostsLeadingUpToBargainingRound = 10000,
                AllowAbandonAndDefaults = true,
                LoserPays = false,
                LoserPaysMultiple = 1.0,
                LoserPaysAfterAbandonment = true,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumPotentialBargainingRounds = 1,
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
                MyGameDisputeGenerator = null,
                ProbabilityTrulyLiable_LitigationQuality75 = 0.75,
                ProbabilityTrulyLiable_LitigationQuality90 = 0.90,
                NumSignals = 10,
                NumNoiseValues = 100,
                NumCourtNoiseValues = 10,
                NumOffers = 11,
                PFilingCost = 5000,
                DAnswerCost = 5000,
                PNoiseStdev = 0.3,
                DNoiseStdev = 0.3,
                CourtNoiseStdev = 0.3,
                PTrialCosts = 15000,
                DTrialCosts = 15000,
                RegretAversion = 0.0,
                IncludeAgreementToBargainDecisions = false,
                PerPartyCostsLeadingUpToBargainingRound = 10000,
                AllowAbandonAndDefaults = true,
                LoserPays = false,
                LoserPaysMultiple = 1.0,
                LoserPaysAfterAbandonment = false, 
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
                IncludeCourtSuccessReport = false,
            };
            // options.AdditionalTableOverrides = new List<(Func<Decision, GameProgress, byte>, string)>() { (MyGameActionsGenerator.GamePlaysOutToTrial, "GamePlaysOutToTrial") };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            //options.PUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth };
            //options.DUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth };
            //options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth, Alpha = 10 * 0.000001 };
            //options.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth, Alpha = 10 * 0.000001 };
            //options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth, Alpha = 10 * 0.000001 };
            //options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
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
                MyGameDisputeGenerator = null,
                ProbabilityTrulyLiable_LitigationQuality75 = 0.75,
                ProbabilityTrulyLiable_LitigationQuality90 = 0.90,
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
