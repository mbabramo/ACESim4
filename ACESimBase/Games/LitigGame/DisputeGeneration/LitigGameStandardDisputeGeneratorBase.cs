using ACESimBase.GameSolvingSupport.Symmetry;
using System;
using System.Collections.Generic;

namespace ACESim
{
    /// <summary>
    /// Default implementation of the “standard” dispute-generator pattern
    /// (pre-chance → primary decision → post-chance → liability/damages strength
    ///  → P/D signals).  Sub-classes that need a different ordering may override
    /// GenerateDisputeDecisions.
    /// </summary>
    public abstract class LitigGameStandardDisputeGeneratorBase : ILitigGameStandardDisputeGenerator
    {
        public LitigGameDefinition LitigGameDefinition { get; set; }

        #region ILitigGameDisputeGenerator – generic parts

        public virtual void Setup(LitigGameDefinition litigGameDefinition)
            => LitigGameDefinition = litigGameDefinition;

        public virtual List<Decision> GenerateDisputeDecisions(
            LitigGameDefinition gameDefinition)
        {
            var decisions = new List<Decision>();

            // Classic three-slot sequence --------------------------------------------------
            GetActionsSetup(
                gameDefinition,
                out var prePrimaryActions,
                out var primaryActions,
                out var postPrimaryActions,
                out var prePrimaryInform,
                out var primaryInform,
                out var postPrimaryInform,
                out var preUneven,
                out var postUneven,
                out var qualUneven,
                out var primaryTerminates,
                out var postTerminates);

            if (prePrimaryActions > 0)
            {
                var (n, a) = PrePrimaryNameAndAbbreviation;
                decisions.Add(new Decision(
                    n, a, true,
                    (byte)LitigGamePlayers.PrePrimaryChance,
                    prePrimaryInform,
                    prePrimaryActions,
                    (byte)LitigGameDecisions.PrePrimaryActionChance)
                {
                    UnevenChanceActions = preUneven,
                    IsReversible = true,
                    DistributedChanceDecision = true
                });
            }

            if (primaryActions > 0)
            {
                var (n, a) = PrimaryNameAndAbbreviation;
                decisions.Add(new Decision(
                    n, a, false,
                    (byte)LitigGamePlayers.Defendant,
                    primaryInform,
                    primaryActions,
                    (byte)LitigGameDecisions.PrimaryAction)
                {
                    CanTerminateGame = primaryTerminates,
                    IsReversible = true
                });
            }

            if (postPrimaryActions > 0)
            {
                var (n, a) = PostPrimaryNameAndAbbreviation;
                decisions.Add(new Decision(
                    n, a, true,
                    (byte)LitigGamePlayers.PostPrimaryChance,
                    postPrimaryInform,
                    postPrimaryActions,
                    (byte)LitigGameDecisions.PostPrimaryActionChance)
                {
                    UnevenChanceActions = postUneven,
                    CanTerminateGame = postTerminates,
                    IsReversible = true,
                    DistributedChanceDecision = true
                });
            }

            // Liability and damages strength ----------------------------------------------
            var opt = gameDefinition.Options;

            decisions.Add(new Decision(
                opt.NumDamagesStrengthPoints > 1 ? "Liability Strength" : "Case Strength",
                "LiabStr",
                true,
                (byte)LitigGamePlayers.LiabilityStrengthChance,
                new byte[] {
                    (byte)LitigGamePlayers.PLiabilitySignalChance,
                    (byte)LitigGamePlayers.DLiabilitySignalChance,
                    (byte)LitigGamePlayers.CourtLiabilityChance,
                    (byte)LitigGamePlayers.Resolution },
                opt.NumLiabilityStrengthPoints,
                (byte)LitigGameDecisions.LiabilityStrength)
            {
                UnevenChanceActions = qualUneven,
                IsReversible = true,
                DistributedChanceDecision = true
            });

            if (opt.NumDamagesStrengthPoints > 1)
            {
                decisions.Add(new Decision(
                    "Damages Strength",
                    "DamStr",
                    true,
                    (byte)LitigGamePlayers.DamagesStrengthChance,
                    new byte[] {
                        (byte)LitigGamePlayers.PDamagesSignalChance,
                        (byte)LitigGamePlayers.DDamagesSignalChance,
                        (byte)LitigGamePlayers.CourtDamagesChance,
                        (byte)LitigGamePlayers.Resolution },
                    opt.NumDamagesStrengthPoints,
                    (byte)LitigGameDecisions.DamagesStrength)
                {
                    UnevenChanceActions = qualUneven,
                    IsReversible = true,
                    DistributedChanceDecision = true
                });
            }

            // Private signals --------------------------------------------------------------
            AddSignalDecisions(gameDefinition, decisions);

            return decisions;
        }

