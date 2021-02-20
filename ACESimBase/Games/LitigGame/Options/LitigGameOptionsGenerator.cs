using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;

namespace ACESim
{
    public static class LitigGameOptionsGenerator
    {
        public enum LitigGameOptionSetChoices
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
            FeeShiftingArticleBase,
        }

        static LitigGameOptionSetChoices LitigGameChoice => LitigGameOptionSetChoices.FeeShiftingArticleBase;

        public static LitigGameOptions GetLitigGameOptions() => LitigGameChoice switch
        {
            LitigGameOptionSetChoices.Custom => Custom(),
            LitigGameOptionSetChoices.Custom2 => Custom2(),
            LitigGameOptionSetChoices.Custom3 => Custom3(),
            LitigGameOptionSetChoices.LiabilityUncertainty_1BR => LiabilityUncertainty_1BR(),
            LitigGameOptionSetChoices.LiabilityUncertainty_2BR => LiabilityUncertainty_2BR(),
            LitigGameOptionSetChoices.LiabilityUncertainty_3BR => LiabilityUncertainty_3BR(),
            LitigGameOptionSetChoices.DamagesUncertainty_1BR => DamagesUncertainty_1BR(),
            LitigGameOptionSetChoices.DamagesUncertainty_2BR => DamagesUncertainty_2BR(),
            LitigGameOptionSetChoices.DamagesUncertainty_3BR => DamagesUncertainty_3BR(),
            LitigGameOptionSetChoices.BothUncertain_1BR => BothUncertain_1BR(),
            LitigGameOptionSetChoices.BothUncertain_2BR => BothUncertain_2BR(),
            LitigGameOptionSetChoices.BothUncertain_3BR => BothUncertain_3BR(),
            LitigGameOptionSetChoices.Shootout => Shootout(),
            LitigGameOptionSetChoices.Shootout_Triple => Shootout_Triple(),
            LitigGameOptionSetChoices.Shootout_AllRounds => Shootout_AllRounds(),
            LitigGameOptionSetChoices.Shootout_IncludingAbandoment => Shootout_IncludingAbandoment(),
            LitigGameOptionSetChoices.Shootout_AllRoundsIncludingAbandoment => Shootout_AllRoundsIncludingAbandoment(),
            LitigGameOptionSetChoices.SuperSimple => SuperSimple(),
            LitigGameOptionSetChoices.Faster => Faster(),
            LitigGameOptionSetChoices.Fast => Fast(),
            LitigGameOptionSetChoices.Usual => BothUncertain_2BR(),
            LitigGameOptionSetChoices.Ambitious => Ambitious(),
            LitigGameOptionSetChoices.PerfectInfo => PerfectInformation(courtIsPerfectToo: false),
            LitigGameOptionSetChoices.FeeShiftingArticleBase => FeeShiftingArticleBase(),
            _ => throw new Exception()
        };


