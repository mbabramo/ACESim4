using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.GameSolvingSupport.Symmetry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ACESim // Assuming the ACESim base namespace; adjust if needed
{
    /// <summary>
    /// Dispute generator for a negligence case with precaution investment and accident chance.
    /// Simulates the sequence from precaution choice through accident, engagement, settlement, and trial.
    /// Supports full simulation mode (explicit chance nodes for accident/trial) and collapsed chance mode (integrated probabilities).
    /// </summary>
    public class PrecautionNegligenceDisputeGenerator : ILitigGameDisputeGenerator
    {
        public LitigGameDefinition LitigGameDefinition { get; set; }

        public LitigGameOptions Options => LitigGameDefinition.Options;

        public string OptionsString => throw new NotImplementedException();

        public double CostOfAccident = 1.0; // normalized harm in the event of an accident
        public byte PrecautionPowerLevels = 10; // can be high if we're collapsing chance decisions, since this is the decision that gets collapsed
        public byte PrecautionLevels = 5;
        public double PrecautionPowerFactor = 0.8;
        public double ProbabilityAccidentNoActivity = 0.0;
        public double ProbabilityAccidentNoPrecaution = 0.0001;
        public double ProbabilityAccidentWrongfulAttribution = 0.000025;
        public double MarginalPrecautionCost = 0.00001; // DEBUG -- try to pick one where the social optimum is roughly half way with a middling precaution power.
        public double LiabilityThreshold = 1.0; // liability if benefit/cost ratio for marginal forsaken precaution > 1


        // Models for domain-specific logic
        private PrecautionImpactModel _impactModel;
        private PrecautionSignalModel _signalModel;
        private PrecautionCourtDecisionModel _courtDecisionModel;

        // Precomputed lookup tables
        private double[] _precautionPowerProbabilities; // Probability of each precaution level
        private double[] _dSignalProbabilities; // Probability of each defendant signal, when NOT conditioned on the precaution power level (in collapse chance mode)
        private double[][] _pSignalProbabilities;
        private double[] _accidentProbabilities;  // Probability of accident for each precaution level
        private bool[] _breachByPrecaution;       // Whether each precaution level is below the legal standard (true = breach of duty)

        /// <summary>
        /// Initializes a new PrecautionNegligenceDisputeGenerator with the given models and settings.
        /// </summary>
        /// <param name="impactModel">Model mapping precaution level to accident probability (and possibly related parameters).</param>
        /// <param name="signalModel">Model for generating private precaution signals for each party.</param>
        /// <param name="courtDecisionModel">Model defining the court's liability threshold and decision logic.</param>
        /// <param name="collapseChanceDecisions">If true, use collapsed chance mode; if false, use full simulation mode.</param>
        /// <param name="enableSettlement">If true, include a settlement negotiation stage before trial.</param>
        /// <param name="random">Optional random number generator for chance events (useful for simulation runs).</param>
        public void Setup(LitigGameDefinition myGameDefinition)
        {
            LitigGameDefinition = myGameDefinition;
            var options = LitigGameDefinition.Options;
            options.DamagesMax = options.DamagesMin = CostOfAccident;
            options.NumDamagesStrengthPoints = 1;
            _impactModel = new PrecautionImpactModel(PrecautionPowerLevels, PrecautionLevels, ProbabilityAccidentNoActivity, ProbabilityAccidentNoPrecaution, MarginalPrecautionCost, CostOfAccident, null, PrecautionPowerFactor, PrecautionPowerFactor, LiabilityThreshold, ProbabilityAccidentWrongfulAttribution, null);
            int numSamplesToMakeForCourtLiablityDetermination = 1000; // Note: This is different from NumCourtLiabilitySignals, which indicates the number of different branches that the court will receive and will thus generally be set to 2, for liability and no liability. Instead, this affects the fineness of the calculation of the probability of liability.
            _signalModel = new PrecautionSignalModel(PrecautionPowerLevels, options.NumLiabilitySignals, options.NumLiabilitySignals, numSamplesToMakeForCourtLiablityDetermination, options.PLiabilityNoiseStdev, options.DLiabilityNoiseStdev, options.CourtLiabilityNoiseStdev, includeExtremes: false);
            _courtDecisionModel = new PrecautionCourtDecisionModel(_impactModel, _signalModel);
            _precautionPowerProbabilities = Enumerable.Range(1, PrecautionPowerLevels).Select(x => 1.0 / PrecautionPowerLevels).ToArray();
            if (Options.CollapseChanceDecisions)
            {
                _dSignalProbabilities = _signalModel.GetUnconditionalDefendantSignalDistribution();
                _pSignalProbabilities = _signalModel.GetPlaintiffSignalDistributionGivenDefendantSignal();
            }
        }

        public List<Decision> GenerateDisputeDecisions(LitigGameDefinition litigGameDefinition)
        {
            var list = new List<Decision>();
            bool collapse = litigGameDefinition.Options.CollapseChanceDecisions;

            if (!collapse)
                list.Add(new(
                    "Precaution Power", "PPow", true,
                    (byte)LitigGamePlayers.LiabilityStrengthChance,
                    new byte[] { (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                    PrecautionPowerLevels,
                    (byte)LitigGameDecisions.LiabilityStrength,
                    unevenChanceActions: false)
                {
                    StoreActionInGameCacheItem = litigGameDefinition.GameHistoryCacheIndex_LiabilityStrength,
                    IsReversible = true,
                    DistributedChanceDecision = false, // using collapse chance instead
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = false,
                    SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)
                }
                );

            list.Add(new("Defendant Signal", "DLS", true,
                (byte)LitigGamePlayers.DLiabilitySignalChance,
                new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.AccidentChance, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                Options.NumLiabilitySignals,
                (byte)LitigGameDecisions.DLiabilitySignal,
                unevenChanceActions: Options.CollapseChanceDecisions) // DEBUG 
            {
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)
            });

            var plaintiffSignalDecision = new Decision("Plaintiff Signal", "PLS", true,
                (byte)LitigGamePlayers.PLiabilitySignalChance,
                new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.AccidentChance, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                Options.NumLiabilitySignals,
                (byte)LitigGameDecisions.PLiabilitySignal,
                unevenChanceActions: Options.CollapseChanceDecisions) 
            {
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)
            };
            if (!Options.CollapseChanceDecisions)
                list.Add(plaintiffSignalDecision); // when not collapsing chance decisions, we want the game tree to be as straightforward as possible, and don't want to worry about Bayesian calculations. So, we give the plaintiff it's signal (conditional on the underlying precaution power level) early.

            list.Add(new("Engage in Activity", "ENG", false,
                (byte)LitigGamePlayers.Defendant,
                new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.AccidentChance, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                Options.NumLiabilitySignals,
                (byte)LitigGameDecisions.EngageInActivity)
            {
                StoreActionInGameCacheItem = litigGameDefinition.GameHistoryCacheIndex_EngagesInActivity,
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)
            }); // 1 = yes, 2 = no

            list.Add(new("Precaution", "PREC", false,
                (byte)LitigGamePlayers.Defendant,
                new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.AccidentChance, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                PrecautionLevels,
                (byte)LitigGameDecisions.TakePrecaution)
            {
                StoreActionInGameCacheItem = litigGameDefinition.GameHistoryCacheIndex_PrecautionLevel,
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)
            }); // 1 = no precaution, ...

            list.Add(new("Accident", "ACC", true,
                (byte)LitigGamePlayers.AccidentChance,
                new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                (byte)2, /* no accident or accident */
                (byte)LitigGameDecisions.Accident,
                unevenChanceActions: true
                )
            {
                StoreActionInGameCacheItem = litigGameDefinition.GameHistoryCacheIndex_Accident,
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)

            }); // 1 --> accident, 2 --> no accident


            if (Options.CollapseChanceDecisions)
                list.Add(plaintiffSignalDecision); // when collapsing chance decisions, we can add the plaintiff's signal here, so we don't need to even deal with it if no accident occurs. We'll have to deal with the Bayesian calculations.

            // The liability signals tables are used when NOT collapsing decisions. This logic is already built in, so we don't need to enhance it.
            litigGameDefinition.CreateLiabilitySignalsTables();
            if (litigGameDefinition.Options.NumDamagesStrengthPoints > 1)
                throw new NotImplementedException(); // gameDefinition.CreateDamagesSignalsTables();

            if (litigGameDefinition.Options.LoserPaysOnlyLargeMarginOfVictory || litigGameDefinition.Options.NumCourtLiabilitySignals != 2)
                throw new NotImplementedException(); // we are generally implementing only 2 signals for the court (plaintiff wins and plaintiff loses) -- margin of victory fee shifting requires more.

            return list;
        }

        public bool PotentialDisputeArises(LitigGameDefinition gameDef, LitigGameStandardDisputeGeneratorActions acts, LitigGameProgress gameProgress)
        {
            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)gameProgress;
            return precautionProgress.EngagesInActivity && (precautionProgress.AccidentOccurs || ProbabilityAccidentWrongfulAttribution > 0);
        }

        public bool MarkCompleteAfterEngageInActivity(LitigGameDefinition g, byte engagesInActivityCode) => engagesInActivityCode == 2;
        public bool MarkCompleteAfterAccidentDecision(LitigGameDefinition g, byte accidentCode) => accidentCode == 2 && ProbabilityAccidentWrongfulAttribution == 0;

        public bool HandleUpdatingGameProgress(LitigGameProgress gameProgress, byte currentDecisionByteCode, byte action)
        {
            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)gameProgress;

            switch (currentDecisionByteCode)
            {
                case (byte)LitigGameDecisions.EngageInActivity:
                    bool engagesInActivity = action == 1;
                    precautionProgress.EngagesInActivity = engagesInActivity;
                    if (!engagesInActivity)
                        gameProgress.GameComplete = true;
                    break;
                case (byte)LitigGameDecisions.TakePrecaution:
                    precautionProgress.RelativePrecautionLevel = action - 1;
                    precautionProgress.OpportunityCost = action * MarginalPrecautionCost;
                    break;
                case (byte)LitigGameDecisions.Accident:
                    bool accidentOccurs = action == 1;
                    precautionProgress.AccidentOccurs = accidentOccurs;
                    // We're not going to set HarmCost yet --> instead, we'll do that when we generate consistent game progresses.
                    // The reason for this is that if there is an accident, there is some probability that the accident was
                    // wrongfully attributed to the defendant. But when reporting, we would like to separate out the cases in 
                    // which there was and wasn't wrongful attribution, so that we can have graphics that separate out these
                    // two sets of cases. We can do that based on information later, taking into account the precaution level.
                    if (!accidentOccurs && ProbabilityAccidentWrongfulAttribution == 0)
                        gameProgress.GameComplete = true;
                    break;
                default:
                    return false;
            }
            return true;
        }

        public bool IsTrulyLiable(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions,
            GameProgress gameProgress)
        {
            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)gameProgress;
            bool isTrulyLiable = _impactModel.IsTrulyLiable(precautionProgress.LiabilityStrengthDiscrete, precautionProgress.RelativePrecautionLevel);
            return isTrulyLiable;
        }

        public double[] GetLiabilityStrengthProbabilities(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions) => _precautionPowerProbabilities;

        public double[] GetDamagesStrengthProbabilities(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions) => [1.0];

        public double GetLitigationIndependentSocialWelfare(LitigGameDefinition gameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGameProgress gameProgress)
        {
            var costs = GetOpportunityAndHarmCosts(gameDefinition, disputeGeneratorActions, gameProgress);
            return 0 - costs.harmCost - costs.opportunityCost;
        }

        public double[] GetLitigationIndependentWealthEffects(LitigGameDefinition gameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGameProgress gameProgress)
        {
            var costs = GetOpportunityAndHarmCosts(gameDefinition, disputeGeneratorActions, gameProgress);
            return [0 - costs.opportunityCost, 0 - costs.harmCost];
        }

        public (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition gameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGameProgress gameProgress)
        {
            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)gameProgress;

            // recalculate this from scratch here, in case there have been changes based on post-game info beng reset
            var precautionTaken = precautionProgress.RelativePrecautionLevel * MarginalPrecautionCost;
            var harmCost = precautionProgress.AccidentProperlyCausallyAttributedToDefendant ? CostOfAccident : 0; // an accident that is not causally attributable to the defendant is not counted as a cost here, since it's exogenous to the model.

            return (precautionTaken, harmCost);
        }

        public bool SupportsSymmetry() => false;

        public string GetGeneratorName() => "PrecautionNegligence";

        public string GetActionString(byte action, byte decisionByteCode) => action.ToString();


        public double[] BayesianCalculations_GetDLiabilitySignalProbabilities(byte? pLiabilitySignal)
        {
            // Defendant goes first, so we can just use the original liability signal tables.
            if (pLiabilitySignal is not null)
                throw new NotSupportedException(); // This should only be called in collapse decision mode, as the first signal to be computed
            return _dSignalProbabilities; // not conditioned on anything
        }

        public double[] BayesianCalculations_GetPLiabilitySignalProbabilities(byte? dLiabilitySignal)
        {
            // Note: This is used ONLY when collapsing chance decisions. We know the accident occurred, and that depends
            // on the level of precaution chosen by the defendant, but that decision is solely a function of the defendant's
            // liability signal, so it doesn't add any information to this signal calculation. However, whether an accident
            // occurred also depends on the hidden state itself. 
            byte dSignal = (byte)dLiabilitySignal; // should not be null, because defendant gets signal first in this game
            return _pSignalProbabilities[dSignal - 1];
        }

        public double GetAccidentProbability(
            byte? precautionPowerLevel,
            byte dLiabilitySignal,
            byte chosenPrecautionLevel // zero-based, unlike others
            )
        {
            if ((uint)(dLiabilitySignal - 1) >= _signalModel.NumDSignals)
                throw new ArgumentOutOfRangeException(nameof(dLiabilitySignal));

            // Collapsed chance mode → integrate over hidden states.
            if (precautionPowerLevel is null)
            {
                return _impactModel.GetAccidentProbabilityGivenDSignalAndPrecautionLevel(
                    (int)dLiabilitySignal - 1,
                    chosenPrecautionLevel,
                    _signalModel);
            }

            // Full tree mode → hidden state is known, signals add no extra information.
            return _impactModel.GetAccidentProbability(
                (int)precautionPowerLevel.Value - 1,
                chosenPrecautionLevel);
        }

        public double[] BayesianCalculations_GetCLiabilitySignalProbabilities(PrecautionNegligenceProgress gameProgress)
        {
            if (Options.CollapseChanceDecisions)
            {
                double[] pr = _courtDecisionModel.GetLiabilityOutcomeProbabilities(
                     gameProgress.PLiabilitySignalDiscrete - 1,   // zero-based
                     gameProgress.DLiabilitySignalDiscrete - 1,
                     gameProgress.AccidentOccurs,
                     gameProgress.RelativePrecautionLevel);

                return pr;
            }
            else
            {
                double[] pr = _courtDecisionModel.GetLiabilityOutcomeProbabilities(gameProgress.LiabilityStrengthDiscrete - 1, gameProgress.RelativePrecautionLevel /* already zero based */);
                return pr;
            }
        }

        public double[] BayesianCalculations_GetCLiabilitySignalProbabilities(byte pLiabilitySignal, byte dLiabilitySignal)
        {
            throw new NotSupportedException(); // because above is implemented, this won't be called.
        }

        public double[] BayesianCalculations_GetPDamagesSignalProbabilities(byte? dDamagesSignal)
        {
            return [1.0];
        }

        public double[] BayesianCalculations_GetDDamagesSignalProbabilities(byte? pDamagesSignal)
        {
            return [1.0];
        }

        public double[] BayesianCalculations_GetCDamagesSignalProbabilities(byte pDamagesSignal, byte dDamagesSignal)
        {
            return [1.0];
        }

        public void BayesianCalculations_WorkBackwardsFromSignals(LitigGameProgress gameProgress, byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, int randomSeed)
        {
            throw new NotImplementedException();
        }

        public bool GenerateConsistentGameProgressesWhenNotCollapsing => true;

        public List<(GameProgress progress, double weight)> BayesianCalculations_GenerateAllConsistentGameProgresses(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, LitigGameProgress baseProgress)
        {
            baseProgress.ResetPostGameInfo(); // reset this because we're going to figure out wrongful attribution here and that 

            throw new NotImplementedException();
        }
    }
}