        void AddSignalDecisions(
            LitigGameDefinition gameDefinition,
            List<Decision> decisions)
        {
            var opt = gameDefinition.Options;

            if (opt.PLiabilityNoiseStdev != 0)
            {
                decisions.Add(new Decision(
                    "P Liability Signal", "PLS",
                    true,
                    (byte)LitigGamePlayers.PLiabilitySignalChance,
                    new byte[] {
                        (byte)LitigGamePlayers.Plaintiff,
                        (byte)LitigGamePlayers.DLiabilitySignalChance,
                        (byte)LitigGamePlayers.CourtLiabilityChance,
                        (byte)LitigGamePlayers.Resolution },
                    opt.NumLiabilitySignals,
                    (byte)LitigGameDecisions.PLiabilitySignal,
                    unevenChanceActions: true)
                {
                    IsReversible = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true
                });
            }

            if (opt.DLiabilityNoiseStdev != 0)
            {
                decisions.Add(new Decision(
                    "D Liability Signal", "DLS",
                    true,
                    (byte)LitigGamePlayers.DLiabilitySignalChance,
                    new byte[] {
                        (byte)LitigGamePlayers.Defendant,
                        (byte)LitigGamePlayers.CourtLiabilityChance,
                        (byte)LitigGamePlayers.Resolution },
                    opt.NumLiabilitySignals,
                    (byte)LitigGameDecisions.DLiabilitySignal,
                    unevenChanceActions: true)
                {
                    IsReversible = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true
                });
            }

            if (opt.NumDamagesStrengthPoints <= 1)
                return;

            if (opt.PDamagesNoiseStdev != 0)
            {
                decisions.Add(new Decision(
                    "P Damages Signal", "PDS",
                    true,
                    (byte)LitigGamePlayers.PDamagesSignalChance,
                    new byte[] {
                        (byte)LitigGamePlayers.Plaintiff,
                        (byte)LitigGamePlayers.DDamagesSignalChance,
                        (byte)LitigGamePlayers.CourtDamagesChance },
                    opt.NumDamagesSignals,
                    (byte)LitigGameDecisions.PDamagesSignal,
                    unevenChanceActions: true)
                {
                    IsReversible = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true
                });
            }

            if (opt.DDamagesNoiseStdev != 0)
            {
                decisions.Add(new Decision(
                    "D Damages Signal", "DDS",
                    true,
                    (byte)LitigGamePlayers.DDamagesSignalChance,
                    new byte[] {
                        (byte)LitigGamePlayers.Defendant,
                        (byte)LitigGamePlayers.CourtDamagesChance },
                    opt.NumDamagesSignals,
                    (byte)LitigGameDecisions.DDamagesSignal,
                    unevenChanceActions: true)
                {
                    IsReversible = true,
                    DistributorChanceInputDecision = true,
                    DistributableDistributorChanceInput = true
                });
            }
        }

        public virtual bool SupportsSymmetry() => false;

        #endregion

        #region ILitigGameStandardDisputeGenerator – abstract hooks

        public abstract void GetActionsSetup(
            LitigGameDefinition gameDefinition,
            out byte prePrimaryChanceActions,
            out byte primaryActions,
            out byte postPrimaryChanceActions,
            out byte[] prePrimaryPlayersToInform,
            out byte[] primaryPlayersToInform,
            out byte[] postPrimaryPlayersToInform,
            out bool prePrimaryUnevenChance,
            out bool postPrimaryUnevenChance,
            out bool litigationQualityUnevenChance,
            out bool primaryActionCanTerminate,
            out bool postPrimaryChanceCanTerminate);

