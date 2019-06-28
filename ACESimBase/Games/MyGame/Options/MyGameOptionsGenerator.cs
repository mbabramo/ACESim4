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
            Custom,
            Custom2,
            SuperSimple,
            Fast,
            Faster,
            Usual,
            Ambitious,
            PerfectInfo
        }

        static MyGameOptionSetChoices MyGameChoice => MyGameOptionSetChoices.Custom2;

        public static MyGameOptions GetMyGameOptions() => MyGameChoice switch
        {
            MyGameOptionSetChoices.Custom => Custom(),
            MyGameOptionSetChoices.Custom2 => Custom2(),
            MyGameOptionSetChoices.SuperSimple => SuperSimple(),
            MyGameOptionSetChoices.Faster => Faster(),
            MyGameOptionSetChoices.Fast => Fast(),
            MyGameOptionSetChoices.Usual => Usual(),
            MyGameOptionSetChoices.Ambitious => Ambitious(),
            MyGameOptionSetChoices.PerfectInfo => PerfectInformation(courtIsPerfectToo: false),
            _ => throw new Exception()
        };


        public static MyGameOptions BaseOptions()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesMin = 50_000,
                DamagesMax = 150_000,
                NumLiabilityStrengthPoints = 5,
                NumLiabilitySignals = 5,
                NumDamagesStrengthPoints = 5,
                NumDamagesSignals = 5,
                NumOffers = 5,
                IncludeEndpointsForOffers = false,
                MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
                {
                    ExogenousProbabilityTrulyLiable = 0.5,
                    StdevNoiseToProduceLiabilityStrength = 0.5
                },
                SkipFileAndAnswerDecisions = false,
                PFilingCost = 5000,
                DAnswerCost = 5000,
                PLiabilityNoiseStdev = 0.1,
                DLiabilityNoiseStdev = 0.1,
                CourtLiabilityNoiseStdev = 0.15,
                PDamagesNoiseStdev = 0.1,
                DDamagesNoiseStdev = 0.1,
                CourtDamagesNoiseStdev = 0.15,
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
            return options;
        }

        public static MyGameOptions Custom()
        {
            var options = BaseOptions();
            options.DamagesMax = 150_000;
            options.DamagesMin = 50_000;
            //options.PUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth };
            //options.DUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth };            
            options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth, Alpha = 10 * 0.000001 }; 
            options.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth, Alpha = 10 * 0.000001 }; 
            //options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            //options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };

            options.NumDamagesStrengthPoints = 1;
            options.NumDamagesSignals = 1;
            options.NumLiabilityStrengthPoints = 4;
            options.NumLiabilitySignals = 4;
            options.NumOffers = 4;
            options.NumPotentialBargainingRounds = 2;

            options.SkipFileAndAnswerDecisions = true; 
            options.AllowAbandonAndDefaults = false; 
            options.SimultaneousOffersUltimatelyRevealed = true;
            options.BargainingRoundsSimultaneous = true;
            return options;
        }

        public static MyGameOptions Custom2()
        {
            var options = BaseOptions();
            options.NumDamagesStrengthPoints = 4;
            options.NumDamagesSignals = 4;
            options.NumLiabilityStrengthPoints = 4;
            options.NumLiabilitySignals = 4;
            options.NumOffers = 15;  // DEBUG
            options.NumPotentialBargainingRounds = 1; // DEBUG
            options.AllowAbandonAndDefaults = true;
            options.IncludeAgreementToBargainDecisions = true;
            options.SkipFileAndAnswerDecisions = false;

            options.PFilingCost = options.DAnswerCost = 10_000;
            options.PerPartyCostsLeadingUpToBargainingRound = 0;
            options.PTrialCosts = options.DTrialCosts = 25_000;
            

            // DEBUG
            double level = .5;
            options.PLiabilityNoiseStdev = level;
            options.DLiabilityNoiseStdev = level;
            options.CourtLiabilityNoiseStdev = level;
            options.PDamagesNoiseStdev = level;
            options.DDamagesNoiseStdev = level;
            options.CourtDamagesNoiseStdev = level;

            //options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth, Alpha = 10 * 0.000001 };
            //options.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth, Alpha = 10 * 0.000001 };

            return options;
        }

        public static MyGameOptions SuperSimple()
        {
            var options = BaseOptions();
            options.NumDamagesStrengthPoints = 1;
            options.NumDamagesSignals = 1;
            options.NumLiabilityStrengthPoints = 2;
            options.NumLiabilitySignals = 2;
            options.NumOffers = 2;
            options.NumPotentialBargainingRounds = 1;
            options.AllowAbandonAndDefaults = false;
            options.SkipFileAndAnswerDecisions = true; // set to true to make game fully symmetrical
            //options.MyGameDisputeGenerator = new MyGameEqualQualityProbabilitiesDisputeGenerator()
            //{
            //    ProbabilityTrulyLiable_LiabilityStrength75 = 0.75,
            //    ProbabilityTrulyLiable_LiabilityStrength90 = 0.90,
            //    NumPointsToDetermineTrulyLiable = 1
            //};
            options.PLiabilityNoiseStdev = 0.30;
            options.DLiabilityNoiseStdev = 0.30;
            options.CourtLiabilityNoiseStdev = 0.30;
            return options;
        }

        public static MyGameOptions Faster()
        {
            var options = BaseOptions();
            options.NumDamagesStrengthPoints = 3;
            options.NumDamagesSignals = 3;
            options.NumLiabilityStrengthPoints = 3;
            options.NumLiabilitySignals = 3;
            options.NumOffers = 3;
            options.NumPotentialBargainingRounds = 2;
            options.AllowAbandonAndDefaults = false;
            options.SkipFileAndAnswerDecisions = false;

            options.PLiabilityNoiseStdev = 0.30;
            options.DLiabilityNoiseStdev = 0.30;
            options.CourtLiabilityNoiseStdev = 0.30;
            return options;
        }

        public static MyGameOptions Fast()
        {
            var options = BaseOptions();
            options.NumDamagesStrengthPoints = 4;
            options.NumDamagesSignals = 4;
            options.NumLiabilityStrengthPoints = 4;
            options.NumLiabilitySignals = 4;
            options.NumOffers = 4;  
            options.NumPotentialBargainingRounds = 2;
            options.AllowAbandonAndDefaults = true;
            options.IncludeAgreementToBargainDecisions = true;
            options.SkipFileAndAnswerDecisions = false;

            //options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth, Alpha = 10 * 0.000001 };
            //options.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth, Alpha = 10 * 0.000001 };

            return options;
        }

        public static MyGameOptions Usual()
        {
            var options = BaseOptions();
            return options;
        }

        public static MyGameOptions Ambitious()
        {
            var options = BaseOptions();
            options.NumDamagesStrengthPoints = 5;
            options.NumDamagesSignals = 5;
            options.NumLiabilityStrengthPoints = 5;
            options.NumLiabilitySignals = 5;
            options.NumOffers = 5;
            options.NumPotentialBargainingRounds = 2;
            options.AllowAbandonAndDefaults = true;
            options.IncludeAgreementToBargainDecisions = true;
            options.SkipFileAndAnswerDecisions = false;
            return options;
        }

        public static MyGameOptions PerfectInformation(bool courtIsPerfectToo)
        {
            var options = BaseOptions();
            options.PLiabilityNoiseStdev = 0.001;
            options.DLiabilityNoiseStdev = 0.001;
            options.PDamagesNoiseStdev = 0.001;
            options.DDamagesNoiseStdev = 0.001;
            if (courtIsPerfectToo)
                options.CourtLiabilityNoiseStdev = 0.001;
            return options;
        }
    }
}
