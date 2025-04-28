using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;
using ACESimBase.Util.Mathematics;

namespace ACESim
{
    public static class LitigGameOptionsGenerator
    {
        public enum LitigGameOptionSetChoices
        {
            EndogenousArticleBase,
            FeeShiftingBase,
            AppropriationGame,
            SmallGame,
        }

        static LitigGameOptionSetChoices LitigGameChoice => LitigGameOptionSetChoices.SmallGame; // DEBUG

        public static LitigGameOptions GetLitigGameOptions() => LitigGameChoice switch
        {
            LitigGameOptionSetChoices.EndogenousArticleBase => EndogenousArticleBase(),
            LitigGameOptionSetChoices.AppropriationGame => AppropriationGame(),
            LitigGameOptionSetChoices.FeeShiftingBase => FeeShiftingBase(),
            LitigGameOptionSetChoices.SmallGame => SmallGame(),
            _ => throw new Exception()
        };

        public static LitigGameOptions EndogenousArticleBase()
        {
            var options = new LitigGameOptions();

            bool collapse = false;
            options.CollapseChanceDecisions = collapse; 
            options.CollapseAlternativeEndings = collapse; 

            options.IncludeSignalsReport = false;
            options.IncludeCourtSuccessReport = false;
            options.FirstRowOnly = false;

            options.PInitialWealth = options.DInitialWealth = 10.0;
            options.DamagesMin = 0.0;
            options.DamagesMax = 1.0;
            options.DamagesMultiplier = 1.0;

            options.PTrialCosts = options.DTrialCosts = 0.15;
            options.PFilingCost = options.DAnswerCost = 0.15;
            options.PFilingCost_PortionSavedIfDDoesntAnswer = 0;
            options.PerPartyCostsLeadingUpToBargainingRound = 0;
            options.RoundSpecificBargainingCosts = null;
            options.CostsMultiplier = 1.0;

            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            options.RegretAversion = 0;

            bool smallerTree = true;
            byte numOfEach = smallerTree ? (byte) 5 : (byte) 10;
            options.NumOffers = numOfEach;
            options.NumLiabilityStrengthPoints = numOfEach;
            options.NumLiabilitySignals = numOfEach;
            options.IncludeEndpointsForOffers = false;

            options.PLiabilityNoiseStdev = 0.2;
            options.DLiabilityNoiseStdev = 0.2;
            options.CourtLiabilityNoiseStdev = Math.Min(options.PLiabilityNoiseStdev, options.DLiabilityNoiseStdev);

            options.NumDamagesSignals = 1;
            options.NumDamagesStrengthPoints = 1;
            options.PDamagesNoiseStdev = 0.1;
            options.DDamagesNoiseStdev = 0.1;
            options.CourtDamagesNoiseStdev = 0.15;

            options.LoserPays = false;
            options.LoserPaysMultiple = options.LoserPays ? 1 : 0;
            options.LoserPaysAfterAbandonment = false;
            options.LoserPaysMarginOfVictoryThreshold = 0.7; 
            options.LoserPaysOnlyLargeMarginOfVictory = false; 

            options.NumPotentialBargainingRounds = 1;
            options.BargainingRoundsSimultaneous = true;
            options.SimultaneousOffersUltimatelyRevealed = true;
            options.PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false, true, false, true, false };

            options.SkipFileAndAnswerDecisions = false; 
            options.IncludeAgreementToBargainDecisions = false;
            options.AllowAbandonAndDefaults = true;
            options.PredeterminedAbandonAndDefaults = true;

            options.DeltaOffersOptions = new DeltaOffersOptions()
            {
                SubsequentOffersAreDeltas = false,
                DeltaStartingValue = 0.01,
                MaxDelta = 0.25
            };

            options.WarmStartThroughIteration = null;
            options.WarmStartOptions = LitigGameWarmStartOptions.NoWarmStart;

            options.LitigGameDisputeGenerator = new LitigGameExogenousDisputeGenerator()
            {
                ExogenousProbabilityTrulyLiable = 0.5,
                StdevNoiseToProduceLiabilityStrength = 0.35,
            };

            return options;
        }

        public static LitigGameOptions FeeShiftingBase()
        {
            var options = EndogenousArticleBase();
            options.CollapseAlternativeEndings = true; // can't do this where we're really using endogenous disputes
            options.CollapseChanceDecisions = true;
            options.NumLiabilitySignals = options.NumLiabilityStrengthPoints = options.NumOffers = 5; // DEBUG
            return options;
        }

        public static LitigGameOptions SmallGame()
        {
            var options = EndogenousArticleBase();
            options.CollapseAlternativeEndings = true; 
            options.CollapseChanceDecisions = true;
            options.AllowAbandonAndDefaults = false;
            options.NumLiabilitySignals = options.NumLiabilityStrengthPoints = options.NumOffers = 2;
            return options;
        }

        public static LitigGameOptions AppropriationGame()
        {
            var options = EndogenousArticleBase();

            options.AllowAbandonAndDefaults = false; // DEBUG
            options.NumLiabilitySignals = options.NumLiabilityStrengthPoints = options.NumOffers = 5; // DEBUG

            var disputeGenerator = new LitigGameAppropriationDisputeGenerator();
            disputeGenerator.NumSystemicRandomnessLevels = 3; // DEBUG

            options.LitigGameDisputeGenerator = disputeGenerator;
            return options;
        }

    }
}