        public abstract (string name, string abbreviation) PrePrimaryNameAndAbbreviation { get; }
        public abstract (string name, string abbreviation) PrimaryNameAndAbbreviation { get; }
        public abstract (string name, string abbreviation) PostPrimaryNameAndAbbreviation { get; }

        // ---------- model-specific calculations remain abstract ----------
        public abstract bool PotentialDisputeArises(LitigGameDefinition g, LitigGameDisputeGeneratorActions a);
        public abstract bool MarkComplete(LitigGameDefinition g, GameProgress p, Decision d, byte action);
        public abstract bool MarkComplete(LitigGameDefinition g, byte pre, byte primary);
        public abstract bool MarkComplete(LitigGameDefinition g, byte pre, byte primary, byte post);
        public abstract bool IsTrulyLiable(LitigGameDefinition g, LitigGameDisputeGeneratorActions a, GameProgress p);
        public abstract double[] GetLiabilityStrengthProbabilities(LitigGameDefinition g, LitigGameDisputeGeneratorActions a);
        public abstract double[] GetDamagesStrengthProbabilities(LitigGameDefinition g, LitigGameDisputeGeneratorActions a);
        public abstract double GetLitigationIndependentSocialWelfare(LitigGameDefinition g, LitigGameDisputeGeneratorActions a);
        public abstract double[] GetLitigationIndependentWealthEffects(LitigGameDefinition g, LitigGameDisputeGeneratorActions a);
        public abstract (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition g, LitigGameDisputeGeneratorActions a);

        public abstract string GetGeneratorName();
        public abstract string OptionsString { get; }
        public abstract string GetActionString(byte action, byte decisionByteCode);

        // Inverted-calculation methods – leave abstract for those generators that use them
        public abstract double[] InvertedCalculations_GetPLiabilitySignalProbabilities();
        public abstract double[] InvertedCalculations_GetDLiabilitySignalProbabilities(byte pSignal);
        public abstract double[] InvertedCalculations_GetCLiabilitySignalProbabilities(byte pSignal, byte dSignal);
        public abstract double[] InvertedCalculations_GetPDamagesSignalProbabilities();
        public abstract double[] InvertedCalculations_GetDDamagesSignalProbabilities(byte pSignal);
        public abstract double[] InvertedCalculations_GetCDamagesSignalProbabilities(byte pSignal, byte dSignal);
        public abstract double[] InvertedCalculations_GetLiabilityStrengthProbabilities(byte pL, byte dL, byte? cL);
        public abstract double[] InvertedCalculations_GetDamagesStrengthProbabilities(byte pD, byte dD, byte? cD);
        public abstract (bool trulyLiable, byte liabilityStrength, byte damagesStrength)
            InvertedCalculations_WorkBackwardsFromSignals(byte pL, byte dL, byte? cL, byte pD, byte dD, byte? cD, int seed);
        public abstract List<(GameProgress progress, double weight)>
            InvertedCalculations_GenerateAllConsistentGameProgresses(byte pL, byte dL, byte? cL, byte pD, byte dD, byte? cD, LitigGameProgress baseProgress);

        // Chance-ordering helper defaults
        public virtual (bool, bool, SymmetryMapInput) GetPrePrimaryUnrollSettings()
            => (false, false, SymmetryMapInput.SameInfo);
        public virtual (bool, bool, SymmetryMapInput) GetPrimaryUnrollSettings()
            => (false, false, SymmetryMapInput.SameInfo);
        public virtual (bool, bool, SymmetryMapInput) GetPostPrimaryUnrollSettings()
            => (false, false, SymmetryMapInput.SameInfo);
        public virtual (bool, bool, SymmetryMapInput) GetLiabilityStrengthUnrollSettings()
            => (false, false, SymmetryMapInput.SameInfo);
        public virtual (bool, bool, SymmetryMapInput) GetDamagesStrengthUnrollSettings()
            => (false, false, SymmetryMapInput.SameInfo);

        public abstract double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition g);
        public abstract double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition g, LitigGameDisputeGeneratorActions a);
        public abstract bool PostPrimaryDoesNotAffectStrategy();

        #endregion
    }
}
