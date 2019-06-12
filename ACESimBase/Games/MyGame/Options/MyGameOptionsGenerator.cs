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
        public enum MyGameOptionSetChoices
        {
            SuperSimple,
            Fast,
            Usual,
            Ambitious,
        }

        static MyGameOptionSetChoices MyGameChoice => MyGameOptionSetChoices.Ambitious;

        public static MyGameOptions SingleRound() => MyGameChoice switch
        {
            MyGameOptionSetChoices.SuperSimple => SingleRound_SuperSimple(),
            MyGameOptionSetChoices.Fast => SingleRound_Fast(),
            MyGameOptionSetChoices.Usual => SingleRound_Usual(),
            MyGameOptionSetChoices.Ambitious => SingleRound_Ambitious(),
            _ => throw new Exception()
        };

        public static MyGameOptions SingleRound_SuperSimple()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesToAllege = 100000,
                NumLitigationQualityPoints = 2,
                NumSignals = 2,
                NumOffers = 2,
                MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
                {
                    ExogenousProbabilityTrulyLiable = 0.5,
                    StdevNoiseToProduceLitigationQuality = 0.5
                },
                PFilingCost = 5000,
                DAnswerCost = 5000,
                PNoiseStdev = 0.1,
                DNoiseStdev = 0.1,
                CourtNoiseStdev = 0.1,
                CostsMultiplier = 1.0,
                PTrialCosts = 15_000,
                DTrialCosts = 15_000,
                RegretAversion = 0.0,
                IncludeAgreementToBargainDecisions = false,
                PerPartyCostsLeadingUpToBargainingRound = 15_000,
                AllowAbandonAndDefaults = false,
                LoserPays = false,
                LoserPaysMultiple = 1.0,
                LoserPaysAfterAbandonment = false,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumPotentialBargainingRounds = 1,
                BargainingRoundRecall = MyGameBargainingRoundRecall.RememberAllBargainingRounds,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = false,
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


        public static MyGameOptions SingleRound_Fast()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesToAllege = 100000,
                NumLitigationQualityPoints = 4,
                NumSignals = 4,
                NumOffers = 4,
                MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
                {
                    ExogenousProbabilityTrulyLiable = 0.5,
                    StdevNoiseToProduceLitigationQuality = 0.5
                },
                PFilingCost = 5000,
                DAnswerCost = 5000,
                PNoiseStdev = 0.1,
                DNoiseStdev = 0.1,
                CourtNoiseStdev = 0.1,
                CostsMultiplier = 1.0,
                PTrialCosts = 15_000,
                DTrialCosts = 15_000,
                RegretAversion = 0.0,
                IncludeAgreementToBargainDecisions = false,
                PerPartyCostsLeadingUpToBargainingRound = 15_000,
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
                NumPotentialBargainingRounds = 2,
                BargainingRoundRecall = MyGameBargainingRoundRecall.RememberAllBargainingRounds,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = false,
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

        public static MyGameOptions SingleRound_Usual()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesToAllege = 100000,
                NumLitigationQualityPoints = 6,
                NumSignals = 6, 
                NumOffers = 6, 
                MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
                {
                    ExogenousProbabilityTrulyLiable = 0.5,
                    StdevNoiseToProduceLitigationQuality = 0.5
                },
                PFilingCost = 5000,
                DAnswerCost = 5000,
                PNoiseStdev = 0.1, 
                DNoiseStdev = 0.1,
                CourtNoiseStdev = 0.1,
                CostsMultiplier = 1.0,
                PTrialCosts = 15_000,
                DTrialCosts = 15_000,
                RegretAversion = 0.0,
                IncludeAgreementToBargainDecisions = false,
                PerPartyCostsLeadingUpToBargainingRound = 15_000,
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
                NumPotentialBargainingRounds = 2, 
                BargainingRoundRecall = MyGameBargainingRoundRecall.RememberAllBargainingRounds,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = false,
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



        public static MyGameOptions SingleRound_Ambitious()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesToAllege = 100000,
                NumLitigationQualityPoints = 10,
                NumSignals = 10,
                NumOffers = 10,
                MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
                {
                    ExogenousProbabilityTrulyLiable = 0.5,
                    StdevNoiseToProduceLitigationQuality = 0.5
                },
                PFilingCost = 5000,
                DAnswerCost = 5000,
                PNoiseStdev = 0.1,
                DNoiseStdev = 0.1,
                CourtNoiseStdev = 0.1,
                CostsMultiplier = 1.0,
                PTrialCosts = 15_000,
                DTrialCosts = 15_000,
                RegretAversion = 0.0,
                IncludeAgreementToBargainDecisions = false,
                PerPartyCostsLeadingUpToBargainingRound = 15_000,
                AllowAbandonAndDefaults = false,
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
                BargainingRoundRecall = MyGameBargainingRoundRecall.RememberAllBargainingRounds,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = false,
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

        public static MyGameOptions PerfectInformation()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesToAllege = 100000,
                NumLitigationQualityPoints = 10,
                MyGameDisputeGenerator = new MyGameEqualQualityProbabilitiesDisputeGenerator()
                {
                    ProbabilityTrulyLiable_LitigationQuality75 = 0.75,
                    ProbabilityTrulyLiable_LitigationQuality90 = 0.90,
                    NumPointsToDetermineTrulyLiable = 100,
                },
                NumSignals = 10,
                NumOffers = 10,
                PFilingCost = 0,
                DAnswerCost = 0,
                PNoiseStdev = 0.001,
                DNoiseStdev = 0.001,
                CourtNoiseStdev = 0.1,
                CostsMultiplier = 1.0,
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
                BargainingRoundRecall = MyGameBargainingRoundRecall.RememberAllBargainingRounds,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = true,
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false, true, false, true, false },
                IncludeSignalsReport = false,
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            return options;
        }

        public static MyGameOptions BaseForMultipleOptionsSets()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesToAllege = 100000,
                NumLitigationQualityPoints = 10,
                MyGameDisputeGenerator = new MyGameEqualQualityProbabilitiesDisputeGenerator()
                {
                    ProbabilityTrulyLiable_LitigationQuality75 = 0.75,
                    ProbabilityTrulyLiable_LitigationQuality90 = 0.90,
                    NumPointsToDetermineTrulyLiable = 100,
                },
                NumSignals = 10,
                NumOffers = 11,
                PFilingCost = 5000,
                DAnswerCost = 5000,
                PNoiseStdev = 0.1,
                DNoiseStdev = 0.1,
                CourtNoiseStdev = 0.1,
                CostsMultiplier = 1.0,
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
                BargainingRoundRecall = MyGameBargainingRoundRecall.RememberAllBargainingRounds,
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
    }
}