        public static void NormalizeToDamages(LitigGameOptions o)
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
            if (o.RoundSpecificBargainingCosts != null)
                o.RoundSpecificBargainingCosts = o.RoundSpecificBargainingCosts.Select(x => (x.pCosts / divideBy, x.dCosts / divideBy)).ToArray();
        }

        public static LitigGameOptions BaseOptions()
        {
            var options = new LitigGameOptions()
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
                LitigGameDisputeGenerator = new LitigGameExogenousDisputeGenerator()
                {
                    ExogenousProbabilityTrulyLiable = 0.5,
                    StdevNoiseToProduceLiabilityStrength = 0.5
                },
                CollapseChanceDecisions = false,
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
                RoundSpecificBargainingCosts = null,
                AllowAbandonAndDefaults = true, 
                PredeterminedAbandonAndDefaults = false,
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
                BargainingRoundsSimultaneous = true, 
                SimultaneousOffersUltimatelyRevealed = true, 
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false, true, false, true, false },

                IncludeSignalsReport = true, 
                IncludeCourtSuccessReport = false, 
                FirstRowOnly = false,

                WarmStartThroughIteration = null,
                WarmStartOptions = LitigGameWarmStartOptions.NoWarmStart
            };
            // options.AdditionalTableOverrides = new List<(Func<Decision, GameProgress, byte>, string)>() { (LitigGameActionsGenerator.GamePlaysOutToTrial, "GamePlaysOutToTrial") };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            //options.PUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth };
            //options.DUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth };
            //options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth, Alpha = 10 * 0.000001 };
            //options.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth, Alpha = 10 * 0.000001 };
            return options;
        }

        public static LitigGameOptions Custom()
        {
            var options = BothUncertain_1BR();

            options.NumDamagesSignals = 2;
            options.NumLiabilitySignals = 2;
            options.NumOffers = 2;
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

        public static LitigGameOptions KlermanEtAl()
        {
            return GetKlermanEtAlOptions(0.5, true, false, false);
        }

        public static LitigGameOptions KlermanEtAl_MultipleStrengthPoints()
        {
            return GetKlermanEtAlOptions(0.5, false, false, false);
        }


        public static LitigGameOptions KlermanEtAl_WithOptions()
        {
            return GetKlermanEtAlOptions(0.5, false, true, false);
        }

        public static LitigGameOptions KlermanEtAl_WithDamagesUncertainty()
        {
            return GetKlermanEtAlOptions(0.5, false, true, true);
        }

        public static LitigGameOptions GetSimple1BROptions() => GetSimple1BROptions2(2, 2, 2);
        public static LitigGameOptions GetSimple2BROptions() => GetSimple2BROptions2(2, 2, 2);

        public static LitigGameOptions GetSimple1BROptions2(byte numLiabilityStrengthPoints, byte numLiabilitySignals, byte numOffers)
        {
            var options = BaseOptions();

            options.PLiabilityNoiseStdev = options.DLiabilityNoiseStdev = options.CourtLiabilityNoiseStdev = 0.15; 

            options.NumLiabilityStrengthPoints = numLiabilityStrengthPoints;
            options.NumLiabilitySignals = numLiabilitySignals;
            options.NumOffers = numOffers;

            options.NumDamagesSignals = 1;
            options.NumDamagesStrengthPoints = 1;
            options.DamagesMax = options.DamagesMin = 100_000;

            options.NumPotentialBargainingRounds = 1;
            options.AllowAbandonAndDefaults = false;
            options.IncludeAgreementToBargainDecisions = false;
            options.SkipFileAndAnswerDecisions = false;

            return options;
        }
        public static LitigGameOptions GetSimple2BROptions2(byte numLiabilityStrengthPoints, byte numLiabilitySignals, byte numOffers)
        {
            var options = GetSimple1BROptions2(numLiabilityStrengthPoints, numLiabilitySignals, numOffers);

            options.NumPotentialBargainingRounds = 2;

            return options;
        }

        public static LitigGameOptions GetKlermanEtAlOptions(double exogenousProbabilityTrulyLiable, bool useOnlyTwoLiabilityStrengthPoints, bool includeOptions, bool includeDamagesStrengths)
        {

            var options = LiabilityUncertainty_1BR();
            options.PInitialWealth = 0;
            options.DInitialWealth = 1;
            options.DamagesMin = options.DamagesMax = 1.0;
            options.PFilingCost = options.DAnswerCost = 0.05; // but see below for when we're using options
            options.PTrialCosts = options.DTrialCosts = 0.15; 
            options.PerPartyCostsLeadingUpToBargainingRound = 0;
            options.RoundSpecificBargainingCosts = null;
            options.CostsMultiplier = 1;
            options.SkipFileAndAnswerDecisions = true;
            options.AllowAbandonAndDefaults = false;
            if (includeOptions)
            {
                options.SkipFileAndAnswerDecisions = false;
                options.AllowAbandonAndDefaults = true;
                options.PFilingCost = options.DAnswerCost = 0.30; // higher values so we can see effect of dropping out
            }
            options.LitigGameDisputeGenerator = new LitigGameExogenousDisputeGenerator()
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
                options.LitigGameDisputeGenerator = new LitigGameExogenousDisputeGenerator()
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

        public static LitigGameOptions Custom2()
        {
            // make it symmetric
            var options = DamagesUncertainty_2BR();

            options.NumPotentialBargainingRounds = 5;
            options.PDamagesNoiseStdev = 0.09;
            options.DDamagesNoiseStdev = 0.09;
            options.CostsMultiplier = 0.2;
            options.SkipFileAndAnswerDecisions = true;
            options.AllowAbandonAndDefaults = true;
            options.PInitialWealth = options.DInitialWealth - options.DamagesMax; // so, fees aside, the losing party will end up at PInitialWealth, and the winning party will end up at DInitialWealth.

            return options;
        }

        public static LitigGameOptions DamagesUncertainty_1BR()
        {
            var options = BaseOptions();

            options.NumLiabilityStrengthPoints = 1; // Note: same with 2 br -- higher strength will be 100% of the time, b/c of ExogenousProbabilityTrulyLiable
            options.NumLiabilitySignals = 1;
            options.LitigGameDisputeGenerator = new LitigGameExogenousDisputeGenerator()
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

        public static LitigGameOptions DamagesUncertainty_2BR()
        {
            var options = DamagesUncertainty_1BR();
            options.NumPotentialBargainingRounds = 2;

            return options;
        }
        public static LitigGameOptions DamagesUncertainty_3BR()
        {
            var options = DamagesUncertainty_1BR();
            options.NumPotentialBargainingRounds = 3;

            return options;
        }

        public static LitigGameOptions LiabilityUncertainty_1BR()
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


        public static LitigGameOptions LiabilityUncertainty_2BR()
        {
            var options = LiabilityUncertainty_1BR();
            options.NumPotentialBargainingRounds = 2;

            return options;
        }

        public static LitigGameOptions Custom3()
        {
            var options = BaseOptions();

            options.CollapseChanceDecisions = true;

            options.NumOffers = 15;
            options.NumLiabilityStrengthPoints = 5;
            options.NumLiabilitySignals = 5;

            options.NumDamagesSignals = 1;
            options.NumDamagesStrengthPoints = 1;
            options.PInitialWealth = options.DInitialWealth = 10.0;
            options.DamagesMin = 0.0;
            options.DamagesMax = 1.0;
            options.PTrialCosts = options.DTrialCosts = 0.25;
            options.PFilingCost = options.DAnswerCost = 0.10;
            options.PerPartyCostsLeadingUpToBargainingRound = 0;
            options.CostsMultiplier = 1;

            options.LoserPays = false;
            options.LoserPaysMultiple = 1.0;

            options.SkipFileAndAnswerDecisions = false;
            options.NumPotentialBargainingRounds = 1;
            options.IncludeAgreementToBargainDecisions = false;
            options.AllowAbandonAndDefaults = true;
            options.PredeterminedAbandonAndDefaults = true;

            return options;
        }

        public static LitigGameOptions FeeShiftingArticleBase()
        {
            var options = BaseOptions();

            options.CollapseChanceDecisions = true; 
            options.CollapseAlternativeEndings = true; 

            options.IncludeSignalsReport = false;
            options.IncludeCourtSuccessReport = false;

            bool simplestCase = true; // DEBUG
            if (simplestCase)
            {
                options.NumOffers = 2;
                options.NumLiabilityStrengthPoints = 2;
                options.NumLiabilitySignals = 2;
            }
            else
            {
                options.NumOffers = 10;
                options.NumLiabilityStrengthPoints = 10; // DEBUG
                options.NumLiabilitySignals = 10; // DEBUG
            }

            options.NumDamagesSignals = 1;
            options.NumDamagesStrengthPoints = 1;

            options.PInitialWealth = options.DInitialWealth = 10.0;
            options.DamagesMin = 0.0;
            options.DamagesMax = 1.0;
            options.PTrialCosts = options.DTrialCosts = 0.15;
            options.PFilingCost = options.DAnswerCost = 0.15;
            options.PFilingCost_PortionSavedIfDDoesntAnswer = 0;
            options.PerPartyCostsLeadingUpToBargainingRound = 0;
            options.CostsMultiplier =  1.0;

            options.LoserPays = false;
            options.LoserPaysMultiple = 1;
            options.LoserPaysMarginOfVictoryThreshold = 0.8; 
            options.LoserPaysOnlyLargeMarginOfVictory = false; 

            options.LitigGameDisputeGenerator = new LitigGameExogenousDisputeGenerator()
            {
                ExogenousProbabilityTrulyLiable = 0.5,
                StdevNoiseToProduceLiabilityStrength = 0.35,
            };

            options.PLiabilityNoiseStdev =  0.2;
            options.DLiabilityNoiseStdev = 0.2;
            options.CourtLiabilityNoiseStdev = Math.Min(options.PLiabilityNoiseStdev, options.DLiabilityNoiseStdev);

            options.SkipFileAndAnswerDecisions = false; 
            options.NumPotentialBargainingRounds = 1;
            options.IncludeAgreementToBargainDecisions = false;
            options.AllowAbandonAndDefaults = true; 
            options.PredeterminedAbandonAndDefaults = true;

            return options;
        }

        public static LitigGameOptions Shootout()
        {
            var options = BaseOptions();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = false;
            options.ShootoutStrength = 1.0;
            options.ShootoutOfferValueIsAveraged = false;

            return options;
        }

        public static LitigGameOptions Shootout_Triple()
        {
            var options = BaseOptions();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = false;
            options.ShootoutStrength = 3.0;
            options.ShootoutOfferValueIsAveraged = false;

            return options;
        }

        public static LitigGameOptions Shootout_AllRounds()
        {
            var options = BaseOptions();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = false;
            options.ShootoutStrength = 1.0;
            options.ShootoutOfferValueIsAveraged = true;

            return options;
        }

        public static LitigGameOptions Shootout_IncludingAbandoment()
        {
            var options = BaseOptions();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = true;
            options.ShootoutStrength = 1.0;
            options.ShootoutOfferValueIsAveraged = false;

            return options;
        }

        public static LitigGameOptions Shootout_AllRoundsIncludingAbandoment()
        {
            var options = BaseOptions();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = true;
            options.ShootoutStrength = 1.0;
            options.ShootoutOfferValueIsAveraged = true;

            return options;
        }

        public static LitigGameOptions LiabilityUncertainty_3BR()
        {
            var options = LiabilityUncertainty_1BR();

            options.NumPotentialBargainingRounds = 3;

            return options;
        }

        public static LitigGameOptions SuperSimple()
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
            //options.LitigGameDisputeGenerator = new LitigGameEqualQualityProbabilitiesDisputeGenerator()
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

        public static LitigGameOptions Faster()
        {
            var options = BaseOptions();
            options.NumDamagesStrengthPoints = 1;
            options.NumDamagesSignals = 1;
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

        public static LitigGameOptions Fast()
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

        public static LitigGameOptions BothUncertain_2BR()
        {
            var options = BaseOptions();

            return options;
        }

        public static LitigGameOptions BothUncertain_3BR()
        {
            var options = BaseOptions();
            options.NumPotentialBargainingRounds = 3;

            return options;
        }

        public static LitigGameOptions BothUncertain_1BR()
        {
            var options = BaseOptions();
            options.NumPotentialBargainingRounds = 1;

            return options;
        }

        public static LitigGameOptions Ambitious()
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

        public static LitigGameOptions PerfectInformation(bool courtIsPerfectToo)
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

        public static LitigGameOptions DamagesShootout()
        {
            var options = DamagesUncertainty_2BR();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = false;
            options.ShootoutStrength = 1.0;
            options.ShootoutOfferValueIsAveraged = false;
            return options;
        }

        public static LitigGameOptions LiabilityShootout()
        {
            var options = LiabilityUncertainty_2BR();
            options.ShootoutSettlements = true;
            options.ShootoutsApplyAfterAbandonment = false;
            options.ShootoutStrength = 1.0;
            options.ShootoutOfferValueIsAveraged = false;
            return options;
        }
    }
}
