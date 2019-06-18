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
        public enum MyGameOptionSetChoices
        {
            SuperSimple,
            Fast,
            Usual,
            Ambitious,
            PerfectInfo
        }

        static MyGameOptionSetChoices MyGameChoice => MyGameOptionSetChoices.Ambitious;

        public static MyGameOptions GetMyGameOptions() => MyGameChoice switch
        {
            MyGameOptionSetChoices.SuperSimple => SuperSimple(),
            MyGameOptionSetChoices.Fast => Fast(),
            MyGameOptionSetChoices.Usual => Usual(),
            MyGameOptionSetChoices.Ambitious => Ambitious(),
            MyGameOptionSetChoices.PerfectInfo => PerfectInformation(courtIsPerfectToo: false),
            _ => throw new Exception()
        };


        public static MyGameOptions BaseForSingleOptionsSet()
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
                PNoiseStdev = 0.2,
                DNoiseStdev = 0.2,
                CourtNoiseStdev = 0.2,
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
                BargainingRoundsSimultaneous = true,
                SimultaneousOffersUltimatelyRevealed = true,
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false, true, false, true, false },
                IncludeSignalsReport = true,
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

        public static MyGameOptions SuperSimple()
        {
            var options = BaseForSingleOptionsSet();
            options.NumLitigationQualityPoints = 2;
            options.NumSignals = 2;
            options.NumOffers = 2;
            options.NumPotentialBargainingRounds = 1;
            options.AllowAbandonAndDefaults = false;
            return options;
        }

        public static MyGameOptions Fast()
        {
            var options = BaseForSingleOptionsSet();
            options.NumLitigationQualityPoints = 4;
            options.NumSignals = 4;
            options.NumOffers = 4;
            options.NumPotentialBargainingRounds = 2;
            options.AllowAbandonAndDefaults = true;
            return options;
        }

        public static MyGameOptions Usual()
        {
            var options = BaseForSingleOptionsSet();
            return options;
        }

        public static MyGameOptions Ambitious()
        {
            var options = BaseForSingleOptionsSet();
            options.NumLitigationQualityPoints = 10;
            options.NumSignals = 10;
            options.NumOffers = 10;
            options.NumPotentialBargainingRounds = 2;
            options.AllowAbandonAndDefaults = false;
            return options;
        }

        public static MyGameOptions PerfectInformation(bool courtIsPerfectToo)
        {
            var options = BaseForSingleOptionsSet();
            options.PNoiseStdev = 0.001;
            options.DNoiseStdev = 0.001;
            if (courtIsPerfectToo)
                options.CourtNoiseStdev = 0.001;
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
                PNoiseStdev = 0.2,
                DNoiseStdev = 0.2,
                CourtNoiseStdev = 0.2,
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
                BargainingRoundsSimultaneous = true,
                SimultaneousOffersUltimatelyRevealed = true,
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
