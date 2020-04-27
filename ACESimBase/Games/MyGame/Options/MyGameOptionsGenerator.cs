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
            Custom3,
            LiabilityUncertainty_1BR,
            LiabilityUncertainty_2BR,
            LiabilityUncertainty_3BR,
            DamagesUncertainty_1BR,
            DamagesUncertainty_2BR,
            DamagesUncertainty_3BR,
            BothUncertain_1BR,
            BothUncertain_2BR,
            BothUncertain_3BR,
            Shootout,
            Shootout_Triple,
            Shootout_AllRounds,
            Shootout_IncludingAbandoment,
            Shootout_AllRoundsIncludingAbandoment,
            SuperSimple,
            Fast,
            Faster,
            Usual,
            Ambitious,
            PerfectInfo,
        }

        static MyGameOptionSetChoices MyGameChoice => MyGameOptionSetChoices.Custom;

        public static MyGameOptions GetMyGameOptions() => MyGameChoice switch
        {
            MyGameOptionSetChoices.Custom => Custom(),
            MyGameOptionSetChoices.Custom2 => Custom2(),
            MyGameOptionSetChoices.Custom3 => Custom3(),
            MyGameOptionSetChoices.LiabilityUncertainty_1BR => LiabilityUncertainty_1BR(),
            MyGameOptionSetChoices.LiabilityUncertainty_2BR => LiabilityUncertainty_2BR(),
            MyGameOptionSetChoices.LiabilityUncertainty_3BR => LiabilityUncertainty_3BR(),
            MyGameOptionSetChoices.DamagesUncertainty_1BR => DamagesUncertainty_1BR(),
            MyGameOptionSetChoices.DamagesUncertainty_2BR => DamagesUncertainty_2BR(),
            MyGameOptionSetChoices.DamagesUncertainty_3BR => DamagesUncertainty_3BR(),
            MyGameOptionSetChoices.BothUncertain_1BR => BothUncertain_1BR(),
            MyGameOptionSetChoices.BothUncertain_2BR => BothUncertain_2BR(),
            MyGameOptionSetChoices.BothUncertain_3BR => BothUncertain_3BR(),
            MyGameOptionSetChoices.Shootout => Shootout(),
            MyGameOptionSetChoices.Shootout_Triple => Shootout_Triple(),
            MyGameOptionSetChoices.Shootout_AllRounds => Shootout_AllRounds(),
            MyGameOptionSetChoices.Shootout_IncludingAbandoment => Shootout_IncludingAbandoment(),
            MyGameOptionSetChoices.Shootout_AllRoundsIncludingAbandoment => Shootout_AllRoundsIncludingAbandoment(),
            MyGameOptionSetChoices.SuperSimple => SuperSimple(),
            MyGameOptionSetChoices.Faster => Faster(),
            MyGameOptionSetChoices.Fast => Fast(),
            MyGameOptionSetChoices.Usual => BothUncertain_2BR(),
            MyGameOptionSetChoices.Ambitious => Ambitious(),
            MyGameOptionSetChoices.PerfectInfo => PerfectInformation(courtIsPerfectToo: false),
            _ => throw new Exception()
        };


        public static void NormalizeToDamages(MyGameOptions o)
        {
            double divideBy = o.DamagesMax;
            o.PInitialWealth /= divideBy;
            o.DInitialWealth /= divideBy;
            o.DamagesMin /= divideBy;
            o.DamagesMax /= divideBy;
            o.PFilingCost /= divideBy;
            o.DAnswerCost /= divideBy;
            o.PTrialCosts /= divideBy;
            o.DTrialCosts /= divideBy;
            o.PerPartyCostsLeadingUpToBargainingRound /= divideBy;
        }

        public static MyGameOptions BaseOptions()
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesMin = 0_000,
                DamagesMax = 100_000,
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
                FirstRowOnly = false,

                WarmStartThroughIteration = null,
                WarmStartOptions = MyGameWarmStartOptions.NoWarmStart
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
            var options = DamagesUncertainty_2BR();
            options.NumPotentialBargainingRounds = 6;
            options.CostsMultiplier = 0.25;
            //var options = DamagesUncertainty_1BR();
            //options.NumDamagesSignals = 3;
            //options.NumOffers = 3;
            //NormalizeToDamages(options);
            //options.AllowAbandonAndDefaults = false;
            //options.PFilingCost = 0;
            //options.DAnswerCost = 0;
            //options.PerPartyCostsLeadingUpToBargainingRound = 0;
            //options.PTrialCosts = 0;
            //options.DTrialCosts = 0;
            return options;
        }

        public static MyGameOptions KlermanEtAl()
        {
            return GetKlermanEtAlOptions(0.5, true, false, false);
        }

        public static MyGameOptions KlermanEtAl_MultipleStrengthPoints()
        {
            return GetKlermanEtAlOptions(0.5, false, false, false);
        }


        public static MyGameOptions KlermanEtAl_WithOptions()
        {
            return GetKlermanEtAlOptions(0.5, false, true, false);
        }

        public static MyGameOptions KlermanEtAl_WithDamagesUncertainty()
        {
            return GetKlermanEtAlOptions(0.5, false, true, true);
        }

        public static MyGameOptions GetKlermanEtAlOptions(double exogenousProbabilityTrulyLiable, bool useOnlyTwoLiabilityStrengthPoints, bool includeOptions, bool includeDamagesStrengths)
        {

            var options = LiabilityUncertainty_1BR();
            options.PInitialWealth = 0;
            options.DInitialWealth = 1;
            options.DamagesMin = options.DamagesMax = 1.0;
            options.PFilingCost = options.DAnswerCost = 0.05; // but see below for when we're using options
            options.PTrialCosts = options.DTrialCosts = 0.15; 
            options.PerPartyCostsLeadingUpToBargainingRound = 0;
            options.CostsMultiplier = 1;
            options.SkipFileAndAnswerDecisions = true;
            options.AllowAbandonAndDefaults = false;
            if (includeOptions)
            {
                options.SkipFileAndAnswerDecisions = false;
                options.AllowAbandonAndDefaults = true;
                options.PFilingCost = options.DAnswerCost = 0.30; // higher values so we can see effect of dropping out
            }
            options.MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
            {
                ExogenousProbabilityTrulyLiable = exogenousProbabilityTrulyLiable,
                StdevNoiseToProduceLiabilityStrength = 0.4
            };
            options.PLiabilityNoiseStdev = options.DLiabilityNoiseStdev = 0.1;
            options.NumLiabilitySignals = 50;
            options.NumLiabilityStrengthPoints = 100;
            if (includeDamagesStrengths)
            {
                options.NumDamagesSignals = 5;
                options.NumDamagesStrengthPoints = 5;
            }
            if (useOnlyTwoLiabilityStrengthPoints)
            {
                options.NumLiabilityStrengthPoints = 2;
                options.MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
                {
                    ExogenousProbabilityTrulyLiable = exogenousProbabilityTrulyLiable,
                    StdevNoiseToProduceLiabilityStrength = 0.001
                };
            }
            options.CourtLiabilityNoiseStdev = 0.001;
            options.IncludeCourtSuccessReport = true;
            options.IncludeSignalsReport = true;

            return options;
        }

        public static MyGameOptions Custom2()
        {
            var options = BaseOptions();

            options.NumLiabilityStrengthPoints = 5;
            options.NumLiabilitySignals = 5;
            options.NumOffers = 5;

            options.NumDamagesSignals = 1;
            options.NumDamagesStrengthPoints = 1;
            options.DamagesMax = options.DamagesMin = 100_000;
            //options.NumDamagesStrengthPoints = 5; 
            //options.NumDamagesSignals = 5;

            double level = .20;
            options.PLiabilityNoiseStdev = level;
            options.DLiabilityNoiseStdev = level;
            options.CourtLiabilityNoiseStdev = level;
            options.PDamagesNoiseStdev = level;
            options.DDamagesNoiseStdev = level;
            options.CourtDamagesNoiseStdev = level;

            options.AllowAbandonAndDefaults = true; 
            options.IncludeAgreementToBargainDecisions = false;
            options.SkipFileAndAnswerDecisions = false;

            //options.WarmStartOptions = MyGameWarmStartOptions.FacilitateSettlementByMakingOpponentStingy; 
            //options.WarmStartThroughIteration = null; 
            //options.IncludeAgreementToBargainDecisions = false; 
            //options.SkipFileAndAnswerDecisions = true;
            //options.AllowAbandonAndDefaults = false;

            options.PFilingCost = options.DAnswerCost = 10_000;
            options.PerPartyCostsLeadingUpToBargainingRound = 0;
            options.PTrialCosts = 10_000;
            options.DTrialCosts = 10_000;
            options.PerPartyCostsLeadingUpToBargainingRound = 7_500;
            options.NumPotentialBargainingRounds = 2;

            //options.LoserPays = true;
            //options.LoserPaysAfterAbandonment = true;
            //options.LoserPaysMultiple = 1.0; 

            //options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth, Alpha = 5 * 0.000001 };
            //options.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth, Alpha = 5 * 0.000001 };

            return options;
        }

        public static MyGameOptions DamagesUncertainty_1BR()
        {
            var options = BaseOptions();

            options.NumLiabilityStrengthPoints = 2; // Note: higher strength will be 100% of the time, b/c of ExogenousProbabilityTrulyLiable
            options.NumLiabilitySignals = 1;
            options.MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
            {
                ExogenousProbabilityTrulyLiable = 1.0,
                StdevNoiseToProduceLiabilityStrength = 0.001
            };
            options.PLiabilityNoiseStdev = 0.001;
            options.DLiabilityNoiseStdev = 0.001;
            options.CourtLiabilityNoiseStdev = 0.001;

            options.NumPotentialBargainingRounds = 1;

            return options;
        }

        public static MyGameOptions DamagesUncertainty_2BR()
        {
            var options = DamagesUncertainty_1BR();
            options.NumPotentialBargainingRounds = 2;

            return options;
        }
        public static MyGameOptions DamagesUncertainty_3BR()
        {
            var options = DamagesUncertainty_1BR();
            options.NumPotentialBargainingRounds = 3;

            return options;
        }

        public static MyGameOptions LiabilityUncertainty_1BR()
        {
            var options = BaseOptions();

            options.NumLiabilityStrengthPoints = 5;
            options.NumLiabilitySignals = 5;

            options.NumDamagesSignals = 1;
            options.NumDamagesStrengthPoints = 1;
            options.DamagesMax = options.DamagesMin = 100_000;

            options.NumPotentialBargainingRounds = 1;

            return options;
        }


        public static MyGameOptions LiabilityUncertainty_2BR()
        {
            var options = LiabilityUncertainty_1BR();
            options.NumPotentialBargainingRounds = 2;

            return options;
        }

        public static MyGameOptions Custom3()
        {
            var options = DamagesUncertainty_2BR();

            options.CostsMultiplier = 3.0;
            return options;
        }

        public static MyGameOptions Shootout()
        {
            var options = BaseOptions();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = false;
            options.ShootoutStrength = 1.0;
            options.ShootoutsAverageAllRounds = false;

            return options;
        }

        public static MyGameOptions Shootout_Triple()
        {
            var options = BaseOptions();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = false;
            options.ShootoutStrength = 3.0;
            options.ShootoutsAverageAllRounds = false;

            return options;
        }

        public static MyGameOptions Shootout_AllRounds()
        {
            var options = BaseOptions();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = false;
            options.ShootoutStrength = 1.0;
            options.ShootoutsAverageAllRounds = true;

            return options;
        }

        public static MyGameOptions Shootout_IncludingAbandoment()
        {
            var options = BaseOptions();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = true;
            options.ShootoutStrength = 1.0;
            options.ShootoutsAverageAllRounds = false;

            return options;
        }

        public static MyGameOptions Shootout_AllRoundsIncludingAbandoment()
        {
            var options = BaseOptions();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = true;
            options.ShootoutStrength = 1.0;
            options.ShootoutsAverageAllRounds = true;

            return options;
        }

        public static MyGameOptions LiabilityUncertainty_3BR()
        {
            var options = LiabilityUncertainty_1BR();

            options.NumPotentialBargainingRounds = 3;

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
            options.NumDamagesStrengthPoints = 1;
            options.NumDamagesSignals = 1;
            options.NumLiabilityStrengthPoints = 4;
            options.NumLiabilitySignals = 4;
            options.NumOffers = 4;
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
            options.IncludeAgreementToBargainDecisions = false;
            options.SkipFileAndAnswerDecisions = false;

            //options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth, Alpha = 10 * 0.000001 };
            //options.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth, Alpha = 10 * 0.000001 };

            return options;
        }

        public static MyGameOptions BothUncertain_2BR()
        {
            var options = BaseOptions();

            return options;
        }

        public static MyGameOptions BothUncertain_3BR()
        {
            var options = BaseOptions();
            options.NumPotentialBargainingRounds = 3;

            return options;
        }

        public static MyGameOptions BothUncertain_1BR()
        {
            var options = BaseOptions();
            options.NumPotentialBargainingRounds = 1;

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
            options.NumPotentialBargainingRounds = 3;
            options.IncludeAgreementToBargainDecisions = true;
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

        public static MyGameOptions DamagesShootout()
        {
            var options = DamagesUncertainty_2BR();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = false;
            options.ShootoutStrength = 1.0;
            options.ShootoutsAverageAllRounds = false;
            return options;
        }

        public static MyGameOptions LiabilityShootout()
        {
            var options = LiabilityUncertainty_2BR();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = false;
            options.ShootoutStrength = 1.0;
            options.ShootoutsAverageAllRounds = false;
            return options;
        }
    }
}
