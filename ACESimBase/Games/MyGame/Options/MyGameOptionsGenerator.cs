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
            SuperSimple,
            Fast,
            Usual,
            Ambitious,
            PerfectInfo
        }

        static MyGameOptionSetChoices MyGameChoice => MyGameOptionSetChoices.Custom;

        public static MyGameOptions GetMyGameOptions() => MyGameChoice switch
        {
            MyGameOptionSetChoices.Custom => Custom(),
            MyGameOptionSetChoices.SuperSimple => SuperSimple(),
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
                DamagesMin = 100000,
                DamagesMax = 100000,
                NumLiabilityStrengthPoints = 6,
                NumLiabilitySignals = 6,
                NumOffers = 6,
                MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
                {
                    ExogenousProbabilityTrulyLiable = 0.5,
                    StdevNoiseToProduceLiabilityStrength = 0.5
                },
                PFilingCost = 5000,
                DAnswerCost = 5000,
                PLiabilityNoiseStdev = 0.15,
                DLiabilityNoiseStdev = 0.15,
                CourtLiabilityNoiseStdev = 0.15,
                NumDamagesStrengthPoints = 1,
                NumDamagesSignals = 1,
                PDamagesNoiseStdev = 0.15, // DEBUG -- 0.25 before
                DDamagesNoiseStdev = 0.15,
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

        public static MyGameOptions Custom()
        {
            var options = BaseOptions();
            options.DamagesMax = 150_000;
            options.DamagesMin = 50_000;
            options.PUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth };
            //options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth, Alpha = 20 * 0.000001 };
            //options.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth, Alpha = 20 * 0.000001 };

            //options.NumDamagesStrengthPoints = 4;
            //options.NumDamagesSignals = 4;
            //options.NumLiabilityStrengthPoints = 4;
            //options.NumLiabilitySignals = 4;
            //options.NumOffers = 6;

            options.NumDamagesStrengthPoints = 5;
            options.NumDamagesSignals = 5;
            options.NumLiabilityStrengthPoints = 5;
            options.NumLiabilitySignals = 5;
            options.NumOffers = 5;
            options.NumPotentialBargainingRounds = 3; // DEBUG
            options.AllowAbandonAndDefaults = true;
            options.BargainingRoundsSimultaneous = true;
            return options;
        }

        public static MyGameOptions SuperSimple()
        {
            var options = BaseOptions();
            options.NumLiabilityStrengthPoints = 2;
            options.NumLiabilitySignals = 2;
            options.NumOffers = 2;
            options.NumPotentialBargainingRounds = 1;
            options.AllowAbandonAndDefaults = false;

            options.PLiabilityNoiseStdev = 0.30;
            options.DLiabilityNoiseStdev = 0.30;
            options.CourtLiabilityNoiseStdev = 0.30;
            return options;
        }

        public static MyGameOptions Fast()
        {
            var options = BaseOptions();
            options.NumLiabilityStrengthPoints = 4;
            options.NumLiabilitySignals = 4;
            options.NumOffers = 4;
            options.NumPotentialBargainingRounds = 2;
            options.AllowAbandonAndDefaults = false;

            options.PLiabilityNoiseStdev = 0.30;
            options.DLiabilityNoiseStdev = 0.30;
            options.CourtLiabilityNoiseStdev = 0.30;
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
            options.NumLiabilityStrengthPoints = 10;
            options.NumLiabilitySignals = 10;
            options.NumOffers = 10;
            options.NumPotentialBargainingRounds = 2;
            options.AllowAbandonAndDefaults = true;
            return options;
        }

        public static MyGameOptions PerfectInformation(bool courtIsPerfectToo)
        {
            var options = BaseOptions();
            options.PLiabilityNoiseStdev = 0.001;
            options.DLiabilityNoiseStdev = 0.001;
            if (courtIsPerfectToo)
                options.CourtLiabilityNoiseStdev = 0.001;
            return options;
        }
    }
}
