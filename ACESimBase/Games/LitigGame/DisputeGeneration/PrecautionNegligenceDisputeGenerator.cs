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
            _signalModel = new PrecautionSignalModel(PrecautionPowerLevels, options.NumLiabilitySignals, options.NumLiabilitySignals, options.NumCourtLiabilitySignals, options.PLiabilityNoiseStdev, options.DLiabilityNoiseStdev, options.CourtLiabilityNoiseStdev, includeExtremes: false);
            _courtDecisionModel = new PrecautionCourtDecisionModel(_impactModel, _signalModel);
            _precautionPowerProbabilities = Enumerable.Range(1, PrecautionPowerLevels).Select(x => 1.0 / PrecautionPowerLevels).ToArray();
        }

        public List<Decision> GenerateDisputeDecisions(LitigGameDefinition g)
        {
            var list = new List<Decision>();
            bool collapse = g.Options.CollapseChanceDecisions;

            if (!collapse)
                list.Add(new(
                    "Precaution Power", "PPow", true,
                    (byte)LitigGamePlayers.LiabilityStrengthChance,
                    new byte[] { (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                    PrecautionPowerLevels,
                    (byte)LitigGameDecisions.LiabilityStrength,
                    unevenChanceActions: false)
                {
                    StoreActionInGameCacheItem = g.GameHistoryCacheIndex_LiabilityStrength,
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

            list.Add(new("Engage in Activity", "ENG", false,
                (byte)LitigGamePlayers.Defendant,
                new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.AccidentChance, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                Options.NumLiabilitySignals,
                (byte)LitigGameDecisions.EngageInActivity)
            {
                StoreActionInGameCacheItem = g.GameHistoryCacheIndex_EngagesInActivity,
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
                StoreActionInGameCacheItem = g.GameHistoryCacheIndex_PrecautionLevel,
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
                (byte)LitigGameDecisions.Accident
                )
            {
                StoreActionInGameCacheItem = g.GameHistoryCacheIndex_Accident,
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)

            }); // 1 --> accident, 2 --> no accident

            list.Add(new("Plaintiff Signal", "PLS", true,
                (byte)LitigGamePlayers.PLiabilitySignalChance,
                new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.AccidentChance, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                Options.NumLiabilitySignals,
                (byte)LitigGameDecisions.PLiabilitySignal,
                unevenChanceActions: Options.CollapseChanceDecisions) // DEBUG 
            {
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)
            });

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
                    precautionProgress.RelativePrecautionLevel = action;
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
            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)gameProgress;
            throw new NotImplementedException();
        }

        public double[] GetLitigationIndependentWealthEffects(LitigGameDefinition gameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGameProgress gameProgress)
        {
            throw new NotImplementedException();
        }

        public (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition gameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGameProgress gameProgress)
        {
            throw new NotImplementedException();
        }

        public bool SupportsSymmetry() => false;

        public string GetGeneratorName() => "PrecautionNegligence";

        public string GetActionString(byte action, byte decisionByteCode) => action.ToString();

        public double[] InvertedCalculations_GetPLiabilitySignalProbabilities(byte? dLiabilitySignal)
        {
            throw new NotImplementedException();
        }

        public double[] InvertedCalculations_GetDLiabilitySignalProbabilities(byte? pLiabilitySignal)
        {
            throw new NotImplementedException();
        }

        public double[] InvertedCalculations_GetCLiabilitySignalProbabilities(byte pLiabilitySignal, byte dLiabilitySignal)
        {
            throw new NotImplementedException();
        }

        public double[] InvertedCalculations_GetPDamagesSignalProbabilities(byte? dDamagesSignal)
        {
            throw new NotImplementedException();
        }

        public double[] InvertedCalculations_GetDDamagesSignalProbabilities(byte? pDamagesSignal)
        {
            throw new NotImplementedException();
        }

        public double[] InvertedCalculations_GetCDamagesSignalProbabilities(byte pDamagesSignal, byte dDamagesSignal)
        {
            throw new NotImplementedException();
        }

        public double[] InvertedCalculations_GetLiabilityStrengthProbabilities(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal)
        {
            throw new NotImplementedException();
        }

        public double[] InvertedCalculations_GetDamagesStrengthProbabilities(byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal)
        {
            throw new NotImplementedException();
        }

        public void InvertedCalculations_WorkBackwardsFromSignals(LitigGameProgress gameProgress, byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, int randomSeed)
        {
            throw new NotImplementedException();
        }

        public bool GenerateConsistentGameProgressesWhenNotCollapsing => true;

        public List<(GameProgress progress, double weight)> InvertedCalculations_GenerateAllConsistentGameProgresses(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, LitigGameProgress baseProgress)
        {
            throw new NotImplementedException();
        }
    }
}