using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.Util.Mathematics;

namespace ACESim
{
    public static class LitigGameOptionsGenerator
    {
        public enum LitigGameOptionSetChoices
        {
            EndogenousArticleBase,
            FeeShiftingBaseSmallTree,
            FeeShiftingBaseLargeTree,
            AppropriationGame, 
            PrecautionNegligenceGame,
            SmallGame,
        }

        // This choice has an effect only when in ACESimConsole mode (playing a single game). 
        static LitigGameOptionSetChoices LitigGameChoice => LitigGameOptionSetChoices.PrecautionNegligenceGame;

        public static LitigGameOptions GetLitigGameOptions()
        {
            var options = LitigGameChoice switch
            {
                LitigGameOptionSetChoices.EndogenousArticleBase => BaseBeforeApplyingEndogenousGenerator(),
                LitigGameOptionSetChoices.AppropriationGame => AppropriationGame(),
                LitigGameOptionSetChoices.PrecautionNegligenceGame => PrecautionNegligenceGame(),
                LitigGameOptionSetChoices.FeeShiftingBaseSmallTree => FeeShiftingBase(true),
                LitigGameOptionSetChoices.FeeShiftingBaseLargeTree => FeeShiftingBase(false),
                LitigGameOptionSetChoices.SmallGame => SmallGame(),
                _ => throw new Exception()
            };
            return options;
        }

        private static LitigGameOptions BaseBeforeApplyingEndogenousGenerator()
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

            options.PTrialCosts = options.DTrialCosts = 0.10;
            options.PFilingCost = options.DAnswerCost = 0.10;
            options.PFilingCost_PortionSavedIfDDoesntAnswer = 0;
            options.PerPartyCostsLeadingUpToBargainingRound = 0.10;
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

        public static LitigGameOptions FeeShiftingBase(bool smallerTree)
        {
            var options = BaseBeforeApplyingEndogenousGenerator();
            options.CollapseAlternativeEndings = true; // can't do this where we're really using endogenous disputes
            options.CollapseChanceDecisions = true;
            options.NumLiabilitySignals = options.NumLiabilityStrengthPoints = options.NumOffers = smallerTree ? (byte) 5 : (byte) 10;
            return options;
        }

        public static LitigGameOptions SmallGame()
        {
            var options = BaseBeforeApplyingEndogenousGenerator();
            options.CollapseAlternativeEndings = false; 
            options.CollapseChanceDecisions = true;
            options.AllowAbandonAndDefaults = true;
            options.NumLiabilitySignals = options.NumLiabilityStrengthPoints = options.NumOffers = 2;
            return options;
        }

        public static LitigGameOptions AppropriationGame()
        {
            var options = BaseBeforeApplyingEndogenousGenerator();

            options.AllowAbandonAndDefaults = true; 
            options.NumLiabilitySignals = options.NumLiabilityStrengthPoints = options.NumOffers = 5;

            var disputeGenerator = new LitigGameAppropriationDisputeGenerator();
            disputeGenerator.NumDetectabilityLevels = 5;

            options.LitigGameDisputeGenerator = disputeGenerator;
            return options;
        }

        public static LitigGameOptions PrecautionNegligenceGame(bool collapseChanceDecisions, bool allowQuitting, byte numSignalsAndOffers, byte numPotentialBargainingRounds, byte numPrecautionPowerLevels, byte precautionLevels)
        {
            var options = BaseBeforeApplyingEndogenousGenerator();

            options.CollapseChanceDecisions = collapseChanceDecisions;

            options.AllowAbandonAndDefaults = allowQuitting;
            options.NumLiabilitySignals = options.NumLiabilityStrengthPoints = options.NumOffers = numSignalsAndOffers;
            options.SkipFileAndAnswerDecisions = !allowQuitting;
            options.NumPotentialBargainingRounds = numPotentialBargainingRounds;

            var disputeGenerator = new PrecautionNegligenceDisputeGenerator();
            disputeGenerator.PrecautionPowerLevels = numPrecautionPowerLevels;
            disputeGenerator.RelativePrecautionLevels = precautionLevels;
            disputeGenerator.UnitPrecautionCost = 0.00001; // chosen so that the bc ratio varies below and above 1 depending on precaution power
            disputeGenerator.ProbabilityAccidentNoPrecaution = 0.0001;
            disputeGenerator.AlphaLowPower = 2;
            disputeGenerator.AlphaHighPower = 1.5;
            disputeGenerator.ProbabilityAccidentMaxPrecaution_LowPower = 0.00008;
            disputeGenerator.ProbabilityAccidentMaxPrecaution_HighPower = 0.00002;
            disputeGenerator.ProbabilityAccidentWrongfulAttribution = 0.00001;
            disputeGenerator.CourtDecisionRule = CourtDecisionRule.ProbitThreshold;
            disputeGenerator.CourtProbitScale = 0.25;

            options.NumLiabilityStrengthPoints = disputeGenerator.PrecautionPowerLevels;

            options.LitigGameDisputeGenerator = disputeGenerator;

            return options;
        }

        static bool UseSimplifiedPrecautionNegligenceGame = false;
        static bool CollapseDecisionsInSimplifiedPrecautionNegligenceGame = false;
        static bool PerfectAdjudication = false;
        static bool PerfectInformationToo = false;

        static byte ParameterForMultipleOptions_Simplified = 2;
        static byte ParameterForMultipleOptions = 8;
        public static LitigGameOptions PrecautionNegligenceGame()
        {
            var game = UseSimplifiedPrecautionNegligenceGame ?
                PrecautionNegligenceGame(CollapseDecisionsInSimplifiedPrecautionNegligenceGame, allowQuitting: true, numSignalsAndOffers: ParameterForMultipleOptions_Simplified, numPotentialBargainingRounds: 1, numPrecautionPowerLevels: ParameterForMultipleOptions_Simplified, precautionLevels: ParameterForMultipleOptions_Simplified)
                :
                PrecautionNegligenceGame(true, true, ParameterForMultipleOptions, 1, ParameterForMultipleOptions, ParameterForMultipleOptions);
            if (PerfectAdjudication)
            {
                game.CourtDamagesNoiseStdev = 0;
                game.CourtLiabilityNoiseStdev = 0;
                game.DTrialCosts = game.PTrialCosts = game.PerPartyCostsLeadingUpToBargainingRound = game.PFilingCost = game.DAnswerCost = 0;
                if (PerfectInformationToo)
                {
                    game.PLiabilityNoiseStdev = 0;
                    game.DLiabilityNoiseStdev = 0;
                    game.PDamagesNoiseStdev = 0;
                    game.DDamagesNoiseStdev = 0;
                }
            }
            return game;
        }
    } 
}
